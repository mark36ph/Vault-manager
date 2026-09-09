const JSON_HEADERS = {
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store",
};

export async function handleSocialStatsApi(request, env, url) {
  if (!url.pathname.startsWith("/api/social/stats")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  await ensureSocialStatsSchema(env.DB);

  if (request.method === "GET" && url.pathname === "/api/social/stats") {
    if (!(await isAdminAuthorized(request, env))) return json({ error: "Administrator authentication required." }, 401);
    return listSocialStats(env.DB, url);
  }

  if (request.method === "POST" && url.pathname === "/api/social/stats") {
    if (!isStatsWriterAuthorized(request, env)) return json({ error: "Social stats API key is invalid." }, 401);
    return upsertSocialStats(request, env.DB);
  }

  if (request.method === "DELETE" && url.pathname === "/api/social/stats") {
    if (!isStatsWriterAuthorized(request, env)) return json({ error: "Social stats API key is invalid." }, 401);
    await env.DB.prepare("DELETE FROM site_social_stats").run();
    return json({ ok: true, deleted: true });
  }

  return json({ error: "Method not allowed." }, 405, { allow: "GET, POST, DELETE" });
}

async function ensureSocialStatsSchema(db) {
  await db.batch([
    db.prepare(`CREATE TABLE IF NOT EXISTS site_social_stats (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      quiz_slug TEXT NOT NULL,
      platform TEXT NOT NULL,
      platform_id TEXT NOT NULL DEFAULT '',
      url TEXT NOT NULL DEFAULT '',
      status TEXT NOT NULL DEFAULT 'unknown',
      scheduled_for TEXT,
      published_at TEXT,
      views INTEGER NOT NULL DEFAULT 0,
      likes INTEGER NOT NULL DEFAULT 0,
      comments INTEGER NOT NULL DEFAULT 0,
      shares INTEGER NOT NULL DEFAULT 0,
      reactions INTEGER NOT NULL DEFAULT 0,
      captured_at TEXT NOT NULL,
      error_message TEXT NOT NULL DEFAULT '',
      UNIQUE(quiz_slug, platform)
    )`),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_social_stats_platform ON site_social_stats(platform, captured_at)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_social_stats_slug ON site_social_stats(quiz_slug)"),
  ]);
}

async function listSocialStats(db, url) {
  const platform = normalizePlatform(url.searchParams.get("platform"));
  const slug = String(url.searchParams.get("slug") || "").trim().toLowerCase();
  const limit = Math.min(Math.max(Number.parseInt(url.searchParams.get("limit") || "100", 10), 1), 500);
  const where = [];
  const binds = [];
  if (platform) { where.push("platform = ?"); binds.push(platform); }
  if (slug) { where.push("quiz_slug = ?"); binds.push(slug); }
  const whereSql = where.length ? `WHERE ${where.join(" AND ")}` : "";
  const result = await db.prepare(`SELECT quiz_slug,platform,platform_id,url,status,scheduled_for,published_at,views,likes,comments,shares,reactions,captured_at,error_message FROM site_social_stats ${whereSql} ORDER BY captured_at DESC, id DESC LIMIT ?`).bind(...binds, limit).all();
  return json({ stats: result.results || [] });
}

async function upsertSocialStats(request, db) {
  let body;
  try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }
  const records = Array.isArray(body?.stats) ? body.stats : [body];
  if (records.length === 0 || records.length > 100) return json({ error: "Send between 1 and 100 social stat records." }, 400);

  const now = new Date().toISOString();
  const statements = [];
  for (const item of records) {
    const quizSlug = normalizeSlug(item?.quiz_slug);
    const platform = normalizePlatform(item?.platform);
    if (!quizSlug) return json({ error: "Each record needs a valid quiz_slug." }, 400);
    if (!platform) return json({ error: "Each record needs YouTube, Facebook, or Instagram as platform." }, 400);
    const status = normalizeStatus(item?.status);
    const values = {
      platformId: String(item?.platform_id || "").trim().slice(0, 200),
      url: String(item?.url || "").trim().slice(0, 1000),
      status,
      scheduledFor: normalizeDate(item?.scheduled_for),
      publishedAt: normalizeDate(item?.published_at),
      views: metric(item?.views),
      likes: metric(item?.likes),
      comments: metric(item?.comments),
      shares: metric(item?.shares),
      reactions: metric(item?.reactions),
      capturedAt: normalizeDate(item?.captured_at) || now,
      errorMessage: String(item?.error_message || "").trim().slice(0, 1000),
    };
    statements.push(db.prepare(`INSERT INTO site_social_stats(quiz_slug,platform,platform_id,url,status,scheduled_for,published_at,views,likes,comments,shares,reactions,captured_at,error_message)
      VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)
      ON CONFLICT(quiz_slug,platform) DO UPDATE SET
        platform_id=excluded.platform_id,url=excluded.url,status=excluded.status,scheduled_for=excluded.scheduled_for,
        published_at=excluded.published_at,views=excluded.views,likes=excluded.likes,comments=excluded.comments,
        shares=excluded.shares,reactions=excluded.reactions,captured_at=excluded.captured_at,error_message=excluded.error_message`)
      .bind(quizSlug, platform, values.platformId, values.url, values.status, values.scheduledFor, values.publishedAt,
        values.views, values.likes, values.comments, values.shares, values.reactions, values.capturedAt, values.errorMessage));
  }
  await db.batch(statements);
  return json({ ok: true, saved: records.length, captured_at: now });
}

async function isAdminAuthorized(request, env) {
  const expected = String(env.SITE_ADMIN_KEY || "").trim();
  if (!expected) return false;
  const header = String(request.headers.get("authorization") || "").trim();
  if (header.startsWith("Bearer ") && header.slice(7).trim() === expected) return true;
  const cookie = String(request.headers.get("cookie") || "").match(/(?:^|;\s*)fb_admin_session=([^;]+)/)?.[1] || "";
  return Boolean(cookie && await verifyAdminSessionCookie(cookie, expected));
}

async function verifyAdminSessionCookie(token, siteKey) {
  const parts = String(token).split(".");
  if (parts.length !== 3) return false;
  const timestamp = Number(parts[0]);
  const now = Date.now() / 1000;
  const maxAge = 7 * 24 * 60 * 60;
  if (!Number.isInteger(timestamp) || now - timestamp > maxAge || timestamp - now > 60) return false;
  try {
    const expected = await hmacSha256(siteKey, `factburst-admin-session:${parts[0]}.${parts[1]}`);
    const received = fromBase64Url(parts[2]);
    if (expected.length !== received.length) return false;
    let difference = 0;
    for (let i = 0; i < expected.length; i++) difference |= expected[i] ^ received[i];
    return difference === 0;
  } catch { return false; }
}

async function hmacSha256(secret, text) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(text)));
}

function fromBase64Url(value) {
  const normalized = value.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((value.length + 3) % 4);
  const binary = atob(normalized);
  return Uint8Array.from(binary, char => char.charCodeAt(0));
}

function isStatsWriterAuthorized(request, env) {
  const expected = String(env.SOCIAL_STATS_API_KEY || "").trim();
  if (!expected) return false;
  const header = String(request.headers.get("authorization") || "").trim();
  return header.startsWith("Bearer ") && header.slice(7).trim() === expected;
}

function normalizeSlug(value) {
  const slug = String(value || "").trim().toLowerCase();
  return /^[a-z0-9][a-z0-9-]{0,79}$/.test(slug) ? slug : "";
}

function normalizePlatform(value) {
  const platform = String(value || "").trim().toLowerCase();
  if (["youtube", "youtube-promo", "yt"].includes(platform)) return "youtube";
  if (["facebook", "fb"].includes(platform)) return "facebook";
  if (["instagram", "ig"].includes(platform)) return "instagram";
  return "";
}

function normalizeStatus(value) {
  const status = String(value || "unknown").trim().toLowerCase();
  return ["scheduled", "uploading", "pending", "published", "failed", "cancelled", "not_uploaded", "not_connected", "unknown"].includes(status) ? status : "unknown";
}

function normalizeDate(value) {
  const text = String(value || "").trim();
  if (!text) return null;
  const parsed = new Date(text);
  return Number.isFinite(parsed.getTime()) ? parsed.toISOString() : null;
}

function metric(value) {
  const number = Number(value);
  return Number.isFinite(number) && number >= 0 ? Math.floor(number) : 0;
}

function json(value, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(value), { status, headers: { ...JSON_HEADERS, ...extraHeaders } });
}
