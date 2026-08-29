const DEFAULT_MESSAGE = "Factburst Quiz is currently undergoing maintenance. Please check back shortly.";

export async function handleSiteAccessAdmin(request, env, url) {
  const db = env.DB;
  if (!db) return json({ error: "Database is not configured." }, 500);

  if (url.pathname === "/api/site/settings") {
    await ensureAccessSchema(db);
    if (request.method === "GET") return readMaintenance(db);
    if (request.method === "PATCH") return updateMaintenance(request, db);
    return json({ error: "Method not allowed." }, 405, { Allow: "GET, PATCH" });
  }

  const accessMatch = url.pathname.match(/^\/api\/site\/users\/(\d+)\/access$/);
  if (accessMatch) {
    await ensureAccessSchema(db);
    const userId = Number.parseInt(accessMatch[1], 10);
    if (request.method === "GET") return readUserAccess(db, userId);
    if (request.method === "PATCH") return updateUserAccess(request, db, userId);
    return json({ error: "Method not allowed." }, 405, { Allow: "GET, PATCH" });
  }

  return null;
}

async function ensureAccessSchema(db) {
  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_settings (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL DEFAULT '',
      updated_at TEXT NOT NULL
    )
  `).run();
  const columns = await db.prepare("PRAGMA table_info(site_users)").all();
  const names = new Set((columns.results || []).map(column => String(column?.name || "")));
  if (!names.has("role")) {
    await db.prepare("ALTER TABLE site_users ADD COLUMN role TEXT NOT NULL DEFAULT 'user'").run();
  }
}

async function readMaintenance(db) {
  const rows = await db.prepare(`
    SELECT key, value, updated_at FROM site_settings
    WHERE key IN ('maintenance_enabled', 'maintenance_message')
  `).all();
  const values = {};
  let updatedAt = "";
  for (const row of rows.results || []) {
    values[String(row.key || "")] = String(row.value || "");
    if (String(row.updated_at || "") > updatedAt) updatedAt = String(row.updated_at || "");
  }
  return json({
    maintenance: {
      enabled: values.maintenance_enabled === "1",
      message: String(values.maintenance_message || "").trim() || DEFAULT_MESSAGE,
      updated_at: updatedAt,
    },
  });
}

async function updateMaintenance(request, db) {
  let body;
  try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }
  const enabled = Boolean(body?.enabled);
  const message = String(body?.message || "").trim().slice(0, 500) || DEFAULT_MESSAGE;
  const now = new Date().toISOString();
  await db.batch([
    setting(db, "maintenance_enabled", enabled ? "1" : "0", now),
    setting(db, "maintenance_message", message, now),
  ]);
  return readMaintenance(db);
}

async function readUserAccess(db, userId) {
  const user = await db.prepare(`
    SELECT id, username, role, status FROM site_users WHERE id = ? LIMIT 1
  `).bind(userId).first();
  if (!user) return json({ error: "Website user was not found." }, 404);
  return json({ user: mapAccess(user) });
}

async function updateUserAccess(request, db, userId) {
  const existing = await db.prepare("SELECT id, username, role, status FROM site_users WHERE id = ? LIMIT 1").bind(userId).first();
  if (!existing) return json({ error: "Website user was not found." }, 404);
  let body;
  try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }
  const role = normalizeRole(body?.role);
  if (!role) return json({ error: "Role must be user, moderator or admin." }, 400);
  await db.prepare("UPDATE site_users SET role = ? WHERE id = ?").bind(role, userId).run();
  return readUserAccess(db, userId);
}

function normalizeRole(value) {
  const role = String(value || "").trim().toLowerCase();
  return ["user", "moderator", "admin"].includes(role) ? role : "";
}

function mapAccess(user) {
  return {
    id: Number(user.id || 0),
    username: String(user.username || ""),
    role: normalizeRole(user.role) || "user",
    status: String(user.status || "active"),
  };
}

function setting(db, key, value, now) {
  return db.prepare(`
    INSERT INTO site_settings (key, value, updated_at) VALUES (?, ?, ?)
    ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at
  `).bind(key, value, now);
}

function json(value, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(value, null, 2), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
      ...extraHeaders,
    },
  });
}
