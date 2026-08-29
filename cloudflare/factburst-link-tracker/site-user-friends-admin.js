export async function handleSiteUserFriendsAdmin(request, env, url) {
  const match = url.pathname.match(/^\/api\/site\/users\/(\d+)\/friends$/);
  if (!match || request.method !== "GET") return null;
  const userId = Number.parseInt(match[1], 10);
  if (!Number.isInteger(userId) || userId <= 0) return json({ error: "Invalid user id." }, 400);

  const user = await env.DB.prepare("SELECT id, username FROM site_users WHERE id = ? LIMIT 1").bind(userId).first();
  if (!user) return json({ error: "User not found." }, 404);
  if (!await tableExists(env.DB, "site_friendships")) {
    return json({ user_id: userId, friends: [], incoming: [], outgoing: [] });
  }

  const result = await env.DB.prepare(`
    SELECT
      f.id AS friendship_id,
      f.status,
      f.requested_by_user_id,
      f.created_at,
      f.responded_at,
      other.id AS other_user_id,
      other.username AS other_username,
      COALESCE(other.status, 'active') AS other_status
    FROM site_friendships f
    JOIN site_users other ON other.id = CASE WHEN f.user_a_id = ? THEN f.user_b_id ELSE f.user_a_id END
    WHERE f.user_a_id = ? OR f.user_b_id = ?
    ORDER BY CASE f.status WHEN 'pending' THEN 0 ELSE 1 END,
             other.username_key COLLATE NOCASE ASC
  `).bind(userId, userId, userId).all();

  const friends = [];
  const incoming = [];
  const outgoing = [];
  for (const row of result.results || []) {
    const item = {
      friendship_id: Number(row.friendship_id || 0),
      user_id: Number(row.other_user_id || 0),
      username: String(row.other_username || ""),
      user_status: String(row.other_status || "active"),
      created_at: String(row.created_at || ""),
      responded_at: row.responded_at ? String(row.responded_at) : null,
    };
    if (String(row.status || "") === "accepted") {
      friends.push(item);
    } else if (Number(row.requested_by_user_id) === userId) {
      outgoing.push(item);
    } else {
      incoming.push(item);
    }
  }

  return json({ user_id: userId, friends, incoming, outgoing });
}

async function tableExists(db, tableName) {
  const row = await db.prepare(`
    SELECT name FROM sqlite_master WHERE type = 'table' AND name = ? LIMIT 1
  `).bind(tableName).first();
  return Boolean(row);
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
