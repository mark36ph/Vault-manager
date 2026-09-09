import { derivePasswordHash, PASSWORD_POLICY, normalizeEmail, normalizeUsername } from "./account-auth.js";

const ADMIN_SESSION_COOKIE = "fb_admin_session";
const ADMIN_SESSION_SECONDS = 30 * 24 * 60 * 60;

export async function handleAdminUserManagementApi(request, env, url) {
  if (!url.pathname.startsWith("/api/admin/users")) return null;
  const auth = await requireAdmin(request, env);
  if (!auth.ok) return auth.response;
  if (!env.DB) return json({ error: "The quiz database is not configured yet." }, 503);
  await ensureReferralSchema(env.DB);
  const path = url.pathname.replace(/^\/+|\/+$/g, "");
  const parts = path.split("/");
  if (path === "api/admin/users" && request.method === "GET") return listUsers(env.DB, url);
  if (parts.length === 4 && parts[0] === "api" && parts[1] === "admin" && parts[2] === "users") {
    const userId = Number.parseInt(parts[3], 10);
    if (!Number.isInteger(userId) || userId <= 0) return json({ error: "Invalid user id." }, 400);
    if (request.method === "GET") return detailUser(env.DB, userId);
    if (request.method === "PATCH") return updateUser(request, env, userId);
  }
  if (parts.length === 5 && parts[0] === "api" && parts[1] === "admin" && parts[2] === "users" && parts[4] === "friends" && request.method === "DELETE") {
    const userId = Number.parseInt(parts[3], 10);
    const friendId = Number.parseInt(url.searchParams.get("friend_id") || "", 10);
    if (!Number.isInteger(userId) || !Number.isInteger(friendId) || userId <= 0 || friendId <= 0) return json({ error: "Invalid user or friend id." }, 400);
    await env.DB.prepare(`DELETE FROM site_friendships WHERE (user_a_id = ? AND user_b_id = ?) OR (user_a_id = ? AND user_b_id = ?)`)
      .bind(Math.min(userId, friendId), Math.max(userId, friendId), Math.max(userId, friendId), Math.min(userId, friendId)).run();
    return detailUser(env.DB, userId);
  }
  return json({ error: "Not found." }, 404);
}

async function requireAdmin(request, env) {
  if (!env.SITE_ADMIN_KEY) return { response: json({ error: "Website publishing is not enabled yet." }, 503) };
  if (await verifyAdminSession(request, env)) return { ok: true };
  const supplied = request.headers.get("authorization") || "";
  if (supplied !== `Bearer ${env.SITE_ADMIN_KEY}`) return { response: json({ error: "Unauthorized." }, 401) };
  return { ok: true };
}
async function verifyAdminSession(request, env) {
  const cookie = (request.headers.get("cookie") || "").split(";").map(v => v.trim()).find(v => v.startsWith(`${ADMIN_SESSION_COOKIE}=`));
  if (!cookie) return false;
  const parts = cookie.slice(`${ADMIN_SESSION_COOKIE}=`.length).split(".");
  if (parts.length === 2) return constantTimeEqual(await hmacSha256(env.SITE_ADMIN_KEY, parts[0]), fromBase64Url(parts[1]));
  if (parts.length !== 3) return false;
  const timestamp = Number(parts[0]);
  const now = Math.floor(Date.now() / 1000);
  if (!Number.isInteger(timestamp) || now - timestamp > ADMIN_SESSION_SECONDS || timestamp - now > 60) return false;
  return constantTimeEqual(await hmacSha256(env.SITE_ADMIN_KEY, `factburst-admin-session:${parts[0]}.${parts[1]}`), fromBase64Url(parts[2]));
}
function constantTimeEqual(expected, received) { if (expected.length !== received.length) return false; let difference = 0; for (let i = 0; i < expected.length; i++) difference |= expected[i] ^ received[i]; return difference === 0; }
async function hmacSha256(secret, text) { const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]); return new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(text))); }
function fromBase64Url(value) { const normalized = value.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((value.length + 3) % 4); return Uint8Array.from(atob(normalized), char => char.charCodeAt(0)); }

async function listUsers(db, url) {
  const search = String(url.searchParams.get("search") || "").trim().slice(0, 100);
  const like = `%${search.replace(/[\\%_]/g, value => `\\${value}`)}%`;
  const result = await db.prepare(`SELECT u.id,u.username,u.email,u.email_verified_at,u.status,u.created_at,u.last_login_at,u.referral_code,r.username AS referred_by_username,COUNT(DISTINCT s.quiz_id) AS quizzes_completed,COUNT(DISTINCT f.id) AS friend_count FROM site_users u LEFT JOIN site_users r ON r.id=u.referred_by_user_id LEFT JOIN site_user_scores s ON s.user_id=u.id LEFT JOIN site_friendships f ON (f.user_a_id=u.id OR f.user_b_id=u.id) AND f.status='accepted' WHERE u.username LIKE ? ESCAPE '\\' COLLATE NOCASE OR u.email LIKE ? ESCAPE '\\' COLLATE NOCASE OR COALESCE(u.referral_code,'') LIKE ? ESCAPE '\\' COLLATE NOCASE GROUP BY u.id ORDER BY u.created_at DESC LIMIT 500`).bind(like, like, like).all();
  const summary = await db.prepare(`SELECT COUNT(*) total,SUM(CASE WHEN email_verified_at IS NOT NULL THEN 1 ELSE 0 END) verified,SUM(CASE WHEN email_verified_at IS NULL THEN 1 ELSE 0 END) unverified FROM site_users`).first();
  return json({ users:(result.results||[]).map(mapUser), summary:{total:Number(summary?.total||0),verified:Number(summary?.verified||0),unverified:Number(summary?.unverified||0)} });
}
async function detailUser(db,userId){
  const user=await db.prepare(`SELECT u.*,r.username AS referred_by_username FROM site_users u LEFT JOIN site_users r ON r.id=u.referred_by_user_id WHERE u.id=? LIMIT 1`).bind(userId).first();
  if(!user)return json({error:"User not found."},404);
  const friends=await db.prepare(`SELECT other.id,other.username,other.email,other.email_verified_at,f.status,f.created_at FROM site_friendships f JOIN site_users other ON other.id=CASE WHEN f.user_a_id=? THEN f.user_b_id ELSE f.user_a_id END WHERE (f.user_a_id=? OR f.user_b_id=?) ORDER BY other.username COLLATE NOCASE ASC`).bind(userId,userId,userId).all();
  const referrals=await db.prepare(`SELECT id,username,email,email_verified_at,created_at FROM site_users WHERE referred_by_user_id=? ORDER BY created_at DESC`).bind(userId).all();
  return json({user:mapUser(user),friends:(friends.results||[]).map(r=>({id:Number(r.id),username:String(r.username||""),email:String(r.email||""),email_verified:Boolean(r.email_verified_at),status:String(r.status||"pending"),created_at:String(r.created_at||"")})),referrals:(referrals.results||[]).map(r=>({id:Number(r.id),username:String(r.username||""),email:String(r.email||""),email_verified:Boolean(r.email_verified_at),created_at:String(r.created_at||"")}))});
}
async function updateUser(request,env,userId){
  const existing=await env.DB.prepare("SELECT id,username_key,email_key FROM site_users WHERE id=? LIMIT 1").bind(userId).first();
  if(!existing)return json({error:"User not found."},404);
  let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}
  const username=normalizeUsername(body?.username),email=normalizeEmail(body?.email),password=String(body?.password||"");
  if(!username)return json({error:"Choose a valid username."},400); if(!email)return json({error:"Enter a valid email address."},400); if(password&&(password.length<10||password.length>128))return json({error:"Password must be 10–128 characters."},400);
  const usernameKey=username.toLowerCase(),emailKey=email.toLowerCase();
  const duplicate=await env.DB.prepare("SELECT id FROM site_users WHERE id<>? AND (username_key=? OR email_key=?) LIMIT 1").bind(userId,usernameKey,emailKey).first(); if(duplicate)return json({error:"That username or email address is already in use."},409);
  const emailChanged=emailKey!==String(existing.email_key||"").toLowerCase(); const statements=[]; let passwordChanged=false;
  if(password){const pepper=String(env.PASSWORD_PEPPER||"").trim();if(pepper.length<32)return json({error:"Password security is not configured on the site."},503);const salt=randomToken(18);const hash=await derivePasswordHash(password,salt,pepper,PASSWORD_POLICY.iterations);statements.push(env.DB.prepare(`UPDATE site_users SET username=?,username_key=?,email=?,email_key=?,email_verified_at=CASE WHEN ? THEN NULL ELSE email_verified_at END,password_hash=?,password_salt=?,password_iterations=?,password_scheme=? WHERE id=?`).bind(username,usernameKey,email,emailKey,emailChanged?1:0,hash,salt,PASSWORD_POLICY.iterations,PASSWORD_POLICY.scheme,userId));passwordChanged=true;}else statements.push(env.DB.prepare(`UPDATE site_users SET username=?,username_key=?,email=?,email_key=?,email_verified_at=CASE WHEN ? THEN NULL ELSE email_verified_at END WHERE id=?`).bind(username,usernameKey,email,emailKey,emailChanged?1:0,userId));
  if(emailChanged)statements.push(env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id=?").bind(userId)); if(passwordChanged)statements.push(env.DB.prepare("DELETE FROM site_sessions WHERE user_id=?").bind(userId)); await env.DB.batch(statements); return detailUser(env.DB,userId);
}
async function ensureReferralSchema(db){
  const columns=await db.prepare("PRAGMA table_info(site_users)").all();const names=new Set((columns.results||[]).map(c=>String(c.name||"")));
  if(!names.has("referral_code"))await db.prepare("ALTER TABLE site_users ADD COLUMN referral_code TEXT").run(); if(!names.has("referred_by_user_id"))await db.prepare("ALTER TABLE site_users ADD COLUMN referred_by_user_id INTEGER").run(); if(!names.has("referred_at"))await db.prepare("ALTER TABLE site_users ADD COLUMN referred_at TEXT").run();
  await db.prepare("CREATE UNIQUE INDEX IF NOT EXISTS idx_site_users_referral_code ON site_users(referral_code) WHERE referral_code IS NOT NULL AND referral_code <> ''").run();
  const missing=await db.prepare("SELECT id FROM site_users WHERE referral_code IS NULL OR referral_code='' LIMIT 500").all();
  for(const row of missing.results||[]){let code="";for(let i=0;i<8&&!code;i++){const candidate=`FB-${randomToken(6).replace(/[^A-Za-z0-9]/g,"").slice(0,8).toUpperCase()}`;if(!await db.prepare("SELECT id FROM site_users WHERE referral_code=? LIMIT 1").bind(candidate).first())code=candidate;}if(code)await db.prepare("UPDATE site_users SET referral_code=? WHERE id=? AND (referral_code IS NULL OR referral_code='')").bind(code,row.id).run();}
}
function mapUser(row){return{id:Number(row.id||0),username:String(row.username||""),email:String(row.email||""),email_verified:Boolean(row.email_verified_at),email_verified_at:row.email_verified_at?String(row.email_verified_at):null,status:String(row.status||"active"),created_at:String(row.created_at||""),last_login_at:String(row.last_login_at||""),referral_code:String(row.referral_code||""),referred_by_username:row.referred_by_username?String(row.referred_by_username):null,friend_count:Number(row.friend_count||0),quizzes_completed:Number(row.quizzes_completed||0)}}
function randomToken(n){const b=new Uint8Array(n);crypto.getRandomValues(b);let s="";for(const x of b)s+=String.fromCharCode(x);return btoa(s).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/g,"")}
function json(v,s=200){return new Response(JSON.stringify(v),{status:s,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff"}})}
