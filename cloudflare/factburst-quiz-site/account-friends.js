import { activeSessionUser } from "./account-access.js";

export async function handleFriendsApi(request, db, url) {
  if (url.pathname === "/api/friends" && request.method === "GET") {
    return listFriends(request, db);
  }
  if (url.pathname === "/api/friends" && request.method === "POST") {
    return sendFriendRequest(request, db);
  }

  const match = url.pathname.match(/^\/api\/friends\/(\d+)$/);
  if (!match) return null;
  const friendshipId = Number.parseInt(match[1], 10);
  if (!Number.isInteger(friendshipId) || friendshipId <= 0) return json({ error: "Invalid friendship id." }, 400);

  if (request.method === "PATCH") return respondToFriendRequest(request, db, friendshipId);
  if (request.method === "DELETE") return removeFriendship(request, db, friendshipId);
  return null;
}

async function listFriends(request, db) {
  const current = await verifiedUser(request, db);
  if (current.response) return current.response;
  const user = current.user;

  const result = await db.prepare(`
    SELECT
      f.id AS friendship_id,
      f.status,
      f.requested_by_user_id,
      f.created_at,
      f.responded_at,
      u.id AS other_user_id,
      u.username AS other_username,
      COALESCE(SUM(s.best_score), 0) AS total_score,
      COALESCE(SUM(s.total), 0) AS total_possible,
      COUNT(s.quiz_id) AS quizzes_completed,
      COALESCE(SUM(s.attempts), 0) AS attempts
    FROM site_friendships f
    JOIN site_users u ON u.id = CASE WHEN f.user_a_id = ? THEN f.user_b_id ELSE f.user_a_id END
    LEFT JOIN site_user_scores s ON s.user_id = u.id
    WHERE (f.user_a_id = ? OR f.user_b_id = ?)
      AND COALESCE(u.status, 'active') = 'active'
      AND u.email_verified_at IS NOT NULL
    GROUP BY f.id, u.id
    ORDER BY CASE f.status WHEN 'pending' THEN 0 ELSE 1 END, u.username_key ASC
  `).bind(user.id, user.id, user.id).all();

  const friends = [];
  const incoming = [];
  const outgoing = [];
  for (const row of result.results || []) {
    const entry = mapFriend(row);
    if (String(row.status || "") === "accepted") {
      friends.push(entry);
    } else if (Number(row.requested_by_user_id) === Number(user.id)) {
      outgoing.push(entry);
    } else {
      incoming.push(entry);
    }
  }

  return json({ friends, incoming, outgoing });
}

async function sendFriendRequest(request, db) {
  const current = await verifiedUser(request, db);
  if (current.response) return current.response;
  const user = current.user;

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }

  const usernameKey = String(body?.username || "").trim().replace(/\s+/g, " ").toLowerCase();
  if (!usernameKey) return json({ error: "Enter a username." }, 400);

  const other = await db.prepare(`
    SELECT id, username FROM site_users
    WHERE username_key = ?
      AND email_verified_at IS NOT NULL
      AND COALESCE(status, 'active') = 'active'
    LIMIT 1
  `).bind(usernameKey).first();
  if (!other) return json({ error: "No active verified user was found with that username." }, 404);
  if (Number(other.id) === Number(user.id)) return json({ error: "You cannot add yourself as a friend." }, 400);

  const userA = Math.min(Number(user.id), Number(other.id));
  const userB = Math.max(Number(user.id), Number(other.id));
  const existing = await db.prepare(`
    SELECT id, status, requested_by_user_id
    FROM site_friendships
    WHERE user_a_id = ? AND user_b_id = ?
    LIMIT 1
  `).bind(userA, userB).first();

  if (existing) {
    if (String(existing.status) === "accepted") return json({ error: "You are already friends." }, 409);
    if (Number(existing.requested_by_user_id) === Number(user.id)) return json({ error: "Friend request already sent." }, 409);
    return json({
      error: "This user has already sent you a friend request. Open your profile to accept it.",
      code: "friend_request_waiting",
    }, 409);
  }

  const now = new Date().toISOString();
  await db.prepare(`
    INSERT INTO site_friendships
      (user_a_id, user_b_id, requested_by_user_id, status, created_at, responded_at)
    VALUES (?, ?, ?, 'pending', ?, NULL)
  `).bind(userA, userB, user.id, now).run();

  const created = await db.prepare(`
    SELECT id FROM site_friendships WHERE user_a_id = ? AND user_b_id = ? LIMIT 1
  `).bind(userA, userB).first();

  return json({
    friendship_id: Number(created?.id || 0),
    status: "pending",
    username: String(other.username || ""),
    message: `Friend request sent to ${String(other.username || "that user")}.`,
  }, 201);
}

async function respondToFriendRequest(request, db, friendshipId) {
  const current = await verifiedUser(request, db);
  if (current.response) return current.response;
  const user = current.user;

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }
  const action = String(body?.action || "").trim().toLowerCase();
  if (action !== "accept" && action !== "decline") {
    return json({ error: "Action must be accept or decline." }, 400);
  }

  const friendship = await db.prepare(`
    SELECT id, user_a_id, user_b_id, requested_by_user_id, status
    FROM site_friendships WHERE id = ? LIMIT 1
  `).bind(friendshipId).first();
  if (!friendship) return json({ error: "Friend request not found." }, 404);
  if (String(friendship.status) !== "pending") return json({ error: "That friend request is no longer pending." }, 409);
  if (Number(friendship.requested_by_user_id) === Number(user.id)) {
    return json({ error: "Only the recipient can accept or decline this friend request." }, 403);
  }
  if (Number(friendship.user_a_id) !== Number(user.id) && Number(friendship.user_b_id) !== Number(user.id)) {
    return json({ error: "That friend request is not yours." }, 403);
  }

  if (action === "decline") {
    await db.prepare("DELETE FROM site_friendships WHERE id = ?").bind(friendshipId).run();
    return json({ removed: true, friendship_id: friendshipId });
  }

  const now = new Date().toISOString();
  await db.prepare(`
    UPDATE site_friendships SET status = 'accepted', responded_at = ? WHERE id = ?
  `).bind(now, friendshipId).run();
  return json({ accepted: true, friendship_id: friendshipId });
}

async function removeFriendship(request, db, friendshipId) {
  const current = await verifiedUser(request, db);
  if (current.response) return current.response;
  const user = current.user;

  const friendship = await db.prepare(`
    SELECT id, user_a_id, user_b_id FROM site_friendships WHERE id = ? LIMIT 1
  `).bind(friendshipId).first();
  if (!friendship) return json({ error: "Friendship not found." }, 404);
  if (Number(friendship.user_a_id) !== Number(user.id) && Number(friendship.user_b_id) !== Number(user.id)) {
    return json({ error: "That friendship is not yours." }, 403);
  }

  await db.prepare("DELETE FROM site_friendships WHERE id = ?").bind(friendshipId).run();
  return json({ removed: true, friendship_id: friendshipId });
}

async function verifiedUser(request, db) {
  const user = await activeSessionUser(request, db);
  if (!user) {
    return { response: json({ error: "Log in to manage friends.", code: "account_required" }, 401), user: null };
  }
  if (!user.email_verified_at) {
    return { response: json({ error: "Verify your email before using friends.", code: "verified_account_required" }, 403), user: null };
  }
  return { response: null, user };
}

function mapFriend(row) {
  const score = Number(row.total_score || 0);
  const possible = Number(row.total_possible || 0);
  return {
    friendship_id: Number(row.friendship_id || 0),
    user_id: Number(row.other_user_id || 0),
    username: String(row.other_username || ""),
    quizzes_completed: Number(row.quizzes_completed || 0),
    attempts: Number(row.attempts || 0),
    total_score: score,
    total_possible: possible,
    percentage: possible > 0 ? Math.round((score / possible) * 100) : 0,
    created_at: String(row.created_at || ""),
    responded_at: row.responded_at ? String(row.responded_at) : null,
  };
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
