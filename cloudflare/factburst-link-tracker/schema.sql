CREATE TABLE IF NOT EXISTS campaigns (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    slug TEXT NOT NULL UNIQUE,
    quiz_id INTEGER,
    title TEXT NOT NULL DEFAULT '',
    destination_url TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS clicks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    campaign_slug TEXT NOT NULL,
    source TEXT NOT NULL,
    clicked_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    device_type TEXT NOT NULL DEFAULT '',
    FOREIGN KEY (campaign_slug) REFERENCES campaigns(slug)
);

CREATE TABLE IF NOT EXISTS unique_clicks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    campaign_slug TEXT NOT NULL,
    source TEXT NOT NULL,
    visitor_hash TEXT NOT NULL,
    clicked_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    device_type TEXT NOT NULL DEFAULT '',
    FOREIGN KEY (campaign_slug) REFERENCES campaigns(slug)
);

CREATE INDEX IF NOT EXISTS idx_clicks_campaign
ON clicks(campaign_slug);

CREATE INDEX IF NOT EXISTS idx_clicks_source
ON clicks(source);

CREATE INDEX IF NOT EXISTS idx_clicks_campaign_source
ON clicks(campaign_slug, source);

CREATE INDEX IF NOT EXISTS idx_clicks_clicked_at
ON clicks(clicked_at);

CREATE INDEX IF NOT EXISTS idx_unique_clicks_campaign
ON unique_clicks(campaign_slug);

CREATE INDEX IF NOT EXISTS idx_unique_clicks_visitor_time
ON unique_clicks(campaign_slug, visitor_hash, clicked_at);
