import { activeSessionUser } from "./account-access.js";

const MAX_COMMENT_LENGTH = 600;
const COMMENT_COOLDOWN_SECONDS = 15;

export async function handleCommentsApi(request, db, url) {
  const match = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/comments$/i);
  if (!match) return null;
  const slug = match[1].toLowerCase();

  await ensureCommentsSchema(db);
  if (request.method === "GET") return listComments(db, slug, url);
  if (request.method === "POST") return postComment(request, db, slug);
  return json({ error: "Method not allowed." }, 405, { allow: "GET, POST" });
}

async function listComments(db, slug, url) {
  const quiz = await findPublishedQuiz(db, slug);
  if (!quiz) return json({ error: "Quiz not found." }, 404);

  const requested = Number.parseInt(String(url.searchParams.get("limit") || "50"), 10);
  const limit = Number.isFinite(requested) ? Math.min(Math.max(requested, 1), 100) : 50;
  const result = await db.prepare(`
    SELECT c.id, c.body, c.created_at, u.id AS user_id, u.username
    FROM site_comments c
    JOIN site_users u ON u.id = c.user_id
    WHERE c.quiz_id = ?
      AND COALESCE(u.status, 'active') = 'active'
      AND u.email_verified_at IS NOT NULL
    ORDER BY c.created_at DESC, c.id DESC
    LIMIT ?
  `).bind(quiz.id, limit).all();

  return json({
    quiz: { slug: String(quiz.slug), title: String(quiz.title || "Quiz") },
    comments: (result.results || []).map(row => ({
      id: Number(row.id || 0),
      user_id: Number(row.user_id || 0),
      username: String(row.username || "Player"),
      body: String(row.body || ""),
      created_at: String(row.created_at || ""),
    })),
  });
}

async function postComment(request, db, slug) {
  const user = await activeSessionUser(request, db);
  if (!user) {
    return json({ error: "Log in to your Factburst account to comment.", code: "account_required" }, 401);
  }
  if (!user.email_verified_at) {
    return json({ error: "Verify your email before posting comments.", code: "verified_account_required" }, 403);
  }

  const quiz = await findPublishedQuiz(db, slug);
  if (!quiz) return json({ error: "Quiz not found." }, 404);

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }
  const comment = normalizeCommentBody(body?.comment);
  if (!comment) {
    return json({ error: `Write a comment between 2 and ${MAX_COMMENT_LENGTH} characters.` }, 400);
  }

  const now = new Date();
  const latest = await db.prepare(`
    SELECT created_at FROM site_comments
    WHERE user_id = ? ORDER BY created_at DESC LIMIT 1
  `).bind(user.id).first();
  if (latest?.created_at) {
    const elapsed = Math.floor((now.getTime() - Date.parse(String(latest.created_at))) / 1000);
    if (Number.isFinite(elapsed) && elapsed >= 0 && elapsed < COMMENT_COOLDOWN_SECONDS) {
      return json({
        error: `Please wait ${COMMENT_COOLDOWN_SECONDS - elapsed} seconds before posting another comment.`,
        code: "comment_rate_limited",
      }, 429);
    }
  }

  const createdAt = now.toISOString();
  const inserted = await db.prepare(`
    INSERT INTO site_comments (quiz_id, user_id, body, created_at)
    VALUES (?, ?, ?, ?)
  `).bind(quiz.id, user.id, comment, createdAt).run();

  return json({
    comment: {
      id: Number(inserted?.meta?.last_row_id || 0),
      user_id: Number(user.id),
      username: String(user.username || "Player"),
      body: comment,
      created_at: createdAt,
    },
  }, 201);
}

async function findPublishedQuiz(db, slug) {
  return db.prepare(`
    SELECT id, slug, title FROM site_quizzes
    WHERE slug = ? AND status = 'published' LIMIT 1
  `).bind(slug).first();
}

async function ensureCommentsSchema(db) {
  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_comments (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      quiz_id INTEGER NOT NULL,
      user_id INTEGER NOT NULL,
      body TEXT NOT NULL,
      created_at TEXT NOT NULL,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE,
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
    )
  `).run();
  await db.prepare("CREATE INDEX IF NOT EXISTS idx_site_comments_quiz ON site_comments(quiz_id, created_at DESC)").run();
  await db.prepare("CREATE INDEX IF NOT EXISTS idx_site_comments_user ON site_comments(user_id, created_at DESC)").run();
}

export function normalizeCommentBody(value) {
  const text = String(value || "")
    .replace(/\r\n?/g, "\n")
    .replace(/[\t ]+/g, " ")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
  return text.length >= 2 && text.length <= MAX_COMMENT_LENGTH ? text : "";
}

function json(value, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(value), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
      ...extraHeaders,
    },
  });
}
