const CREATE_SITE_USERS = `
  CREATE TABLE IF NOT EXISTS site_users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT NOT NULL,
    username_key TEXT NOT NULL UNIQUE,
    email TEXT NOT NULL DEFAULT '',
    email_key TEXT NOT NULL DEFAULT '',
    email_verified_at TEXT,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    password_iterations INTEGER NOT NULL,
    password_scheme TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'active',
    suspended_at TEXT,
    suspension_reason TEXT NOT NULL DEFAULT '',
    referral_code TEXT NOT NULL DEFAULT '',
    referred_by_user_id INTEGER,
    created_at TEXT NOT NULL,
    last_login_at TEXT NOT NULL,
    FOREIGN KEY (referred_by_user_id) REFERENCES site_users(id) ON DELETE SET NULL
  )
`;

export const SITE_USER_UPGRADES = [
  { name: "email", sql: "ALTER TABLE site_users ADD COLUMN email TEXT NOT NULL DEFAULT ''" },
  { name: "email_key", sql: "ALTER TABLE site_users ADD COLUMN email_key TEXT NOT NULL DEFAULT ''" },
  { name: "email_verified_at", sql: "ALTER TABLE site_users ADD COLUMN email_verified_at TEXT" },
  { name: "password_scheme", sql: "ALTER TABLE site_users ADD COLUMN password_scheme TEXT NOT NULL DEFAULT ''" },
  { name: "status", sql: "ALTER TABLE site_users ADD COLUMN status TEXT NOT NULL DEFAULT 'active'" },
  { name: "suspended_at", sql: "ALTER TABLE site_users ADD COLUMN suspended_at TEXT" },
  { name: "suspension_reason", sql: "ALTER TABLE site_users ADD COLUMN suspension_reason TEXT NOT NULL DEFAULT ''" },
  { name: "referral_code", sql: "ALTER TABLE site_users ADD COLUMN referral_code TEXT NOT NULL DEFAULT ''" },
  { name: "referred_by_user_id", sql: "ALTER TABLE site_users ADD COLUMN referred_by_user_id INTEGER" },
];

const AFTER_USER_SCHEMA = [
  `CREATE TABLE IF NOT EXISTS site_sessions (token_hash TEXT PRIMARY KEY,user_id INTEGER NOT NULL,created_at TEXT NOT NULL,expires_at TEXT NOT NULL,FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE)`,
  `CREATE TABLE IF NOT EXISTS site_user_scores (user_id INTEGER NOT NULL,quiz_id INTEGER NOT NULL,best_score INTEGER NOT NULL,total INTEGER NOT NULL,attempts INTEGER NOT NULL DEFAULT 1,first_completed_at TEXT NOT NULL,last_completed_at TEXT NOT NULL,PRIMARY KEY (user_id, quiz_id),FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE)`,
  `CREATE TABLE IF NOT EXISTS site_email_verifications (token_hash TEXT PRIMARY KEY,user_id INTEGER NOT NULL,email_key TEXT NOT NULL,created_at TEXT NOT NULL,expires_at TEXT NOT NULL,FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE)`,
  `CREATE TABLE IF NOT EXISTS site_challenges (token_hash TEXT PRIMARY KEY,challenger_user_id INTEGER NOT NULL,challenged_user_id INTEGER,quiz_id INTEGER NOT NULL,challenger_score INTEGER NOT NULL,total INTEGER NOT NULL,created_at TEXT NOT NULL,expires_at TEXT NOT NULL,FOREIGN KEY (challenger_user_id) REFERENCES site_users(id) ON DELETE CASCADE,FOREIGN KEY (challenged_user_id) REFERENCES site_users(id) ON DELETE CASCADE,FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE)`,
  `CREATE TABLE IF NOT EXISTS site_friendships (id INTEGER PRIMARY KEY AUTOINCREMENT,user_a_id INTEGER NOT NULL,user_b_id INTEGER NOT NULL,requested_by_user_id INTEGER NOT NULL,status TEXT NOT NULL DEFAULT 'pending',created_at TEXT NOT NULL,responded_at TEXT,UNIQUE(user_a_id,user_b_id),CHECK(user_a_id < user_b_id),FOREIGN KEY (user_a_id) REFERENCES site_users(id) ON DELETE CASCADE,FOREIGN KEY (user_b_id) REFERENCES site_users(id) ON DELETE CASCADE,FOREIGN KEY (requested_by_user_id) REFERENCES site_users(id) ON DELETE CASCADE)`,
  "CREATE INDEX IF NOT EXISTS idx_site_sessions_expiry ON site_sessions(expires_at)",
  "CREATE INDEX IF NOT EXISTS idx_site_user_scores_quiz ON site_user_scores(quiz_id, best_score DESC)",
  "CREATE INDEX IF NOT EXISTS idx_site_email_verifications_user ON site_email_verifications(user_id, created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_site_challenges_expiry ON site_challenges(expires_at)",
  "CREATE INDEX IF NOT EXISTS idx_site_challenges_user ON site_challenges(challenger_user_id, created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_site_challenges_target ON site_challenges(challenged_user_id, created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_site_friendships_a ON site_friendships(user_a_id, status, created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_site_friendships_b ON site_friendships(user_b_id, status, created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_site_users_status ON site_users(status, created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_site_users_referred_by ON site_users(referred_by_user_id, created_at DESC)",
  "CREATE UNIQUE INDEX IF NOT EXISTS idx_site_users_email_unique ON site_users(email_key) WHERE email_key <> ''",
  "CREATE UNIQUE INDEX IF NOT EXISTS idx_site_users_referral_code ON site_users(referral_code) WHERE referral_code <> ''",
];

export function missingSiteUserUpgrades(columns) {
  const names = new Set((columns || []).map(column => String(column?.name || "")));
  return SITE_USER_UPGRADES.filter(upgrade => !names.has(upgrade.name));
}

export async function prepareAccountSchema(db) {
  await db.prepare(CREATE_SITE_USERS).run();
  const columns = await db.prepare("PRAGMA table_info(site_users)").all();
  for (const upgrade of missingSiteUserUpgrades(columns.results || [])) await db.prepare(upgrade.sql).run();
  for (const statement of AFTER_USER_SCHEMA) await db.prepare(statement).run();
  await backfillReferralCodes(db);
}

async function backfillReferralCodes(db) {
  const users = await db.prepare("SELECT id FROM site_users WHERE referral_code='' OR referral_code IS NULL LIMIT 500").all();
  for (const user of users.results || []) {
    for (let attempt = 0; attempt < 5; attempt++) {
      const code = `FB-${randomCode(8)}`;
      try {
        await db.prepare("UPDATE site_users SET referral_code=? WHERE id=? AND (referral_code='' OR referral_code IS NULL)").bind(code,user.id).run();
        break;
      } catch {}
    }
  }
}
function randomCode(length){const chars="ABCDEFGHJKLMNPQRSTUVWXYZ23456789";const bytes=new Uint8Array(length);crypto.getRandomValues(bytes);return [...bytes].map(b=>chars[b%chars.length]).join("");}
