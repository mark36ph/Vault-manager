const JSON_HEADERS = {
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store",
};

const MAX_IMAGE_DATA_URL_LENGTH = 1_250_000;
const IMAGE_PREFIX = "quiz-images/";
const IMAGE_ROUTE_PREFIX = `/${IMAGE_PREFIX}`;
const IMAGE_CACHE_CONTROL = "public, max-age=31536000, immutable";
const ADMIN_SESSION_COOKIE = "fb_admin_session";
const ADMIN_SESSION_SECONDS = 30 * 24 * 60 * 60;
let schemaReady = false;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname.startsWith(IMAGE_ROUTE_PREFIX)) return serveQuizImage(request, env, url);
    if (url.pathname.startsWith("/api/")) {
      try { return await handleApi(request, env, url); }
      catch (error) { console.error("Factburst site API error", error); return json({ error: "Something went wrong." }, 500); }
    }
    if (!env.ASSETS) return new Response("Factburst Quiz website assets are not configured.", { status: 503 });
    return env.ASSETS.fetch(request);
  },
};

async function serveQuizImage(request, env, url) {
  if (request.method !== "GET" && request.method !== "HEAD") return new Response("Method not allowed.", { status: 405, headers: { allow: "GET, HEAD" } });
  if (!env.QUIZ_IMAGES) return new Response("Quiz image storage is not configured.", { status: 503 });
  const key = url.pathname.slice(1);
  if (!/^quiz-images\/[a-z0-9][a-z0-9-]{0,79}\/q\d{3}-[a-f0-9]{20}\.png$/.test(key)) return new Response("Image not found.", { status: 404 });
  const object = await env.QUIZ_IMAGES.get(key);
  if (!object) return new Response("Image not found.", { status: 404 });
  const headers = new Headers();
  object.writeHttpMetadata(headers);
  headers.set("etag", object.httpEtag);
  headers.set("cache-control", object.httpMetadata?.cacheControl || IMAGE_CACHE_CONTROL);
  headers.set("x-content-type-options", "nosniff");
  return new Response(request.method === "HEAD" ? null : object.body, { headers });
}

async function handleApi(request, env, url) {
  if (url.pathname === "/api/health" && request.method === "GET") return json({ ok: true, service: "factburst-quiz-site", image_storage: env.QUIZ_IMAGES ? "r2" : "unavailable" });
  if (url.pathname === "/api/admin/auth/setup" && request.method === "POST") return adminAuthSetup(request, env);
  if (url.pathname === "/api/admin/auth/login" && request.method === "POST") return adminAuthLogin(request, env);
  if (url.pathname === "/api/admin/auth/session" && request.method === "GET") return adminAuthSession(request, env);
  if (url.pathname === "/api/admin/auth/logout" && request.method === "POST") return adminAuthLogout();
  if (!env.DB) return json({ error: "The quiz database is not configured yet." }, 503);
  await ensureSchema(env.DB);
  if (url.pathname === "/api/quizzes" && request.method === "GET") return listQuizzes(env.DB, url);
  if (url.pathname === "/api/quizzes/latest" && request.method === "GET") return latestQuiz(env.DB);
  if (url.pathname === "/api/admin/quizzes" && request.method === "GET") return listAdminQuizzes(request, env);
  if (url.pathname === "/api/admin/quizzes" && request.method === "POST") return upsertQuiz(request, env);
  const adminQuizMatch = url.pathname.match(/^\/api\/admin\/quizzes\/([a-z0-9][a-z0-9-]{0,79})$/i);
  if (adminQuizMatch && request.method === "GET") return getAdminQuiz(request, env, adminQuizMatch[1].toLowerCase());
  const quizMatch = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})$/i);
  if (quizMatch && request.method === "GET") return getQuiz(env, quizMatch[1].toLowerCase());
  const scoreMatch = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/score$/i);
  if (scoreMatch && request.method === "POST") return scoreQuiz(request, env.DB, scoreMatch[1].toLowerCase());
  return json({ error: "Not found." }, 404);
}

async function ensureSchema(db) {
  if (schemaReady) return;
  await db.batch([
    db.prepare(`CREATE TABLE IF NOT EXISTS site_quizzes (id INTEGER PRIMARY KEY AUTOINCREMENT, slug TEXT NOT NULL UNIQUE, title TEXT NOT NULL, category TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', youtube_url TEXT NOT NULL DEFAULT '', publish_at TEXT, status TEXT NOT NULL DEFAULT 'draft', created_at TEXT NOT NULL, updated_at TEXT NOT NULL)`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_questions (id INTEGER PRIMARY KEY AUTOINCREMENT, quiz_id INTEGER NOT NULL, position INTEGER NOT NULL, question TEXT NOT NULL, answer_a TEXT NOT NULL, answer_b TEXT NOT NULL, answer_c TEXT NOT NULL, answer_d TEXT NOT NULL, correct_answer TEXT NOT NULL, explanation TEXT NOT NULL DEFAULT '', image_key TEXT NOT NULL DEFAULT '', image_data_url TEXT NOT NULL DEFAULT '', UNIQUE(quiz_id, position), FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE)`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_attempts (id INTEGER PRIMARY KEY AUTOINCREMENT, quiz_id INTEGER NOT NULL, score INTEGER NOT NULL, total INTEGER NOT NULL, completed_at TEXT NOT NULL, FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE)`),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_quizzes_publish ON site_quizzes(status, publish_at)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_attempts_quiz ON site_attempts(quiz_id, completed_at)"),
  ]);
  const columns = await db.prepare("PRAGMA table_info(site_questions)").all();
  const names = new Set((columns.results || []).map(column => column.name));
  if (!names.has("image_key")) await db.prepare("ALTER TABLE site_questions ADD COLUMN image_key TEXT NOT NULL DEFAULT ''").run();
  if (!names.has("image_data_url")) await db.prepare("ALTER TABLE site_questions ADD COLUMN image_data_url TEXT NOT NULL DEFAULT ''").run();
  schemaReady = true;
}

async function getLaunchQuizSlug(db, now) {
  const alreadyLive = await db.prepare(`SELECT id FROM site_quizzes WHERE status = 'published' AND (publish_at IS NULL OR publish_at <= ?) LIMIT 1`).bind(now).first();
  if (alreadyLive) return "";
  const launchQuiz = await db.prepare(`SELECT slug FROM site_quizzes WHERE status = 'published' AND publish_at > ? ORDER BY publish_at ASC, id ASC LIMIT 1`).bind(now).first();
  return String(launchQuiz?.slug || "");
}

async function listQuizzes(db, url) {
  const requestedLimit = Number.parseInt(url.searchParams.get("limit") || "12", 10);
  const limit = Number.isFinite(requestedLimit) ? Math.min(Math.max(requestedLimit, 1), 50) : 12;
  const requestedPage = Number.parseInt(url.searchParams.get("page") || "1", 10);
  const page = Number.isFinite(requestedPage) ? Math.max(requestedPage, 1) : 1;
  const offset = (page - 1) * limit;
  const category = (url.searchParams.get("category") || "").trim();
  const search = (url.searchParams.get("search") || "").trim();
  const liveOnly = ["1", "true", "yes"].includes((url.searchParams.get("live_only") || "").toLowerCase());
  const now = new Date().toISOString();
  const launchSlug = await getLaunchQuizSlug(db, now);
  const where = ["q.status = 'published'"];
  const binds = [];
  if (liveOnly) { where.push("(q.publish_at IS NULL OR q.publish_at <= ? OR q.slug = ?)"); binds.push(now, launchSlug || "__no_launch_quiz__"); }
  if (category) { where.push("lower(q.category) = lower(?)"); binds.push(category); }
  if (search) { where.push("(lower(q.title) LIKE lower(?) OR lower(q.description) LIKE lower(?) OR lower(q.category) LIKE lower(?))"); const term = `%${search}%`; binds.push(term, term, term); }
  const whereSql = where.join(" AND ");
  const countResult = await db.prepare(`SELECT COUNT(*) AS total FROM site_quizzes q WHERE ${whereSql}`).bind(...binds).first();
  const total = Number(countResult?.total || 0);
  const statement = db.prepare(`SELECT q.id, q.slug, q.title, q.category, q.description, q.youtube_url, q.publish_at, COUNT(DISTINCT sq.id) AS question_count, COUNT(DISTINCT sa.id) AS attempts FROM site_quizzes q LEFT JOIN site_questions sq ON sq.quiz_id = q.id LEFT JOIN site_attempts sa ON sa.quiz_id = q.id WHERE ${whereSql} GROUP BY q.id ORDER BY COALESCE(q.publish_at, q.created_at) DESC, q.id DESC LIMIT ? OFFSET ?`).bind(...binds, limit, offset);
  const result = await statement.all();
  const quizzes = (result.results || []).map(quiz => launchSlug && quiz.slug === launchSlug ? { ...quiz, publish_at: now, launch_quiz: true } : quiz);
  const totalPages = Math.max(1, Math.ceil(total / limit));
  return json({ quizzes, page, page_size: limit, total, total_pages: totalPages, has_more: page < totalPages, has_previous: page > 1 });
}

async function latestQuiz(db) {
  const now = new Date().toISOString();
  let quiz = await db.prepare(`SELECT q.id, q.slug, q.title, q.category, q.description, q.youtube_url, q.publish_at, COUNT(sq.id) AS question_count FROM site_quizzes q LEFT JOIN site_questions sq ON sq.quiz_id = q.id WHERE q.status = 'published' AND (q.publish_at IS NULL OR q.publish_at <= ?) GROUP BY q.id ORDER BY COALESCE(q.publish_at, q.created_at) DESC, q.id DESC LIMIT 1`).bind(now).first();
  if (quiz) return json({ quiz });
  quiz = await db.prepare(`SELECT q.id, q.slug, q.title, q.category, q.description, '' AS youtube_url, q.publish_at AS scheduled_publish_at, ? AS publish_at, COUNT(sq.id) AS question_count FROM site_quizzes q LEFT JOIN site_questions sq ON sq.quiz_id = q.id WHERE q.status = 'published' AND q.publish_at > ? GROUP BY q.id ORDER BY q.publish_at ASC, q.id ASC LIMIT 1`).bind(now, now).first();
  if (quiz) quiz.launch_quiz = true;
  return quiz ? json({ quiz }) : json({ quiz: null });
}

async function requireAdmin(request, env) {
  if (!env.SITE_ADMIN_KEY) return { response: json({ error: "Website publishing is not enabled yet." }, 503) };
  if (await verifyAdminSession(request, env)) return { ok: true };
  const supplied = request.headers.get("authorization") || "";
  if (supplied !== `Bearer ${env.SITE_ADMIN_KEY}`) return { response: json({ error: "Unauthorized." }, 401) };
  return { ok: true };
}

async function adminAuthSetup(request, env) {
  if (!env.SITE_ADMIN_KEY) return json({ error: "Website publishing is not enabled yet." }, 503);
  const supplied = request.headers.get("authorization") || "";
  if (supplied !== `Bearer ${env.SITE_ADMIN_KEY}`) return json({ error: "Unauthorized." }, 401);
  const secret = base32Encode(await adminTotpSecretBytes(env.SITE_ADMIN_KEY));
  return json({ ok: true, account: "Factburst Admin", issuer: "Factburst Quiz", secret });
}

async function adminAuthLogin(request, env) {
  if (!env.SITE_ADMIN_KEY) return json({ error: "Website publishing is not enabled yet." }, 503);
  const body = await readJson(request);
  const code = String(body?.code || "").replace(/\D/g, "").slice(0, 6);
  if (!/^\d{6}$/.test(code)) return json({ error: "Enter the 6-digit authenticator code." }, 400);
  const secret = await adminTotpSecretBytes(env.SITE_ADMIN_KEY);
  if (!(await verifyTotpCode(secret, code))) return json({ error: "Authenticator code was not accepted." }, 401);
  const token = await createAdminSession(env.SITE_ADMIN_KEY);
  const headers = new Headers(JSON_HEADERS);
  headers.append("set-cookie", `${ADMIN_SESSION_COOKIE}=${token}; Max-Age=${ADMIN_SESSION_SECONDS}; Path=/; HttpOnly; Secure; SameSite=Strict`);
  return new Response(JSON.stringify({ ok: true, trusted_for_days: 30 }), { status: 200, headers });
}

async function adminAuthSession(request, env) {
  if (!env.SITE_ADMIN_KEY) return json({ error: "Website publishing is not enabled yet." }, 503);
  return (await verifyAdminSession(request, env)) ? json({ ok: true }) : json({ error: "Not signed in." }, 401);
}

function adminAuthLogout() {
  const headers = new Headers(JSON_HEADERS);
  headers.append("set-cookie", `${ADMIN_SESSION_COOKIE}=; Max-Age=0; Path=/; HttpOnly; Secure; SameSite=Strict`);
  return new Response(JSON.stringify({ ok: true }), { status: 200, headers });
}

async function adminTotpSecretBytes(siteKey) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(`factburst-totp-v1:${siteKey}`));
  return new Uint8Array(digest).slice(0, 20);
}

async function verifyTotpCode(secretBytes, code) {
  const step = Math.floor(Date.now() / 1000 / 30);
  for (const offset of [-1, 0, 1]) if (await totp(secretBytes, step + offset) === code) return true;
  return false;
}

async function totp(secretBytes, counter) {
  const message = new Uint8Array(8);
  let value = BigInt(counter);
  for (let i = 7; i >= 0; i--) { message[i] = Number(value & 255n); value >>= 8n; }
  const key = await crypto.subtle.importKey("raw", secretBytes, { name: "HMAC", hash: "SHA-1" }, false, ["sign"]);
  const digest = new Uint8Array(await crypto.subtle.sign("HMAC", key, message));
  const offset = digest[digest.length - 1] & 15;
  const binary = ((digest[offset] & 127) << 24) | (digest[offset + 1] << 16) | (digest[offset + 2] << 8) | digest[offset + 3];
  return String(binary % 1000000).padStart(6, "0");
}

function base32Encode(bytes) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = 0, value = 0, output = "";
  for (const byte of bytes) { value = (value << 8) | byte; bits += 8; while (bits >= 5) { output += alphabet[(value >>> (bits - 5)) & 31]; bits -= 5; } }
  if (bits > 0) output += alphabet[(value << (5 - bits)) & 31];
  return output;
}

function base64Url(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function fromBase64Url(value) {
  const normalized = value.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((value.length + 3) % 4);
  const binary = atob(normalized);
  return Uint8Array.from(binary, char => char.charCodeAt(0));
}

async function hmacSha256(secret, text) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(text)));
}

async function createAdminSession(siteKey) {
  const random = crypto.getRandomValues(new Uint8Array(32));
  const token = base64Url(random);
  const signature = base64Url(await hmacSha256(siteKey, token));
  return `${token}.${signature}`;
}

async function verifyAdminSession(request, env) {
  const cookie = parseCookie(request.headers.get("cookie") || "")[ADMIN_SESSION_COOKIE] || "";
  if (!cookie || !cookie.includes(".")) return false;
  const [token, signature] = cookie.split(".");
  if (!token || !signature) return false;
  const expected = base64Url(await hmacSha256(env.SITE_ADMIN_KEY, token));
  return signature === expected;
}

function parseCookie(value) {
  return Object.fromEntries(value.split(";").map(part => part.trim().split("=")).filter(part => part.length >= 2));
}

async function listAdminQuizzes(request, env) {
  const auth = await requireAdmin(request, env); if (!auth.ok) return auth.response;
  const result = await env.DB.prepare(`SELECT q.id, q.slug, q.title, q.category, q.description, q.youtube_url, q.publish_at, q.status, q.created_at, q.updated_at, COUNT(DISTINCT sq.id) AS question_count, COUNT(DISTINCT sa.id) AS attempts FROM site_quizzes q LEFT JOIN site_questions sq ON sq.quiz_id = q.id LEFT JOIN site_attempts sa ON sa.quiz_id = q.id GROUP BY q.id ORDER BY COALESCE(q.publish_at, q.created_at) DESC, q.id DESC`).all();
  const questionResult = await env.DB.prepare(`SELECT quiz_id, position, question, answer_a, answer_b, answer_c, answer_d, correct_answer, explanation, image_key, image_data_url FROM site_questions ORDER BY quiz_id ASC, position ASC`).all();
  const questionsByQuiz = new Map();
  for (const row of questionResult.results || []) {
    const id = Number(row.quiz_id);
    if (!questionsByQuiz.has(id)) questionsByQuiz.set(id, []);
    questionsByQuiz.get(id).push(row);
  }
  const quizzes = (result.results || []).map(quiz => ({ ...quiz, health: analyzeQuizHealth(quiz, questionsByQuiz.get(Number(quiz.id)) || []) }));
  const [totals, published, drafts, questions, attempts] = await Promise.all([
    env.DB.prepare("SELECT COUNT(*) AS value FROM site_quizzes").first(),
    env.DB.prepare("SELECT COUNT(*) AS value FROM site_quizzes WHERE status = 'published'").first(),
    env.DB.prepare("SELECT COUNT(*) AS value FROM site_quizzes WHERE status = 'draft'").first(),
    env.DB.prepare("SELECT COUNT(*) AS value FROM site_questions").first(),
    env.DB.prepare("SELECT COUNT(*) AS value FROM site_attempts").first(),
  ]);
  const healthStats = quizzes.reduce((stats, quiz) => { stats[quiz.health.status] += 1; stats.issues += quiz.health.issues.length; return stats; }, { healthy: 0, warning: 0, error: 0, issues: 0 });
  return json({ quizzes, stats: { total_quizzes: Number(totals?.value || 0), published: Number(published?.value || 0), drafts: Number(drafts?.value || 0), questions: Number(questions?.value || 0), attempts: Number(attempts?.value || 0), health: healthStats } });
}

function analyzeQuizHealth(quiz, rows) {
  const issues = [];
  const title = String(quiz?.title || "").trim();
  const category = String(quiz?.category || "").trim();
  const description = String(quiz?.description || "").trim();
  if (!title) issues.push({ severity: "error", code: "missing-title", message: "Missing title." });
  if (!category) issues.push({ severity: "error", code: "missing-category", message: "Missing category." });
  if (!description) issues.push({ severity: "warning", code: "missing-description", message: "Description is empty." });
  if (rows.length === 0) issues.push({ severity: "error", code: "no-questions", message: "Quiz has no questions." });
  if (rows.length > 200) issues.push({ severity: "error", code: "too-many-questions", message: `Quiz has ${rows.length} questions; maximum is 200.` });
  if (rows.length > 0 && rows.length < 5) issues.push({ severity: "warning", code: "few-questions", message: `Only ${rows.length} question${rows.length === 1 ? "" : "s"}; consider adding at least 5.` });
  const seenQuestions = new Map();
  for (const row of rows) {
    const position = Number(row.position || 0);
    const prefix = `Question ${position || "?"}`;
    const text = String(row.question || "").trim();
    const answers = [row.answer_a, row.answer_b, row.answer_c, row.answer_d].map(value => String(value || "").trim());
    if (!text) issues.push({ severity: "error", code: "missing-question", position, message: `${prefix} has no question text.` });
    else {
      const key = text.toLowerCase().replace(/\s+/g, " ");
      if (seenQuestions.has(key)) issues.push({ severity: "error", code: "duplicate-question", position, message: `${prefix} duplicates question ${seenQuestions.get(key)}.` });
      else seenQuestions.set(key, position || "?");
    }
    const missingAnswers = answers.map((value, index) => value ? "" : String.fromCharCode(65 + index)).filter(Boolean);
    if (missingAnswers.length) issues.push({ severity: "error", code: "missing-answer", position, message: `${prefix} is missing answer${missingAnswers.length > 1 ? "s" : ""}: ${missingAnswers.join(", ")}.` });
    const normalizedAnswers = answers.filter(Boolean).map(value => value.toLowerCase().replace(/\s+/g, " "));
    if (new Set(normalizedAnswers).size !== normalizedAnswers.length) issues.push({ severity: "error", code: "duplicate-answers", position, message: `${prefix} contains duplicate answer choices.` });
    const correct = String(row.correct_answer || "").trim().toUpperCase();
    if (!/^[A-D]$/.test(correct)) issues.push({ severity: "error", code: "invalid-correct-answer", position, message: `${prefix} has an invalid correct answer.` });
    else if (!answers[correct.charCodeAt(0) - 65]) issues.push({ severity: "error", code: "correct-answer-missing", position, message: `${prefix} points to ${correct}, but that answer is empty.` });
  }
  const errors = issues.filter(issue => issue.severity === "error").length;
  const warnings = issues.filter(issue => issue.severity === "warning").length;
  return { status: errors ? "error" : warnings ? "warning" : "healthy", errors, warnings, issue_count: issues.length, issues: issues.slice(0, 20) };
}

async function getAdminQuiz(request, env, slug) {
  const auth = await requireAdmin(request, env); if (!auth.ok) return auth.response;
  const quiz = await env.DB.prepare(`SELECT id, slug, title, category, description, youtube_url, publish_at, status, created_at, updated_at FROM site_quizzes WHERE slug = ? LIMIT 1`).bind(slug).first();
  if (!quiz) return json({ error: "Quiz not found." }, 404);
  const result = await env.DB.prepare(`SELECT position, question, answer_a, answer_b, answer_c, answer_d, correct_answer, explanation, image_key FROM site_questions WHERE quiz_id = ? ORDER BY position ASC`).bind(quiz.id).all();
  const questions = result.results || [];
  return json({ quiz: { ...quiz, questions: questions.map(row => ({ position: row.position, question: row.question, answers: [row.answer_a, row.answer_b, row.answer_c, row.answer_d], correct_answer: normalizeAnswer(row.correct_answer), explanation: row.explanation || "", image_key: String(row.image_key || "") })), health: analyzeQuizHealth(quiz, questions) } });
}

async function loadPlayableQuiz(db, slug, now, columns) {
  const quiz = await db.prepare(`SELECT ${columns} FROM site_quizzes WHERE slug = ? AND status = 'published' LIMIT 1`).bind(slug).first();
  if (!quiz) return null;
  if (!quiz.publish_at || quiz.publish_at <= now) return { quiz, launchQuiz: false };
  const launchSlug = await getLaunchQuizSlug(db, now);
  return launchSlug === slug ? { quiz, launchQuiz: true } : null;
}

async function getQuiz(env, slug) {
  const now = new Date().toISOString();
  const playable = await loadPlayableQuiz(env.DB, slug, now, "id, slug, title, category, description, youtube_url, publish_at");
  if (!playable) return json({ error: "Quiz not found." }, 404);
  const { quiz, launchQuiz } = playable;
  const questions = await env.DB.prepare(`SELECT position, question, answer_a, answer_b, answer_c, answer_d, image_key, image_data_url FROM site_questions WHERE quiz_id = ? ORDER BY position ASC`).bind(quiz.id).all();
  const rows = await Promise.all((questions.results || []).map(async row => { const image = await resolveQuestionImage(env, quiz.id, quiz.slug, row); return { position: row.position, question: row.question, answers: [row.answer_a, row.answer_b, row.answer_c, row.answer_d], image_url: image.imageUrl, image_data_url: image.legacyDataUrl }; }));
  return json({ quiz: { ...quiz, youtube_url: launchQuiz ? "" : (quiz.youtube_url || ""), publish_at: launchQuiz ? now : quiz.publish_at, launch_quiz: launchQuiz, questions: rows } });
}

async function resolveQuestionImage(env, quizId, slug, row) {
  const existingKey = String(row.image_key || "").trim();
  if (existingKey) return { imageUrl: imageUrlForKey(existingKey), legacyDataUrl: "" };
  const legacy = String(row.image_data_url || "").trim();
  if (!legacy) return { imageUrl: "", legacyDataUrl: "" };
  if (!env.QUIZ_IMAGES) return { imageUrl: "", legacyDataUrl: legacy };
  try {
    const key = await storeImageDataUrl(env.QUIZ_IMAGES, slug, Number(row.position), legacy);
    await env.DB.prepare(`UPDATE site_questions SET image_key = ?, image_data_url = '' WHERE quiz_id = ? AND position = ?`).bind(key, quizId, row.position).run();
    return { imageUrl: imageUrlForKey(key), legacyDataUrl: "" };
  } catch (error) { console.error("Could not migrate legacy quiz image to R2", error); return { imageUrl: "", legacyDataUrl: legacy }; }
}

async function scoreQuiz(request, db, slug) {
  const body = await readJson(request); const answers = Array.isArray(body?.answers) ? body.answers.map(normalizeAnswer) : []; const now = new Date().toISOString();
  const playable = await loadPlayableQuiz(db, slug, now, "id, slug, title, youtube_url, publish_at");
  if (!playable) return json({ error: "Quiz not found." }, 404);
  const { quiz, launchQuiz } = playable;
  const questionResult = await db.prepare(`SELECT position, correct_answer, explanation FROM site_questions WHERE quiz_id = ? ORDER BY position ASC`).bind(quiz.id).all();
  const questions = questionResult.results || [];
  if (questions.length === 0) return json({ error: "This quiz has no questions yet." }, 409);
  if (answers.length !== questions.length) return json({ error: `Submit exactly ${questions.length} answers.` }, 400);
  const results = answers.map((answer, index) => { const question = questions[index]; const correctAnswer = normalizeAnswer(question.correct_answer); return { correct: answer === correctAnswer, correct_answer: correctAnswer, explanation: question.explanation || "" }; });
  const score = results.filter(result => result.correct).length;
  const total = questions.length;
  await db.prepare(`INSERT INTO site_attempts (quiz_id, score, total, completed_at) VALUES (?, ?, ?, ?)`).bind(quiz.id, score, total, now).run();
  return json({ score, total, percentage: Math.round((score / total) * 100), results, youtube_url: launchQuiz ? "" : (quiz.youtube_url || "") });
}

async function upsertQuiz(request, env) {
  const auth = await requireAdmin(request, env); if (!auth.ok) return auth.response;
  const body = await readJson(request);
  const title = String(body?.title || "").trim(); const category = String(body?.category || "").trim(); const description = String(body?.description || "").trim(); const youtubeUrl = String(body?.youtube_url || "").trim();
  const status = String(body?.status || "draft").toLowerCase() === "published" ? "published" : "draft";
  const publishAt = body?.publish_at ? new Date(body.publish_at).toISOString() : null;
  const slug = slugify(String(body?.slug || title)); const questions = Array.isArray(body?.questions) ? body.questions : [];
  if (!title || !category || !slug) return json({ error: "Title, category and a valid slug are required." }, 400);
  if (questions.length === 0) return json({ error: "Add at least one question before saving." }, 400);
  if (questions.length > 200) return json({ error: "A quiz can contain at most 200 questions." }, 400);
  const now = new Date().toISOString();
  const existing = await env.DB.prepare("SELECT id FROM site_quizzes WHERE slug = ? LIMIT 1").bind(slug).first(); let quizId;
  if (existing) { quizId = Number(existing.id); await env.DB.prepare("UPDATE site_quizzes SET title = ?, category = ?, description = ?, youtube_url = ?, publish_at = ?, status = ?, updated_at = ? WHERE id = ?").bind(title, category, description, youtubeUrl, publishAt, status, now, quizId).run(); await env.DB.prepare("DELETE FROM site_questions WHERE quiz_id = ?").bind(quizId).run(); }
  else { const inserted = await env.DB.prepare("INSERT INTO site_quizzes (slug, title, category, description, youtube_url, publish_at, status, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)").bind(slug, title, category, description, youtubeUrl, publishAt, status, now, now).run(); quizId = Number(inserted.meta?.last_row_id || 0); }
  const statements = questions.map((question, index) => { const answers = Array.isArray(question?.answers) ? question.answers.map(value => String(value || "").trim()) : []; const correct = normalizeAnswer(question?.correct_answer); return env.DB.prepare("INSERT INTO site_questions (quiz_id, position, question, answer_a, answer_b, answer_c, answer_d, correct_answer, explanation, image_key, image_data_url) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)").bind(quizId, index + 1, String(question?.question || "").trim(), answers[0] || "", answers[1] || "", answers[2] || "", answers[3] || "", correct, String(question?.explanation || "").trim(), String(question?.image_key || "").trim(), String(question?.image_data_url || "").trim()); });
  await env.DB.batch(statements);
  return json({ ok: true, quiz: { id: quizId, slug, title, status, question_count: questions.length } }, existing ? 200 : 201);
}

function imageUrlForKey(key) { const value = String(key || "").trim(); return /^quiz-images\/[a-z0-9][a-z0-9-]{0,79}\/q\d{3}-[a-f0-9]{20}\.png$/.test(value) ? `/${value}` : ""; }

async function storeImageDataUrl(bucket, slug, position, dataUrl) {
  const value = String(dataUrl || "").trim();
  if (value.length === 0 || value.length > MAX_IMAGE_DATA_URL_LENGTH) throw new Error("Quiz image is too large.");
  const match = value.match(/^data:image\/png;base64,([A-Za-z0-9+/=]+)$/);
  if (!match) throw new Error("Quiz images must be PNG data.");
  const binary = atob(match[1]);
  const bytes = Uint8Array.from(binary, char => char.charCodeAt(0));
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-1", bytes));
  const hash = Array.from(digest.slice(0, 10), byte => byte.toString(16).padStart(2, "0")).join("");
  const safeSlug = slugify(slug);
  const safePosition = Math.min(999, Math.max(1, Number(position) || 1));
  const key = `${IMAGE_PREFIX}${safeSlug}/q${String(safePosition).padStart(3, "0")}-${hash}.png`;
  await bucket.put(key, bytes, { httpMetadata: { contentType: "image/png", cacheControl: IMAGE_CACHE_CONTROL } });
  return key;
}

async function readJson(request) { try { return await request.json(); } catch { return {}; } }
function normalizeAnswer(value) { const answer = String(value || "").trim().toUpperCase(); return /^[A-D]$/.test(answer) ? answer : ""; }
function slugify(value) { return value.toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 80); }
function json(value, status = 200) { return new Response(JSON.stringify(value), { status, headers: JSON_HEADERS }); }
