const PUBLIC_EVENT_PATH = "/api/analytics/event";
const ADMIN_ANALYTICS_PATH = "/api/admin/analytics";
const ALLOWED_EVENTS = new Set([
  "home_view", "quiz_directory_view", "leaderboard_view", "quiz_link_clicked",
  "quiz_opened", "quiz_started", "quiz_completed", "score_shared", "youtube_clicked",
]);

let analyticsSchemaReady = false;

export async function handleAnalyticsApi(request, env, url) {
  if (url.pathname === PUBLIC_EVENT_PATH && request.method === "POST") {
    if (!env.DB) return json({ ok: true, recorded: false });
    await ensureAnalyticsSchema(env.DB);
    return recordPublicEvent(request, env.DB);
  }
  if (url.pathname === ADMIN_ANALYTICS_PATH && request.method === "GET") {
    if (!env.DB) return json({ error: "Database unavailable." }, 503);
    const auth = await requireAdmin(request, env);
    if (!auth.ok) return auth.response;
    await ensureAnalyticsSchema(env.DB);
    return getAdminAnalytics(env.DB, url);
  }
  return null;
}

async function ensureAnalyticsSchema(db) {
  if (analyticsSchemaReady) return;
  await db.batch([
    db.prepare(`CREATE TABLE IF NOT EXISTS site_analytics_daily (day TEXT NOT NULL,event_name TEXT NOT NULL,quiz_slug TEXT NOT NULL DEFAULT '',source TEXT NOT NULL DEFAULT '',count INTEGER NOT NULL DEFAULT 0,PRIMARY KEY (day,event_name,quiz_slug,source))`),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_analytics_daily_event ON site_analytics_daily(event_name, day)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_analytics_daily_quiz ON site_analytics_daily(quiz_slug, day)"),
  ]);
  analyticsSchemaReady = true;
}

async function recordPublicEvent(request, db) {
  let body; try { body = await request.json(); } catch { return json({ error: "Invalid analytics event." }, 400); }
  const eventName = String(body?.event || "").trim().toLowerCase();
  if (!ALLOWED_EVENTS.has(eventName)) return json({ error: "Unsupported analytics event." }, 400);
  const quizSlug = normalizeSlug(body?.quiz_slug), source = normalizeSource(body?.source), day = new Date().toISOString().slice(0, 10);
  await db.prepare(`INSERT INTO site_analytics_daily (day,event_name,quiz_slug,source,count) VALUES (?,?,?,?,1) ON CONFLICT(day,event_name,quiz_slug,source) DO UPDATE SET count=count+1`).bind(day,eventName,quizSlug,source).run();
  return json({ ok: true, recorded: true });
}

async function getAdminAnalytics(db, url) {
  const requestedDays = Number(url.searchParams.get("days") || 30);
  const days = Math.min(Math.max(Number.isFinite(requestedDays) ? Math.floor(requestedDays) : 30, 1), 365);
  const cutoff = new Date(Date.now() - (days - 1) * 86400000).toISOString().slice(0, 10);
  const [daily, events, quizzes, sources] = await Promise.all([
    db.prepare(`SELECT day,SUM(count) count FROM site_analytics_daily WHERE day>=? GROUP BY day ORDER BY day`).bind(cutoff).all(),
    db.prepare(`SELECT event_name,SUM(count) count FROM site_analytics_daily WHERE day>=? GROUP BY event_name ORDER BY count DESC`).bind(cutoff).all(),
    db.prepare(`SELECT quiz_slug,SUM(count) count FROM site_analytics_daily WHERE day>=? AND quiz_slug<>'' GROUP BY quiz_slug ORDER BY count DESC LIMIT 25`).bind(cutoff).all(),
    db.prepare(`SELECT source,SUM(count) count FROM site_analytics_daily WHERE day>=? AND source<>'' GROUP BY source ORDER BY count DESC LIMIT 25`).bind(cutoff).all(),
  ]);
  const eventMap = Object.fromEntries((events.results || []).map(row => [String(row.event_name), Number(row.count || 0)]));
  return json({ days, cutoff, totals: { events: Object.values(eventMap).reduce((sum, value) => sum + value, 0), starts: eventMap.quiz_started || 0, completions: eventMap.quiz_completed || 0, shares: eventMap.score_shared || 0, youtube_clicks: eventMap.youtube_clicked || 0 }, daily: daily.results || [], events: events.results || [], quizzes: quizzes.results || [], sources: sources.results || [] });
}

async function requireAdmin(request, env) {
  const expected = String(env.SITE_ADMIN_KEY || "").trim();
  const supplied = String(request.headers.get("authorization") || "").replace(/^Bearer\s+/i, "").trim();
  if (expected && supplied === expected) return { ok: true };
  const cookie = String(request.headers.get("cookie") || "").match(/(?:^|;\s*)fb_admin_session=([^;]+)/)?.[1] || "";
  if (cookie && env.DB) {
    const hash = await sha256(cookie);
    const row = await env.DB.prepare("SELECT expires_at FROM site_admin_sessions WHERE token_hash=? LIMIT 1").bind(hash).first().catch(() => null);
    if (row?.expires_at && new Date(row.expires_at).getTime() > Date.now()) return { ok: true };
  }
  return { ok: false, response: json({ error: "Administrator authentication required." }, 401) };
}

function normalizeSlug(value){const slug=String(value||"").trim().toLowerCase();return/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug)?slug:"";}
function normalizeSource(value){const source=String(value||"").trim().toLowerCase();return/^[a-z0-9_-]{1,40}$/.test(source)?source:"";}
async function sha256(value){const digest=await crypto.subtle.digest("SHA-256",new TextEncoder().encode(value));return[...new Uint8Array(digest)].map(b=>b.toString(16).padStart(2,"0")).join("");}
function json(payload,status=200,extraHeaders={}){return new Response(JSON.stringify(payload),{status,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff",...extraHeaders}});}
