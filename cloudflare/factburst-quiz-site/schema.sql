CREATE TABLE IF NOT EXISTS site_quizzes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    slug TEXT NOT NULL UNIQUE,
    title TEXT NOT NULL,
    category TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    seo_title TEXT NOT NULL DEFAULT '',
    seo_description TEXT NOT NULL DEFAULT '',
    social_title TEXT NOT NULL DEFAULT '',
    social_description TEXT NOT NULL DEFAULT '',
    youtube_url TEXT NOT NULL DEFAULT '',
    publish_at TEXT,
    status TEXT NOT NULL DEFAULT 'draft',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS site_questions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    quiz_id INTEGER NOT NULL,
    position INTEGER NOT NULL,
    question TEXT NOT NULL,
    answer_a TEXT NOT NULL,
    answer_b TEXT NOT NULL,
    answer_c TEXT NOT NULL,
    answer_d TEXT NOT NULL,
    correct_answer TEXT NOT NULL,
    explanation TEXT NOT NULL DEFAULT '',
    image_key TEXT NOT NULL DEFAULT '',
    image_data_url TEXT NOT NULL DEFAULT '',
    UNIQUE(quiz_id, position),
    FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS site_attempts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    quiz_id INTEGER NOT NULL,
    score INTEGER NOT NULL,
    total INTEGER NOT NULL,
    completed_at TEXT NOT NULL,
    FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
);

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
    created_at TEXT NOT NULL,
    last_login_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS site_sessions (
    token_hash TEXT PRIMARY KEY,
    user_id INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS site_user_scores (
    user_id INTEGER NOT NULL,
    quiz_id INTEGER NOT NULL,
    best_score INTEGER NOT NULL,
    total INTEGER NOT NULL,
    attempts INTEGER NOT NULL DEFAULT 1,
    first_completed_at TEXT NOT NULL,
    last_completed_at TEXT NOT NULL,
    PRIMARY KEY (user_id, quiz_id),
    FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,
    FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS site_email_verifications (
    token_hash TEXT PRIMARY KEY,
    user_id INTEGER NOT NULL,
    email_key TEXT NOT NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_site_quizzes_publish
    ON site_quizzes(status, publish_at);

CREATE INDEX IF NOT EXISTS idx_site_attempts_quiz
    ON site_attempts(quiz_id, completed_at);

CREATE INDEX IF NOT EXISTS idx_site_sessions_expiry
    ON site_sessions(expires_at);

CREATE INDEX IF NOT EXISTS idx_site_user_scores_quiz
    ON site_user_scores(quiz_id, best_score DESC);

CREATE UNIQUE INDEX IF NOT EXISTS idx_site_users_email_unique
    ON site_users(email_key) WHERE email_key <> '';

CREATE INDEX IF NOT EXISTS idx_site_email_verifications_user
    ON site_email_verifications(user_id, created_at DESC);
