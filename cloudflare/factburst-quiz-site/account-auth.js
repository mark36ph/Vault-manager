const SESSION_COOKIE = "factburst_session";
const SESSION_DAYS = 30;
const PASSWORD_ITERATIONS = 25_000;
const PASSWORD_SCHEME = "pbkdf2-sha256-pepper-v1";
const VERIFICATION_HOURS = 24;

export const PASSWORD_POLICY = Object.freeze({
  iterations: PASSWORD_ITERATIONS,
  scheme: PASSWORD_SCHEME,
});

export async function handleAuthApi(request, env, url) {
  const { pathname } = url;
  if (pathname !== "/api/account/signup" && pathname !== "/api/account/login") return null;
  if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);

  try {
    if (pathname === "/api/account/signup" && request.method === "POST") {
      return signup(request, env, url);
    }
    if (pathname === "/api/account/login" && request.method === "POST") {
      return login(request, env);
    }
    return null;
  } catch (error) {
    console.error("Factburst account authentication failed", error);
    return json({
      error: "Account authentication is temporarily unavailable. Please try again shortly.",
      code: "account_auth_error",
    }, 503);
  }
}

export async function derivePasswordHash(password, salt, pepper, iterations = PASSWORD_ITERATIONS) {
  const passwordBytes = new TextEncoder().encode(String(password || ""));
  const pepperKey = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(String(pepper || "")),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const peppered = await crypto.subtle.sign("HMAC", pepperKey, passwordBytes);
  const pbkdfKey = await crypto.subtle.importKey("raw", peppered, "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits({
    name: "PBKDF2",
    hash: "SHA-256",
    salt: base64UrlDecode(salt),
    iterations,
  }, pbkdfKey, 256);
  return base64UrlEncode(new Uint8Array(bits));
}

async function signup(request, env, url) {
  const body = await readJson(request);
  if (String(body?.website || "").trim()) return json({ error: "Could not create that account." }, 400);

  const username = normalizeUsername(body?.username);
  const email = normalizeEmail(body?.email);
  const password = String(body?.password || "");
  if (!username) {
    return json({ error: "Choose a username 3–24 characters long using letters, numbers, spaces, dots, dashes or underscores." }, 400);
  }
  if (!email) return json({ error: "Enter a valid email address." }, 400);
  const passwordError = validatePassword(password);
  if (passwordError) return json({ error: passwordError }, 400);

  const pepper = String(env.PASSWORD_PEPPER || "").trim();
  if (pepper.length < 32) {
    return json({
      error: "Account password security is still being configured. Please try again shortly.",
      code: "password_security_not_configured",
    }, 503);
  }

  const usernameKey = username.toLowerCase();
  const emailKey = email.toLowerCase();
  const existing = await env.DB.prepare(`
    SELECT username_key, email_key FROM site_users
    WHERE username_key = ? OR email_key = ? LIMIT 1
  `).bind(usernameKey, emailKey).first();
  if (existing?.username_key === usernameKey) return json({ error: "That username is already taken." }, 409);
  if (existing?.email_key === emailKey) return json({ error: "That email address is already in use." }, 409);

  const salt = randomToken(18);
  const hash = await derivePasswordHash(password, salt, pepper, PASSWORD_ITERATIONS);
  const now = new Date().toISOString();

  try {
    await env.DB.prepare(`
      INSERT INTO site_users
        (username, username_key, email, email_key, email_verified_at,
         password_hash, password_salt, password_iterations, password_scheme,
         created_at, last_login_at)
      VALUES (?, ?, ?, ?, NULL, ?, ?, ?, ?, ?, ?)
    `).bind(
      username,
      usernameKey,
      email,
      emailKey,
      hash,
      salt,
      PASSWORD_ITERATIONS,
      PASSWORD_SCHEME,
      now,
      now,
    ).run();
  } catch (error) {
    if (/unique/i.test(String(error?.message || ""))) {
      return json({ error: "That username or email address is already in use." }, 409);
    }
    console.error("Factburst signup user insert failed", error);
    return json({ error: "Could not create the account record. Please try again.", code: "signup_user_write_failed" }, 503);
  }

  const user = await env.DB.prepare(`
    SELECT id, username, email, email_key, email_verified_at
    FROM site_users WHERE username_key = ? LIMIT 1
  `).bind(usernameKey).first();
  if (!user) {
    return json({ error: "Could not finish creating that account. Please try again.", code: "signup_user_read_failed" }, 503);
  }

  let session;
  try {
    session = await createSession(env.DB, user.id, now);
  } catch (error) {
    console.error("Factburst signup session creation failed", error);
    return json({ error: "Your account was created, but sign-in could not be started. Please log in.", code: "signup_session_failed" }, 503);
  }

  const delivery = await issueVerification(env, url, user);
  const summary = await userSummary(env.DB, user);
  return json({
    authenticated: true,
    user: summary,
    verification_sent: delivery.sent,
    message: delivery.sent
      ? "Account created. Check your email and verify it before playing."
      : delivery.error,
  }, 201, { "set-cookie": session.cookie });
}

async function login(request, env) {
  const body = await readJson(request);
  const username = normalizeUsername(body?.username);
  const password = String(body?.password || "");
  if (!username || !password) return json({ error: "Enter your username and password." }, 400);

  const user = await env.DB.prepare(`
    SELECT id, username, email, email_key, email_verified_at,
           password_hash, password_salt, password_iterations, password_scheme
    FROM site_users WHERE username_key = ? LIMIT 1
  `).bind(username.toLowerCase()).first();

  if (!user) {
    await fakePasswordWork(password, String(env.PASSWORD_PEPPER || ""));
    return json({ error: "Username or password is incorrect." }, 401);
  }

  const scheme = String(user.password_scheme || "");
  if (scheme !== PASSWORD_SCHEME) {
    return json({
      error: "This account needs a password security upgrade before it can log in. Please create a new account for now.",
      code: "legacy_password_upgrade_required",
    }, 409);
  }

  const pepper = String(env.PASSWORD_PEPPER || "").trim();
  if (pepper.length < 32) {
    return json({
      error: "Account password security is temporarily unavailable. Please try again shortly.",
      code: "password_security_not_configured",
    }, 503);
  }

  const iterations = Number(user.password_iterations || PASSWORD_ITERATIONS);
  const hash = await derivePasswordHash(password, user.password_salt, pepper, iterations);
  if (!constantTimeEqual(hash, user.password_hash)) {
    return json({ error: "Username or password is incorrect." }, 401);
  }

  const now = new Date().toISOString();
  await env.DB.prepare("UPDATE site_users SET last_login_at = ? WHERE id = ?").bind(now, user.id).run();
  const session = await createSession(env.DB, user.id, now);
  const summary = await userSummary(env.DB, user);
  return json({ authenticated: true, user: summary }, 200, { "set-cookie": session.cookie });
}

async function issueVerification(env, url, user) {
  if (!env.EMAIL || typeof env.EMAIL.send !== "function") {
    return { sent: false, error: "Account created, but verification email delivery is not configured yet." };
  }
  const from = String(env.EMAIL_FROM || "").trim();
  const email = normalizeEmail(user.email);
  if (!normalizeEmail(from) || !email) {
    return { sent: false, error: "Account created, but the verification email could not be prepared." };
  }

  const now = new Date().toISOString();
  const token = randomToken(32);
  const tokenHash = await sha256(token);
  const expiresAt = new Date(Date.parse(now) + VERIFICATION_HOURS * 60 * 60 * 1000).toISOString();

  try {
    await env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(user.id).run();
    await env.DB.prepare(`
      INSERT INTO site_email_verifications (token_hash, user_id, email_key, created_at, expires_at)
      VALUES (?, ?, ?, ?, ?)
    `).bind(tokenHash, user.id, String(user.email_key || email.toLowerCase()), now, expiresAt).run();
  } catch (error) {
    console.error("Factburst verification token write failed", error);
    return { sent: false, error: "Account created, but the verification email could not be prepared. Use Resend verification from your account." };
  }

  const verifyUrl = new URL("/", url.origin);
  verifyUrl.searchParams.set("verify_email", token);
  const username = String(user.username || "Factburst player");
  const text = [
    `Hi ${username},`,
    "",
    "Verify your email to unlock Factburst Quiz:",
    verifyUrl.toString(),
    "",
    `This link expires in ${VERIFICATION_HOURS} hours and can only be used once.`,
    "If you did not create this account, you can ignore this email.",
  ].join("\n");
  const html = `
    <div style="font-family:Arial,sans-serif;line-height:1.55;color:#101828">
      <h2>Verify your Factburst Quiz email</h2>
      <p>Hi ${escapeHtml(username)},</p>
      <p>Verify your email to unlock quizzes and save scores to the leaderboards.</p>
      <p><a href="${escapeHtml(verifyUrl.toString())}" style="display:inline-block;padding:12px 18px;background:#087ea4;color:white;text-decoration:none;border-radius:8px;font-weight:700">Verify email</a></p>
      <p>This link expires in ${VERIFICATION_HOURS} hours and can only be used once.</p>
    </div>`;

  try {
    await env.EMAIL.send({
      from: { email: from, name: "Factburst Quiz" },
      to: [email],
      subject: "Verify your Factburst Quiz email",
      text,
      html,
    });
    return { sent: true };
  } catch (error) {
    console.error("Factburst verification email failed", error);
    return { sent: false, error: "Account created, but we could not send the verification email. Use Resend verification from your account." };
  }
}

async function createSession(db, userId, now) {
  const token = randomToken(32);
  const tokenHash = await sha256(token);
  const expires = new Date(Date.parse(now) + SESSION_DAYS * 24 * 60 * 60 * 1000).toISOString();
  await db.prepare("DELETE FROM site_sessions WHERE expires_at <= ?").bind(now).run();
  await db.prepare(`
    INSERT INTO site_sessions (token_hash, user_id, created_at, expires_at) VALUES (?, ?, ?, ?)
  `).bind(tokenHash, userId, now, expires).run();
  return {
    cookie: `${SESSION_COOKIE}=${token}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${SESSION_DAYS * 24 * 60 * 60}`,
  };
}

async function userSummary(db, user) {
  const totals = await db.prepare(`
    SELECT
      COUNT(*) AS quizzes_completed,
      COALESCE(SUM(best_score), 0) AS total_score,
      COALESCE(SUM(total), 0) AS total_possible,
      COALESCE(SUM(attempts), 0) AS attempts
    FROM site_user_scores WHERE user_id = ?
  `).bind(user.id).first();
  const totalScore = Number(totals?.total_score || 0);
  const totalPossible = Number(totals?.total_possible || 0);
  return {
    id: Number(user.id),
    username: String(user.username || ""),
    email: String(user.email || ""),
    email_verified: Boolean(user.email_verified_at),
    email_verified_at: user.email_verified_at || null,
    quizzes_completed: Number(totals?.quizzes_completed || 0),
    total_score: totalScore,
    total_possible: totalPossible,
    percentage: totalPossible > 0 ? Math.round((totalScore / totalPossible) * 100) : 0,
    attempts: Number(totals?.attempts || 0),
  };
}

async function fakePasswordWork(password, pepper) {
  const safePepper = pepper.length >= 32 ? pepper : "factburst-temporary-password-work-pepper";
  await derivePasswordHash(password, "Y29uc3RhbnQtZmFrZS1zYWx0", safePepper, 10_000);
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

function validatePassword(password) {
  if (password.length < 10) return "Use a password with at least 10 characters.";
  if (password.length > 128) return "Password is too long.";
  return "";
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

function constantTimeEqual(left, right) {
  const a = new TextEncoder().encode(String(left || ""));
  const b = new TextEncoder().encode(String(right || ""));
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let index = 0; index < a.length; index++) diff |= a[index] ^ b[index];
  return diff === 0;
}

function base64UrlEncode(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function base64UrlDecode(value) {
  const normalized = String(value || "").replace(/-/g, "+").replace(/_/g, "/");
  const padded = normalized + "=".repeat((4 - (normalized.length % 4)) % 4);
  const binary = atob(padded);
  return Uint8Array.from(binary, character => character.charCodeAt(0));
}

function sameOrigin(request, url) {
  const origin = String(request.headers.get("origin") || "").trim();
  return origin === url.origin;
}

function escapeHtml(value) {
  return String(value || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

async function readJson(request) {
  try {
    return await request.json();
  } catch {
    return null;
  }
}

function json(value, status = 200, extraHeaders = {}) {
  const headers = new Headers({
    "content-type": "application/json; charset=utf-8",
    "cache-control": "no-store",
    "x-content-type-options": "nosniff",
    ...extraHeaders,
  });
  return new Response(JSON.stringify(value), { status, headers });
}
