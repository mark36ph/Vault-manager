import { reservedUsernameReason } from "./account-policy.js";

const SESSION_COOKIE = "factburst_session";

export async function enforceAccountRequestPolicy(request, db, url) {
  const { pathname } = url;

  if (pathname === "/api/account/signup" && request.method === "POST") {
    const body = await readJsonClone(request);
    const reason = reservedUsernameReason(body?.username);
    if (reason) {
      return json({ error: reason, code: "username_reserved" }, 400);
    }
    return null;
  }

  if (pathname === "/api/account/login" && request.method === "POST") {
    const body = await readJsonClone(request);
    const usernameKey = String(body?.username || "").trim().replace(/\s+/g, " ").toLowerCase();
    if (!usernameKey) return null;
    const user = await db.prepare(`
      SELECT status FROM site_users WHERE username_key = ? LIMIT 1
    `).bind(usernameKey).first();
    if (String(user?.status || "active").toLowerCase() === "suspended") {
      return json({ error: suspendedAccountMessage(), code: "account_suspended" }, 403);
    }
    return null;
  }

  if (pathname === "/api/account" ||
      pathname === "/api/account/email" ||
      pathname === "/api/account/resend-verification" ||
      pathname === "/api/account/history") {
    return enforceActiveSession(request, db);
  }

  return null;
}

export async function enforceActiveSession(request, db) {
  const token = cookieValue(request, SESSION_COOKIE);
  if (!token) return null;

  const tokenHash = await sha256(token);
  const row = await db.prepare(`
    SELECT s.token_hash, s.user_id, u.status
    FROM site_sessions s
    LEFT JOIN site_users u ON u.id = s.user_id
    WHERE s.token_hash = ?
    LIMIT 1
  `).bind(tokenHash).first();

  if (!row) return null;
  const status = String(row.status || "active").toLowerCase();
  if (status === "active") return null;

  await db.prepare("DELETE FROM site_sessions WHERE token_hash = ?").bind(tokenHash).run();
  return json({
    error: suspendedAccountMessage(),
    code: "account_suspended",
  }, 403, {
    "set-cookie": `${SESSION_COOKIE}=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0`,
  });
}

export async function activeSessionUser(request, db) {
  const token = cookieValue(request, SESSION_COOKIE);
  if (!token) return null;
  const tokenHash = await sha256(token);
  const now = new Date().toISOString();
  return db.prepare(`
    SELECT u.id, u.username, u.email, u.email_key, u.email_verified_at, u.status
    FROM site_sessions s
    JOIN site_users u ON u.id = s.user_id
    WHERE s.token_hash = ?
      AND s.expires_at > ?
      AND COALESCE(u.status, 'active') = 'active'
    LIMIT 1
  `).bind(tokenHash, now).first();
}

export function suspendedAccountMessage() {
  return "Your Factburst account has been suspended. Contact Factburst support if you think this is a mistake.";
}

async function readJsonClone(request) {
  try {
    return await request.clone().json();
  } catch {
    return null;
  }
}

function cookieValue(request, name) {
  const header = request.headers.get("cookie") || "";
  for (const part of header.split(";")) {
    const separator = part.indexOf("=");
    if (separator < 0) continue;
    if (part.slice(0, separator).trim() === name) return part.slice(separator + 1).trim();
  }
  return "";
}

async function sha256(value) {
  const bytes = new TextEncoder().encode(String(value || ""));
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return base64UrlEncode(new Uint8Array(digest));
}

function base64UrlEncode(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function json(value, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(value), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
      ...extraHeaders,
    },
  });
}
