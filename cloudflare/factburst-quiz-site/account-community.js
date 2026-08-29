import { activeSessionUser } from "./account-access.js";
import { roleForUser } from "./site-controls.js";

const MAX_COMMENT_LENGTH = 600;
const COMMENT_COOLDOWN_SECONDS = 15;

export async function handleCommunityApi(request, db, url) {
  const base = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/comments$/i);
  const nested = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/comments\/(\d+)(?:\/(reply|like|report|moderate))?$/i);
  const moderationList = url.pathname === "/api/moderation/comments";
  if (!base && !nested && !moderationList) return null;

  await ensureCommunitySchema(db);
  if (moderationList) return moderationQueue(request, db, url);

  const slug = (base?.[1] || nested?.[1] || "").toLowerCase();
  if (base) {
    if (request.method === "GET") return listComments(request, db, slug, url);
    if (request.method === "POST") return postComment(request, db, slug, null);
    return json({ error: "Method not allowed." }, 405);
  }

  const commentId = Number(nested[2]);
  const action = nested[3] || "";
  if (action === "reply" && request.method === "POST") return postComment(request, db, slug, commentId);
  if (action === "like" && request.method === "POST") return toggleLike(request, db, slug, commentId);
  if (action === "report" && request.method === "POST") return reportComment(request, db, slug, commentId);
  if (action === "moderate" && request.method === "PATCH") return moderateComment(request, db, slug, commentId);
  if (!action && request.method === "PATCH") return editComment(request, db, slug, commentId);
  if (!action && request.method === "DELETE") return deleteComment(request, db, slug, commentId);
  return json({ error: "Method not allowed." }, 405);
}

async function ensureCommunitySchema(db) {
  await db.prepare(`CREATE TABLE IF NOT EXISTS site_comments (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    quiz_id INTEGER NOT NULL,
    user_id INTEGER NOT NULL,
    parent_id INTEGER,
    body TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'active',
    created_at TEXT NOT NULL,
    edited_at TEXT,
    FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,
    FOREIGN KEY (parent_id) REFERENCES site_comments(id) ON DELETE CASCADE
  )`).run();
  const columns = await db.prepare("PRAGMA table_info(site_comments)").all();
  const names = new Set((columns.results || []).map(column => String(column?.name || "")));
  if (!names.has("parent_id")) await db.prepare("ALTER TABLE site_comments ADD COLUMN parent_id INTEGER").run();
  if (!names.has("status")) await db.prepare("ALTER TABLE site_comments ADD COLUMN status TEXT NOT NULL DEFAULT 'active'").run();
  if (!names.has("edited_at")) await db.prepare("ALTER TABLE site_comments ADD COLUMN edited_at TEXT").run();
  await db.batch([
    db.prepare(`CREATE TABLE IF NOT EXISTS site_comment_likes (
      comment_id INTEGER NOT NULL,
      user_id INTEGER NOT NULL,
      created_at TEXT NOT NULL,
      PRIMARY KEY (comment_id,user_id),
      FOREIGN KEY (comment_id) REFERENCES site_comments(id) ON DELETE CASCADE,
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
    )`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_comment_reports (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      comment_id INTEGER NOT NULL,
      user_id INTEGER NOT NULL,
      reason TEXT NOT NULL,
      detail TEXT NOT NULL DEFAULT '',
      status TEXT NOT NULL DEFAULT 'open',
      created_at TEXT NOT NULL,
      FOREIGN KEY (comment_id) REFERENCES site_comments(id) ON DELETE CASCADE,
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
    )`),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_comments_quiz ON site_comments(quiz_id, created_at DESC)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_comments_parent ON site_comments(parent_id, created_at ASC)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_comment_reports_status ON site_comment_reports(status,created_at DESC)"),
  ]);
}

async function listComments(request, db, slug, url) {
  const quiz = await findPublishedQuiz(db, slug);
  if (!quiz) return json({ error: "Quiz not found." }, 404);
  const viewer = await activeSessionUser(request, db);
  const role = viewer ? await roleForUser(db, viewer.id) : "guest";
  const canModerate = role === "admin" || role === "moderator";
  const requested = Number.parseInt(String(url.searchParams.get("limit") || "100"), 10);
  const limit = Number.isFinite(requested) ? Math.min(Math.max(requested, 1), 200) : 100;
  const result = await db.prepare(`
    SELECT c.id,c.parent_id,c.user_id,c.body,c.status,c.created_at,c.edited_at,u.username,
           (SELECT COUNT(*) FROM site_comment_likes l WHERE l.comment_id=c.id) AS likes,
           CASE WHEN ? > 0 THEN EXISTS(SELECT 1 FROM site_comment_likes l2 WHERE l2.comment_id=c.id AND l2.user_id=?) ELSE 0 END AS liked
    FROM site_comments c JOIN site_users u ON u.id=c.user_id
    WHERE c.quiz_id=? AND COALESCE(u.status,'active')='active' AND u.email_verified_at IS NOT NULL
      AND (c.status='active' OR ?=1)
    ORDER BY CASE WHEN c.parent_id IS NULL THEN c.created_at ELSE (SELECT created_at FROM site_comments p WHERE p.id=c.parent_id) END DESC,
             CASE WHEN c.parent_id IS NULL THEN 0 ELSE 1 END ASC,c.created_at ASC,c.id ASC
    LIMIT ?
  `).bind(Number(viewer?.id || 0), Number(viewer?.id || 0), quiz.id, canModerate ? 1 : 0, limit).all();
  return json({
    quiz: { slug: String(quiz.slug), title: String(quiz.title || "Quiz") },
    can_moderate: canModerate,
    role,
    comments: (result.results || []).map(row => mapComment(row, viewer, canModerate)),
  });
}

async function postComment(request, db, slug, parentId) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const quiz = await findPublishedQuiz(db, slug);
  if (!quiz) return json({ error: "Quiz not found." }, 404);
  if (parentId) {
    const parent = await db.prepare("SELECT id FROM site_comments WHERE id=? AND quiz_id=? AND status='active' LIMIT 1").bind(parentId, quiz.id).first();
    if (!parent) return json({ error: "That comment is no longer available." }, 404);
  }
  const body = await readJson(request);
  const comment = normalizeCommentBody(body?.comment);
  if (!comment) return json({ error: `Write a comment between 2 and ${MAX_COMMENT_LENGTH} characters.` }, 400);
  const now = new Date();
  const latest = await db.prepare("SELECT created_at FROM site_comments WHERE user_id=? ORDER BY created_at DESC LIMIT 1").bind(user.id).first();
  if (latest?.created_at) {
    const elapsed = Math.floor((now.getTime() - Date.parse(String(latest.created_at))) / 1000);
    if (Number.isFinite(elapsed) && elapsed >= 0 && elapsed < COMMENT_COOLDOWN_SECONDS)
      return json({ error: `Please wait ${COMMENT_COOLDOWN_SECONDS - elapsed} seconds before posting another comment.`, code: "comment_rate_limited" }, 429);
  }
  const inserted = await db.prepare("INSERT INTO site_comments (quiz_id,user_id,parent_id,body,status,created_at,edited_at) VALUES (?,?,?,?, 'active',?,NULL)")
    .bind(quiz.id, user.id, parentId || null, comment, now.toISOString()).run();
  return json({ comment: { id: Number(inserted?.meta?.last_row_id || 0), parent_id: parentId || null, user_id: Number(user.id), username: String(user.username), body: comment, status: "active", likes: 0, liked: false, can_edit: true, can_moderate: false, created_at: now.toISOString(), edited_at: "" } }, 201);
}

async function editComment(request, db, slug, commentId) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const row = await commentForQuiz(db, slug, commentId);
  if (!row) return json({ error: "Comment not found." }, 404);
  if (Number(row.user_id) !== Number(user.id)) return json({ error: "You can only edit your own comments." }, 403);
  const body = await readJson(request); const text = normalizeCommentBody(body?.comment);
  if (!text) return json({ error: `Write a comment between 2 and ${MAX_COMMENT_LENGTH} characters.` }, 400);
  await db.prepare("UPDATE site_comments SET body=?,edited_at=? WHERE id=?").bind(text,new Date().toISOString(),commentId).run();
  return json({ updated: true });
}

async function deleteComment(request, db, slug, commentId) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const row = await commentForQuiz(db, slug, commentId);
  if (!row) return json({ error: "Comment not found." }, 404);
  const role = await roleForUser(db, user.id);
  const allowed = Number(row.user_id) === Number(user.id) || role === "admin" || role === "moderator";
  if (!allowed) return json({ error: "You cannot remove this comment." }, 403);
  await db.prepare("UPDATE site_comments SET status='deleted',body='',edited_at=? WHERE id=?").bind(new Date().toISOString(),commentId).run();
  return json({ deleted: true });
}

async function toggleLike(request, db, slug, commentId) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const row = await commentForQuiz(db, slug, commentId);
  if (!row || String(row.status) !== "active") return json({ error: "Comment not found." }, 404);
  const existing = await db.prepare("SELECT comment_id FROM site_comment_likes WHERE comment_id=? AND user_id=? LIMIT 1").bind(commentId,user.id).first();
  let liked;
  if (existing) { await db.prepare("DELETE FROM site_comment_likes WHERE comment_id=? AND user_id=?").bind(commentId,user.id).run(); liked=false; }
  else { await db.prepare("INSERT INTO site_comment_likes (comment_id,user_id,created_at) VALUES (?,?,?)").bind(commentId,user.id,new Date().toISOString()).run(); liked=true; }
  const count = await db.prepare("SELECT COUNT(*) AS total FROM site_comment_likes WHERE comment_id=?").bind(commentId).first();
  return json({ liked, likes: Number(count?.total || 0) });
}

async function reportComment(request, db, slug, commentId) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const row = await commentForQuiz(db, slug, commentId);
  if (!row) return json({ error: "Comment not found." }, 404);
  const body=await readJson(request);const reason=String(body?.reason||"other").trim().toLowerCase();
  if (!["spam","abuse","off_topic","other"].includes(reason)) return json({ error: "Choose a valid report reason." },400);
  const detail=String(body?.detail||"").trim().slice(0,600);
  await db.prepare("INSERT INTO site_comment_reports (comment_id,user_id,reason,detail,status,created_at) VALUES (?,?,?,?, 'open',?)").bind(commentId,user.id,reason,detail,new Date().toISOString()).run();
  return json({ reported:true },201);
}

async function moderateComment(request, db, slug, commentId) {
  const moderator = await moderatorUser(request, db);
  if (moderator instanceof Response) return moderator;
  const row = await commentForQuiz(db, slug, commentId);
  if (!row) return json({ error: "Comment not found." }, 404);
  const body=await readJson(request);const action=String(body?.action||"").toLowerCase();
  if (!['hide','restore'].includes(action)) return json({ error: "Action must be hide or restore." },400);
  const status=action==='hide'?'hidden':'active';
  await db.prepare("UPDATE site_comments SET status=?,edited_at=? WHERE id=?").bind(status,new Date().toISOString(),commentId).run();
  return json({ moderated:true,status });
}

async function moderationQueue(request, db, url) {
  const moderator = await moderatorUser(request, db);
  if (moderator instanceof Response) return moderator;
  const result=await db.prepare(`SELECT c.id,c.body,c.status,c.created_at,u.username,q.slug,q.title,(SELECT COUNT(*) FROM site_comment_reports r WHERE r.comment_id=c.id AND r.status='open') AS reports FROM site_comments c JOIN site_users u ON u.id=c.user_id JOIN site_quizzes q ON q.id=c.quiz_id WHERE c.status<>'deleted' ORDER BY reports DESC,c.created_at DESC LIMIT 200`).all();
  return json({ comments:(result.results||[]).map(row=>({id:Number(row.id),username:String(row.username),body:String(row.body||""),status:String(row.status||"active"),reports:Number(row.reports||0),slug:String(row.slug),quiz_title:String(row.title),created_at:String(row.created_at||"")})) });
}

async function moderatorUser(request, db) {
  const user=await verifiedUser(request,db);if(user instanceof Response)return user;const role=await roleForUser(db,user.id);if(role!=="admin"&&role!=="moderator")return json({error:"Moderator access is required.",code:"moderator_required"},403);return {...user,role};
}
async function verifiedUser(request,db){const user=await activeSessionUser(request,db);if(!user)return json({error:"Log in to use comments.",code:"account_required"},401);if(!user.email_verified_at)return json({error:"Verify your email before posting comments.",code:"verified_account_required"},403);return user;}
async function findPublishedQuiz(db,slug){return db.prepare("SELECT id,slug,title FROM site_quizzes WHERE slug=? AND status='published' LIMIT 1").bind(slug).first();}
async function commentForQuiz(db,slug,id){return db.prepare(`SELECT c.* FROM site_comments c JOIN site_quizzes q ON q.id=c.quiz_id WHERE c.id=? AND q.slug=? LIMIT 1`).bind(id,slug).first();}
function mapComment(row,viewer,canModerate){return {id:Number(row.id||0),parent_id:row.parent_id===null?null:Number(row.parent_id),user_id:Number(row.user_id||0),username:String(row.username||"Player"),body:String(row.status||"active")==="active"?String(row.body||""):String(row.status)==="hidden"?"This comment has been hidden by a moderator.":"Comment removed.",status:String(row.status||"active"),likes:Number(row.likes||0),liked:Boolean(row.liked),can_edit:Boolean(viewer&&Number(viewer.id)===Number(row.user_id)&&String(row.status)==="active"),can_moderate:canModerate,created_at:String(row.created_at||""),edited_at:String(row.edited_at||"")};}
export function normalizeCommentBody(value){const text=String(value||"").replace(/\r\n?/g,"\n").replace(/[\t ]+/g," ").replace(/\n{3,}/g,"\n\n").trim();return text.length>=2&&text.length<=MAX_COMMENT_LENGTH?text:"";}
async function readJson(request){try{return await request.json();}catch{return {};}}
function json(value,status=200,extraHeaders={}){return new Response(JSON.stringify(value),{status,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff",...extraHeaders}});}
