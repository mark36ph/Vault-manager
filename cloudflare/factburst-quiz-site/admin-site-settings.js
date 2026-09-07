const ADMIN_SESSION_COOKIE = "fb_admin_session";
const ADMIN_SESSION_SECONDS = 7 * 24 * 60 * 60;

export async function handleAdminSiteSettingsApi(request, env, url) {
  if (!url.pathname.startsWith("/api/admin/site-settings")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  const auth = await requireAdmin(request, env);
  if (!auth.ok) return auth.response;
  await ensureSettings(env.DB);
  if (request.method === "GET") return getSettings(env.DB);
  if (request.method === "PATCH") return updateSettings(request, env.DB);
  return json({ error: "Method not allowed." }, 405);
}
async function ensureSettings(db) { await db.prepare(`CREATE TABLE IF NOT EXISTS site_settings (key TEXT PRIMARY KEY,value TEXT NOT NULL DEFAULT '',updated_at TEXT NOT NULL)`).run(); }
async function getSettings(db) {
  const rows = await db.prepare("SELECT key,value FROM site_settings WHERE key IN ('maintenance_enabled','maintenance_message')").all();
  const values = Object.fromEntries((rows.results || []).map(row => [String(row.key), String(row.value || "")]));
  return json({ maintenance_enabled: values.maintenance_enabled === "1", maintenance_message: values.maintenance_message || "Factburst Quiz is currently undergoing maintenance. Please check back shortly." });
}
async function updateSettings(request, db) {
  let body; try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." },400); }
  const enabled = body?.maintenance_enabled === true;
  const message = String(body?.maintenance_message || "").trim().slice(0,500) || "Factburst Quiz is currently undergoing maintenance. Please check back shortly.";
  const now = new Date().toISOString();
  await db.batch([
    db.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES ('maintenance_enabled',?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at").bind(enabled?"1":"0",now),
    db.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES ('maintenance_message',?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at").bind(message,now)
  ]);
  return getSettings(db);
}
async function requireAdmin(request,env) {
  const siteKey=String(env.SITE_ADMIN_KEY||"").trim(); if(!siteKey)return {ok:false,response:json({error:"Website publishing is not enabled yet."},503)};
  const cookie=String(request.headers.get("cookie")||""); const match=cookie.split(";").map(v=>v.trim()).find(v=>v.startsWith(`${ADMIN_SESSION_COOKIE}=`));
  if(match&&await verifySession(match.slice(ADMIN_SESSION_COOKIE.length+1),siteKey))return {ok:true};
  const key=String(request.headers.get("authorization")||"").replace(/^Bearer\s+/i,"").trim(); return key===siteKey?{ok:true}:{ok:false,response:json({error:"Administrator access required."},401)};
}
async function verifySession(token,siteKey) {
  const parts=token.split("."); if(parts.length!==3)return false; const timestamp=Number(parts[0]); const now=Date.now()/1000;
  if(!Number.isInteger(timestamp)||now-timestamp>ADMIN_SESSION_SECONDS||timestamp-now>60)return false;
  const key=await crypto.subtle.importKey("raw",new TextEncoder().encode(siteKey),{name:"HMAC",hash:"SHA-256"},false,["sign"]);
  const expected=new Uint8Array(await crypto.subtle.sign("HMAC",key,new TextEncoder().encode(`factburst-admin-session:${parts[0]}.${parts[1]}`)));
  let received; try{received=fromBase64Url(parts[2]);}catch{return false;} if(expected.length!==received.length)return false; let diff=0; for(let i=0;i<expected.length;i++)diff|=expected[i]^received[i]; return diff===0;
}
function fromBase64Url(v){const s=v.replace(/-/g,"+").replace(/_/g,"/")+"===".slice((v.length+3)%4);return Uint8Array.from(atob(s),c=>c.charCodeAt(0));}
function json(value,status=200){return new Response(JSON.stringify(value),{status,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff"}})}
