import { derivePasswordHash, PASSWORD_POLICY } from "./account-auth.js";

const MAX_TOKEN_LENGTH = 200;
const ADMIN_SESSION_COOKIE = "fb_admin_session";

export async function handleAdminUsersApi(request, env, url) {
  if (!url.pathname.startsWith("/api/admin/users")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);

  const auth = await requireAdmin(request, env);
  if (!auth.ok) return auth.response;

  try {
    const parts = url.pathname.split("/").filter(Boolean);
    const userId = parts.length >= 4 ? Number(parts[3]) : 0;

    if (request.method === "GET" && !userId) return listUsers(env.DB, url);
    if (request.method === "GET" && userId) return getUser(env.DB, userId);
    if (request.method === "PATCH" && userId) return updateUser(request, env, userId);
    if (request.method === "DELETE" && userId && parts[4] === "friendship") return unlinkFriend(request, env.DB, userId);
    if (request.method === "POST" && userId && parts[4] === "verify") return verifyUser(env.DB, userId);

    return json({ error: "Admin user endpoint not found." }, 404);
  } catch (error) {
    console.error("Factburst admin user management failed", error);
    return json({ error: "The user management request could not be completed." }, 503);
  }
}

async function listUsers(db, url) {
  const search = String(url.searchParams.get("search") || "").trim();
  const limit = Math.min(Math.max(Number(url.searchParams.get("limit") || 50), 1), 100);
  const pattern = `%${search.replace(/[%_]/g, "\\$&")}%`;
  const where = search ? "WHERE u.username LIKE ? ESCAPE '\\\\' OR u.email LIKE ? ESCAPE '\\\\' OR u.referral_code LIKE ? ESCAPE '\\\\'" : "";
  const bindings = search ? [pattern, pattern, pattern, limit] : [limit];
  const result = await db.prepare(`
    SELECT u.id,u.username,u.email,u.email_verified_at,u.status,u.created_at,u.referral_code,
      (SELECT COUNT(*) FROM site_friendships f WHERE (f.user_a_id=u.id OR f.user_b_id=u.id) AND f.status='accepted') AS friends_count,
      (SELECT r.username FROM site_users r WHERE r.id=u.referred_by_user_id LIMIT 1) AS referred_by
    FROM site_users u ${where}
    ORDER BY u.created_at DESC, u.id DESC LIMIT ?
  `).bind(...bindings).all();
  return json({ users: (result.results || []).map(publicUser) });
}

async function getUser(db, userId) {
  const user = await db.prepare(`
    SELECT u.id,u.username,u.email,u.email_verified_at,u.status,u.suspended_at,u.suspension_reason,
      u.created_at,u.last_login_at,u.referral_code,
      (SELECT r.username FROM site_users r WHERE r.id=u.referred_by_user_id LIMIT 1) AS referred_by
    FROM site_users u WHERE u.id=? LIMIT 1
  `).bind(userId).first();
  if (!user) return json({ error: "User not found." }, 404);

  const friends = await db.prepare(`
    SELECT f.id,f.status,f.created_at,
      CASE WHEN f.user_a_id=? THEN f.user_b_id ELSE f.user_a_id END AS friend_id,
      CASE WHEN f.user_a_id=? THEN b.username ELSE a.username END AS friend_username,
      CASE WHEN f.user_a_id=? THEN b.email ELSE a.email END AS friend_email
    FROM site_friendships f
    JOIN site_users a ON a.id=f.user_a_id
    JOIN site_users b ON b.id=f.user_b_id
    WHERE f.user_a_id=? OR f.user_b_id=?
    ORDER BY f.created_at DESC
  `).bind(userId,userId,userId,userId,userId).all();

  const referred = await db.prepare(`
    SELECT id,username,email,created_at,email_verified_at,status
    FROM site_users WHERE referred_by_user_id=? ORDER BY created_at DESC
  `).bind(userId).all();

  return json({
    user: publicUser(user),
    friends: friends.results || [],
    referred_users: referred.results || [],
  });
}

async function updateUser(request, env, userId) {
  let body;
  try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }

  const existing = await env.DB.prepare("SELECT id,username_key,email_key,email_verified_at FROM site_users WHERE id=? LIMIT 1").bind(userId).first();
  if (!existing) return json({ error: "User not found." }, 404);

  const username = normalizeUsername(body?.username);
  const email = normalizeEmail(body?.email);
  const password = String(body?.password || "");
  if (!username) return json({ error: "Choose a username 3–24 characters long." }, 400);
  if (!email) return json({ error: "Enter a valid email address." }, 400);
  if (password && (password.length < 10 || password.length > 128)) return json({ error: "Password must be between 10 and 128 characters." }, 400);

  const usernameKey = username.toLowerCase();
  const emailKey = email.toLowerCase();
  const duplicate = await env.DB.prepare(`SELECT id FROM site_users WHERE id<>? AND (username_key=? OR email_key=?) LIMIT 1`).bind(userId,usernameKey,emailKey).first();
  if (duplicate) return json({ error: "That username or email is already in use." }, 409);

  const emailChanged = emailKey !== String(existing.email_key || "").toLowerCase();
  const statements = [];
  if (password) {
    const pepper = String(env.PASSWORD_PEPPER || "").trim();
    if (pepper.length < 32) return json({ error: "Password security is not configured." }, 503);
    const salt = randomToken(18);
    const hash = await derivePasswordHash(password, salt, pepper, PASSWORD_POLICY.iterations);
    statements.push(env.DB.prepare(`UPDATE site_users SET username=?,username_key=?,email=?,email_key=?,email_verified_at=CASE WHEN ? THEN NULL ELSE email_verified_at END,password_hash=?,password_salt=?,password_iterations=?,password_scheme=? WHERE id=?`).bind(username,usernameKey,email,emailKey,emailChanged?1:0,hash,salt,PASSWORD_POLICY.iterations,PASSWORD_POLICY.scheme,userId));
    statements.push(env.DB.prepare("DELETE FROM site_sessions WHERE user_id=?").bind(userId));
  } else {
    statements.push(env.DB.prepare(`UPDATE site_users SET username=?,username_key=?,email=?,email_key=?,email_verified_at=CASE WHEN ? THEN NULL ELSE email_verified_at END WHERE id=?`).bind(username,usernameKey,email,emailKey,emailChanged?1:0,userId));
  }
  if (emailChanged) statements.push(env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id=?").bind(userId));
  await env.DB.batch(statements);
  return getUser(env.DB, userId);
}

async function verifyUser(db, userId) {
  const exists = await db.prepare("SELECT id FROM site_users WHERE id=? LIMIT 1").bind(userId).first();
  if (!exists) return json({ error: "User not found." }, 404);
  const now = new Date().toISOString();
  await db.prepare("UPDATE site_users SET email_verified_at=? WHERE id=?").bind(now,userId).run();
  await db.prepare("DELETE FROM site_email_verifications WHERE user_id=?").bind(userId).run();
  return getUser(db,userId);
}

async function unlinkFriend(request, db, userId) {
  let body; try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." },400); }
  const friendshipId = Number(body?.friendship_id || 0);
  if (!friendshipId) return json({ error: "Friendship id is required." },400);
  const result = await db.prepare("DELETE FROM site_friendships WHERE id=? AND (user_a_id=? OR user_b_id=?)").bind(friendshipId,userId,userId).run();
  if (!result.meta?.changes) return json({ error: "Friendship not found." },404);
  return json({ unlinked:true });
}

function publicUser(user) {
  return {
    id:Number(user.id), username:String(user.username||""), email:String(user.email||""),
    email_verified:Boolean(user.email_verified_at), email_verified_at:user.email_verified_at||null,
    status:String(user.status||"active"), created_at:user.created_at||null, last_login_at:user.last_login_at||null,
    referral_code:String(user.referral_code||""), referred_by:user.referred_by||null,
    friends_count:Number(user.friends_count||0), suspended_at:user.suspended_at||null,
    suspension_reason:String(user.suspension_reason||""),
  };
}
function normalizeUsername(value){const v=String(value||"").trim().replace(/\s+/g," ");return v.length>=3&&v.length<=24&&/^[A-Za-z0-9][A-Za-z0-9 _.-]*[A-Za-z0-9]$/.test(v)?v:"";}
function normalizeEmail(value){const v=String(value||"").trim();return v&&v.length<=254&&!/\s/.test(v)&&/^[^@]+@[^@]+\.[^@]+$/.test(v)?v:"";}
function randomToken(n){const b=new Uint8Array(n);crypto.getRandomValues(b);let s="";for(const x of b)s+=String.fromCharCode(x);return btoa(s).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/g,"");}
async function requireAdmin(request,env){const cookie=String(request.headers.get("cookie")||"");const match=cookie.match(new RegExp(`(?:^|;\\s*)${ADMIN_SESSION_COOKIE}=([^;]+)`));if(match){const row=await env.DB.prepare("SELECT expires_at FROM site_admin_sessions WHERE token_hash=? AND expires_at>? LIMIT 1").bind(await sha256(match[1]),new Date().toISOString()).first();if(row)return {ok:true};}const key=String(request.headers.get("authorization")||"").replace(/^Bearer\s+/i,"").trim();const expected=String(env.SITE_ADMIN_KEY||"").trim();if(key&&expected&&key===expected)return {ok:true};return {ok:false,response:json({error:"Administrator access required."},401)};}
async function sha256(value){const d=await crypto.subtle.digest("SHA-256",new TextEncoder().encode(value));let s="";for(const b of new Uint8Array(d))s+=String.fromCharCode(b);return btoa(s).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/g,"");}
function json(value,status=200){return new Response(JSON.stringify(value),{status,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff"}})}
