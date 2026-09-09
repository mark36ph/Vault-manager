const ADS_KEYS = ["ads_enabled", "adsense_client", "adsense_left_slot", "adsense_right_slot"];

export async function handlePublicAdsConfig(request, db, url) {
  if (url.pathname !== "/api/site/ads" || request.method !== "GET") return null;
  await ensureSettingsTable(db);
  const values = await readAdsSettings(db);
  return json(publicAds(values));
}

export async function handleAdminAdsConfig(request, env, url) {
  if (!url.pathname.startsWith("/api/admin/site/ads")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  if (!(await requireAdmin(request, env))) return json({ error: "Administrator authentication required." }, 401);
  await ensureSettingsTable(env.DB);

  if (request.method === "GET" && url.pathname === "/api/admin/site/ads") {
    const values = await readAdsSettings(env.DB);
    return json({
      enabled: values.ads_enabled === "1",
      client: normalizeClient(values.adsense_client),
      left_slot: normalizeSlot(values.adsense_left_slot),
      right_slot: normalizeSlot(values.adsense_right_slot),
      active: publicAds(values).enabled,
    });
  }

  if (request.method !== "PATCH" && request.method !== "POST") return json({ error: "Ads settings endpoint not found." }, 404);

  let body;
  try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }
  const enabled = Boolean(body?.enabled);
  const client = normalizeClient(body?.client);
  const left = normalizeSlot(body?.left_slot);
  const right = normalizeSlot(body?.right_slot);
  if (enabled && !client) return json({ error: "Enter a valid AdSense Publisher ID such as ca-pub-1234567890123456." }, 400);
  if (enabled && !left && !right) return json({ error: "Enter at least one ad slot when Google Ads is enabled." }, 400);

  const now = new Date().toISOString();
  const values = {
    ads_enabled: enabled ? "1" : "0",
    adsense_client: client,
    adsense_left_slot: left,
    adsense_right_slot: right,
  };
  for (const key of ADS_KEYS) {
    await env.DB.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES (?,?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at")
      .bind(key, values[key], now).run();
  }
  return json({ ok: true, ...{ enabled, client, left_slot: left, right_slot: right }, active: enabled && Boolean(client) && Boolean(left || right), updated_at: now });
}

async function readAdsSettings(db) {
  const rows = await db.prepare(`SELECT key, value FROM site_settings WHERE key IN ('ads_enabled', 'adsense_client', 'adsense_left_slot', 'adsense_right_slot')`).all();
  return Object.fromEntries((rows.results || []).map(row => [String(row.key || ""), String(row.value || "")]));
}

function publicAds(values) {
  const client = normalizeClient(values.adsense_client);
  const left = normalizeSlot(values.adsense_left_slot);
  const right = normalizeSlot(values.adsense_right_slot);
  return { enabled: values.ads_enabled === "1" && Boolean(client) && Boolean(left || right), client, left_slot: left, right_slot: right };
}

export async function ensureSettingsTable(db) {
  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_settings (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL DEFAULT '',
      updated_at TEXT NOT NULL
    )
  `).run();
}

export function normalizeClient(value) {
  const text = String(value || "").trim();
  return /^ca-pub-\d{10,24}$/.test(text) ? text : "";
}

export function normalizeSlot(value) {
  const text = String(value || "").trim();
  return /^\d{4,20}$/.test(text) ? text : "";
}

async function requireAdmin(request, env) {
  const expected = String(env.SITE_ADMIN_KEY || "").trim();
  if (!expected) return false;
  const supplied = String(request.headers.get("authorization") || "").replace(/^Bearer\s+/i, "").trim();
  if (supplied === expected) return true;
  const cookie = String(request.headers.get("cookie") || "").match(/(?:^|;\s*)fb_admin_session=([^;]+)/)?.[1] || "";
  if (!cookie) return false;
  return verifyAdminSessionCookie(cookie, expected);
}

async function verifyAdminSessionCookie(token, siteKey) {
  const parts = String(token).split(".");
  if (parts.length !== 3) return false;
  const timestamp = Number(parts[0]);
  const now = Date.now() / 1000;
  const maxAge = 7 * 24 * 60 * 60;
  if (!Number.isInteger(timestamp) || now - timestamp > maxAge || timestamp - now > 60) return false;
  const expected = await hmacSha256(siteKey, `factburst-admin-session:${parts[0]}.${parts[1]}`);
  const received = fromBase64Url(parts[2]);
  if (expected.length !== received.length) return false;
  let difference = 0;
  for (let i = 0; i < expected.length; i++) difference |= expected[i] ^ received[i];
  return difference === 0;
}

async function hmacSha256(secret, text) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(text)));
}

function fromBase64Url(value) {
  const normalized = String(value).replace(/-/g, "+").replace(/_/g, "/") + "===".slice((String(value).length + 3) % 4);
  const binary = atob(normalized);
  return Uint8Array.from(binary, char => char.charCodeAt(0));
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "x-content-type-options": "nosniff" } });
}
