export async function handleSiteAdAdmin(request, env, url) {
  if (url.pathname !== "/api/site/ads") return null;
  await ensureSettingsTable(env.DB);
  if (request.method === "GET") return readSettings(env.DB);
  if (request.method === "PATCH") return updateSettings(request, env.DB);
  return json({ error: "Method not allowed." }, 405, { Allow: "GET, PATCH" });
}

async function readSettings(db) {
  const rows = await db.prepare(`
    SELECT key, value FROM site_settings
    WHERE key IN ('ads_enabled', 'adsense_client', 'adsense_left_slot', 'adsense_right_slot')
  `).all();
  const values = Object.fromEntries((rows.results || []).map(row => [String(row.key || ""), String(row.value || "")]));
  return json({
    enabled: values.ads_enabled === "1",
    client: String(values.adsense_client || ""),
    left_slot: String(values.adsense_left_slot || ""),
    right_slot: String(values.adsense_right_slot || ""),
  });
}

async function updateSettings(request, db) {
  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }

  const enabled = Boolean(body?.enabled);
  const client = normalizeClient(body?.client);
  const leftSlot = normalizeSlot(body?.left_slot);
  const rightSlot = normalizeSlot(body?.right_slot);
  if (String(body?.client || "").trim() && !client) {
    return json({ error: "AdSense publisher ID must look like ca-pub-1234567890123456." }, 400);
  }
  if (String(body?.left_slot || "").trim() && !leftSlot) {
    return json({ error: "Left AdSense slot must contain digits only." }, 400);
  }
  if (String(body?.right_slot || "").trim() && !rightSlot) {
    return json({ error: "Right AdSense slot must contain digits only." }, 400);
  }
  if (enabled && (!client || (!leftSlot && !rightSlot))) {
    return json({ error: "Add an AdSense publisher ID and at least one side-rail ad slot before enabling ads." }, 400);
  }

  const now = new Date().toISOString();
  await db.batch([
    setting(db, "ads_enabled", enabled ? "1" : "0", now),
    setting(db, "adsense_client", client, now),
    setting(db, "adsense_left_slot", leftSlot, now),
    setting(db, "adsense_right_slot", rightSlot, now),
  ]);
  return readSettings(db);
}

function setting(db, key, value, now) {
  return db.prepare(`
    INSERT INTO site_settings (key, value, updated_at) VALUES (?, ?, ?)
    ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at
  `).bind(key, value, now);
}

async function ensureSettingsTable(db) {
  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_settings (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL DEFAULT '',
      updated_at TEXT NOT NULL
    )
  `).run();
}

function normalizeClient(value) {
  const text = String(value || "").trim();
  return /^ca-pub-\d{10,24}$/.test(text) ? text : "";
}

function normalizeSlot(value) {
  const text = String(value || "").trim();
  return /^\d{4,20}$/.test(text) ? text : "";
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
