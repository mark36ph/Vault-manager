import { activeSessionUser } from "./account-access.js";

const CHALLENGE_DAYS = 30;

export async function handleChallengeApi(request, db, url) {
  if (request.method === "POST" && url.pathname === "/api/challenges") {
    return createChallenge(request, db, url);
  }

  const match = url.pathname.match(/^\/api\/challenges\/([A-Za-z0-9_-]{20,120})$/);
  if (request.method === "GET" && match) {
    return getChallenge(db, match[1]);
  }

  return null;
}

async function createChallenge(request, db, url) {
  const user = await activeSessionUser(request, db);
  if (!user || !user.email_verified_at) {
    return json({ error: "Log in with a verified account to challenge a friend.", code: "verified_account_required" }, 401);
  }

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }

  const slug = String(body?.slug || "").trim().toLowerCase();
  if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug)) {
    return json({ error: "That quiz is not valid." }, 400);
  }

  const score = await db.prepare(`
    SELECT q.id AS quiz_id, q.slug, q.title, s.best_score, s.total
    FROM site_user_scores s
    JOIN site_quizzes q ON q.id = s.quiz_id
    WHERE s.user_id = ? AND q.slug = ?
    LIMIT 1
  `).bind(user.id, slug).first();
  if (!score) {
    return json({ error: "Finish this quiz before challenging a friend." }, 409);
  }

  const token = randomToken(24);
  const tokenHash = await sha256(token);
  const now = new Date().toISOString();
  const expiresAt = new Date(Date.parse(now) + CHALLENGE_DAYS * 24 * 60 * 60 * 1000).toISOString();

  await db.prepare(`
    INSERT INTO site_challenges
      (token_hash, challenger_user_id, quiz_id, challenger_score, total, created_at, expires_at)
    VALUES (?, ?, ?, ?, ?, ?, ?)
  `).bind(
    tokenHash,
    user.id,
    Number(score.quiz_id),
    Number(score.best_score),
    Number(score.total),
    now,
    expiresAt,
  ).run();

  const challengeUrl = new URL("/quiz.html", url.origin);
  challengeUrl.searchParams.set("slug", String(score.slug));
  challengeUrl.searchParams.set("challenge", token);

  return json({
    challenge: {
      challenger: String(user.username || "Factburst player"),
      quiz_slug: String(score.slug),
      quiz_title: String(score.title || score.slug),
      score: Number(score.best_score),
      total: Number(score.total),
      expires_at: expiresAt,
      url: challengeUrl.toString(),
    },
  }, 201);
}

async function getChallenge(db, token) {
  const tokenHash = await sha256(token);
  const now = new Date().toISOString();
  const row = await db.prepare(`
    SELECT
      c.quiz_id,
      c.challenger_score,
      c.total,
      c.created_at,
      c.expires_at,
      u.username,
      q.slug,
      q.title
    FROM site_challenges c
    JOIN site_users u ON u.id = c.challenger_user_id
    JOIN site_quizzes q ON q.id = c.quiz_id
    WHERE c.token_hash = ?
      AND c.expires_at > ?
      AND COALESCE(u.status, 'active') = 'active'
      AND u.email_verified_at IS NOT NULL
      AND q.status = 'published'
    LIMIT 1
  `).bind(tokenHash, now).first();

  if (!row) {
    return json({ error: "This challenge is no longer available.", code: "challenge_unavailable" }, 404);
  }

  return json({
    challenge: {
      challenger: String(row.username || "Factburst player"),
      quiz_slug: String(row.slug || ""),
      quiz_title: String(row.title || row.slug || "Quiz"),
      score: Number(row.challenger_score || 0),
      total: Number(row.total || 0),
      created_at: String(row.created_at || ""),
      expires_at: String(row.expires_at || ""),
    },
  });
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
  return new Response(JSON.stringify(value), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
    },
  });
}
