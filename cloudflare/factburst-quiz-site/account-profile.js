import { activeSessionUser } from "./account-access.js";

export async function handleProfileApi(request, db, url) {
  if (request.method !== "GET" || url.pathname !== "/api/account/history") return null;

  const user = await activeSessionUser(request, db);
  if (!user) return json({ error: "Log in to view your profile.", code: "account_required" }, 401);

  const historyResult = await db.prepare(`
    WITH ranked AS (
      SELECT
        s.user_id,
        s.quiz_id,
        q.slug,
        q.title,
        s.best_score,
        s.total,
        s.attempts,
        s.first_completed_at,
        s.last_completed_at,
        RANK() OVER (
          PARTITION BY s.quiz_id
          ORDER BY (1.0 * s.best_score / NULLIF(s.total, 0)) DESC,
                   s.best_score DESC,
                   s.first_completed_at ASC,
                   u.username_key ASC
        ) AS leaderboard_rank
      FROM site_user_scores s
      JOIN site_users u ON u.id = s.user_id
      JOIN site_quizzes q ON q.id = s.quiz_id
      WHERE u.email_verified_at IS NOT NULL
        AND COALESCE(u.status, 'active') = 'active'
    )
    SELECT * FROM ranked
    WHERE user_id = ?
    ORDER BY last_completed_at DESC, title COLLATE NOCASE ASC
  `).bind(user.id).all();

  const rankRow = await db.prepare(`
    WITH totals AS (
      SELECT
        u.id AS user_id,
        u.username_key,
        COUNT(s.quiz_id) AS quizzes_completed,
        COALESCE(SUM(s.best_score), 0) AS total_score,
        COALESCE(SUM(s.total), 0) AS total_possible
      FROM site_users u
      JOIN site_user_scores s ON s.user_id = u.id
      WHERE u.email_verified_at IS NOT NULL
        AND COALESCE(u.status, 'active') = 'active'
      GROUP BY u.id, u.username_key
    ), ranked AS (
      SELECT
        user_id,
        RANK() OVER (
          ORDER BY total_score DESC,
                   quizzes_completed DESC,
                   (1.0 * total_score / NULLIF(total_possible, 0)) DESC,
                   username_key ASC
        ) AS overall_rank
      FROM totals
    )
    SELECT overall_rank FROM ranked WHERE user_id = ? LIMIT 1
  `).bind(user.id).first();

  const history = (historyResult.results || []).map(row => ({
    quiz_id: Number(row.quiz_id),
    slug: String(row.slug || ""),
    title: String(row.title || row.slug || "Quiz"),
    best_score: Number(row.best_score || 0),
    total: Number(row.total || 0),
    percentage: percentage(row.best_score, row.total),
    attempts: Number(row.attempts || 0),
    leaderboard_rank: Number(row.leaderboard_rank || 0),
    first_completed_at: String(row.first_completed_at || ""),
    last_completed_at: String(row.last_completed_at || ""),
  }));

  return json({
    overall_rank: rankRow?.overall_rank ? Number(rankRow.overall_rank) : null,
    quizzes: history,
  });
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
