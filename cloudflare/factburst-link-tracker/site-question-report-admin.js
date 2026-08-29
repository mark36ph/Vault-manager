export async function handleSiteQuestionReportAdmin(request, env, url) {
  if (!url.pathname.startsWith("/api/site/question-reports")) return null;
  if (!env.DB) return json({ error: "DB binding is not configured." }, 500);

  if (request.method === "GET" && url.pathname === "/api/site/question-reports") {
    return listReports(env.DB, url);
  }

  const match = url.pathname.match(/^\/api\/site\/question-reports\/(\d+)$/);
  if (match && request.method === "PATCH") {
    return updateReport(env.DB, Number(match[1]), request);
  }

  return json({ error: "Not found" }, 404);
}

async function listReports(db, url) {
  if (!await tableExists(db, "site_question_reports")) {
    return json({ reports: [], summary: { open: 0, resolved: 0, dismissed: 0 } });
  }

  const requested = String(url.searchParams.get("status") || "open").trim().toLowerCase();
  const status = ["open", "resolved", "dismissed", "all"].includes(requested) ? requested : "open";
  const where = status === "all" ? "" : "WHERE r.status = ?";
  const statement = db.prepare(`
    SELECT
      r.id,
      r.question_position,
      r.reason,
      r.detail,
      r.status,
      r.created_at,
      u.username AS reporter,
      q.slug AS quiz_slug,
      q.title AS quiz_title,
      sq.question AS question_text
    FROM site_question_reports r
    LEFT JOIN site_users u ON u.id = r.user_id
    LEFT JOIN site_quizzes q ON q.id = r.quiz_id
    LEFT JOIN site_questions sq ON sq.quiz_id = r.quiz_id AND sq.position = r.question_position
    ${where}
    ORDER BY CASE r.status WHEN 'open' THEN 0 WHEN 'resolved' THEN 1 ELSE 2 END, r.created_at DESC
    LIMIT 500
  `);
  const result = status === "all" ? await statement.all() : await statement.bind(status).all();
  const summaryRows = await db.prepare(`
    SELECT status, COUNT(*) AS total
    FROM site_question_reports
    GROUP BY status
  `).all();
  const summary = { open: 0, resolved: 0, dismissed: 0 };
  for (const row of summaryRows.results || []) {
    const key = String(row.status || "").toLowerCase();
    if (key in summary) summary[key] = Number(row.total || 0);
  }

  return json({
    reports: (result.results || []).map(row => ({
      id: Number(row.id || 0),
      question_position: Number(row.question_position || 0),
      reason: String(row.reason || "other"),
      detail: String(row.detail || ""),
      status: String(row.status || "open"),
      created_at: String(row.created_at || ""),
      reporter: String(row.reporter || "Deleted user"),
      quiz_slug: String(row.quiz_slug || ""),
      quiz_title: String(row.quiz_title || row.quiz_slug || "Unknown quiz"),
      question_text: String(row.question_text || "Question no longer exists"),
    })),
    summary,
  });
}

async function updateReport(db, id, request) {
  if (!Number.isInteger(id) || id <= 0) return json({ error: "Invalid report id." }, 400);
  if (!await tableExists(db, "site_question_reports")) return json({ error: "Report not found." }, 404);

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Invalid JSON body." }, 400);
  }
  const status = String(body?.status || "").trim().toLowerCase();
  if (!["open", "resolved", "dismissed"].includes(status)) {
    return json({ error: "Status must be open, resolved or dismissed." }, 400);
  }

  const existing = await db.prepare("SELECT id FROM site_question_reports WHERE id = ? LIMIT 1").bind(id).first();
  if (!existing) return json({ error: "Report not found." }, 404);
  await db.prepare("UPDATE site_question_reports SET status = ? WHERE id = ?").bind(status, id).run();
  return json({ updated: true, id, status });
}

async function tableExists(db, name) {
  const row = await db.prepare("SELECT 1 AS present FROM sqlite_master WHERE type='table' AND name=? LIMIT 1").bind(name).first();
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
