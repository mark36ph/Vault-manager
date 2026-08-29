const SESSION_COOKIE = "factburst_session";
const SESSION_DAYS = 30;
const PASSWORD_ITERATIONS = 210_000;
const MAX_LEADERBOARD = 50;
const VERIFICATION_HOURS = 24;
const RESEND_SECONDS = 60;

export async function ensureAccountSchema(db) {
  await db.batch([
    db.prepare(`
      CREATE TABLE IF NOT EXISTS site_users (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        username TEXT NOT NULL,
        username_key TEXT NOT NULL UNIQUE,
        email TEXT NOT NULL DEFAULT '',
        email_key TEXT NOT NULL DEFAULT '',
        email_verified_at TEXT,
        password_hash TEXT NOT NULL,
        password_salt TEXT NOT NULL,
        password_iterations INTEGER NOT NULL,
        created_at TEXT NOT NULL,
        last_login_at TEXT NOT NULL
      )
    `),
    db.prepare(`
      CREATE TABLE IF NOT EXISTS site_sessions (
        token_hash TEXT PRIMARY KEY,
        user_id INTEGER NOT NULL,
        created_at TEXT NOT NULL,
        expires_at TEXT NOT NULL,
        FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
      )
    `),
    db.prepare(`
      CREATE TABLE IF NOT EXISTS site_user_scores (
        user_id INTEGER NOT NULL,
        quiz_id INTEGER NOT NULL,
        best_score INTEGER NOT NULL,
        total INTEGER NOT NULL,
        attempts INTEGER NOT NULL DEFAULT 1,
        first_completed_at TEXT NOT NULL,
        last_completed_at TEXT NOT NULL,
        PRIMARY KEY (user_id, quiz_id),
        FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,
        FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
      )
    `),
    db.prepare(`
      CREATE TABLE IF NOT EXISTS site_email_verifications (
        token_hash TEXT PRIMARY KEY,
        user_id INTEGER NOT NULL,
        email_key TEXT NOT NULL,
        created_at TEXT NOT NULL,
        expires_at TEXT NOT NULL,
        FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
      )
    `),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_sessions_expiry ON site_sessions(expires_at)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_user_scores_quiz ON site_user_scores(quiz_id, best_score DESC)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_email_verifications_user ON site_email_verifications(user_id, created_at DESC)"),
  ]);

  const columns = await db.prepare("PRAGMA table_info(site_users)").all();
  const names = new Set((columns.results || []).map(column => column.name));
  if (!names.has("email")) {
    await db.prepare("ALTER TABLE site_users ADD COLUMN email TEXT NOT NULL DEFAULT ''").run();
  }
  if (!names.has("email_key")) {
    await db.prepare("ALTER TABLE site_users ADD COLUMN email_key TEXT NOT NULL DEFAULT ''").run();
  }
  if (!names.has("email_verified_at")) {
    await db.prepare("ALTER TABLE site_users ADD COLUMN email_verified_at TEXT").run();
  }

  await db.prepare(`
    CREATE UNIQUE INDEX IF NOT EXISTS idx_site_users_email_unique
    ON site_users(email_key) WHERE email_key <> ''
  `).run();
}

export async function handleAccountApi(request, env, url) {
  const { pathname } = url;
  if (pathname === "/api/account" && request.method === "GET") {
    return accountSummary(request, env.DB);
  }
  if (pathname === "/api/account/signup" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return signup(request, env, url);
  }
  if (pathname === "/api/account/login" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return login(request, env.DB);
  }
  if (pathname === "/api/account/logout" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return logout(request, env.DB);
  }
  if (pathname === "/api/account/email" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return setAccountEmail(request, env, url);
  }
  if (pathname === "/api/account/resend-verification" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return resendVerification(request, env, url);
  }
  if (pathname === "/api/account/verify" && request.method === "GET") {
    return verifyEmail(env.DB, url);
  }
  if (pathname === "/api/leaderboard" && request.method === "GET") {
    return overallLeaderboard(request, env.DB, url);
  }

  const quizLeaderboard = pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/leaderboard$/i);
  if (quizLeaderboard && request.method === "GET") {
    return perQuizLeaderboard(request, env.DB, quizLeaderboard[1].toLowerCase(), url);
  }

  return null;
}

export async function requireVerifiedQuizAccess(request, db) {
  const user = await sessionUser(request, db);
  if (!user) {
    return json({
      error: "Sign up or log in and verify your email before playing quizzes.",
      code: "account_required",
    }, 401);
  }
  if (!user.email_verified_at) {
    return json({
      error: user.email
        ? "Verify your email before playing quizzes."
        : "Add and verify an email address before playing quizzes.",
      code: "email_verification_required",
    }, 403);
  }
  return null;
}

export async function recordAuthenticatedScore(request, db, quizId, score, total, completedAt) {
  const user = await sessionUser(request, db);
  if (!user?.email_verified_at) return null;

  await db.prepare(`
    INSERT INTO site_user_scores
      (user_id, quiz_id, best_score, total, attempts, first_completed_at, last_completed_at)
    VALUES (?, ?, ?, ?, 1, ?, ?)
    ON CONFLICT(user_id, quiz_id) DO UPDATE SET
      best_score = CASE
        WHEN site_user_scores.total = excluded.total
          THEN MAX(site_user_scores.best_score, excluded.best_score)
        ELSE excluded.best_score
      END,
      total = excluded.total,
      attempts = CASE
        WHEN site_user_scores.total = excluded.total
          THEN site_user_scores.attempts + 1
        ELSE 1
      END,
      first_completed_at = CASE
        WHEN site_user_scores.total = excluded.total
          THEN site_user_scores.first_completed_at
        ELSE excluded.first_completed_at
      END,
      last_completed_at = excluded.last_completed_at
  `).bind(user.id, quizId, score, total, completedAt, completedAt).run();

  const row = await db.prepare(`
    SELECT best_score, total, attempts, first_completed_at, last_completed_at
    FROM site_user_scores WHERE user_id = ? AND quiz_id = ? LIMIT 1
  `).bind(user.id, quizId).first();

  return row ? {
    username: user.username,
    best_score: Number(row.best_score || 0),
    total: Number(row.total || 0),
    attempts: Number(row.attempts || 0),
  } : null;
}

export function normalizeUsername(value) {
  const username = String(value || "").trim().replace(/\s+/g, " ");
  if (username.length < 3 || username.length > 24) return "";
  if (!/^[A-Za-z0-9][A-Za-z0-9 _.-]*[A-Za-z0-9]$/.test(username)) return "";
  return username;
}

export function normalizeEmail(value) {
  const email = String(value || "").trim();
  if (!email || email.length > 254 || /\s/.test(email)) return "";
  if (!/^[^@]+@[^@]+\.[^@]+$/.test(email)) return "";
  return email;
}

export function normalizeLeaderboardLimit(value) {
  const parsed = Number.parseInt(String(value || "25"), 10);
  if (!Number.isFinite(parsed)) return 25;
  return Math.min(Math.max(parsed, 1), MAX_LEADERBOARD);
}

export function totalPercentage(score, possible) {
  if (!Number.isFinite(Number(possible)) || Number(possible) <= 0) return 0;
  return Math.round((Number(score || 0) / Number(possible)) * 100);
}

export function verificationExpiry(nowIso) {
  return new Date(Date.parse(nowIso) + VERIFICATION_HOURS * 60 * 60 * 1000).toISOString();
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

  const usernameKey = username.toLowerCase();
  const emailKey = email.toLowerCase();
  const existing = await env.DB.prepare(`
    SELECT username_key, email_key FROM site_users
    WHERE username_key = ? OR email_key = ? LIMIT 1
  `).bind(usernameKey, emailKey).first();
  if (existing?.username_key === usernameKey) return json({ error: "That username is already taken." }, 409);
  if (existing?.email_key === emailKey) return json({ error: "That email address is already in use." }, 409);

  const salt = randomToken(18);
  const hash = await passwordHash(password, salt, PASSWORD_ITERATIONS);
  const now = new Date().toISOString();
  try {
    await env.DB.prepare(`
      INSERT INTO site_users
        (username, username_key, email, email_key, email_verified_at,
         password_hash, password_salt, password_iterations, created_at, last_login_at)
      VALUES (?, ?, ?, ?, NULL, ?, ?, ?, ?, ?)
    `).bind(username, usernameKey, email, emailKey, hash, salt, PASSWORD_ITERATIONS, now, now).run();
  } catch (error) {
    if (/unique/i.test(String(error?.message || ""))) {
      return json({ error: "That username or email address is already in use." }, 409);
    }
    throw error;
  }

  const user = await env.DB.prepare(`
    SELECT id, username, email, email_key, email_verified_at
    FROM site_users WHERE username_key = ? LIMIT 1
  `).bind(usernameKey).first();
  if (!user) return json({ error: "Could not create that account." }, 500);

  const session = await createSession(env.DB, user.id, now);
  const delivery = await issueVerification(env, url, user, { bypassRateLimit: true });
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

async function login(request, db) {
  const body = await readJson(request);
  const username = normalizeUsername(body?.username);
  const password = String(body?.password || "");
  if (!username || !password) return json({ error: "Enter your username and password." }, 400);

  const user = await db.prepare(`
    SELECT id, username, email, email_key, email_verified_at,
           password_hash, password_salt, password_iterations
    FROM site_users WHERE username_key = ? LIMIT 1
  `).bind(username.toLowerCase()).first();

  if (!user) {
    await fakePasswordWork(password);
    return json({ error: "Username or password is incorrect." }, 401);
  }

  const hash = await passwordHash(password, user.password_salt, Number(user.password_iterations || PASSWORD_ITERATIONS));
  if (!constantTimeEqual(hash, user.password_hash)) {
    return json({ error: "Username or password is incorrect." }, 401);
  }

  const now = new Date().toISOString();
  await db.prepare("UPDATE site_users SET last_login_at = ? WHERE id = ?").bind(now, user.id).run();
  const session = await createSession(db, user.id, now);
  const summary = await userSummary(db, user);
  return json({ authenticated: true, user: summary }, 200, { "set-cookie": session.cookie });
}

async function logout(request, db) {
  const token = cookieValue(request, SESSION_COOKIE);
  if (token) {
    const tokenHash = await sha256(token);
    await db.prepare("DELETE FROM site_sessions WHERE token_hash = ?").bind(tokenHash).run();
  }
  return json({ authenticated: false }, 200, {
    "set-cookie": `${SESSION_COOKIE}=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0`,
  });
}

async function setAccountEmail(request, env, url) {
  const user = await sessionUser(request, env.DB);
  if (!user) return json({ error: "Log in before changing your email address." }, 401);

  const body = await readJson(request);
  const email = normalizeEmail(body?.email);
  if (!email) return json({ error: "Enter a valid email address." }, 400);
  const emailKey = email.toLowerCase();

  const existing = await env.DB.prepare(`
    SELECT id FROM site_users WHERE email_key = ? AND id <> ? LIMIT 1
  `).bind(emailKey, user.id).first();
  if (existing) return json({ error: "That email address is already in use." }, 409);

  await env.DB.prepare(`
    UPDATE site_users SET email = ?, email_key = ?, email_verified_at = NULL WHERE id = ?
  `).bind(email, emailKey, user.id).run();
  await env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(user.id).run();

  const updated = { ...user, email, email_key: emailKey, email_verified_at: null };
  const delivery = await issueVerification(env, url, updated, { bypassRateLimit: true });
  return json({
    authenticated: true,
    user: await userSummary(env.DB, updated),
    verification_sent: delivery.sent,
    message: delivery.sent ? "Verification email sent." : delivery.error,
  });
}

async function resendVerification(request, env, url) {
  const user = await sessionUser(request, env.DB);
  if (!user) return json({ error: "Log in before requesting another verification email." }, 401);
  if (user.email_verified_at) {
    return json({ authenticated: true, user: await userSummary(env.DB, user), verification_sent: false, message: "Email is already verified." });
  }
  if (!user.email) return json({ error: "Add an email address first." }, 400);

  const delivery = await issueVerification(env, url, user);
  if (delivery.retry_after) {
    return json({ error: `Please wait ${delivery.retry_after} seconds before requesting another email.`, retry_after: delivery.retry_after }, 429);
  }
  return json({
    authenticated: true,
    user: await userSummary(env.DB, user),
    verification_sent: delivery.sent,
    message: delivery.sent ? "Verification email sent." : delivery.error,
  }, delivery.sent ? 200 : 503);
}

async function verifyEmail(db, url) {
  const token = String(url.searchParams.get("token") || "").trim();
  if (token.length < 32 || token.length > 200) return json({ error: "That verification link is not valid." }, 400);

  const tokenHash = await sha256(token);
  const now = new Date().toISOString();
  const row = await db.prepare(`
    SELECT v.user_id, v.email_key, v.expires_at, u.email_key AS current_email_key
    FROM site_email_verifications v
    JOIN site_users u ON u.id = v.user_id
    WHERE v.token_hash = ? LIMIT 1
  `).bind(tokenHash).first();

  if (!row) return json({ error: "That verification link has already been used or is not valid." }, 400);
  if (String(row.expires_at || "") <= now) {
    await db.prepare("DELETE FROM site_email_verifications WHERE token_hash = ?").bind(tokenHash).run();
    return json({ error: "That verification link has expired. Request a new one from your account." }, 410);
  }
  if (String(row.email_key || "") !== String(row.current_email_key || "")) {
    await db.prepare("DELETE FROM site_email_verifications WHERE token_hash = ?").bind(tokenHash).run();
    return json({ error: "That verification link no longer matches your account email." }, 409);
  }

  await db.batch([
    db.prepare("UPDATE site_users SET email_verified_at = ? WHERE id = ?").bind(now, row.user_id),
    db.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(row.user_id),
  ]);
  return json({ verified: true, message: "Email verified. You can now play Factburst quizzes." });
}

async function issueVerification(env, url, user, options = {}) {
  if (!env.EMAIL || typeof env.EMAIL.send !== "function") {
    return { sent: false, error: "Verification email delivery is not configured yet." };
  }
  const from = String(env.EMAIL_FROM || "").trim();
  if (!normalizeEmail(from)) {
    return { sent: false, error: "Verification email sender is not configured yet." };
  }
  const email = normalizeEmail(user.email);
  if (!email) return { sent: false, error: "Add a valid email address first." };

  const now = new Date().toISOString();
  if (!options.bypassRateLimit) {
    const latest = await env.DB.prepare(`
      SELECT created_at FROM site_email_verifications
      WHERE user_id = ? ORDER BY created_at DESC LIMIT 1
    `).bind(user.id).first();
    if (latest?.created_at) {
      const elapsed = Math.floor((Date.parse(now) - Date.parse(latest.created_at)) / 1000);
      if (Number.isFinite(elapsed) && elapsed >= 0 && elapsed < RESEND_SECONDS) {
        return { sent: false, retry_after: RESEND_SECONDS - elapsed };
      }
    }
  }

  const token = randomToken(32);
  const tokenHash = await sha256(token);
  const expiresAt = verificationExpiry(now);
  await env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(user.id).run();
  await env.DB.prepare(`
    INSERT INTO site_email_verifications (token_hash, user_id, email_key, created_at, expires_at)
    VALUES (?, ?, ?, ?, ?)
  `).bind(tokenHash, user.id, String(user.email_key || email.toLowerCase()), now, expiresAt).run();

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
      <p>If you did not create this account, you can ignore this email.</p>
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
    return { sent: false, error: "We could not send the verification email. Try again shortly." };
  }
}

async function accountSummary(request, db) {
  const user = await sessionUser(request, db);
  if (!user) return json({ authenticated: false, user: null });
  return json({ authenticated: true, user: await userSummary(db, user) });
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

  return {
    id: Number(user.id),
    username: String(user.username || ""),
    email: String(user.email || ""),
    email_verified: Boolean(user.email_verified_at),
    email_verified_at: user.email_verified_at || null,
    quizzes_completed: Number(totals?.quizzes_completed || 0),
    total_score: Number(totals?.total_score || 0),
    total_possible: Number(totals?.total_possible || 0),
    percentage: totalPercentage(totals?.total_score, totals?.total_possible),
    attempts: Number(totals?.attempts || 0),
  };
}

async function overallLeaderboard(request, db, url) {
  const limit = normalizeLeaderboardLimit(url.searchParams.get("limit"));
  const result = await db.prepare(`
    WITH totals AS (
      SELECT
        user_id,
        COUNT(*) AS quizzes_completed,
        SUM(best_score) AS total_score,
        SUM(total) AS total_possible,
        SUM(attempts) AS attempts
      FROM site_user_scores
      GROUP BY user_id
    )
    SELECT u.id AS user_id, u.username, t.quizzes_completed, t.total_score, t.total_possible, t.attempts
    FROM totals t
    JOIN site_users u ON u.id = t.user_id
    WHERE u.email_verified_at IS NOT NULL
    ORDER BY t.total_score DESC, t.quizzes_completed DESC,
             (1.0 * t.total_score / NULLIF(t.total_possible, 0)) DESC,
             u.username_key ASC
    LIMIT ?
  `).bind(limit).all();

  const current = await sessionUser(request, db);
  const leaders = (result.results || []).map((row, index) => ({
    rank: index + 1,
    username: String(row.username || ""),
    quizzes_completed: Number(row.quizzes_completed || 0),
    total_score: Number(row.total_score || 0),
    total_possible: Number(row.total_possible || 0),
    percentage: totalPercentage(row.total_score, row.total_possible),
    attempts: Number(row.attempts || 0),
    current_user: Boolean(current && Number(current.id) === Number(row.user_id)),
  }));

  return json({ leaderboard: leaders });
}

async function perQuizLeaderboard(request, db, slug, url) {
  const limit = normalizeLeaderboardLimit(url.searchParams.get("limit"));
  const quiz = await db.prepare(`
    SELECT id, slug, title FROM site_quizzes WHERE slug = ? AND status = 'published' LIMIT 1
  `).bind(slug).first();
  if (!quiz) return json({ error: "Quiz not found." }, 404);

  const result = await db.prepare(`
    SELECT u.id AS user_id, u.username, s.best_score, s.total, s.attempts,
           s.first_completed_at, s.last_completed_at
    FROM site_user_scores s
    JOIN site_users u ON u.id = s.user_id
    WHERE s.quiz_id = ? AND u.email_verified_at IS NOT NULL
    ORDER BY (1.0 * s.best_score / NULLIF(s.total, 0)) DESC,
             s.best_score DESC,
             s.first_completed_at ASC,
             u.username_key ASC
    LIMIT ?
  `).bind(quiz.id, limit).all();

  const current = await sessionUser(request, db);
  const leaders = (result.results || []).map((row, index) => ({
    rank: index + 1,
    username: String(row.username || ""),
    score: Number(row.best_score || 0),
    total: Number(row.total || 0),
    percentage: totalPercentage(row.best_score, row.total),
    attempts: Number(row.attempts || 0),
    current_user: Boolean(current && Number(current.id) === Number(row.user_id)),
  }));

  let mine = null;
  if (current?.email_verified_at) {
    const row = await db.prepare(`
      SELECT best_score, total, attempts FROM site_user_scores
      WHERE user_id = ? AND quiz_id = ? LIMIT 1
    `).bind(current.id, quiz.id).first();
    if (row) {
      const rankRow = await db.prepare(`
        SELECT 1 + COUNT(*) AS rank
        FROM site_user_scores other
        JOIN site_users other_user ON other_user.id = other.user_id
        WHERE other.quiz_id = ? AND other_user.email_verified_at IS NOT NULL AND (
          (1.0 * other.best_score / NULLIF(other.total, 0)) > (1.0 * ? / NULLIF(?, 0)) OR
          ((1.0 * other.best_score / NULLIF(other.total, 0)) = (1.0 * ? / NULLIF(?, 0)) AND other.best_score > ?)
        )
      `).bind(quiz.id, row.best_score, row.total, row.best_score, row.total, row.best_score).first();
      mine = {
        rank: Number(rankRow?.rank || 1),
        score: Number(row.best_score || 0),
        total: Number(row.total || 0),
        percentage: totalPercentage(row.best_score, row.total),
        attempts: Number(row.attempts || 0),
      };
    }
  }

  return json({
    quiz: { slug: String(quiz.slug), title: String(quiz.title) },
    leaderboard: leaders,
    mine,
  });
}

async function sessionUser(request, db) {
  const token = cookieValue(request, SESSION_COOKIE);
  if (!token) return null;
  const tokenHash = await sha256(token);
  const now = new Date().toISOString();
  const user = await db.prepare(`
    SELECT u.id, u.username, u.email, u.email_key, u.email_verified_at
    FROM site_sessions s
    JOIN site_users u ON u.id = s.user_id
    WHERE s.token_hash = ? AND s.expires_at > ?
    LIMIT 1
  `).bind(tokenHash, now).first();
  if (!user) {
    await db.prepare("DELETE FROM site_sessions WHERE token_hash = ?").bind(tokenHash).run();
    return null;
  }
  return user;
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

function validatePassword(password) {
  if (password.length < 10) return "Use a password with at least 10 characters.";
  if (password.length > 128) return "Password is too long.";
  return "";
}

async function fakePasswordWork(password) {
  const salt = "Y29uc3RhbnQtZmFrZS1zYWx0";
  await passwordHash(password, salt, Math.min(PASSWORD_ITERATIONS, 30_000));
}

async function passwordHash(password, salt, iterations) {
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey("raw", encoder.encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits({
    name: "PBKDF2",
    hash: "SHA-256",
    salt: base64UrlDecode(salt),
    iterations,
  }, key, 256);
  return base64UrlEncode(new Uint8Array(bits));
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

function cookieValue(request, name) {
  const header = request.headers.get("cookie") || "";
  for (const part of header.split(";")) {
    const separator = part.indexOf("=");
    if (separator < 0) continue;
    const key = part.slice(0, separator).trim();
    if (key === name) return part.slice(separator + 1).trim();
  }
  return "";
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
