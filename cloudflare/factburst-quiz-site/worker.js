const JSON_HEADERS = {
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store",
};

const MAX_IMAGE_DATA_URL_LENGTH = 1_250_000;
let schemaReady = false;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname.startsWith("/api/")) {
      try {
        return await handleApi(request, env, url);
      } catch (error) {
        console.error("Factburst site API error", error);
        return json({ error: "Something went wrong." }, 500);
      }
    }

    if (!env.ASSETS) {
      return new Response("Factburst Quiz website assets are not configured.", { status: 503 });
    }

    return env.ASSETS.fetch(request);
  },
};

async function handleApi(request, env, url) {
  if (url.pathname === "/api/health" && request.method === "GET") {
    return json({ ok: true, service: "factburst-quiz-site" });
  }

  if (!env.DB) {
    return json({ error: "The quiz database is not configured yet." }, 503);
  }

  await ensureSchema(env.DB);

  if (url.pathname === "/api/quizzes" && request.method === "GET") {
    return listQuizzes(env.DB, url);
  }

  if (url.pathname === "/api/quizzes/latest" && request.method === "GET") {
    return latestQuiz(env.DB);
  }

  if (url.pathname === "/api/admin/quizzes" && request.method === "POST") {
    return upsertQuiz(request, env);
  }

  const quizMatch = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})$/i);
  if (quizMatch && request.method === "GET") {
    return getQuiz(env.DB, quizMatch[1].toLowerCase());
  }

  const scoreMatch = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/score$/i);
  if (scoreMatch && request.method === "POST") {
    return scoreQuiz(request, env.DB, scoreMatch[1].toLowerCase());
  }

  return json({ error: "Not found." }, 404);
}

async function ensureSchema(db) {
  if (schemaReady) return;

  await db.batch([
    db.prepare(`
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
      )
    `),
    db.prepare(`
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
      )
    `),
    db.prepare(`
      CREATE TABLE IF NOT EXISTS site_attempts (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        quiz_id INTEGER NOT NULL,
        score INTEGER NOT NULL,
        total INTEGER NOT NULL,
        completed_at TEXT NOT NULL,
        FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
      )
    `),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_quizzes_publish ON site_quizzes(status, publish_at)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_attempts_quiz ON site_attempts(quiz_id, completed_at)"),
  ]);

  const columns = await db.prepare("PRAGMA table_info(site_questions)").all();
  const hasImageColumn = (columns.results || []).some(column => column.name === "image_data_url");
  if (!hasImageColumn) {
    await db.prepare("ALTER TABLE site_questions ADD COLUMN image_data_url TEXT NOT NULL DEFAULT ''").run();
  }

  schemaReady = true;
}

async function getLaunchQuizSlug(db, now) {
  const alreadyLive = await db.prepare(`
    SELECT id
    FROM site_quizzes
    WHERE status = 'published'
      AND (publish_at IS NULL OR publish_at <= ?)
    LIMIT 1
  `).bind(now).first();

  if (alreadyLive) return "";

  const launchQuiz = await db.prepare(`
    SELECT slug
    FROM site_quizzes
    WHERE status = 'published'
      AND publish_at > ?
    ORDER BY publish_at ASC, id ASC
    LIMIT 1
  `).bind(now).first();

  return String(launchQuiz?.slug || "");
}

async function listQuizzes(db, url) {
  const requestedLimit = Number.parseInt(url.searchParams.get("limit") || "24", 10);
  const limit = Number.isFinite(requestedLimit) ? Math.min(Math.max(requestedLimit, 1), 100) : 24;
  const category = (url.searchParams.get("category") || "").trim();
  const now = new Date().toISOString();
  const launchSlug = await getLaunchQuizSlug(db, now);

  let statement;
  if (category) {
    statement = db.prepare(`
      SELECT q.id, q.slug, q.title, q.category, q.description, q.youtube_url, q.publish_at,
             COUNT(sq.id) AS question_count,
             COUNT(sa.id) AS attempts
      FROM site_quizzes q
      LEFT JOIN site_questions sq ON sq.quiz_id = q.id
      LEFT JOIN site_attempts sa ON sa.quiz_id = q.id
      WHERE q.status = 'published'
        AND lower(q.category) = lower(?)
      GROUP BY q.id
      ORDER BY COALESCE(q.publish_at, q.created_at) DESC, q.id DESC
      LIMIT ?
    `).bind(category, limit);
  } else {
    statement = db.prepare(`
      SELECT q.id, q.slug, q.title, q.category, q.description, q.youtube_url, q.publish_at,
             COUNT(DISTINCT sq.id) AS question_count,
             COUNT(DISTINCT sa.id) AS attempts
      FROM site_quizzes q
      LEFT JOIN site_questions sq ON sq.quiz_id = q.id
      LEFT JOIN site_attempts sa ON sa.quiz_id = q.id
      WHERE q.status = 'published'
      GROUP BY q.id
      ORDER BY COALESCE(q.publish_at, q.created_at) DESC, q.id DESC
      LIMIT ?
    `).bind(limit);
  }

  const result = await statement.all();
  const quizzes = (result.results || []).map((quiz) => {
    if (launchSlug && quiz.slug === launchSlug) {
      return {
        ...quiz,
        publish_at: now,
        launch_quiz: true,
      };
    }
    return quiz;
  });

  return json({ quizzes });
}

async function latestQuiz(db) {
  const now = new Date().toISOString();
  let quiz = await db.prepare(`
    SELECT q.id, q.slug, q.title, q.category, q.description, q.youtube_url, q.publish_at,
           COUNT(sq.id) AS question_count
    FROM site_quizzes q
    LEFT JOIN site_questions sq ON sq.quiz_id = q.id
    WHERE q.status = 'published'
      AND (q.publish_at IS NULL OR q.publish_at <= ?)
    GROUP BY q.id
    ORDER BY COALESCE(q.publish_at, q.created_at) DESC, q.id DESC
    LIMIT 1
  `).bind(now).first();

  if (quiz) return json({ quiz });

  quiz = await db.prepare(`
    SELECT q.id, q.slug, q.title, q.category, q.description, '' AS youtube_url,
           q.publish_at AS scheduled_publish_at, ? AS publish_at,
           COUNT(sq.id) AS question_count
    FROM site_quizzes q
    LEFT JOIN site_questions sq ON sq.quiz_id = q.id
    WHERE q.status = 'published'
      AND q.publish_at > ?
    GROUP BY q.id
    ORDER BY q.publish_at ASC, q.id ASC
    LIMIT 1
  `).bind(now, now).first();

  if (quiz) quiz.launch_quiz = true;
  return quiz ? json({ quiz }) : json({ quiz: null });
}

async function loadPlayableQuiz(db, slug, now, columns) {
  const quiz = await db.prepare(`
    SELECT ${columns}
    FROM site_quizzes
    WHERE slug = ?
      AND status = 'published'
    LIMIT 1
  `).bind(slug).first();

  if (!quiz) return null;
  if (!quiz.publish_at || quiz.publish_at <= now) {
    return { quiz, launchQuiz: false };
  }

  const launchSlug = await getLaunchQuizSlug(db, now);
  if (launchSlug && launchSlug === slug) {
    return { quiz, launchQuiz: true };
  }

  return null;
}

async function getQuiz(db, slug) {
  const now = new Date().toISOString();
  const playable = await loadPlayableQuiz(
    db,
    slug,
    now,
    "id, slug, title, category, description, youtube_url, publish_at",
  );

  if (!playable) return json({ error: "Quiz not found." }, 404);

  const { quiz, launchQuiz } = playable;
  const questions = await db.prepare(`
    SELECT position, question, answer_a, answer_b, answer_c, answer_d, image_data_url
    FROM site_questions
    WHERE quiz_id = ?
    ORDER BY position ASC
  `).bind(quiz.id).all();

  return json({
    quiz: {
      ...quiz,
      youtube_url: launchQuiz ? "" : (quiz.youtube_url || ""),
      publish_at: launchQuiz ? now : quiz.publish_at,
      launch_quiz: launchQuiz,
      questions: (questions.results || []).map((row) => ({
        position: row.position,
        question: row.question,
        answers: [row.answer_a, row.answer_b, row.answer_c, row.answer_d],
        image_data_url: row.image_data_url || "",
      })),
    },
  });
}

async function scoreQuiz(request, db, slug) {
  const body = await readJson(request);
  const answers = Array.isArray(body?.answers) ? body.answers.map(normalizeAnswer) : [];
  const now = new Date().toISOString();
  const playable = await loadPlayableQuiz(db, slug, now, "id, slug, title, youtube_url, publish_at");

  if (!playable) return json({ error: "Quiz not found." }, 404);

  const { quiz, launchQuiz } = playable;
  const questionResult = await db.prepare(`
    SELECT position, correct_answer, explanation
    FROM site_questions
    WHERE quiz_id = ?
    ORDER BY position ASC
  `).bind(quiz.id).all();
  const questions = questionResult.results || [];

  if (questions.length === 0) return json({ error: "This quiz has no questions yet." }, 409);
  if (answers.length !== questions.length) {
    return json({ error: `Submit exactly ${questions.length} answers.` }, 400);
  }
  if (answers.some((answer) => !answer)) {
    return json({ error: "Every answer must be A, B, C or D." }, 400);
  }

  let score = 0;
  const results = questions.map((question, index) => {
    const correct = normalizeAnswer(question.correct_answer);
    const selected = answers[index];
    const isCorrect = selected === correct;
    if (isCorrect) score++;
    return {
      position: question.position,
      selected,
      correct_answer: correct,
      correct: isCorrect,
      explanation: question.explanation || "",
    };
  });

  await db.prepare(`
    INSERT INTO site_attempts (quiz_id, score, total, completed_at)
    VALUES (?, ?, ?, ?)
  `).bind(quiz.id, score, questions.length, now).run();

  return json({
    score,
    total: questions.length,
    percentage: Math.round((score / questions.length) * 100),
    results,
    youtube_url: launchQuiz ? "" : (quiz.youtube_url || ""),
  });
}

async function upsertQuiz(request, env) {
  if (!env.SITE_ADMIN_KEY) {
    return json({ error: "Website publishing is not enabled yet." }, 503);
  }

  const supplied = request.headers.get("authorization") || "";
  if (supplied !== `Bearer ${env.SITE_ADMIN_KEY}`) {
    return json({ error: "Unauthorized." }, 401);
  }

  const body = await readJson(request);
  const validation = validateQuizPayload(body);
  if (validation.error) return json({ error: validation.error }, 400);

  const quiz = validation.quiz;
  const now = new Date().toISOString();

  await env.DB.prepare(`
    INSERT INTO site_quizzes
      (slug, title, category, description, youtube_url, publish_at, status, created_at, updated_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    ON CONFLICT(slug) DO UPDATE SET
      title = excluded.title,
      category = excluded.category,
      description = excluded.description,
      youtube_url = excluded.youtube_url,
      publish_at = excluded.publish_at,
      status = excluded.status,
      updated_at = excluded.updated_at
  `).bind(
    quiz.slug,
    quiz.title,
    quiz.category,
    quiz.description,
    quiz.youtube_url,
    quiz.publish_at,
    quiz.status,
    now,
    now,
  ).run();

  const saved = await env.DB.prepare("SELECT id FROM site_quizzes WHERE slug = ? LIMIT 1")
    .bind(quiz.slug)
    .first();
  if (!saved) return json({ error: "Could not save the quiz." }, 500);

  await env.DB.prepare("DELETE FROM site_questions WHERE quiz_id = ?").bind(saved.id).run();

  const inserts = quiz.questions.map((question, index) =>
    env.DB.prepare(`
      INSERT INTO site_questions
        (quiz_id, position, question, answer_a, answer_b, answer_c, answer_d, correct_answer, explanation, image_data_url)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `).bind(
      saved.id,
      index + 1,
      question.question,
      question.answers[0],
      question.answers[1],
      question.answers[2],
      question.answers[3],
      question.correct_answer,
      question.explanation,
      question.image_data_url,
    ),
  );
  await env.DB.batch(inserts);

  return json({ ok: true, slug: quiz.slug, questions: quiz.questions.length });
}

function validateQuizPayload(body) {
  if (!body || typeof body !== "object") return { error: "A quiz payload is required." };

  const slug = String(body.slug || "").trim().toLowerCase();
  if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug)) return { error: "Use a URL-safe quiz slug." };

  const title = String(body.title || "").trim();
  const category = String(body.category || "").trim();
  if (!title) return { error: "Quiz title is required." };
  if (!category) return { error: "Quiz category is required." };

  const questions = Array.isArray(body.questions) ? body.questions : [];
  if (questions.length < 1 || questions.length > 100) return { error: "A quiz needs between 1 and 100 questions." };

  const normalizedQuestions = [];
  for (let index = 0; index < questions.length; index++) {
    const item = questions[index] || {};
    const question = String(item.question || "").trim();
    const answers = Array.isArray(item.answers) ? item.answers.map((value) => String(value || "").trim()) : [];
    const correctAnswer = normalizeAnswer(item.correct_answer);
    const imageDataUrl = normalizeImageDataUrl(item.image_data_url);
    if (!question) return { error: `Question ${index + 1} is blank.` };
    if (answers.length !== 4 || answers.some((answer) => !answer) || new Set(answers).size !== 4) {
      return { error: `Question ${index + 1} must have four distinct answers.` };
    }
    if (!correctAnswer) return { error: `Question ${index + 1} needs correct_answer A, B, C or D.` };
    if (imageDataUrl === null) return { error: `Question ${index + 1} has an invalid or oversized website image.` };
    normalizedQuestions.push({
      question,
      answers,
      correct_answer: correctAnswer,
      explanation: String(item.explanation || "").trim(),
      image_data_url: imageDataUrl,
    });
  }

  const status = String(body.status || "draft").trim().toLowerCase();
  if (!new Set(["draft", "published"]).has(status)) return { error: "Status must be draft or published." };

  let publishAt = null;
  if (body.publish_at) {
    const parsed = new Date(body.publish_at);
    if (Number.isNaN(parsed.getTime())) return { error: "publish_at must be a valid date/time." };
    publishAt = parsed.toISOString();
  }

  return {
    quiz: {
      slug,
      title,
      category,
      description: String(body.description || "").trim(),
      youtube_url: String(body.youtube_url || "").trim(),
      publish_at: publishAt,
      status,
      questions: normalizedQuestions,
    },
  };
}

function normalizeImageDataUrl(value) {
  const image = String(value || "").trim();
  if (!image) return "";
  if (image.length > MAX_IMAGE_DATA_URL_LENGTH) return null;
  if (!/^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(image)) return null;
  return image;
}

function normalizeAnswer(value) {
  const answer = String(value || "").trim().toUpperCase();
  return new Set(["A", "B", "C", "D"]).has(answer) ? answer : "";
}

async function readJson(request) {
  try {
    return await request.json();
  } catch {
    return null;
  }
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: JSON_HEADERS,
  });
}
