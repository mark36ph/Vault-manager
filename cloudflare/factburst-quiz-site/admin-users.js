import { derivePasswordHash, PASSWORD_POLICY } from "./account-auth.js";

const MAX_TOKEN_LENGTH = 200;
const ADMIN_SESSION_COOKIE = "fb_admin_session";
const ADMIN_SESSION_SECONDS = 7 * 24 * 60 * 60;
const DEFAULT_MAINTENANCE_MESSAGE = "Factburst Quiz is currently undergoing maintenance. Please check back shortly.";
const GENERATION_SETTINGS_KEY = "quiz_generation_settings";
const DEFAULT_GENERATION_SETTINGS = { enabled: false, frequency: "daily", time_utc: "06:00", quizzes_per_run: 1, categories: [], auto_publish: false };

export async function handleAdminUsersApi(request, env, url) {
  if (!url.pathname.startsWith("/api/admin/users")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  const auth = await requireAdmin(request, env); if (!auth.ok) return auth.response;
  try {
    if (url.pathname === "/api/admin/users/site-settings") {
      if (request.method === "GET") return getSiteSettings(env.DB);
      if (request.method === "PATCH") return updateSiteSettings(request, env.DB);
      return json({ error: "Method not allowed." },405);
    }
    if (url.pathname === "/api/admin/users/generation") {
      if (request.method === "GET") return getGeneration(env.DB);
      if (request.method === "PATCH") return updateGeneration(request, env.DB);
      return json({ error: "Method not allowed." },405);
    }
    if (url.pathname === "/api/admin/users/generation/run" && request.method === "POST") return queueGeneration(request, env.DB);
    const parts=url.pathname.split("/").filter(Boolean); const userId=parts.length>=4?Number(parts[3]):0;
    if(request.method==="GET"&&!userId)return listUsers(env.DB,url);
    if(request.method==="GET"&&userId)return getUser(env.DB,userId);
    if(request.method==="POST"&&!userId)return createUser(request,env);
    if(request.method==="PATCH"&&userId)return updateUser(request,env,userId);
    if(request.method==="DELETE"&&userId&&parts[4]==="friendship")return unlinkFriend(request,env.DB,userId);
    if(request.method==="POST"&&userId&&parts[4]==="verify")return verifyUser(env.DB,userId);
    return json({error:"Admin user endpoint not found."},404);
  } catch(error){console.error("Factburst admin user management failed",error);return json({error:"The user management request could not be completed."},500);}
}

async function ensureGenerationSchema(db){
  await db.batch([
    db.prepare(`CREATE TABLE IF NOT EXISTS site_generation_jobs (id INTEGER PRIMARY KEY AUTOINCREMENT, status TEXT NOT NULL DEFAULT 'queued', requested_category TEXT NOT NULL DEFAULT '', questions_per_quiz INTEGER NOT NULL DEFAULT 10, auto_publish INTEGER NOT NULL DEFAULT 0, attempts INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, claimed_at TEXT, completed_at TEXT, worker_id TEXT NOT NULL DEFAULT '', result_summary TEXT NOT NULL DEFAULT '', error_message TEXT NOT NULL DEFAULT '')`),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_generation_jobs_status ON site_generation_jobs(status, created_at)"),
  ]);
}

async function getGeneration(db){
  await ensureGenerationSchema(db);
  const row=await db.prepare("SELECT value,updated_at FROM site_settings WHERE key=? LIMIT 1").bind(GENERATION_SETTINGS_KEY).first();
  let settings={...DEFAULT_GENERATION_SETTINGS};
  if(row?.value){try{const parsed=JSON.parse(String(row.value));settings={...settings,...parsed};}catch{}}
  const result=await db.prepare("SELECT id,status,requested_category,questions_per_quiz,auto_publish,attempts,created_at,claimed_at,completed_at,result_summary,error_message FROM site_generation_jobs ORDER BY id DESC LIMIT 20").all();
  return json({settings,jobs:result.results||[]});
}

async function updateGeneration(request,db){
  let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}
  const frequency=["hourly","daily","weekly"].includes(String(body?.frequency||""))?String(body.frequency):"daily";
  const time=/^([01]\d|2[0-3]):[0-5]\d$/.test(String(body?.time_utc||""))?String(body.time_utc):"06:00";
  const count=Math.min(10,Math.max(1,Number.parseInt(body?.quizzes_per_run||1,10)));
  const categories=Array.isArray(body?.categories)?body.categories.map(value=>String(value||"").trim()).filter(Boolean).slice(0,30):[];
  const settings={enabled:body?.enabled===true,frequency,time_utc:time,quizzes_per_run:Number.isFinite(count)?count:1,categories,auto_publish:body?.auto_publish===true};
  const now=new Date().toISOString();
  await db.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES (?,?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at").bind(GENERATION_SETTINGS_KEY,JSON.stringify(settings),now).run();
  return getGeneration(db);
}

async function queueGeneration(request,db){
  await ensureGenerationSchema(db);
  let body;try{body=await request.json();}catch{body={};}
  const categories=Array.isArray(body?.categories)?body.categories.map(value=>String(value||"").trim()).filter(Boolean).slice(0,30):[];
  const count=Math.min(10,Math.max(1,Number.parseInt(body?.quizzes_per_run||1,10)));
  const autoPublish=body?.auto_publish===true?1:0;
  const now=new Date().toISOString();
  const statements=[];
  for(let i=0;i<count;i++){
    const requestedCategory=categories.length?categories[i%categories.length]:"";
    statements.push(db.prepare("INSERT INTO site_generation_jobs(status,requested_category,questions_per_quiz,auto_publish,attempts,created_at) VALUES ('queued',?,?,?,0,?)").bind(requestedCategory,10,autoPublish,now));
  }
  await db.batch(statements);
  return json({ok:true,queued:count});
}

async function getSiteSettings(db){const rows=await db.prepare("SELECT key,value FROM site_settings WHERE key IN ('maintenance_enabled','maintenance_message')").all();const values=Object.fromEntries((rows.results||[]).map(row=>[String(row.key),String(row.value||"")]));return json({maintenance_enabled:values.maintenance_enabled==="1",maintenance_message:values.maintenance_message||DEFAULT_MAINTENANCE_MESSAGE});}
async function updateSiteSettings(request,db){let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}const enabled=body?.maintenance_enabled===true;const message=String(body?.maintenance_message||"").trim().slice(0,500)||DEFAULT_MAINTENANCE_MESSAGE;const now=new Date().toISOString();await db.batch([db.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES ('maintenance_enabled',?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at").bind(enabled?"1":"0",now),db.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES ('maintenance_message',?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at").bind(message,now)]);return getSiteSettings(db);}
async function listUsers(db,url){const search=String(url.searchParams.get("search")||"").trim();const limit=Math.min(Math.max(Number(url.searchParams.get("limit")||50),1),100);const pattern=`%${search.replace(/[%_]/g,"\\$&")}%`;const where=search?"WHERE u.username LIKE ? ESCAPE '\\\\' OR u.email LIKE ? ESCAPE '\\\\' OR u.referral_code LIKE ? ESCAPE '\\\\'":"";const bindings=search?[pattern,pattern,pattern,limit]:[limit];const result=await db.prepare(`SELECT u.id,u.username,u.email,u.email_verified_at,u.status,u.created_at,u.referral_code,(SELECT COUNT(*) FROM site_friendships f WHERE (f.user_a_id=u.id OR f.user_b_id=u.id) AND f.status='accepted') AS friends_count,(SELECT r.username FROM site_users r WHERE r.id=u.referred_by_user_id LIMIT 1) AS referred_by FROM site_users u ${where} ORDER BY u.created_at DESC,u.id DESC LIMIT ?`).bind(...bindings).all();return json({users:(result.results||[]).map(publicUser)});}
async function createUser(request,env){let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}const username=normalizeUsername(body?.username);const email=normalizeEmail(body?.email);const password=String(body?.password||"");const verifyEmail=body?.verify_email===true||body?.verify_email==="true";if(!username)return json({error:"Choose a username 3–24 characters long."},400);if(!email)return json({error:"Enter a valid email address."},400);if(password.length<10||password.length>128)return json({error:"Password must be between 10 and 128 characters."},400);const usernameKey=username.toLowerCase();const emailKey=email.toLowerCase();const duplicate=await env.DB.prepare("SELECT id FROM site_users WHERE username_key=? OR email_key=? LIMIT 1").bind(usernameKey,emailKey).first();if(duplicate)return json({error:"That username or email is already in use."},409);const pepper=String(env.PASSWORD_PEPPER||"").trim();if(pepper.length<32)return json({error:"Password security is not configured."},503);const salt=randomToken(18);const hash=await derivePasswordHash(password,salt,pepper,PASSWORD_POLICY.iterations);const now=new Date().toISOString();const referralCode=await uniqueReferralCode(env.DB);const verifiedAt=verifyEmail?now:null;const result=await env.DB.prepare(`INSERT INTO site_users (username,username_key,email,email_key,email_verified_at,password_hash,password_salt,password_iterations,password_scheme,status,referral_code,created_at,last_login_at) VALUES (?,?,?,?,?,?,?,?,?,'active',?,?,?)`).bind(username,usernameKey,email,emailKey,verifiedAt,hash,salt,PASSWORD_POLICY.iterations,PASSWORD_POLICY.scheme,referralCode,now,now).run();const userId=Number(result.meta?.last_row_id||0);if(!userId)return json({error:"The user could not be created."},500);return getUser(env.DB,userId);}
async function uniqueReferralCode(db){for(let attempt=0;attempt<10;attempt++){const code=`FB-${randomCode(8)}`;const exists=await db.prepare("SELECT id FROM site_users WHERE referral_code=? LIMIT 1").bind(code).first();if(!exists)return code;}throw new Error("Could not generate a unique referral code.");}
async function getUser(db,userId){const user=await db.prepare(`SELECT u.id,u.username,u.email,u.email_verified_at,u.status,u.suspended_at,u.suspension_reason,u.created_at,u.last_login_at,u.referral_code,(SELECT r.username FROM site_users r WHERE r.id=u.referred_by_user_id LIMIT 1) AS referred_by FROM site_users u WHERE u.id=? LIMIT 1`).bind(userId).first();if(!user)return json({error:"User not found."},404);const friends=await db.prepare(`SELECT f.id,f.status,f.created_at,CASE WHEN f.user_a_id=? THEN f.user_b_id ELSE f.user_a_id END AS friend_id,CASE WHEN f.user_a_id=? THEN b.username ELSE a.username END AS friend_username,CASE WHEN f.user_a_id=? THEN b.email ELSE a.email END AS friend_email FROM site_friendships f JOIN site_users a ON a.id=f.user_a_id JOIN site_users b ON b.id=f.user_b_id WHERE f.user_a_id=? OR f.user_b_id=? ORDER BY f.created_at DESC`).bind(userId,userId,userId,userId,userId).all();const referred=await db.prepare("SELECT id,username,email,created_at,email_verified_at,status FROM site_users WHERE referred_by_user_id=? ORDER BY created_at DESC").bind(userId).all();return json({user:publicUser(user),friends:friends.results||[],referred_users:referred.results||[]});}
async function updateUser(request,env,userId){let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}const existing=await env.DB.prepare("SELECT id,username_key,email_key,email_verified_at FROM site_users WHERE id=? LIMIT 1").bind(userId).first();if(!existing)return json({error:"User not found."},404);const username=normalizeUsername(body?.username);const email=normalizeEmail(body?.email);const password=String(body?.password||"");if(!username)return json({error:"Choose a username 3–24 characters long."},400);if(!email)return json({error:"Enter a valid email address."},400);if(password&&(password.length<10||password.length>128))return json({error:"Password must be between 10 and 128 characters."},400);const usernameKey=username.toLowerCase();const emailKey=email.toLowerCase();const duplicate=await env.DB.prepare("SELECT id FROM site_users WHERE id<>? AND (username_key=? OR email_key=?) LIMIT 1").bind(userId,usernameKey,emailKey).first();if(duplicate)return json({error:"That username or email is already in use."},409);const emailChanged=emailKey!==String(existing.email_key||"").toLowerCase();const statements=[];if(password){const pepper=String(env.PASSWORD_PEPPER||"").trim();if(pepper.length<32)return json({error:"Password security is not configured."},503);const salt=randomToken(18);const hash=await derivePasswordHash(password,salt,pepper,PASSWORD_POLICY.iterations);statements.push(env.DB.prepare(`UPDATE site_users SET username=?,username_key=?,email=?,email_key=?,email_verified_at=CASE WHEN ? THEN NULL ELSE email_verified_at END,password_hash=?,password_salt=?,password_iterations=?,password_scheme=? WHERE id=?`).bind(username,usernameKey,email,emailKey,emailChanged?1:0,hash,salt,PASSWORD_POLICY.iterations,PASSWORD_POLICY.scheme,userId));statements.push(env.DB.prepare("DELETE FROM site_sessions WHERE user_id=?").bind(userId));}else statements.push(env.DB.prepare(`UPDATE site_users SET username=?,username_key=?,email=?,email_key=?,email_verified_at=CASE WHEN ? THEN NULL ELSE email_verified_at END WHERE id=?`).bind(username,usernameKey,email,emailKey,emailChanged?1:0,userId));if(emailChanged)statements.push(env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id=?").bind(userId));await env.DB.batch(statements);return getUser(env.DB,userId);}
async function verifyUser(db,userId){const exists=await db.prepare("SELECT id FROM site_users WHERE id=? LIMIT 1").bind(userId).first();if(!exists)return json({error:"User not found."},404);const now=new Date().toISOString();await db.prepare("UPDATE site_users SET email_verified_at=? WHERE id=?").bind(now,userId).run();await db.prepare("DELETE FROM site_email_verifications WHERE user_id=?").bind(userId).run();return getUser(db,userId);}
async function unlinkFriend(request,db,userId){let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}const friendshipId=Number(body?.friendship_id||0);if(!friendshipId)return json({error:"Friendship id is required."},400);const result=await db.prepare("DELETE FROM site_friendships WHERE id=? AND (user_a_id=? OR user_b_id=?)").bind(friendshipId,userId,userId).run();if(!result.meta?.changes)return json({error:"Friendship not found."},404);return json({unlinked:true});}
function publicUser(user){return{id:Number(user.id),username:String(user.username||""),email:String(user.email||""),email_verified:Boolean(user.email_verified_at),email_verified_at:user.email_verified_at||null,status:String(user.status||"active"),created_at:user.created_at||null,last_login_at:user.last_login_at||null,referral_code:String(user.referral_code||""),referred_by:user.referred_by||null,friends_count:Number(user.friends_count||0),suspended_at:user.suspended_at||null,suspension_reason:String(user.suspension_reason||"")};}
function normalizeUsername(value){const v=String(value||"").trim().replace(/\s+/g," ");return v.length>=3&&v.length<=24&&/^[A-Za-z0-9][A-Za-z0-9 _.-]*[A-Za-z0-9]$/.test(v)?v:"";}
function normalizeEmail(value){const v=String(value||"").trim();return v&&v.length<=254&&!/\s/.test(v)&&/^[^@]+@[^@]+\.[^@]+$/.test(v)?v:"";}
function randomToken(n){const b=new Uint8Array(n);crypto.getRandomValues(b);let s="";for(const x of b)s+=String.fromCharCode(x);return btoa(s).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/g,"");}
function randomCode(length){const chars="ABCDEFGHJKLMNPQRSTUVWXYZ23456789";const bytes=new Uint8Array(length);crypto.getRandomValues(bytes);return [...bytes].map(b=>chars[b%chars.length]).join("");}
async function requireAdmin(request,env){const expected=String(env.SITE_ADMIN_KEY||"").trim();if(!expected)return{ok:false,response:json({error:"Website publishing is not enabled yet."},503)};const cookie=String(request.headers.get("cookie")||"");const match=cookie.split(";").map(value=>value.trim()).find(value=>value.startsWith(`${ADMIN_SESSION_COOKIE}=`));if(match){const token=match.slice(`${ADMIN_SESSION_COOKIE}=`.length);if(await verifyAdminSessionToken(token,expected))return{ok:true};}const key=String(request.headers.get("authorization")||"").replace(/^Bearer\s+/i,"").trim();if(key&&key===expected)return{ok:true};return{ok:false,response:json({error:"Administrator access required."},401)};}
async function verifyAdminSessionToken(token,siteKey){if(!token||token.length>MAX_TOKEN_LENGTH)return false;const parts=token.split(".");if(parts.length!==3)return false;const timestamp=Number(parts[0]);if(!Number.isInteger(timestamp))return false;const now=Date.now()/1000;if(now-timestamp>ADMIN_SESSION_SECONDS||timestamp-now>60)return false;const expected=await hmacSha256(siteKey,`factburst-admin-session:${parts[0]}.${parts[1]}`);let received;try{received=fromBase64Url(parts[2]);}catch{return false;}if(expected.length!==received.length)return false;let difference=0;for(let i=0;i<expected.length;i++)difference|=expected[i]^received[i];return difference===0;}
async function hmacSha256(secret,text){const key=await crypto.subtle.importKey("raw",new TextEncoder().encode(secret),{name:"HMAC",hash:"SHA-256"},false,["sign"]);return new Uint8Array(await crypto.subtle.sign("HMAC",key,new TextEncoder().encode(text)));}
function fromBase64Url(value){const normalized=value.replace(/-/g,"+").replace(/_/g,"/")+"===".slice((value.length+3)%4);const binary=atob(normalized);return Uint8Array.from(binary,char=>char.charCodeAt(0));}
function json(value,status=200){return new Response(JSON.stringify(value),{status,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff"}})}
