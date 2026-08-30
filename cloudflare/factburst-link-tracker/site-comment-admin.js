export async function handleSiteCommentAdmin(request, env, url) {
  if (!url.pathname.startsWith("/api/site/comments")) return null;
  if (!env.DB) return json({ error: "DB binding is not configured." }, 500);

  await ensureModerationSchema(env.DB);

  if (request.method === "GET" && url.pathname === "/api/site/comments") {
    return listComments(env.DB, url);
  }

  const match = url.pathname.match(/^\/api\/site\/comments\/(\d+)$/);
  if (match && request.method === "PATCH") {
    return moderateComment(env.DB, Number(match[1]), request);
  }

  return json({ error: "Not found" }, 404);
}

async function listComments(db, url) {
  if (!await tableExists(db, "site_comments")) {
    return json({ comments: [], summary: { active: 0, hidden: 0, reported: 0 } });
  }

  const requested = String(url.searchParams.get("status") || "reported").trim().toLowerCase();
  const status = ["reported", "active", "hidden", "all"].includes(requested) ? requested : "reported";
  const search = String(url.searchParams.get("q") || "").trim().slice(0, 120);
  const reportsExist = await tableExists(db, "site_comment_reports");

  const conditions = ["c.status <> 'deleted'"];
  const bindings = [];
  if (status === "active") conditions.push("c.status = 'active'");
  if (status === "hidden") conditions.push("c.status = 'hidden'");
  if (status === "reported") {
    conditions.push(reportsExist
      ? "EXISTS (SELECT 1 FROM site_comment_reports rr WHERE rr.comment_id = c.id AND rr.status = 'open')"
      : "1 = 0");
  }
  if (search) {
    const pattern = `%${search}%`;
    conditions.push("(c.body LIKE ? OR u.username LIKE ? OR q.title LIKE ? OR q.slug LIKE ?)");
    bindings.push(pattern, pattern, pattern, pattern);
  }

  const reportCountSql = reportsExist
    ? "(SELECT COUNT(*) FROM site_comment_reports r WHERE r.comment_id = c.id AND r.status = 'open')"
    : "0";
  const reportReasonsSql = reportsExist
    ? "COALESCE((SELECT GROUP_CONCAT(DISTINCT r.reason) FROM site_comment_reports r WHERE r.comment_id = c.id AND r.status = 'open'), '')"
    : "''";

  let statement = db.prepare(`
    SELECT
      c.id,
      c.body,
      c.status,
      c.created_at,
      c.edited_at,
      u.username,
      q.slug AS quiz_slug,
      q.title AS quiz_title,
      ${reportCountSql} AS reports,
      ${reportReasonsSql} AS report_reasons
    FROM site_comments c
    LEFT JOIN site_users u ON u.id = c.user_id
    LEFT JOIN site_quizzes q ON q.id = c.quiz_id
    WHERE ${conditions.join(" AND ")}
    ORDER BY reports DESC, c.created_at DESC, c.id DESC
    LIMIT 500
  `);
  if (bindings.length) statement = statement.bind(...bindings);
  const result = await statement.all();

  const statusRows = await db.prepare(`
    SELECT status, COUNT(*) AS total
    FROM site_comments
    WHERE status <> 'deleted'
    GROUP BY status
  `).all();
  const summary = { active: 0, hidden: 0, reported: 0 };
  for (const row of statusRows.results || []) {
    const key = String(row.status || "active").toLowerCase();
    if (key === "active") summary.active = Number(row.total || 0);
    if (key === "hidden") summary.hidden = Number(row.total || 0);
  }
  if (reportsExist) {
    const reported = await db.prepare(`
      SELECT COUNT(DISTINCT comment_id) AS total
      FROM site_comment_reports
      WHERE status = 'open'
    `).first();
    summary.reported = Number(reported?.total || 0);
  }

  return json({
    comments: (result.results || []).map(row => ({
      id: Number(row.id || 0),
      username: String(row.username || "Deleted user"),
      body: String(row.body || ""),
      status: String(row.status || "active"),
      reports: Number(row.reports || 0),
      report_reasons: String(row.report_reasons || ""),
      quiz_slug: String(row.quiz_slug || ""),
      quiz_title: String(row.quiz_title || row.quiz_slug || "Unknown quiz"),
      created_at: String(row.created_at || ""),
      edited_at: String(row.edited_at || ""),
    })),
    summary,
  });
}

async function moderateComment(db, id, request) {
  if (!Number.isInteger(id) || id <= 0) return json({ error: "Invalid comment id." }, 400);
  if (!await tableExists(db, "site_comments")) return json({ error: "Comment not found." }, 404);

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Invalid JSON body." }, 400);
  }
  const action = String(body?.action || "").trim().toLowerCase();
  if (!["hide", "restore", "dismiss_reports", "delete"].includes(action)) {
    return json({ error: "Action must be hide, restore, dismiss_reports or delete." }, 400);
  }

  const existing = await db.prepare("SELECT id, status FROM site_comments WHERE id = ? LIMIT 1").bind(id).first();
  if (!existing || String(existing.status || "") === "deleted") return json({ error: "Comment not found." }, 404);

  const now = new Date().toISOString();
  if (action === "hide") {
    await db.prepare("UPDATE site_comments SET status = 'hidden', edited_at = ? WHERE id = ?").bind(now, id).run();
    await closeReports(db, id, "resolved");
    return json({ updated: true, id, status: "hidden" });
  }
  if (action === "restore") {
    await db.prepare("UPDATE site_comments SET status = 'active', edited_at = ? WHERE id = ?").bind(now, id).run();
    return json({ updated: true, id, status: "active" });
  }
  if (action === "dismiss_reports") {
    await closeReports(db, id, "dismissed");
    return json({ updated: true, id, status: String(existing.status || "active"), reports: 0 });
  }

  await db.prepare("UPDATE site_comments SET status = 'deleted', body = '', edited_at = ? WHERE id = ?").bind(now, id).run();
  await closeReports(db, id, "resolved");
  return json({ updated: true, id, status: "deleted" });
}

async function closeReports(db, commentId, status) {
  if (!await tableExists(db, "site_comment_reports")) return;
  await db.prepare("UPDATE site_comment_reports SET status = ? WHERE comment_id = ? AND status = 'open'")
    .bind(status, commentId)
    .run();
}

async function ensureModerationSchema(db) {
  if (!await tableExists(db, "site_comments")) return;
  const columns = await db.prepare("PRAGMA table_info(site_comments)").all();
  const names = new Set((columns.results || []).map(column => String(column?.name || "")));
  if (!names.has("status")) {
    await db.prepare("ALTER TABLE site_comments ADD COLUMN status TEXT NOT NULL DEFAULT 'active'").run();
  }
  if (!names.has("edited_at")) {
    await db.prepare("ALTER TABLE site_comments ADD COLUMN edited_at TEXT").run();
  }
  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_comment_reports (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      comment_id INTEGER NOT NULL,
      user_id INTEGER NOT NULL,
      reason TEXT NOT NULL,
      detail TEXT NOT NULL DEFAULT '',
      status TEXT NOT NULL DEFAULT 'open',
      created_at TEXT NOT NULL,
      FOREIGN KEY (comment_id) REFERENCES site_comments(id) ON DELETE CASCADE,
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
    )
  `).run();
  await db.prepare("CREATE INDEX IF NOT EXISTS idx_site_comment_reports_status ON site_comment_reports(status, created_at DESC)").run();
}

async function tableExists(db, name) {
  const row = await db.prepare("SELECT 1 AS present FROM sqlite_master WHERE type = 'table' AND name = ? LIMIT 1")
    .bind(name)
    .first();
  return Boolean(row?.present);
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
