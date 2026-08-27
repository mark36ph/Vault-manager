CREATE TABLE IF NOT EXISTS site_quizzes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    slug TEXT NOT NULL UNIQUE,
    title TEXT NOT NULL,
    category TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
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

CREATE INDEX IF NOT EXISTS idx_site_quizzes_publish
    ON site_quizzes(status, publish_at);

CREATE INDEX IF NOT EXISTS idx_site_attempts_quiz
    ON site_attempts(quiz_id, completed_at);
