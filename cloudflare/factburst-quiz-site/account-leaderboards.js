import { activeSessionUser } from "./account-access.js";

const MAX_LEADERBOARD = 50;

export async function handleFilteredLeaderboardApi(request, db, url) {
  if (request.method !== "GET") return null;
  if (url.pathname === "/api/leaderboard") {
    return overallLeaderboard(request, db, url);
  }

  const match = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/leaderboard$/i);
  if (match) return perQuizLeaderboard(request, db, match[1].toLowerCase(), url);
  return null;
}

async function overallLeaderboard(request, db, url) {
  const limit = normalizeLimit(url.searchParams.get("limit"));
  const result = await db.prepare(`
    WITH totals AS (
      SELECT
        user_id,
        COUNT(*) AS quizzes_completed,
        SUM(best_score) AS total_score,
        SUM(total) AS total_possible,
        SUM(attempts) AS attempts
      FROM site_user_scores
      GROUP BY user_id
    )
    SELECT u.id AS user_id, u.username, t.quizzes_completed, t.total_score, t.total_possible, t.attempts
    FROM totals t
    JOIN site_users u ON u.id = t.user_id
    WHERE u.email_verified_at IS NOT NULL
      AND COALESCE(u.status, 'active') = 'active'
    ORDER BY t.total_score DESC, t.quizzes_completed DESC,
             (1.0 * t.total_score / NULLIF(t.total_possible, 0)) DESC,
             u.username_key ASC
    LIMIT ?
  `).bind(limit).all();

  const current = await activeSessionUser(request, db);
  const leaders = (result.results || []).map((row, index) => ({
    rank: index + 1,
    username: String(row.username || ""),
    quizzes_completed: Number(row.quizzes_completed || 0),
    total_score: Number(row.total_score || 0),
    total_possible: Number(row.total_possible || 0),
    percentage: percentage(row.total_score, row.total_possible),
    attempts: Number(row.attempts || 0),
    current_user: Boolean(current && Number(current.id) === Number(row.user_id)),
  }));
  return json({ leaderboard: leaders });
}

async function perQuizLeaderboard(request, db, slug, url) {
  const limit = normalizeLimit(url.searchParams.get("limit"));
  const quiz = await db.prepare(`
    SELECT id, slug, title FROM site_quizzes WHERE slug = ? AND status = 'published' LIMIT 1
  `).bind(slug).first();
  if (!quiz) return json({ error: "Quiz not found." }, 404);

  const result = await db.prepare(`
    SELECT u.id AS user_id, u.username, s.best_score, s.total, s.attempts,
           s.first_completed_at, s.last_completed_at
    FROM site_user_scores s
    JOIN site_users u ON u.id = s.user_id
    WHERE s.quiz_id = ?
      AND u.email_verified_at IS NOT NULL
      AND COALESCE(u.status, 'active') = 'active'
    ORDER BY (1.0 * s.best_score / NULLIF(s.total, 0)) DESC,
             s.best_score DESC,
             s.first_completed_at ASC,
             u.username_key ASC
    LIMIT ?
  `).bind(quiz.id, limit).all();

  const current = await activeSessionUser(request, db);
  const leaders = (result.results || []).map((row, index) => ({
    rank: index + 1,
    username: String(row.username || ""),
    score: Number(row.best_score || 0),
    total: Number(row.total || 0),
    percentage: percentage(row.best_score, row.total),
    attempts: Number(row.attempts || 0),
    current_user: Boolean(current && Number(current.id) === Number(row.user_id)),
  }));

  let mine = null;
  if (current?.email_verified_at) {
    const row = await db.prepare(`
      SELECT best_score, total, attempts FROM site_user_scores
      WHERE user_id = ? AND quiz_id = ? LIMIT 1
    `).bind(current.id, quiz.id).first();
    if (row) {
      const rankRow = await db.prepare(`
        SELECT 1 + COUNT(*) AS rank
        FROM site_user_scores other
        JOIN site_users other_user ON other_user.id = other.user_id
        WHERE other.quiz_id = ?
          AND other_user.email_verified_at IS NOT NULL
          AND COALESCE(other_user.status, 'active') = 'active'
          AND (
            (1.0 * other.best_score / NULLIF(other.total, 0)) > (1.0 * ? / NULLIF(?, 0)) OR
            ((1.0 * other.best_score / NULLIF(other.total, 0)) = (1.0 * ? / NULLIF(?, 0)) AND other.best_score > ?)
          )
      `).bind(quiz.id, row.best_score, row.total, row.best_score, row.total, row.best_score).first();
      mine = {
        rank: Number(rankRow?.rank || 1),
        score: Number(row.best_score || 0),
        total: Number(row.total || 0),
        percentage: percentage(row.best_score, row.total),
        attempts: Number(row.attempts || 0),
      };
    }
  }

  return json({
    quiz: { slug: String(quiz.slug), title: String(quiz.title) },
    leaderboard: leaders,
    mine,
  });
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
  return new Response(JSON.stringify(value), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
    },
  });
}
