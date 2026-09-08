const JSON_HEADERS={"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff"};
const GENERATION_KEY="quiz_generation_settings";
const DEFAULT_GENERATION={enabled:false,frequency:"daily",time_utc:"06:00",quizzes_per_run:1,categories:[],auto_publish:false};

export async function handleAdminPhase1Api(request,env,url){
  if(!url.pathname.startsWith("/api/admin/phase1/"))return null;
  if(!env.DB)return json({error:"Database unavailable."},503);
  const auth=await requireAdmin(request,env);
  if(!auth.ok)return auth.response;
  try{
    if(url.pathname==="/api/admin/phase1/analytics"&&request.method==="GET")return analytics(env.DB,url);
    if(url.pathname==="/api/admin/phase1/generation/settings"&&request.method==="GET")return generationSettings(env.DB);
    if(url.pathname==="/api/admin/phase1/generation/settings"&&request.method==="PATCH")return saveGenerationSettings(request,env.DB);
    if(url.pathname==="/api/admin/phase1/generation/queue"&&request.method==="POST")return queueGeneration(request,env.DB);
    if(url.pathname==="/api/admin/phase1/generation/jobs"&&request.method==="GET")return generationJobs(env.DB,url);
    return json({error:"Phase 1 admin endpoint not found."},404);
  }catch(error){console.error("Factburst Phase 1 admin API failed",error);return json({error:"The Phase 1 admin request could not be completed."},500);}
}

async function analytics(db,url){
  await db.batch([db.prepare(`CREATE TABLE IF NOT EXISTS site_analytics_daily(day TEXT NOT NULL,event_name TEXT NOT NULL,quiz_slug TEXT NOT NULL DEFAULT '',source TEXT NOT NULL DEFAULT '',count INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(day,event_name,quiz_slug,source))`),db.prepare("CREATE INDEX IF NOT EXISTS idx_site_analytics_daily_event ON site_analytics_daily(event_name,day)"),db.prepare("CREATE INDEX IF NOT EXISTS idx_site_analytics_daily_quiz ON site_analytics_daily(quiz_slug,day)")]);
  const days=Math.min(Math.max(Number(url.searchParams.get("days")||30),1),90);
  const since=new Date(Date.now()-days*86400000).toISOString().slice(0,10);
  const events=await db.prepare("SELECT event_name,SUM(count) AS count FROM site_analytics_daily WHERE day>=? GROUP BY event_name ORDER BY count DESC").bind(since).all();
  const quizzes=await db.prepare("SELECT quiz_slug,SUM(count) AS count FROM site_analytics_daily WHERE day>=? AND quiz_slug<>'' GROUP BY quiz_slug ORDER BY count DESC LIMIT 10").bind(since).all();
  const sources=await db.prepare("SELECT source,SUM(count) AS count FROM site_analytics_daily WHERE day>=? AND source<>'' GROUP BY source ORDER BY count DESC LIMIT 10").bind(since).all();
  return json({days,since,events:events.results||[],top_quizzes:quizzes.results||[],sources:sources.results||[]});
}

async function generationSettings(db){await ensureGenerationSchema(db);const row=await db.prepare("SELECT value FROM site_settings WHERE key=? LIMIT 1").bind(GENERATION_KEY).first();let settings={...DEFAULT_GENERATION};if(row?.value){try{settings={...settings,...JSON.parse(String(row.value))};}catch{}}return json({settings});}
async function saveGenerationSettings(request,db){await ensureGenerationSchema(db);const body=await request.json().catch(()=>({}));const frequency=["hourly","daily","weekly"].includes(String(body?.frequency||""))?String(body.frequency):"daily";const time=/^([01]\d|2[0-3]):[0-5]\d$/.test(String(body?.time_utc||""))?String(body.time_utc):"06:00";const count=Math.min(10,Math.max(1,Number.parseInt(body?.quizzes_per_run,10)||1));const categories=Array.isArray(body?.categories)?body.categories.map(v=>String(v||"").trim().slice(0,80)).filter(Boolean).slice(0,20):String(body?.categories||"").split(",").map(v=>v.trim()).filter(Boolean).slice(0,20);const settings={enabled:body?.enabled===true,frequency,time_utc:time,quizzes_per_run:count,categories,auto_publish:body?.auto_publish===true};await db.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES(?,?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at").bind(GENERATION_KEY,JSON.stringify(settings),new Date().toISOString()).run();return json({settings});}
async function queueGeneration(request,db){await ensureGenerationSchema(db);const body=await request.json().catch(()=>({}));const count=Math.min(10,Math.max(1,Number.parseInt(body?.count,10)||1));const category=String(body?.category||"").trim().slice(0,80);const autoPublish=body?.auto_publish===true?1:0;const now=new Date().toISOString();const rows=[];for(let i=0;i<count;i++)rows.push(db.prepare("INSERT INTO site_generation_jobs(status,requested_category,questions_per_quiz,auto_publish,attempts,created_at) VALUES('queued',?,?,?,0,?)").bind(category,10,autoPublish,now));await db.batch(rows);return generationJobs(db,new URL("https://admin.local/api/admin/phase1/generation/jobs?limit=20"));}
async function generationJobs(db,url){await ensureGenerationSchema(db);const limit=Math.min(Math.max(Number(url.searchParams.get("limit")||20),1),100);const result=await db.prepare("SELECT id,status,requested_category,questions_per_quiz,auto_publish,attempts,created_at,claimed_at,completed_at,worker_id,result_summary,error_message FROM site_generation_jobs ORDER BY id DESC LIMIT ?").bind(limit).all();return json({jobs:result.results||[]});}
async function ensureGenerationSchema(db){await db.batch([db.prepare(`CREATE TABLE IF NOT EXISTS site_generation_jobs(id INTEGER PRIMARY KEY AUTOINCREMENT,status TEXT NOT NULL DEFAULT 'queued',requested_category TEXT NOT NULL DEFAULT '',questions_per_quiz INTEGER NOT NULL DEFAULT 10,auto_publish INTEGER NOT NULL DEFAULT 0,attempts INTEGER NOT NULL DEFAULT 0,created_at TEXT NOT NULL,claimed_at TEXT,completed_at TEXT,worker_id TEXT NOT NULL DEFAULT '',result_summary TEXT NOT NULL DEFAULT '',error_message TEXT NOT NULL DEFAULT '')`),db.prepare("CREATE INDEX IF NOT EXISTS idx_site_generation_jobs_status ON site_generation_jobs(status,created_at)")]);}
async function requireAdmin(request,env){const expected=String(env.SITE_ADMIN_KEY||"").trim();const supplied=String(request.headers.get("authorization")||"").replace(/^Bearer\s+/i,"").trim();if(expected&&supplied===expected)return{ok:true};const cookie=String(request.headers.get("cookie")||"").match(/(?:^|;\s*)fb_admin_session=([^;]+)/)?.[1]||"";if(cookie&&env.DB){const hash=await sha256(cookie);const row=await env.DB.prepare("SELECT expires_at FROM site_admin_sessions WHERE token_hash=? LIMIT 1").bind(hash).first().catch(()=>null);if(row?.expires_at&&new Date(row.expires_at).getTime()>Date.now())return{ok:true};}return{ok:false,response:json({error:"Administrator authentication required."},401)};}
async function sha256(value){const digest=await crypto.subtle.digest("SHA-256",new TextEncoder().encode(value));return[...new Uint8Array(digest)].map(b=>b.toString(16).padStart(2,"0")).join("");}
function json(value,status=200){return new Response(JSON.stringify(value),{status,headers:JSON_HEADERS});}
