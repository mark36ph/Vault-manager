const ADS_KEYS = ["ads_enabled", "adsense_client", "adsense_left_slot", "adsense_right_slot"];

export async function handlePublicAdsConfig(request, db, url) {
  if (url.pathname !== "/api/site/ads" || request.method !== "GET") return null;
  await ensureSettingsTable(db);
  const rows = await db.prepare(`
    SELECT key, value FROM site_settings
    WHERE key IN ('ads_enabled', 'adsense_client', 'adsense_left_slot', 'adsense_right_slot')
  `).all();
  const values = Object.fromEntries((rows.results || []).map(row => [String(row.key || ""), String(row.value || "")]));
  const client = normalizeClient(values.adsense_client);
  const left = normalizeSlot(values.adsense_left_slot);
  const right = normalizeSlot(values.adsense_right_slot);
  const enabled = values.ads_enabled === "1" && Boolean(client) && Boolean(left || right);
  return json({
    enabled,
    client,
    left_slot: left,
    right_slot: right,
  });
}

export async function ensureSettingsTable(db) {
  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_settings (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL DEFAULT '',
      updated_at TEXT NOT NULL
    )
  `).run();
}

export function normalizeClient(value) {
  const text = String(value || "").trim();
  return /^ca-pub-\d{10,24}$/.test(text) ? text : "";
}

export function normalizeSlot(value) {
  const text = String(value || "").trim();
  return /^\d{4,20}$/.test(text) ? text : "";
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
