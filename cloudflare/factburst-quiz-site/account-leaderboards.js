import { activeSessionUser } from "./account-access.js";

const MAX_LEADERBOARD = 50;

export async function handleFilteredLeaderboardApi(request, db, url) {
  if (request.method !== "GET") return null;
  if (url.pathname === "/api/leaderboard") return overallLeaderboard(request, db, url);

  const match = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/leaderboard$/i);
  if (match) return perQuizLeaderboard(request, db, match[1].toLowerCase(), url);
  return null;
}

async function overallLeaderboard(request, db, url) {
  const limit = normalizeLimit(url.searchParams.get("limit"));
  const result = await db.prepare(`
    WITH totals AS (
      SELECT user_id, COUNT(*) AS quizzes_completed, SUM(best_score) AS total_score,
             SUM(total) AS total_possible, SUM(attempts) AS attempts
      FROM site_user_scores GROUP BY user_id
    )
    SELECT u.id AS user_id, u.username, t.quizzes_completed, t.total_score, t.total_possible, t.attempts
    FROM totals t JOIN site_users u ON u.id = t.user_id
    WHERE u.email_verified_at IS NOT NULL AND COALESCE(u.status, 'active') = 'active'
    ORDER BY t.total_score DESC, t.quizzes_completed DESC,
             (1.0 * t.total_score / NULLIF(t.total_possible, 0)) DESC, u.username_key ASC
    LIMIT ?
  `).bind(limit).all();
  const current = await activeSessionUser(request, db);
  return json({ leaderboard: (result.results || []).map((row, index) => ({
    rank: index + 1, username: String(row.username || ""),
    quizzes_completed: Number(row.quizzes_completed || 0), total_score: Number(row.total_score || 0),
    total_possible: Number(row.total_possible || 0), percentage: percentage(row.total_score, row.total_possible),
    attempts: Number(row.attempts || 0), current_user: Boolean(current && Number(current.id) === Number(row.user_id)),
  })) });
}

async function ensureLengthLeaderboard(db) {
  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_user_quiz_scores_by_length (
      user_id INTEGER NOT NULL, quiz_id INTEGER NOT NULL, question_count INTEGER NOT NULL,
      best_score INTEGER NOT NULL, attempts INTEGER NOT NULL DEFAULT 1,
      first_completed_at TEXT NOT NULL, last_completed_at TEXT NOT NULL,
      PRIMARY KEY (user_id, quiz_id, question_count),
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
    )
  `).run();
  await db.prepare("CREATE INDEX IF NOT EXISTS idx_site_user_quiz_scores_length ON site_user_quiz_scores_by_length(quiz_id, question_count, best_score DESC)").run();
  await db.prepare(`
    INSERT INTO site_user_quiz_scores_by_length
      (user_id, quiz_id, question_count, best_score, attempts, first_completed_at, last_completed_at)
    SELECT user_id, quiz_id, total, best_score, attempts, first_completed_at, last_completed_at
    FROM site_user_scores WHERE total > 0
    ON CONFLICT(user_id, quiz_id, question_count) DO UPDATE SET
      best_score = CASE
        WHEN (1.0 * excluded.best_score / NULLIF(excluded.question_count, 0)) >
             (1.0 * site_user_quiz_scores_by_length.best_score / NULLIF(site_user_quiz_scores_by_length.question_count, 0))
          THEN excluded.best_score ELSE site_user_quiz_scores_by_length.best_score END,
      attempts = MAX(site_user_quiz_scores_by_length.attempts, excluded.attempts),
      first_completed_at = MIN(site_user_quiz_scores_by_length.first_completed_at, excluded.first_completed_at),
      last_completed_at = MAX(site_user_quiz_scores_by_length.last_completed_at, excluded.last_completed_at)
  `).run();
  await db.prepare(`
    CREATE TRIGGER IF NOT EXISTS trg_site_user_scores_by_length_insert
    AFTER INSERT ON site_user_scores
    WHEN NEW.total > 0
    BEGIN
      INSERT INTO site_user_quiz_scores_by_length
        (user_id, quiz_id, question_count, best_score, attempts, first_completed_at, last_completed_at)
      VALUES (NEW.user_id, NEW.quiz_id, NEW.total, NEW.best_score, NEW.attempts, NEW.first_completed_at, NEW.last_completed_at)
      ON CONFLICT(user_id, quiz_id, question_count) DO UPDATE SET
        best_score = CASE WHEN (1.0 * excluded.best_score / NULLIF(excluded.question_count, 0)) > (1.0 * site_user_quiz_scores_by_length.best_score / NULLIF(site_user_quiz_scores_by_length.question_count, 0)) THEN excluded.best_score ELSE site_user_quiz_scores_by_length.best_score END,
        attempts = MAX(site_user_quiz_scores_by_length.attempts, excluded.attempts),
        last_completed_at = MAX(site_user_quiz_scores_by_length.last_completed_at, excluded.last_completed_at);
    END
  `).run();
  await db.prepare(`
    CREATE TRIGGER IF NOT EXISTS trg_site_user_scores_by_length_update
    AFTER UPDATE OF best_score, total, attempts, first_completed_at, last_completed_at ON site_user_scores
    WHEN NEW.total > 0
    BEGIN
      INSERT INTO site_user_quiz_scores_by_length
        (user_id, quiz_id, question_count, best_score, attempts, first_completed_at, last_completed_at)
      VALUES (NEW.user_id, NEW.quiz_id, NEW.total, NEW.best_score, NEW.attempts, NEW.first_completed_at, NEW.last_completed_at)
      ON CONFLICT(user_id, quiz_id, question_count) DO UPDATE SET
        best_score = CASE WHEN (1.0 * excluded.best_score / NULLIF(excluded.question_count, 0)) > (1.0 * site_user_quiz_scores_by_length.best_score / NULLIF(site_user_quiz_scores_by_length.question_count, 0)) THEN excluded.best_score ELSE site_user_quiz_scores_by_length.best_score END,
        attempts = MAX(site_user_quiz_scores_by_length.attempts, excluded.attempts),
        last_completed_at = MAX(site_user_quiz_scores_by_length.last_completed_at, excluded.last_completed_at);
    END
  `).run();
}

async function perQuizLeaderboard(request, db, slug, url) {
  await ensureLengthLeaderboard(db);
  const limit = normalizeLimit(url.searchParams.get("limit"));
  const requestedLength = Number.parseInt(String(url.searchParams.get("length") || ""), 10);
  const length = Number.isFinite(requestedLength) && requestedLength > 0 ? requestedLength : null;
  const quiz = await db.prepare(`SELECT id, slug, title FROM site_quizzes WHERE slug = ? AND status = 'published' LIMIT 1`).bind(slug).first();
  if (!quiz) return json({ error: "Quiz not found." }, 404);

  const result = length
    ? await db.prepare(`
        SELECT u.id AS user_id, u.username, s.best_score, s.question_count AS total, s.attempts, s.first_completed_at
        FROM site_user_quiz_scores_by_length s JOIN site_users u ON u.id = s.user_id
        WHERE s.quiz_id = ? AND s.question_count = ?
          AND u.email_verified_at IS NOT NULL AND COALESCE(u.status, 'active') = 'active'
        ORDER BY s.best_score DESC, s.first_completed_at ASC, u.username_key ASC LIMIT ?
      `).bind(quiz.id, length, limit).all()
    : await db.prepare(`
        SELECT u.id AS user_id, u.username, s.best_score, s.question_count AS total, s.attempts, s.first_completed_at
        FROM site_user_quiz_scores_by_length s JOIN site_users u ON u.id = s.user_id
        WHERE s.quiz_id = ? AND u.email_verified_at IS NOT NULL AND COALESCE(u.status, 'active') = 'active'
        ORDER BY (1.0 * s.best_score / NULLIF(s.question_count, 0)) DESC, s.best_score DESC,
                 s.first_completed_at ASC, u.username_key ASC LIMIT ?
      `).bind(quiz.id, limit).all();

  return buildResponse(request, db, quiz, result.results || [], length);
}

async function buildResponse(request, db, quiz, rows, length) {
  const current = await activeSessionUser(request, db);
  const leaders = rows.map((row, index) => ({
    rank: index + 1, username: String(row.username || ""), score: Number(row.best_score || 0),
    total: Number(row.total || 0), percentage: percentage(row.best_score, row.total),
    attempts: Number(row.attempts || 0), current_user: Boolean(current && Number(current.id) === Number(row.user_id)),
  }));
  let mine = null;
  if (current?.email_verified_at && length) {
    const row = await db.prepare(`SELECT best_score, question_count AS total, attempts, first_completed_at FROM site_user_quiz_scores_by_length WHERE user_id = ? AND quiz_id = ? AND question_count = ? LIMIT 1`).bind(current.id, quiz.id, length).first();
    if (row) {
      const rankRow = await db.prepare(`SELECT 1 + COUNT(*) AS rank FROM site_user_quiz_scores_by_length other JOIN site_users other_user ON other_user.id = other.user_id WHERE other.quiz_id = ? AND other.question_count = ? AND other_user.email_verified_at IS NOT NULL AND COALESCE(other_user.status, 'active') = 'active' AND (other.best_score > ? OR (other.best_score = ? AND other.first_completed_at < ?))`).bind(quiz.id, length, row.best_score, row.best_score, row.first_completed_at).first();
      mine = { rank: Number(rankRow?.rank || 1), score: Number(row.best_score || 0), total: Number(row.total || 0), percentage: percentage(row.best_score, row.total), attempts: Number(row.attempts || 0) };
    }
  }
  return json({ quiz: { slug: String(quiz.slug), title: String(quiz.title) }, length, leaderboard: leaders, mine });
}

function normalizeLimit(value) {
  const parsed = Number.parseInt(String(value || "25"), 10);
  if (!Number.isFinite(parsed)) return 25;
  return Math.min(Math.max(parsed, 1), MAX_LEADERBOARD);
}

function percentage(score, total) {
  const possible = Number(total || 0);
  return possible > 0 ? Math.round((Number(score || 0) / possible) * 100) : 0;
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "x-content-type-options": "nosniff" } });
}
