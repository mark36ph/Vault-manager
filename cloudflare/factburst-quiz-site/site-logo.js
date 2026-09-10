const LOGO_KEY = "site_logo";
const MAX_LOGO_BYTES = 150 * 1024;

export async function handlePublicLogo(request, env, url) {
  if (url.pathname !== "/brand-icon.png" || (request.method !== "GET" && request.method !== "HEAD")) return null;
  if (!env.DB) return null;
  await ensureLogoTable(env.DB);
  const row = await env.DB.prepare("SELECT value FROM site_settings WHERE key = ? LIMIT 1").bind(LOGO_KEY).first();
  const parsed = parseDataUrl(row?.value);
  if (!parsed) return null;
  const headers = {
    "content-type": parsed.mime,
    "cache-control": "public, max-age=300, must-revalidate",
    "x-content-type-options": "nosniff",
    "content-length": String(parsed.bytes.length),
  };
  if (request.method === "HEAD") return new Response(null, { status: 200, headers });
  return new Response(parsed.bytes, { status: 200, headers });
}

export async function handleAdminLogo(request, env, url) {
  if (!url.pathname.startsWith("/api/admin/site/logo")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  if (!(await requireAdmin(request, env))) return json({ error: "Administrator authentication required." }, 401);
  await ensureLogoTable(env.DB);

  if (request.method === "GET") {
    const row = await env.DB.prepare("SELECT value, updated_at FROM site_settings WHERE key = ? LIMIT 1").bind(LOGO_KEY).first();
    const parsed = parseDataUrl(row?.value);
    return json({ configured: Boolean(parsed), mime: parsed?.mime || "", size: parsed?.bytes.length || 0, updated_at: row?.updated_at || "" });
  }

  if (request.method !== "POST" && request.method !== "PATCH") return json({ error: "Logo endpoint not found." }, 404);

  let body;
  try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }
  const value = String(body?.data_url || "").trim();
  if (!value) return json({ error: "Select a PNG or JPG logo first." }, 400);
  const parsed = parseDataUrl(value);
  if (!parsed) return json({ error: "Logo must be a valid PNG or JPG image." }, 400);
  if (parsed.bytes.length > MAX_LOGO_BYTES) return json({ error: "Logo must be 150 KB or smaller." }, 400);

  const now = new Date().toISOString();
  await env.DB.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES (?,?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at")
    .bind(LOGO_KEY, value, now).run();
  return json({ ok: true, configured: true, mime: parsed.mime, size: parsed.bytes.length, updated_at: now });
}

async function ensureLogoTable(db) {
  await db.prepare(`CREATE TABLE IF NOT EXISTS site_settings (key TEXT PRIMARY KEY, value TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL)`).run();
}

function parseDataUrl(value) {
  const match = String(value || "").match(/^data:(image\/(?:png|jpeg));base64,([A-Za-z0-9+/=\s]+)$/i);
  if (!match) return null;
  try {
    const binary = atob(match[2].replace(/\s/g, ""));
    const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
    return { mime: match[1].toLowerCase(), bytes };
  } catch { return null; }
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
    return constantTimeEqual(expected, fromBase64Url(signature));
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

async function hmacSha256(secret, text) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(text)));
}

function fromBase64Url(value) {
  const normalized = String(value).replace(/-/g, "+").replace(/_/g, "/") + "===".slice((String(value).length + 3) % 4);
  const binary = atob(normalized);
  return Uint8Array.from(binary, char => char.charCodeAt(0));
}

function constantTimeEqual(expected, received) {
  if (expected.length !== received.length) return false;
  let difference = 0;
  for (let i = 0; i < expected.length; i++) difference |= expected[i] ^ received[i];
  return difference === 0;
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "x-content-type-options": "nosniff" } });
}
