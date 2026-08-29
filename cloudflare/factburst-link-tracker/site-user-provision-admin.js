const ADMIN_SIGNUP_MINUTES = 10;
const SITE_SIGNUP_ORIGIN = "https://factburst-quiz-site.factburstquiz.workers.dev";

export async function handleSiteUserProvisionAdmin(request, env, url) {
  const path = url.pathname.replace(/^\/+|\/+$/g, "");

  if (path === "api/site/users/provision" && request.method === "POST") {
    return provisionUserSignup(request, env);
  }

  const activateMatch = path.match(/^api\/site\/users\/(\d+)\/activate$/);
  if (activateMatch && request.method === "POST") {
    return activateUserProfile(env, Number.parseInt(activateMatch[1], 10));
  }

  return null;
}

async function provisionUserSignup(request, env) {
  if (!await ensureUserProvisionSchema(env.DB)) {
    return json({ error: "Website user accounts are not available yet." }, 503);
  }

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }

  const username = normalizeUsername(body?.username);
  const email = normalizeEmail(body?.email);
  if (!username) {
    return json({ error: "Choose a username 3–24 characters long using letters, numbers, spaces, dots, dashes or underscores." }, 400);
  }
  if (!email) return json({ error: "Enter a valid email address." }, 400);

  const usernameKey = username.toLowerCase();
  const emailKey = email.toLowerCase();
  const existing = await env.DB.prepare(`
    SELECT username_key, email_key FROM site_users
    WHERE username_key = ? OR email_key = ?
    LIMIT 1
  `).bind(usernameKey, emailKey).first();
  if (existing?.username_key === usernameKey) return json({ error: "That username is already taken." }, 409);
  if (existing?.email_key === emailKey) return json({ error: "That email address is already in use." }, 409);

  const token = randomToken(32);
  const tokenHash = await sha256(token);
  const now = new Date().toISOString();
  const expiresAt = new Date(Date.parse(now) + ADMIN_SIGNUP_MINUTES * 60 * 1000).toISOString();
  const emailVerified = body?.email_verified !== false;

  await env.DB.batch([
    env.DB.prepare("DELETE FROM site_admin_signup_tokens WHERE expires_at <= ?").bind(now),
    env.DB.prepare("DELETE FROM site_admin_signup_tokens WHERE username_key = ? OR email_key = ?").bind(usernameKey, emailKey),
    env.DB.prepare(`
      INSERT INTO site_admin_signup_tokens
        (token_hash, username_key, email_key, email_verified, created_at, expires_at)
      VALUES (?, ?, ?, ?, ?, ?)
    `).bind(tokenHash, usernameKey, emailKey, emailVerified ? 1 : 0, now, expiresAt),
  ]);

  return json({
    signup_token: token,
    signup_url: `${SITE_SIGNUP_ORIGIN}/api/account/admin-signup`,
    origin: SITE_SIGNUP_ORIGIN,
    expires_at: expiresAt,
    email_verified: emailVerified,
  }, 201);
}

async function activateUserProfile(env, userId) {
  if (!Number.isInteger(userId) || userId <= 0) return json({ error: "Invalid user id." }, 400);
  if (!await ensureUserProvisionSchema(env.DB)) return json({ error: "User not found." }, 404);

  const existing = await env.DB.prepare("SELECT id, username FROM site_users WHERE id = ? LIMIT 1").bind(userId).first();
  if (!existing) return json({ error: "User not found." }, 404);

  const now = new Date().toISOString();
  const statements = [
    env.DB.prepare(`
      UPDATE site_users
      SET email_verified_at = COALESCE(email_verified_at, ?),
          status = 'active',
          suspended_at = NULL,
          suspension_reason = ''
      WHERE id = ?
    `).bind(now, userId),
  ];
  if (await tableExists(env.DB, "site_email_verifications")) {
    statements.push(env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(userId));
  }
  await env.DB.batch(statements);

  const updated = await env.DB.prepare(`
    SELECT id, username, email, email_verified_at, status
    FROM site_users WHERE id = ? LIMIT 1
  `).bind(userId).first();

  return json({
    activated: true,
    user: {
      id: Number(updated?.id || userId),
      username: String(updated?.username || existing.username || ""),
      email: String(updated?.email || ""),
      email_verified: Boolean(updated?.email_verified_at),
      email_verified_at: updated?.email_verified_at ? String(updated.email_verified_at) : now,
      status: String(updated?.status || "active"),
    },
  });
}

async function ensureUserProvisionSchema(db) {
  const table = await db.prepare(`
    SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'site_users' LIMIT 1
  `).first();
  if (!table) return false;

  const columns = await db.prepare("PRAGMA table_info(site_users)").all();
  const names = new Set((columns.results || []).map(column => String(column.name || "")));
  if (!names.has("email_verified_at")) await db.prepare("ALTER TABLE site_users ADD COLUMN email_verified_at TEXT").run();
  if (!names.has("status")) await db.prepare("ALTER TABLE site_users ADD COLUMN status TEXT NOT NULL DEFAULT 'active'").run();
  if (!names.has("suspended_at")) await db.prepare("ALTER TABLE site_users ADD COLUMN suspended_at TEXT").run();
  if (!names.has("suspension_reason")) await db.prepare("ALTER TABLE site_users ADD COLUMN suspension_reason TEXT NOT NULL DEFAULT ''").run();

  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_admin_signup_tokens (
      token_hash TEXT PRIMARY KEY,
      username_key TEXT NOT NULL,
      email_key TEXT NOT NULL,
      email_verified INTEGER NOT NULL DEFAULT 1,
      created_at TEXT NOT NULL,
      expires_at TEXT NOT NULL
    )
  `).run();
  await db.prepare("CREATE INDEX IF NOT EXISTS idx_site_admin_signup_tokens_expiry ON site_admin_signup_tokens(expires_at)").run();
  return true;
}

async function tableExists(db, tableName) {
  const row = await db.prepare(`
    SELECT name FROM sqlite_master WHERE type = 'table' AND name = ? LIMIT 1
  `).bind(tableName).first();
  return Boolean(row);
}

function normalizeUsername(value) {
  const username = String(value || "").trim().replace(/\s+/g, " ");
  if (username.length < 3 || username.length > 24) return "";
  if (!/^[A-Za-z0-9][A-Za-z0-9 _.-]*[A-Za-z0-9]$/.test(username)) return "";
  return username;
}

function normalizeEmail(value) {
  const email = String(value || "").trim();
  if (!email || email.length > 254 || /\s/.test(email)) return "";
  if (!/^[^@]+@[^@]+\.[^@]+$/.test(email)) return "";
  return email;
}

async function sha256(value) {
  const bytes = new TextEncoder().encode(String(value || ""));
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return base64UrlEncode(new Uint8Array(digest));
}

function randomToken(byteCount) {
  const bytes = new Uint8Array(byteCount);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes);
}

function base64UrlEncode(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
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
