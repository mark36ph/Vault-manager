const ADS_KEYS = ["ads_enabled", "adsense_client", "adsense_left_slot", "adsense_right_slot"];
const LOGO_KEY = "site_logo_data_url";
const MAX_LOGO_DATA_URL_BYTES = 210000;

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
    const client = normalizeClient(values.adsense_client);
    return json({
      enabled: values.ads_enabled === "1",
      client,
      left_slot: normalizeAdSlot(values.adsense_left_slot, client),
      right_slot: normalizeAdSlot(values.adsense_right_slot, client),
      active: publicAds(values).enabled,
    });
  }

  if (request.method !== "PATCH" && request.method !== "POST") return json({ error: "Ads settings endpoint not found." }, 404);

  let body;
  try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }
  const enabled = Boolean(body?.enabled);
  const client = normalizeClient(body?.client);
  const left = normalizeAdSlot(body?.left_slot, client);
  const right = normalizeAdSlot(body?.right_slot, client);
  if (enabled && !client) return json({ error: "Enter a valid AdSense Publisher ID such as ca-pub-1234567890123456." }, 400);
  if (enabled && !left && !right) return json({ error: "Enter at least one real AdSense ad slot ID. Do not use the Publisher ID as the slot ID." }, 400);

  const now = new Date().toISOString();
  const values = { ads_enabled: enabled ? "1" : "0", adsense_client: client, adsense_left_slot: left, adsense_right_slot: right };
  for (const key of ADS_KEYS) {
    await env.DB.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES (?,?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at")
      .bind(key, values[key], now).run();
  }
  return json({ ok: true, ...{ enabled, client, left_slot: left, right_slot: right }, active: enabled && Boolean(client) && Boolean(left || right), updated_at: now });
}

export async function handleLogoApi(request, env, url) {
  if (url.pathname !== "/api/admin/site/logo" && url.pathname !== "/brand-icon.png") return null;
  if (!env.DB) {
    if (url.pathname === "/brand-icon.png") return null;
    return json({ error: "Database unavailable." }, 503);
  }
  await ensureSettingsTable(env.DB);

  if (url.pathname === "/brand-icon.png" && request.method === "GET") {
    const row = await env.DB.prepare("SELECT value,updated_at FROM site_settings WHERE key=? LIMIT 1").bind(LOGO_KEY).first();
    const parsed = parseLogoDataUrl(row?.value);
    if (!parsed) return null;
    return new Response(parsed.bytes, {
      status: 200,
      headers: {
        "content-type": parsed.contentType,
        "cache-control": "no-cache, must-revalidate",
        "etag": `\"${simpleEtag(String(row.updated_at || "logo"))}\"`,
        "x-content-type-options": "nosniff",
      },
    });
  }

  if (url.pathname !== "/api/admin/site/logo") return null;
  if (!(await requireAdmin(request, env))) return json({ error: "Administrator authentication required." }, 401);
  if (request.method !== "POST") return json({ error: "Logo endpoint not found." }, 404);

  let body;
  try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }
  const dataUrl = String(body?.data_url || "").trim();
  const parsed = parseLogoDataUrl(dataUrl);
  if (!parsed) return json({ error: "Logo must be a valid PNG or JPEG image." }, 400);
  if (dataUrl.length > MAX_LOGO_DATA_URL_BYTES) return json({ error: "Logo is too large after optimisation. Keep it below 150 KB." }, 413);

  const now = new Date().toISOString();
  await env.DB.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES (?,?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at")
    .bind(LOGO_KEY, dataUrl, now).run();
  return json({ ok: true, updated_at: now, url: `/brand-icon.png?logo=${encodeURIComponent(now)}` });
}

function parseLogoDataUrl(value) {
  const text = String(value || "").trim();
  const match = text.match(/^data:(image\/(?:png|jpeg));base64,([A-Za-z0-9+/=\s]+)$/i);
  if (!match) return null;
  try {
    const binary = atob(match[2].replace(/\s/g, ""));
    const bytes = Uint8Array.from(binary, char => char.charCodeAt(0));
    if (!bytes.length) return null;
    return { contentType: match[1].toLowerCase(), bytes };
  } catch { return null; }
}

function simpleEtag(value) {
  let hash = 2166136261;
  for (let i = 0; i < value.length; i++) hash = Math.imul(hash ^ value.charCodeAt(i), 16777619);
  return (hash >>> 0).toString(16);
}

async function readAdsSettings(db) {
  const rows = await db.prepare(`SELECT key, value FROM site_settings WHERE key IN ('ads_enabled', 'adsense_client', 'adsense_left_slot', 'adsense_right_slot')`).all();
  return Object.fromEntries((rows.results || []).map(row => [String(row.key || ""), String(row.value || "")]));
}

function publicAds(values) {
  const client = normalizeClient(values.adsense_client);
  const left = normalizeAdSlot(values.adsense_left_slot, client);
  const right = normalizeAdSlot(values.adsense_right_slot, client);
  return { enabled: values.ads_enabled === "1" && Boolean(client) && Boolean(left || right), client, left_slot: left, right_slot: right };
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

async function verifyAdminSessionCookie(cookie, siteKey) {
  const parts = String(cookie).split(".");
  if (parts.length === 2) {
    const [token, signature] = parts;
    if (!token || !signature) return false;
    const expected = await hmacSha256(siteKey, token);
    const received = fromBase64Url(signature);
    return constantTimeEqual(expected, received);
  }
  if (parts.length === 3) {
    const timestamp = Number(parts[0]);
    const now = Date.now() / 1000;
    const maxAge = 30 * 24 * 60 * 60;
    if (!Number.isInteger(timestamp) || now - timestamp > maxAge || timestamp - now > 60) return false;
    const expected = await hmacSha256(siteKey, `factburst-admin-session:${parts[0]}.${parts[1]}`);
    return constantTimeEqual(expected, fromBase64Url(parts[2]));
  }
  return false;
}

function normalizeClient(value) {
  const text = String(value || "").trim();
  return /^ca-pub-\d{10,24}$/.test(text) ? text : "";
}

function normalizeSlot(value) {
  const text = String(value || "").trim();
  return /^\d{4,20}$/.test(text) ? text : "";
}

function normalizeAdSlot(value, client) {
  const slot = normalizeSlot(value);
  if (!slot) return "";
  const publisherNumber = String(client || "").replace(/^ca-pub-/, "");
  return publisherNumber && slot === publisherNumber ? "" : slot;
}

export { normalizeClient, normalizeSlot, normalizeAdSlot };

function constantTimeEqual(expected, received) {
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

export async function ensureSettingsTable(db) {
  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_settings (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL DEFAULT '',
      updated_at TEXT NOT NULL
    )
  `).run();
}