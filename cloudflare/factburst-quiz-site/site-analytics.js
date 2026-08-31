const PUBLIC_EVENT_PATH = "/api/analytics/event";
const ADMIN_ANALYTICS_PATH = "/api/admin/analytics";
const ALLOWED_EVENTS = new Set([
  "home_view",
  "quiz_directory_view",
  "leaderboard_view",
  "quiz_link_clicked",
  "quiz_opened",
  "quiz_started",
  "quiz_completed",
  "score_shared",
  "youtube_clicked",
]);

let analyticsSchemaReady = false;

export async function handleAnalyticsApi(request, env, url) {
  if (url.pathname === PUBLIC_EVENT_PATH && request.method === "POST") {
    if (!env.DB) return json({ ok: true, recorded: false });
    await ensureAnalyticsSchema(env.DB);
    return recordPublicEvent(request, env.DB);
  }

  if (url.pathname === ADMIN_ANALYTICS_PATH && request.method === "GET") {
    if (!env.DB) return json({ error: "Analytics database is not configured." }, 503);
    if (!env.SITE_ADMIN_KEY) return json({ error: "Analytics access is not configured." }, 503);
    const supplied = request.headers.get("authorization") || "";
    if (supplied !== `Bearer ${env.SITE_ADMIN_KEY}`) return json({ error: "Unauthorized." }, 401);
    await ensureAnalyticsSchema(env.DB);
    return analyticsSummary(env.DB, url);
  }

  return null;
}

async function ensureAnalyticsSchema(db) {
  if (analyticsSchemaReady) return;
  await db.batch([
    db.prepare(`
      CREATE TABLE IF NOT EXISTS site_analytics_daily (
        day TEXT NOT NULL,
        event_name TEXT NOT NULL,
        quiz_slug TEXT NOT NULL DEFAULT '',
        source TEXT NOT NULL DEFAULT '',
        count INTEGER NOT NULL DEFAULT 0,
        PRIMARY KEY (day, event_name, quiz_slug, source)
      )
    `),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_analytics_daily_event ON site_analytics_daily(event_name, day)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_analytics_daily_quiz ON site_analytics_daily(quiz_slug, day)"),
  ]);
  analyticsSchemaReady = true;
}

async function recordPublicEvent(request, db) {
  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Invalid analytics event." }, 400);
  }

  const eventName = String(body?.event || "").trim().toLowerCase();
  if (!ALLOWED_EVENTS.has(eventName)) return json({ error: "Unsupported analytics event." }, 400);

  const quizSlug = normalizeSlug(body?.quiz_slug);
  const source = normalizeSource(body?.source);
  const day = new Date().toISOString().slice(0, 10);

  await db.prepare(`
    INSERT INTO site_analytics_daily (day, event_name, quiz_slug, source, count)
    VALUES (?, ?, ?, ?, 1)
    ON CONFLICT(day, event_name, quiz_slug, source)
    DO UPDATE SET count = count + 1
  `).bind(day, eventName, quizSlug, source).run();

  return json({ ok: true, recorded: true });
}

async function analyticsSummary(db, url) {
  const requestedDays = Number.parseInt(url.searchParams.get("days") || "30", 10);
  const days = Number.isFinite(requestedDays) ? Math.min(Math.max(requestedDays, 1), 180) : 30;
  const from = new Date(Date.now() - (days - 1) * 86400000).toISOString().slice(0, 10);

  const [eventRows, quizRows, dailyRows] = await Promise.all([
    db.prepare(`
      SELECT event_name, SUM(count) AS count
      FROM site_analytics_daily
      WHERE day >= ?
      GROUP BY event_name
      ORDER BY count DESC, event_name ASC
    `).bind(from).all(),
    db.prepare(`
      SELECT quiz_slug, event_name, SUM(count) AS count
      FROM site_analytics_daily
      WHERE day >= ? AND quiz_slug <> ''
      GROUP BY quiz_slug, event_name
      ORDER BY quiz_slug ASC, event_name ASC
    `).bind(from).all(),
    db.prepare(`
      SELECT day, event_name, SUM(count) AS count
      FROM site_analytics_daily
      WHERE day >= ?
      GROUP BY day, event_name
      ORDER BY day ASC, event_name ASC
    `).bind(from).all(),
  ]);

  const events = Object.fromEntries((eventRows.results || []).map(row => [row.event_name, Number(row.count) || 0]));
  const quizzes = {};
  for (const row of quizRows.results || []) {
    const slug = String(row.quiz_slug || "");
    if (!quizzes[slug]) quizzes[slug] = {};
    quizzes[slug][row.event_name] = Number(row.count) || 0;
  }

  return json({
    days,
    from,
    to: new Date().toISOString().slice(0, 10),
    events,
    quizzes,
    daily: dailyRows.results || [],
  }, 200, { "cache-control": "no-store" });
}

function normalizeSlug(value) {
  const slug = String(value || "").trim().toLowerCase();
  return /^[a-z0-9][a-z0-9-]{0,79}$/.test(slug) ? slug : "";
}

function normalizeSource(value) {
  const source = String(value || "").trim().toLowerCase();
  return /^[a-z0-9_-]{1,40}$/.test(source) ? source : "";
}

function json(payload, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
      ...extraHeaders,
    },
  });
}
