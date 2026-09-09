const BACKUP_KEY = "api_settings_backup_v1";
const TRACKER_API_KEY_MIN_LENGTH = 16;
const TRACKER_BASE_URL = "https://go.factburstquiz.com";

export async function handleApiSettingsBackupApi(request, env, url) {
  const pathname = url.pathname.replace(/\/$/, "");
  if (!pathname.startsWith("/api/admin/api-settings")) return null;
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  try {
    if (request.method === "POST" && pathname === "/api/admin/api-settings/backup") {
      if (!(await isTrackerAuthorized(request, env))) return json({ error: "API settings backup authentication required." }, 401);
      let body;
      try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }
      const settings = sanitizeSettings(body?.settings);
      if (!Object.keys(settings).length) return json({ error: "No API settings were supplied." }, 400);
      const encrypted = await encryptJson(settings, String(env.SITE_ADMIN_KEY || "").trim());
      const now = new Date().toISOString();
      await env.DB.prepare("INSERT INTO site_settings(key,value,updated_at) VALUES (?,?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at").bind(BACKUP_KEY, encrypted, now).run();
      return json({ ok: true, backed_up_at: now, configured: summarize(settings) });
    }
    if (pathname === "/api/admin/api-settings" && request.method === "GET") {
      if (!(await requireAdmin(request, env))) return json({ error: "Administrator authentication required." }, 401);
      const row = await env.DB.prepare("SELECT value,updated_at FROM site_settings WHERE key=? LIMIT 1").bind(BACKUP_KEY).first();
      if (!row?.value) return json({ configured: false, backed_up_at: null, settings: null });
      const settings = await decryptJson(String(row.value), String(env.SITE_ADMIN_KEY || "").trim());
      return json({ configured: true, backed_up_at: row.updated_at || null, settings: summarize(settings) });
    }
    return json({ error: "API settings endpoint not found." }, 404);
  } catch (error) {
    console.error("Factburst API settings backup failed", error);
    return json({ error: "The API settings backup could not be completed." }, 500);
  }
}

function sanitizeSettings(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return {};
  const allowed = [
    "tracker_base_url", "tracker_api_key",
    "openai_api_key", "openai_model",
    "youtube_api_key", "youtube_oauth_client_id", "youtube_oauth_client_secret", "youtube_oauth_refresh_token", "youtube_approved_channel_id", "youtube_approved_channel_name",
    "facebook_page_access_token", "facebook_approved_page_id", "facebook_approved_page_name",
    "instagram_access_token"
  ];
  const result = {};
  for (const key of allowed) {
    const valueText = String(value[key] ?? "").trim();
    if (valueText) result[key] = valueText.slice(0, 10000);
  }
  return result;
}

function summarize(settings) {
  const result = {};
  for (const [key, value] of Object.entries(settings || {})) {
    if (!String(value).trim()) continue;
    if (key.endsWith("_model") || key.endsWith("_base_url") || key.endsWith("_client_id") || key.endsWith("_channel_id") || key.endsWith("_channel_name") || key.endsWith("_page_id") || key.endsWith("_page_name")) {
      result[key] = String(value);
    } else {
      const text = String(value);
      result[key] = `${"•".repeat(Math.min(8, Math.max(4, text.length)))}${text.slice(-4)}`;
    }
  }
  return result;
}

async function isTrackerAuthorized(request, env) {
  const supplied = String(request.headers.get("authorization") || "").replace(/^Bearer\s+/i, "").trim();
  if (supplied.length < TRACKER_API_KEY_MIN_LENGTH) return false;

  const configured = String(env.TRACKER_API_KEY || "").trim();
  if (configured.length >= TRACKER_API_KEY_MIN_LENGTH) return supplied === configured;

  try {
    const response = await fetch(`${TRACKER_BASE_URL}/api/stats`, {
      method: "GET",
      headers: { Authorization: `Bearer ${supplied}` },
    });
    return response.ok;
  } catch (error) {
    console.error("Tracker API key validation failed", error);
    return false;
  }
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
    return constantTimeEqual(await hmacSha256(siteKey, token), fromBase64Url(signature));
  }
  if (parts.length === 3) {
    const timestamp = Number(parts[0]);
    const now = Date.now() / 1000;
    const maxAge = 30 * 24 * 60 * 60;
    if (!Number.isInteger(timestamp) || now - timestamp > maxAge || timestamp - now > 60) return false;
    return constantTimeEqual(await hmacSha256(siteKey, `factburst-admin-session:${parts[0]}.${parts[1]}`), fromBase64Url(parts[2]));
  }
  return false;
}

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

async function deriveAesKey(secret) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(`factburst-api-backup:${secret}`));
  return crypto.subtle.importKey("raw", digest, { name: "AES-GCM" }, false, ["encrypt", "decrypt"]);
}

async function encryptJson(value, secret) {
  if (!secret) throw new Error("Server encryption key is not configured.");
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const key = await deriveAesKey(secret);
  const plaintext = new TextEncoder().encode(JSON.stringify(value));
  const ciphertext = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, plaintext));
  return `${toBase64Url(iv)}.${toBase64Url(ciphertext)}`;
}

async function decryptJson(value, secret) {
  if (!secret) throw new Error("Server encryption key is not configured.");
  const parts = String(value).split(".");
  if (parts.length !== 2) throw new Error("Stored API settings are invalid.");
  const iv = fromBase64Url(parts[0]);
  const ciphertext = fromBase64Url(parts[1]);
  const key = await deriveAesKey(secret);
  const plaintext = await crypto.subtle.decrypt({ name: "AES-GCM", iv }, key, ciphertext);
  return JSON.parse(new TextDecoder().decode(plaintext));
}

function toBase64Url(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function fromBase64Url(value) {
  const normalized = String(value).replace(/-/g, "+").replace(/_/g, "/") + "===".slice((String(value).length + 3) % 4);
  const binary = atob(normalized);
  return Uint8Array.from(binary, char => char.charCodeAt(0));
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "x-factburst-api": "api-settings-backup-v2" } });
}
