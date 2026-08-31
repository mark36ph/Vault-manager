const TABLE_SQL = `
  CREATE TABLE IF NOT EXISTS site_analytics_daily (
    day TEXT NOT NULL,
    event_name TEXT NOT NULL,
    quiz_slug TEXT NOT NULL DEFAULT '',
    source TEXT NOT NULL DEFAULT '',
    count INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (day, event_name, quiz_slug, source)
  )`;

export async function websiteAnalyticsSummary(env, url) {
  if (!env.DB) return json({ error: "Analytics database is not configured." }, 503);
  await ensureAnalyticsTable(env.DB);

  const requestedDays = Number.parseInt(url.searchParams.get("days") || "30", 10);
  const days = Number.isFinite(requestedDays) ? Math.min(Math.max(requestedDays, 1), 180) : 30;
  const from = new Date(Date.now() - (days - 1) * 86400000).toISOString().slice(0, 10);
  const to = new Date().toISOString().slice(0, 10);

  const [eventRows, quizRows, sourceRows, dailyRows] = await Promise.all([
    env.DB.prepare(`
      SELECT event_name, SUM(count) AS count
      FROM site_analytics_daily
      WHERE day >= ?
      GROUP BY event_name
      ORDER BY count DESC, event_name ASC
    `).bind(from).all(),
    env.DB.prepare(`
      SELECT a.quiz_slug,
             COALESCE(NULLIF(q.title, ''), a.quiz_slug) AS quiz_title,
             a.event_name,
             SUM(a.count) AS count
      FROM site_analytics_daily a
      LEFT JOIN site_quizzes q ON lower(q.slug) = lower(a.quiz_slug)
      WHERE a.day >= ? AND a.quiz_slug <> ''
      GROUP BY a.quiz_slug, q.title, a.event_name
      ORDER BY a.quiz_slug ASC, a.event_name ASC
    `).bind(from).all(),
    env.DB.prepare(`
      SELECT source, event_name, SUM(count) AS count
      FROM site_analytics_daily
      WHERE day >= ? AND source <> ''
      GROUP BY source, event_name
      ORDER BY source ASC, event_name ASC
    `).bind(from).all(),
    env.DB.prepare(`
      SELECT day, event_name, SUM(count) AS count
      FROM site_analytics_daily
      WHERE day >= ?
      GROUP BY day, event_name
      ORDER BY day ASC, event_name ASC
    `).bind(from).all(),
  ]);

  const events = {};
  for (const row of eventRows.results || []) {
    events[String(row.event_name || "")] = Number(row.count || 0);
  }

  const quizzes = {};
  const quizTitles = {};
  for (const row of quizRows.results || []) {
    const slug = String(row.quiz_slug || "");
    if (!slug) continue;
    if (!quizzes[slug]) quizzes[slug] = {};
    quizzes[slug][String(row.event_name || "")] = Number(row.count || 0);
    quizTitles[slug] = String(row.quiz_title || slug);
  }

  const sources = {};
  for (const row of sourceRows.results || []) {
    const source = String(row.source || "");
    if (!source) continue;
    if (!sources[source]) sources[source] = {};
    sources[source][String(row.event_name || "")] = Number(row.count || 0);
  }

  return json({
    days,
    from,
    to,
    events,
    quizzes,
    quiz_titles: quizTitles,
    sources,
    daily: (dailyRows.results || []).map(row => ({
      day: String(row.day || ""),
      event_name: String(row.event_name || ""),
      count: Number(row.count || 0),
    })),
  });
}

async function ensureAnalyticsTable(db) {
  await db.prepare(TABLE_SQL).run();
  await db.prepare(
    "CREATE INDEX IF NOT EXISTS idx_site_analytics_daily_event ON site_analytics_daily(event_name, day)"
  ).run();
  await db.prepare(
    "CREATE INDEX IF NOT EXISTS idx_site_analytics_daily_quiz ON site_analytics_daily(quiz_slug, day)"
  ).run();
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
