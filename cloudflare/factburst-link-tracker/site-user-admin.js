export async function handleSiteUserAdmin(request, env, url) {
  const path = url.pathname.replace(/^\/+|\/+$/g, "");
  const parts = path ? path.split("/") : [];

  if (path === "api/site/users" && request.method === "GET") {
    return listUsers(env, url);
  }

  if (parts.length === 4 && parts[0] === "api" && parts[1] === "site" && parts[2] === "users") {
    const userId = parseUserId(parts[3]);
    if (request.method === "GET") return userDetail(env, userId);
    if (request.method === "PATCH") return updateUser(request, env, userId);
    if (request.method === "DELETE") return deleteUser(env, userId);
  }

  return null;
}

async function listUsers(env, url) {
  if (!await ensureUserAdminSchema(env.DB)) {
    return json({ users: [], summary: emptySummary() });
  }

  const rawSearch = String(url.searchParams.get("search") || "").trim().slice(0, 100);
  const search = rawSearch ? `%${rawSearch.replace(/[\\%_]/g, value => `\\${value}`)}%` : "%";
  const limit = clampInt(url.searchParams.get("limit"), 1, 500, 250);

  const result = await env.DB.prepare(`
    SELECT
      u.id,
      u.username,
      u.email,
      u.email_verified_at,
      COALESCE(u.status, 'active') AS status,
      u.suspended_at,
      COALESCE(u.suspension_reason, '') AS suspension_reason,
      u.created_at,
      u.last_login_at,
      COUNT(s.quiz_id) AS quizzes_completed,
      COALESCE(SUM(s.attempts), 0) AS attempts,
      COALESCE(SUM(s.best_score), 0) AS total_score,
      COALESCE(SUM(s.total), 0) AS total_possible,
      MAX(s.last_completed_at) AS last_played_at
    FROM site_users u
    LEFT JOIN site_user_scores s ON s.user_id = u.id
    WHERE u.username LIKE ? ESCAPE '\\' COLLATE NOCASE
       OR u.email LIKE ? ESCAPE '\\' COLLATE NOCASE
    GROUP BY u.id
    ORDER BY u.created_at DESC, u.username_key ASC
    LIMIT ?
  `).bind(search, search, limit).all();

  const summaryRow = await env.DB.prepare(`
    SELECT
      COUNT(*) AS total,
      SUM(CASE WHEN COALESCE(status, 'active') = 'active' THEN 1 ELSE 0 END) AS active,
      SUM(CASE WHEN status = 'suspended' THEN 1 ELSE 0 END) AS suspended,
      SUM(CASE WHEN email_verified_at IS NOT NULL THEN 1 ELSE 0 END) AS verified,
      SUM(CASE WHEN email_verified_at IS NULL THEN 1 ELSE 0 END) AS unverified
    FROM site_users
  `).first();

  return json({
    users: (result.results || []).map(mapUser),
    summary: {
      total: Number(summaryRow?.total || 0),
      active: Number(summaryRow?.active || 0),
      suspended: Number(summaryRow?.suspended || 0),
      verified: Number(summaryRow?.verified || 0),
      unverified: Number(summaryRow?.unverified || 0),
    },
  });
}

async function userDetail(env, userId) {
  if (!await ensureUserAdminSchema(env.DB)) return json({ error: "User not found." }, 404);

  const user = await env.DB.prepare(`
    SELECT
      u.id,
      u.username,
      u.email,
      u.email_verified_at,
      COALESCE(u.status, 'active') AS status,
      u.suspended_at,
      COALESCE(u.suspension_reason, '') AS suspension_reason,
      u.created_at,
      u.last_login_at,
      COUNT(s.quiz_id) AS quizzes_completed,
      COALESCE(SUM(s.attempts), 0) AS attempts,
      COALESCE(SUM(s.best_score), 0) AS total_score,
      COALESCE(SUM(s.total), 0) AS total_possible,
      MAX(s.last_completed_at) AS last_played_at
    FROM site_users u
    LEFT JOIN site_user_scores s ON s.user_id = u.id
    WHERE u.id = ?
    GROUP BY u.id
    LIMIT 1
  `).bind(userId).first();
  if (!user) return json({ error: "User not found." }, 404);

  const history = await env.DB.prepare(`
    SELECT
      q.id AS quiz_id,
      q.slug,
      q.title,
      s.best_score,
      s.total,
      s.attempts,
      s.first_completed_at,
      s.last_completed_at
    FROM site_user_scores s
    JOIN site_quizzes q ON q.id = s.quiz_id
    WHERE s.user_id = ?
    ORDER BY s.last_completed_at DESC, q.title COLLATE NOCASE ASC
  `).bind(userId).all();

  return json({
    user: mapUser(user),
    quizzes: (history.results || []).map(row => ({
      quiz_id: Number(row.quiz_id || 0),
      slug: String(row.slug || ""),
      title: String(row.title || row.slug || "Quiz"),
      best_score: Number(row.best_score || 0),
      total: Number(row.total || 0),
      percentage: percentage(row.best_score, row.total),
      attempts: Number(row.attempts || 0),
      first_completed_at: String(row.first_completed_at || ""),
      last_completed_at: String(row.last_completed_at || ""),
    })),
  });
}

async function updateUser(request, env, userId) {
  if (!await ensureUserAdminSchema(env.DB)) return json({ error: "User not found." }, 404);
  const existing = await env.DB.prepare("SELECT id, status FROM site_users WHERE id = ? LIMIT 1").bind(userId).first();
  if (!existing) return json({ error: "User not found." }, 404);

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }

  const status = String(body?.status || "").trim().toLowerCase();
  if (status !== "active" && status !== "suspended") {
    return json({ error: "Status must be active or suspended." }, 400);
  }

  const reason = String(body?.reason || "").trim().slice(0, 300);
  const now = new Date().toISOString();
  if (status === "suspended") {
    const statements = [
      env.DB.prepare(`
        UPDATE site_users
        SET status = 'suspended', suspended_at = ?, suspension_reason = ?
        WHERE id = ?
      `).bind(now, reason, userId),
    ];
    if (await tableExists(env.DB, "site_sessions")) {
      statements.push(env.DB.prepare("DELETE FROM site_sessions WHERE user_id = ?").bind(userId));
    }
    await env.DB.batch(statements);
  } else {
    await env.DB.prepare(`
      UPDATE site_users
      SET status = 'active', suspended_at = NULL, suspension_reason = ''
      WHERE id = ?
    `).bind(userId).run();
  }

  return userDetail(env, userId);
}

async function deleteUser(env, userId) {
  if (!await ensureUserAdminSchema(env.DB)) return json({ error: "User not found." }, 404);
  const existing = await env.DB.prepare("SELECT id, username FROM site_users WHERE id = ? LIMIT 1").bind(userId).first();
  if (!existing) return json({ error: "User not found." }, 404);

  const statements = [];
  if (await tableExists(env.DB, "site_sessions")) {
    statements.push(env.DB.prepare("DELETE FROM site_sessions WHERE user_id = ?").bind(userId));
  }
  if (await tableExists(env.DB, "site_email_verifications")) {
    statements.push(env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(userId));
  }
  if (await tableExists(env.DB, "site_user_scores")) {
    statements.push(env.DB.prepare("DELETE FROM site_user_scores WHERE user_id = ?").bind(userId));
  }
  if (await tableExists(env.DB, "site_challenges")) {
    const columns = await env.DB.prepare("PRAGMA table_info(site_challenges)").all();
    const names = new Set((columns.results || []).map(column => String(column.name || "")));
    statements.push(names.has("challenged_user_id")
      ? env.DB.prepare("DELETE FROM site_challenges WHERE challenger_user_id = ? OR challenged_user_id = ?").bind(userId, userId)
      : env.DB.prepare("DELETE FROM site_challenges WHERE challenger_user_id = ?").bind(userId));
  }
  if (await tableExists(env.DB, "site_friendships")) {
    statements.push(env.DB.prepare(`
      DELETE FROM site_friendships
      WHERE user_a_id = ? OR user_b_id = ? OR requested_by_user_id = ?
    `).bind(userId, userId, userId));
  }
  statements.push(env.DB.prepare("DELETE FROM site_users WHERE id = ?").bind(userId));

  await env.DB.batch(statements);
  return json({ deleted: true, user_id: userId, username: String(existing.username || "") });
}

async function ensureUserAdminSchema(db) {
  const table = await db.prepare(`
    SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'site_users' LIMIT 1
  `).first();
  if (!table) return false;

  const columns = await db.prepare("PRAGMA table_info(site_users)").all();
  const names = new Set((columns.results || []).map(column => String(column.name || "")));
  if (!names.has("status")) await db.prepare("ALTER TABLE site_users ADD COLUMN status TEXT NOT NULL DEFAULT 'active'").run();
  if (!names.has("suspended_at")) await db.prepare("ALTER TABLE site_users ADD COLUMN suspended_at TEXT").run();
  if (!names.has("suspension_reason")) await db.prepare("ALTER TABLE site_users ADD COLUMN suspension_reason TEXT NOT NULL DEFAULT ''").run();
  await db.prepare("CREATE INDEX IF NOT EXISTS idx_site_users_status ON site_users(status, created_at DESC)").run();
  return true;
}

async function tableExists(db, tableName) {
  const row = await db.prepare(`
    SELECT name FROM sqlite_master WHERE type = 'table' AND name = ? LIMIT 1
  `).bind(tableName).first();
  return Boolean(row);
}

function mapUser(row) {
  const totalScore = Number(row.total_score || 0);
  const totalPossible = Number(row.total_possible || 0);
  return {
    id: Number(row.id || 0),
    username: String(row.username || ""),
    email: String(row.email || ""),
    email_verified: Boolean(row.email_verified_at),
    email_verified_at: row.email_verified_at ? String(row.email_verified_at) : null,
    status: String(row.status || "active"),
    suspended_at: row.suspended_at ? String(row.suspended_at) : null,
    suspension_reason: String(row.suspension_reason || ""),
    created_at: String(row.created_at || ""),
    last_login_at: String(row.last_login_at || ""),
    last_played_at: row.last_played_at ? String(row.last_played_at) : null,
    quizzes_completed: Number(row.quizzes_completed || 0),
    attempts: Number(row.attempts || 0),
    total_score: totalScore,
    total_possible: totalPossible,
    percentage: totalPossible > 0 ? Math.round((totalScore / totalPossible) * 100) : 0,
  };
}

function parseUserId(value) {
  const userId = Number.parseInt(String(value || ""), 10);
  if (!Number.isInteger(userId) || userId <= 0) {
    const error = new Error("Invalid user id.");
    error.status = 400;
    throw error;
  }
  return userId;
}

function clampInt(value, min, max, fallback) {
  const parsed = Number.parseInt(String(value || ""), 10);
  if (!Number.isFinite(parsed)) return fallback;
  return Math.min(Math.max(parsed, min), max);
}

function percentage(score, total) {
  const possible = Number(total || 0);
  return possible > 0 ? Math.round((Number(score || 0) / possible) * 100) : 0;
}

function emptySummary() {
  return { total: 0, active: 0, suspended: 0, verified: 0, unverified: 0 };
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value, null, 2), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
