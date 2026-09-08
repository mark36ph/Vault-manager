const SOCIAL_PLATFORMS = ["youtube", "facebook", "instagram"];
const SOCIAL_STATUSES = ["ready", "scheduled", "published", "failed", "paused"];

export async function handleAdminSocialApi(request, env, url) {
  if (!url.pathname.startsWith("/api/admin/social")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  const auth = await requireAdmin(request, env);
  if (!auth.ok) return auth.response;
  try {
    await ensureSocialSchema(env.DB);
    const parts = url.pathname.split("/").filter(Boolean);
    const slug = parts[3] ? decodeURIComponent(parts[3]).toLowerCase() : "";
    if (request.method === "GET" && !slug) return listSocial(env.DB, url);
    if (!slug) return json({ error: "Quiz slug is required." }, 400);
    if (request.method === "GET") return getSocial(env.DB, slug);
    if (request.method === "PATCH") return updateSocial(request, env.DB, slug);
    if (request.method === "POST" && parts[4] === "queue") return queueSocial(request, env.DB, slug);
    if (request.method === "POST" && parts[4] === "retry") return retrySocial(env.DB, slug, parts[5]);
    return json({ error: "Social endpoint not found." }, 404);
  } catch (error) {
    console.error("Factburst admin social management failed", error);
    return json({ error: "The social management request could not be completed." }, 500);
  }
}

async function ensureSocialSchema(db) {
  await db.batch([
    db.prepare(`CREATE TABLE IF NOT EXISTS site_social_packages (
      id INTEGER PRIMARY KEY AUTOINCREMENT, quiz_id INTEGER NOT NULL UNIQUE,
      media_url TEXT NOT NULL DEFAULT '', title TEXT NOT NULL DEFAULT '',
      description TEXT NOT NULL DEFAULT '', caption TEXT NOT NULL DEFAULT '',
      hashtags TEXT NOT NULL DEFAULT '', pinned_comment TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE)`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_social_jobs (
      id INTEGER PRIMARY KEY AUTOINCREMENT, quiz_id INTEGER NOT NULL, platform TEXT NOT NULL,
      status TEXT NOT NULL DEFAULT 'ready', scheduled_at TEXT, published_at TEXT,
      published_url TEXT NOT NULL DEFAULT '', error_message TEXT NOT NULL DEFAULT '',
      attempts INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
      UNIQUE(quiz_id, platform), FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE)`),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_social_jobs_status ON site_social_jobs(status, scheduled_at)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_social_jobs_quiz ON site_social_jobs(quiz_id, platform)"),
  ]);
}

async function listSocial(db, url) {
  const search = String(url.searchParams.get("search") || "").trim();
  const category = String(url.searchParams.get("category") || "").trim();
  const limit = Math.min(Math.max(Number(url.searchParams.get("limit") || 50), 1), 100);
  const conditions = [];
  const bindings = [];
  if (search) { const p = `%${search.replace(/[%_]/g, "\\$&")}%`; conditions.push("(q.title LIKE ? ESCAPE '\\\\' OR q.slug LIKE ? ESCAPE '\\\\' OR q.category LIKE ? ESCAPE '\\\\')"); bindings.push(p,p,p); }
  if (category) { conditions.push("q.category=?"); bindings.push(category); }
  bindings.push(limit);
  const where = conditions.length ? `WHERE ${conditions.join(" AND ")}` : "";
  const result = await db.prepare(`SELECT q.id,q.slug,q.title,q.category,q.status AS quiz_status,p.media_url,p.title AS social_title,p.caption,p.hashtags,
    MAX(CASE WHEN j.platform='youtube' THEN j.status END) youtube_status, MAX(CASE WHEN j.platform='youtube' THEN j.scheduled_at END) youtube_scheduled_at, MAX(CASE WHEN j.platform='youtube' THEN j.published_url END) youtube_published_url,
    MAX(CASE WHEN j.platform='facebook' THEN j.status END) facebook_status, MAX(CASE WHEN j.platform='facebook' THEN j.scheduled_at END) facebook_scheduled_at, MAX(CASE WHEN j.platform='facebook' THEN j.published_url END) facebook_published_url,
    MAX(CASE WHEN j.platform='instagram' THEN j.status END) instagram_status, MAX(CASE WHEN j.platform='instagram' THEN j.scheduled_at END) instagram_scheduled_at, MAX(CASE WHEN j.platform='instagram' THEN j.published_url END) instagram_published_url,
    MAX(CASE WHEN j.status='failed' THEN j.error_message ELSE '' END) last_error
    FROM site_quizzes q LEFT JOIN site_social_packages p ON p.quiz_id=q.id LEFT JOIN site_social_jobs j ON j.quiz_id=q.id ${where}
    GROUP BY q.id ORDER BY q.updated_at DESC,q.id DESC LIMIT ?`).bind(...bindings).all();
  return json({ quizzes:(result.results||[]).map(row => ({
    id:Number(row.id),slug:String(row.slug||""),title:String(row.title||""),category:String(row.category||""),quiz_status:String(row.quiz_status||"draft"),
    media_url:String(row.media_url||""),social_title:String(row.social_title||""),caption:String(row.caption||""),hashtags:String(row.hashtags||""),last_error:String(row.last_error||""),
    platforms:{youtube:platform(row.youtube_status,row.youtube_scheduled_at,row.youtube_published_url),facebook:platform(row.facebook_status,row.facebook_scheduled_at,row.facebook_published_url),instagram:platform(row.instagram_status,row.instagram_scheduled_at,row.instagram_published_url)}
  })) });
}

async function getSocial(db, slug) {
  const quiz = await db.prepare("SELECT id,slug,title,category,description,social_title,social_description,status FROM site_quizzes WHERE slug=? LIMIT 1").bind(slug).first();
  if (!quiz) return json({ error:"Quiz not found." },404);
  await ensurePackage(db,quiz); await ensureJobs(db,quiz.id);
  const pkg = await db.prepare("SELECT media_url,title,description,caption,hashtags,pinned_comment,updated_at FROM site_social_packages WHERE quiz_id=? LIMIT 1").bind(quiz.id).first();
  const jobs = await db.prepare("SELECT platform,status,scheduled_at,published_at,published_url,error_message,attempts,created_at,updated_at FROM site_social_jobs WHERE quiz_id=? ORDER BY platform").bind(quiz.id).all();
  return json({quiz:{id:Number(quiz.id),slug:String(quiz.slug),title:String(quiz.title),category:String(quiz.category),description:String(quiz.description||""),status:String(quiz.status)},package:pkg||{},platforms:jobs.results||[]});
}

async function updateSocial(request,db,slug) {
  const quiz=await db.prepare("SELECT id,slug,title,description,social_title,social_description FROM site_quizzes WHERE slug=? LIMIT 1").bind(slug).first();
  if(!quiz)return json({error:"Quiz not found."},404);
  let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}
  await ensurePackage(db,quiz); const p=body?.package||body||{}; const now=new Date().toISOString();
  await db.prepare("UPDATE site_social_packages SET media_url=?,title=?,description=?,caption=?,hashtags=?,pinned_comment=?,updated_at=? WHERE quiz_id=?")
    .bind(str(p.media_url,2000),str(p.title,160),str(p.description,4000),str(p.caption,4000),str(p.hashtags,1000),str(p.pinned_comment,2000),now,quiz.id).run();
  if(Array.isArray(body?.platforms))await applyPlatforms(db,quiz.id,body.platforms);
  return getSocial(db,slug);
}

async function queueSocial(request,db,slug) {
  const quiz=await db.prepare("SELECT id FROM site_quizzes WHERE slug=? LIMIT 1").bind(slug).first();if(!quiz)return json({error:"Quiz not found."},404);
  await ensureJobs(db,quiz.id);let body={};try{body=await request.json();}catch{}
  const platforms=Array.isArray(body.platforms)?body.platforms.map(v=>String(v).toLowerCase()).filter(v=>SOCIAL_PLATFORMS.includes(v)):SOCIAL_PLATFORMS;
  const scheduled=body.scheduled_at?normalizeDate(body.scheduled_at):null;const now=new Date().toISOString();
  await db.batch(platforms.map(p=>db.prepare("UPDATE site_social_jobs SET status=?,scheduled_at=?,error_message='',updated_at=? WHERE quiz_id=? AND platform=?").bind(scheduled?'scheduled':'ready',scheduled,now,quiz.id,p)));
  return getSocial(db,slug);
}

async function retrySocial(db,slug,platform){
  platform=String(platform||'').toLowerCase();if(!SOCIAL_PLATFORMS.includes(platform))return json({error:'Unsupported social platform.'},400);
  const quiz=await db.prepare("SELECT id FROM site_quizzes WHERE slug=? LIMIT 1").bind(slug).first();if(!quiz)return json({error:'Quiz not found.'},404);await ensureJobs(db,quiz.id);
  await db.prepare("UPDATE site_social_jobs SET status='ready',error_message='',updated_at=? WHERE quiz_id=? AND platform=?").bind(new Date().toISOString(),quiz.id,platform).run();return getSocial(db,slug);
}

async function applyPlatforms(db,quizId,items){const now=new Date().toISOString();const statements=[];for(const item of items){const p=String(item?.platform||'').toLowerCase();if(!SOCIAL_PLATFORMS.includes(p))continue;const status=SOCIAL_STATUSES.includes(String(item?.status||'').toLowerCase())?String(item.status).toLowerCase():'ready';const scheduled=item?.scheduled_at?normalizeDate(item.scheduled_at):null;statements.push(db.prepare(`INSERT INTO site_social_jobs(quiz_id,platform,status,scheduled_at,published_at,published_url,error_message,attempts,created_at,updated_at) VALUES(?,?,?, ?,NULL,?,'',0,?,?) ON CONFLICT(quiz_id,platform) DO UPDATE SET status=excluded.status,scheduled_at=excluded.scheduled_at,published_url=excluded.published_url,updated_at=excluded.updated_at`).bind(quizId,p,status,scheduled,str(item?.published_url,2000),now,now));}if(statements.length)await db.batch(statements);}
async function ensurePackage(db,quiz){const row=await db.prepare("SELECT id FROM site_social_packages WHERE quiz_id=? LIMIT 1").bind(quiz.id).first();if(row)return;await db.prepare("INSERT OR IGNORE INTO site_social_packages(quiz_id,title,description,updated_at) VALUES(?,?,?,?)").bind(quiz.id,quiz.social_title||quiz.title||'',quiz.social_description||quiz.description||'',new Date().toISOString()).run();}
async function ensureJobs(db,quizId){const now=new Date().toISOString();await db.batch(SOCIAL_PLATFORMS.map(p=>db.prepare("INSERT OR IGNORE INTO site_social_jobs(quiz_id,platform,status,created_at,updated_at) VALUES(?,?, 'ready',?,?)").bind(quizId,p,now,now)));}
function platform(status,scheduled_at,published_url){return{status:String(status||'ready'),scheduled_at:scheduled_at||null,published_url:String(published_url||'')};}
function str(value,max){return String(value??'').trim().slice(0,max);}function normalizeDate(value){const d=new Date(value);return Number.isNaN(d.getTime())?null:d.toISOString();}
function json(value,status=200){return new Response(JSON.stringify(value),{status,headers:{'content-type':'application/json; charset=utf-8','cache-control':'no-store','x-content-type-options':'nosniff'}});}
async function requireAdmin(request,env){const expected=String(env.SITE_ADMIN_KEY||'').trim();const supplied=String(request.headers.get('authorization')||'').replace(/^Bearer\s+/i,'').trim();if(expected&&supplied===expected)return{ok:true};const cookie=String(request.headers.get('cookie')||'').match(/(?:^|;\s*)fb_admin_session=([^;]+)/)?.[1]||'';if(cookie&&env.DB){const hash=await sha256(cookie);const row=await env.DB.prepare("SELECT expires_at FROM site_admin_sessions WHERE token_hash=? LIMIT 1").bind(hash).first().catch(()=>null);if(row?.expires_at&&new Date(row.expires_at).getTime()>Date.now())return{ok:true};}return{ok:false,response:json({error:'Administrator authentication required.'},401)};}
async function sha256(value){const digest=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value));return[...new Uint8Array(digest)].map(b=>b.toString(16).padStart(2,'0')).join('');}
