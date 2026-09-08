const PLATFORMS = ["youtube", "facebook", "instagram"];
const ACTIONS = ["reply", "hide", "delete", "like", "unlike", "pin", "unpin"];

export async function handleSocialCommandApi(request, env, url) {
  if (!url.pathname.startsWith("/api/social/agent")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  if (!authorizedAgent(request, env)) return json({ error: "Social agent authentication required." }, 401);
  try {
    await ensureSchema(env.DB);
    const parts = url.pathname.split("/").filter(Boolean);
    if (request.method === "GET" && parts[3] === "commands") return getCommands(env.DB, url);
    if (request.method === "POST" && parts[3] === "commands" && parts[4] === "result") return commandResult(request, env.DB);
    if (request.method === "POST" && parts[3] === "comments") return syncComments(request, env.DB);
    return json({ error: "Social agent endpoint not found." }, 404);
  } catch (error) {
    console.error("Factburst social agent API failed", error);
    return json({ error: "The social agent request could not be completed." }, 500);
  }
}

export async function handleAdminSocialCommentsApi(request, env, url) {
  if (!url.pathname.startsWith("/api/admin/social-comments")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  const auth = await requireAdmin(request, env);
  if (!auth.ok) return auth.response;
  try {
    await ensureSchema(env.DB);
    if (request.method === "GET") return listComments(env.DB, url);
    if (request.method === "POST") return createCommand(request, env.DB);
    return json({ error: "Social comments endpoint not found." }, 404);
  } catch (error) {
    console.error("Factburst admin social comments failed", error);
    return json({ error: "The comment request could not be completed." }, 500);
  }
}

async function ensureSchema(db) {
  await db.batch([
    db.prepare(`CREATE TABLE IF NOT EXISTS site_social_comments (
      id INTEGER PRIMARY KEY AUTOINCREMENT, quiz_slug TEXT NOT NULL, platform TEXT NOT NULL,
      platform_comment_id TEXT NOT NULL, parent_comment_id TEXT NOT NULL DEFAULT '', author_name TEXT NOT NULL DEFAULT '',
      author_id TEXT NOT NULL DEFAULT '', text TEXT NOT NULL DEFAULT '', permalink TEXT NOT NULL DEFAULT '',
      status TEXT NOT NULL DEFAULT 'visible', created_at TEXT, updated_at TEXT NOT NULL,
      UNIQUE(platform, platform_comment_id))`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_social_commands (
      id INTEGER PRIMARY KEY AUTOINCREMENT, comment_id INTEGER, quiz_slug TEXT NOT NULL, platform TEXT NOT NULL,
      action TEXT NOT NULL, payload_json TEXT NOT NULL DEFAULT '{}', status TEXT NOT NULL DEFAULT 'queued',
      result_json TEXT NOT NULL DEFAULT '{}', error_message TEXT NOT NULL DEFAULT '', attempts INTEGER NOT NULL DEFAULT 0,
      created_at TEXT NOT NULL, updated_at TEXT NOT NULL, completed_at TEXT,
      FOREIGN KEY(comment_id) REFERENCES site_social_comments(id) ON DELETE SET NULL)`),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_social_commands_queue ON site_social_commands(status, id)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_social_comments_quiz ON site_social_comments(quiz_slug, platform, created_at)"),
  ]);
}

async function listComments(db, url) {
  const slug = String(url.searchParams.get("quiz") || "").trim().toLowerCase();
  const platform = String(url.searchParams.get("platform") || "").trim().toLowerCase();
  const status = String(url.searchParams.get("status") || "").trim().toLowerCase();
  const limit = Math.min(Math.max(Number(url.searchParams.get("limit") || 100), 1), 500);
  const where=[]; const bind=[];
  if(slug){where.push("quiz_slug=?");bind.push(slug);} if(PLATFORMS.includes(platform)){where.push("platform=?");bind.push(platform);} if(status){where.push("status=?");bind.push(status);} bind.push(limit);
  const result=await db.prepare(`SELECT id,quiz_slug,platform,platform_comment_id,parent_comment_id,author_name,author_id,text,permalink,status,created_at,updated_at FROM site_social_comments ${where.length?`WHERE ${where.join(" AND ")}`:""} ORDER BY COALESCE(created_at,updated_at) DESC,id DESC LIMIT ?`).bind(...bind).all();
  return json({comments:result.results||[]});
}

async function createCommand(request,db){
  let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}
  const commentId=Number(body?.comment_id);const platform=String(body?.platform||"").toLowerCase();const action=String(body?.action||"").toLowerCase();
  if(!Number.isInteger(commentId)||commentId<=0)return json({error:"A valid comment is required."},400);
  if(!PLATFORMS.includes(platform)||!ACTIONS.includes(action))return json({error:"Unsupported platform or action."},400);
  const comment=await db.prepare("SELECT id,quiz_slug,platform,platform_comment_id FROM site_social_comments WHERE id=? LIMIT 1").bind(commentId).first();
  if(!comment||comment.platform!==platform)return json({error:"Comment not found for that platform."},404);
  let payload={};if(body?.text!==undefined)payload.text=String(body.text).trim().slice(0,4000);
  if(action==="reply"&&!payload.text)return json({error:"Reply text is required."},400);
  const now=new Date().toISOString();
  const inserted=await db.prepare("INSERT INTO site_social_commands(comment_id,quiz_slug,platform,action,payload_json,status,created_at,updated_at) VALUES(?,?,?,?,?,'queued',?,?) RETURNING id").bind(commentId,comment.quiz_slug,platform,action,JSON.stringify(payload),now,now).first();
  return json({command:{id:Number(inserted.id),status:"queued"}},202);
}

async function getCommands(db,url){
  const limit=Math.min(Math.max(Number(url.searchParams.get("limit")||20),1),100);
  const result=await db.prepare("SELECT id,comment_id,quiz_slug,platform,action,payload_json,status,attempts,created_at,updated_at FROM site_social_commands WHERE status IN ('queued','processing') ORDER BY id LIMIT ?").bind(limit).all();
  const commands=(result.results||[]).map(row=>({...row,id:Number(row.id),comment_id:row.comment_id?Number(row.comment_id):null,attempts:Number(row.attempts||0),payload:parseJson(row.payload_json)}));
  if(commands.length){await db.batch(commands.map(c=>db.prepare("UPDATE site_social_commands SET status='processing',attempts=attempts+1,updated_at=? WHERE id=? AND status='queued'").bind(new Date().toISOString(),c.id)));}
  return json({commands});
}

async function commandResult(request,db){
  let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}
  const id=Number(body?.command_id);if(!Number.isInteger(id)||id<=0)return json({error:"Command ID is required."},400);
  const success=body?.success===true;const now=new Date().toISOString();
  await db.prepare("UPDATE site_social_commands SET status=?,result_json=?,error_message=?,updated_at=?,completed_at=? WHERE id=?").bind(success?'succeeded':'failed',JSON.stringify(body?.result||{}),String(body?.error||'').slice(0,2000),now,now,id).run();
  return json({ok:true});
}

async function syncComments(request,db){
  let body;try{body=await request.json();}catch{return json({error:"Request body must be valid JSON."},400);}
  if(!Array.isArray(body?.comments))return json({error:"comments must be an array."},400);
  const now=new Date().toISOString();
  const statements=body.comments.slice(0,500).map(c=>{
    const platform=String(c?.platform||'').toLowerCase();if(!PLATFORMS.includes(platform)||!c?.platform_comment_id||!c?.quiz_slug)return null;
    return db.prepare(`INSERT INTO site_social_comments(quiz_slug,platform,platform_comment_id,parent_comment_id,author_name,author_id,text,permalink,status,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(platform,platform_comment_id) DO UPDATE SET parent_comment_id=excluded.parent_comment_id,author_name=excluded.author_name,author_id=excluded.author_id,text=excluded.text,permalink=excluded.permalink,status=excluded.status,created_at=excluded.created_at,updated_at=excluded.updated_at`).bind(String(c.quiz_slug).trim().toLowerCase(),platform,String(c.platform_comment_id).slice(0,300),String(c.parent_comment_id||'').slice(0,300),String(c.author_name||'').slice(0,300),String(c.author_id||'').slice(0,300),String(c.text||'').slice(0,10000),String(c.permalink||'').slice(0,2000),String(c.status||'visible').slice(0,40),c.created_at?new Date(c.created_at).toISOString():now,now);
  }).filter(Boolean);if(statements.length)await db.batch(statements);return json({ok:true,count:statements.length});
}

function authorizedAgent(request,env){const expected=String(env.SOCIAL_AGENT_API_KEY||'').trim();const supplied=String(request.headers.get('authorization')||'').replace(/^Bearer\s+/i,'').trim();return Boolean(expected&&supplied&&supplied===expected);}
async function requireAdmin(request,env){const expected=String(env.SITE_ADMIN_KEY||'').trim();const supplied=String(request.headers.get('authorization')||'').replace(/^Bearer\s+/i,'').trim();if(expected&&supplied===expected)return{ok:true};const cookie=String(request.headers.get('cookie')||'').match(/(?:^|;\s*)fb_admin_session=([^;]+)/)?.[1]||'';if(cookie&&env.DB){const hash=await sha256(cookie);const row=await env.DB.prepare("SELECT expires_at FROM site_admin_sessions WHERE token_hash=? LIMIT 1").bind(hash).first().catch(()=>null);if(row?.expires_at&&new Date(row.expires_at).getTime()>Date.now())return{ok:true};}return{ok:false,response:json({error:'Administrator authentication required.'},401)};}
function parseJson(value){try{return JSON.parse(value||'{}');}catch{return{};}}
async function sha256(value){const digest=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value));return[...new Uint8Array(digest)].map(b=>b.toString(16).padStart(2,'0')).join('');}
function json(value,status=200){return new Response(JSON.stringify(value),{status,headers:{'content-type':'application/json; charset=utf-8','cache-control':'no-store','x-content-type-options':'nosniff'}});}
