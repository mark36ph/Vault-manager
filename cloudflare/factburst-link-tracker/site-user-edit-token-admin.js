const EDIT_TOKEN_MINUTES = 5;
const SITE_ACCOUNT_ORIGIN = "https://factburst-quiz-site.factburstquiz.workers.dev";

export async function handleSiteUserEditTokenAdmin(request, env, url) {
  const match = url.pathname.match(/^\/api\/site\/users\/(\d+)\/edit-token$/);
  if (!match || request.method !== "POST") return null;

  const userId = Number.parseInt(match[1], 10);
  if (!Number.isInteger(userId) || userId <= 0) return json({ error: "Invalid user id." }, 400);

  const user = await env.DB.prepare("SELECT id FROM site_users WHERE id = ? LIMIT 1").bind(userId).first();
  if (!user) return json({ error: "User not found." }, 404);

  await env.DB.prepare(`
    CREATE TABLE IF NOT EXISTS site_admin_user_edit_tokens (
      token_hash TEXT PRIMARY KEY,
      user_id INTEGER NOT NULL,
      created_at TEXT NOT NULL,
      expires_at TEXT NOT NULL,
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
    )
  `).run();
  await env.DB.prepare("CREATE INDEX IF NOT EXISTS idx_site_admin_user_edit_tokens_expiry ON site_admin_user_edit_tokens(expires_at)").run();

  const token = randomToken(32);
  const tokenHash = await sha256(token);
  const now = new Date().toISOString();
  const expiresAt = new Date(Date.now() + EDIT_TOKEN_MINUTES * 60 * 1000).toISOString();

  await env.DB.batch([
    env.DB.prepare("DELETE FROM site_admin_user_edit_tokens WHERE expires_at <= ? OR user_id = ?").bind(now, userId),
    env.DB.prepare(`
      INSERT INTO site_admin_user_edit_tokens (token_hash, user_id, created_at, expires_at)
      VALUES (?, ?, ?, ?)
    `).bind(tokenHash, userId, now, expiresAt),
  ]);

  return json({
    edit_token: token,
    edit_url: `${SITE_ACCOUNT_ORIGIN}/api/account/admin-edit`,
    expires_at: expiresAt,
  }, 201);
}

function randomToken(byteCount) {
  const bytes = new Uint8Array(byteCount);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes);
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
