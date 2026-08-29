const SESSION_COOKIE = "factburst_session";
const SESSION_DAYS = 30;
const PASSWORD_ITERATIONS = 210_000;
const MAX_LEADERBOARD = 50;

export async function ensureAccountSchema(db) {
  await db.batch([
    db.prepare(`
      CREATE TABLE IF NOT EXISTS site_users (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        username TEXT NOT NULL,
        username_key TEXT NOT NULL UNIQUE,
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
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_sessions_expiry ON site_sessions(expires_at)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_user_scores_quiz ON site_user_scores(quiz_id, best_score DESC)"),
  ]);
}

export async function handleAccountApi(request, env, url) {
  const { pathname } = url;
  if (pathname === "/api/account" && request.method === "GET") {
    return accountSummary(request, env.DB);
  }
  if (pathname === "/api/account/signup" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return signup(request, env.DB);
  }
  if (pathname === "/api/account/login" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return login(request, env.DB);
  }
  if (pathname === "/api/account/logout" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return logout(request, env.DB);
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

export async function recordAuthenticatedScore(request, db, quizId, score, total, completedAt) {
  const user = await sessionUser(request, db);
  if (!user) return null;

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

export function normalizeLeaderboardLimit(value) {
  const parsed = Number.parseInt(String(value || "25"), 10);
  if (!Number.isFinite(parsed)) return 25;
  return Math.min(Math.max(parsed, 1), MAX_LEADERBOARD);
}

export function totalPercentage(score, possible) {
  if (!Number.isFinite(Number(possible)) || Number(possible) <= 0) return 0;
  return Math.round((Number(score || 0) / Number(possible)) * 100);
}

async function signup(request, db) {
  const body = await readJson(request);
  if (String(body?.website || "").trim()) return json({ error: "Could not create that account." }, 400);

  const username = normalizeUsername(body?.username);
  const password = String(body?.password || "");
  if (!username) {
    return json({ error: "Choose a username 3–24 characters long using letters, numbers, spaces, dots, dashes or underscores." }, 400);
  }
  const passwordError = validatePassword(password);
  if (passwordError) return json({ error: passwordError }, 400);

  const usernameKey = username.toLowerCase();
  const existing = await db.prepare("SELECT id FROM site_users WHERE username_key = ? LIMIT 1").bind(usernameKey).first();
  if (existing) return json({ error: "That username is already taken." }, 409);

  const salt = randomToken(18);
  const hash = await passwordHash(password, salt, PASSWORD_ITERATIONS);
  const now = new Date().toISOString();
  try {
    await db.prepare(`
      INSERT INTO site_users
        (username, username_key, password_hash, password_salt, password_iterations, created_at, last_login_at)
      VALUES (?, ?, ?, ?, ?, ?, ?)
    `).bind(username, usernameKey, hash, salt, PASSWORD_ITERATIONS, now, now).run();
  } catch (error) {
    if (/unique/i.test(String(error?.message || ""))) return json({ error: "That username is already taken." }, 409);
    throw error;
  }

  const user = await db.prepare("SELECT id, username FROM site_users WHERE username_key = ? LIMIT 1").bind(usernameKey).first();
  if (!user) return json({ error: "Could not create that account." }, 500);
  const session = await createSession(db, user.id, now);
  const summary = await userSummary(db, user);
  return json({ authenticated: true, user: summary }, 201, { "set-cookie": session.cookie });
}

async function login(request, db) {
  const body = await readJson(request);
  const username = normalizeUsername(body?.username);
  const password = String(body?.password || "");
  if (!username || !password) return json({ error: "Enter your username and password." }, 400);

  const user = await db.prepare(`
    SELECT id, username, password_hash, password_salt, password_iterations
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
    WHERE s.quiz_id = ?
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
  if (current) {
    const row = await db.prepare(`
      SELECT best_score, total, attempts FROM site_user_scores
      WHERE user_id = ? AND quiz_id = ? LIMIT 1
    `).bind(current.id, quiz.id).first();
    if (row) {
      const rankRow = await db.prepare(`
        SELECT 1 + COUNT(*) AS rank
        FROM site_user_scores other
        WHERE other.quiz_id = ? AND (
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
    SELECT u.id, u.username
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
