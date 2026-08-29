import { activeSessionUser } from "./account-access.js";
import { ensureEngagementSchema } from "./account-engagement.js";

const CHALLENGE_DAYS = 30;

export async function handleChallengeApi(request, env, url) {
  const db = env?.DB;
  if (!db) return null;

  if (request.method === "POST" && url.pathname === "/api/challenges") {
    return createChallenge(request, env, url);
  }

  const match = url.pathname.match(/^\/api\/challenges\/([A-Za-z0-9_-]{20,120})$/);
  if (request.method === "GET" && match) {
    return getChallenge(request, db, match[1]);
  }

  return null;
}

async function createChallenge(request, env, url) {
  const db = env.DB;
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

  const friendUserId = Number.parseInt(String(body?.friend_user_id ?? ""), 10);
  if (!Number.isInteger(friendUserId) || friendUserId <= 0 || friendUserId === Number(user.id)) {
    return json({ error: "Choose a Factburst friend to challenge." }, 400);
  }

  const userA = Math.min(Number(user.id), friendUserId);
  const userB = Math.max(Number(user.id), friendUserId);
  const friendship = await db.prepare(`
    SELECT f.id, other.id AS friend_id, other.username, other.email
    FROM site_friendships f
    JOIN site_users other ON other.id = ?
    WHERE f.user_a_id = ? AND f.user_b_id = ?
      AND f.status = 'accepted'
      AND other.email_verified_at IS NOT NULL
      AND COALESCE(other.status, 'active') = 'active'
    LIMIT 1
  `).bind(friendUserId, userA, userB).first();
  if (!friendship) {
    return json({ error: "You can only challenge an accepted Factburst friend." }, 403);
  }

  const challengedUserId = Number(friendship.friend_id);
  const challengedUsername = String(friendship.username || "");
  const challengedEmail = String(friendship.email || "").trim();

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

  await ensureEngagementSchema(db);

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
  const challengePath = challengeUrl.pathname + challengeUrl.search;
  const challengerName = String(user.username || "Factburst player");
  const quizTitle = String(score.title || score.slug);
  const challengeMessage = `${challengerName} challenged you to beat ${Number(score.best_score)}/${Number(score.total)} on ${quizTitle}.`;

  await db.prepare(`
    INSERT INTO site_notifications (user_id, type, title, message, url, read_at, created_at)
    VALUES (?, 'challenge', 'New quiz challenge', ?, ?, NULL, ?)
  `).bind(challengedUserId, challengeMessage, challengePath, now).run();

  const emailSent = await sendChallengeEmail(env, {
    to: challengedEmail,
    challengedUsername,
    challengerName,
    quizTitle,
    score: Number(score.best_score),
    total: Number(score.total),
    challengeUrl: challengeUrl.toString(),
  });

  return json({
    challenge: {
      challenger: challengerName,
      challenged_user_id: challengedUserId,
      challenged_username: challengedUsername,
      quiz_slug: String(score.slug),
      quiz_title: quizTitle,
      score: Number(score.best_score),
      total: Number(score.total),
      expires_at: expiresAt,
      notification_sent: true,
      email_sent: emailSent,
    },
  }, 201);
}

async function sendChallengeEmail(env, details) {
  if (!env?.EMAIL || typeof env.EMAIL.send !== "function") return false;
  const from = String(env.EMAIL_FROM || "").trim();
  const to = String(details?.to || "").trim();
  if (!from || !to) return false;

  const challengedName = String(details?.challengedUsername || "Factburst player");
  const challengerName = String(details?.challengerName || "A Factburst friend");
  const quizTitle = String(details?.quizTitle || "Factburst Quiz");
  const challengeUrl = String(details?.challengeUrl || "");
  const score = Number(details?.score || 0);
  const total = Number(details?.total || 0);
  const text = [
    `Hi ${challengedName},`,
    "",
    `${challengerName} challenged you to beat ${score}/${total} on “${quizTitle}”.`,
    "",
    "Your challenge is waiting in your Factburst notifications.",
    challengeUrl ? `Open the challenge: ${challengeUrl}` : "Log in to Factburst Quiz to play.",
    "",
    "Good luck!",
  ].join("\n");
  const html = `
    <div style="font-family:Arial,sans-serif;line-height:1.55;color:#101828">
      <h2>You have a new Factburst challenge</h2>
      <p>Hi ${escapeHtml(challengedName)},</p>
      <p><strong>${escapeHtml(challengerName)}</strong> challenged you to beat <strong>${score}/${total}</strong> on <strong>${escapeHtml(quizTitle)}</strong>.</p>
      <p>Your challenge is also waiting in your Factburst notifications.</p>
      ${challengeUrl ? `<p><a href="${escapeHtml(challengeUrl)}" style="display:inline-block;padding:12px 18px;background:#087ea4;color:white;text-decoration:none;border-radius:8px;font-weight:700">Play challenge</a></p>` : ""}
      <p>Good luck!</p>
    </div>`;

  try {
    await env.EMAIL.send({
      from: { email: from, name: "Factburst Quiz" },
      to: [to],
      subject: `${challengerName} challenged you on Factburst Quiz`,
      text,
      html,
    });
    return true;
  } catch (error) {
    console.error("Factburst challenge email failed", error);
    return false;
  }
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

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
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
