import { activeSessionUser } from "./account-access.js";

const CHALLENGE_DAYS = 30;

export async function handleChallengeApi(request, db, url) {
  if (request.method === "POST" && url.pathname === "/api/challenges") {
    return createChallenge(request, db, url);
  }

  const match = url.pathname.match(/^\/api\/challenges\/([A-Za-z0-9_-]{20,120})$/);
  if (request.method === "GET" && match) {
    return getChallenge(request, db, match[1]);
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

  let challengedUserId = null;
  let challengedUsername = "";
  if (body?.friend_user_id !== undefined && body?.friend_user_id !== null && String(body.friend_user_id).trim() !== "") {
    const friendUserId = Number.parseInt(String(body.friend_user_id), 10);
    if (!Number.isInteger(friendUserId) || friendUserId <= 0 || friendUserId === Number(user.id)) {
      return json({ error: "Choose a valid friend." }, 400);
    }

    const userA = Math.min(Number(user.id), friendUserId);
    const userB = Math.max(Number(user.id), friendUserId);
    const friendship = await db.prepare(`
      SELECT f.id, other.id AS friend_id, other.username
      FROM site_friendships f
      JOIN site_users other ON other.id = ?
      WHERE f.user_a_id = ? AND f.user_b_id = ?
        AND f.status = 'accepted'
        AND other.email_verified_at IS NOT NULL
        AND COALESCE(other.status, 'active') = 'active'
      LIMIT 1
    `).bind(friendUserId, userA, userB).first();
    if (!friendship) {
      return json({ error: "You can only send a direct challenge to an accepted friend." }, 403);
    }
    challengedUserId = Number(friendship.friend_id);
    challengedUsername = String(friendship.username || "");
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
      (token_hash, challenger_user_id, challenged_user_id, quiz_id, challenger_score, total, created_at, expires_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(
    tokenHash,
    user.id,
    challengedUserId,
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
      challenged_user_id: challengedUserId,
      challenged_username: challengedUsername,
      quiz_slug: String(score.slug),
      quiz_title: String(score.title || score.slug),
      score: Number(score.best_score),
      total: Number(score.total),
      expires_at: expiresAt,
      url: challengeUrl.toString(),
    },
  }, 201);
}

async function getChallenge(request, db, token) {
  const tokenHash = await sha256(token);
  const now = new Date().toISOString();
  const row = await db.prepare(`
    SELECT
      c.quiz_id,
      c.challenged_user_id,
      c.challenger_score,
      c.total,
      c.created_at,
      c.expires_at,
      u.username,
      q.slug,
      q.title,
      challenged.username AS challenged_username,
      challenged.email_verified_at AS challenged_verified_at,
      challenged.status AS challenged_status
    FROM site_challenges c
    JOIN site_users u ON u.id = c.challenger_user_id
    LEFT JOIN site_users challenged ON challenged.id = c.challenged_user_id
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

  const challengedUserId = row.challenged_user_id === null || row.challenged_user_id === undefined
    ? null
    : Number(row.challenged_user_id);
  if (challengedUserId !== null) {
    if (!row.challenged_verified_at || String(row.challenged_status || "active") !== "active") {
      return json({ error: "This challenge is no longer available.", code: "challenge_unavailable" }, 404);
    }
    const viewer = await activeSessionUser(request, db);
    if (!viewer || Number(viewer.id) !== challengedUserId) {
      return json({ error: "This challenge was sent to a specific Factburst friend.", code: "challenge_wrong_user" }, 403);
    }
  }

  return json({
    challenge: {
      challenger: String(row.username || "Factburst player"),
      challenged_user_id: challengedUserId,
      challenged_username: String(row.challenged_username || ""),
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
