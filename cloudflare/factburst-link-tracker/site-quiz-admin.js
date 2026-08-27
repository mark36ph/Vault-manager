const JSON_HEADERS = {
  "Content-Type": "application/json; charset=utf-8",
  "Cache-Control": "no-store",
  "X-Content-Type-Options": "nosniff",
};

const MAX_IMAGE_DATA_URL_LENGTH = 1_250_000;
let siteSchemaReady = false;

export async function listSiteQuizzes(env) {
  await ensureSiteSchema(env.DB);
  const result = await env.DB.prepare(`
    SELECT q.slug, q.status, q.publish_at, q.updated_at,
           COUNT(sq.id) AS question_count
    FROM site_quizzes q
    LEFT JOIN site_questions sq ON sq.quiz_id = q.id
    GROUP BY q.id
    ORDER BY COALESCE(q.publish_at, q.created_at) DESC, q.id DESC
  `).all();

  return json({ quizzes: result.results || [] });
}

export async function upsertSiteQuiz(request, env) {
  await ensureSiteSchema(env.DB);
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

  const saved = await env.DB.prepare(
    "SELECT id FROM site_quizzes WHERE slug = ? LIMIT 1"
  ).bind(quiz.slug).first();
  if (!saved) return json({ error: "Could not save the website quiz." }, 500);

  const statements = [
    env.DB.prepare("DELETE FROM site_questions WHERE quiz_id = ?").bind(saved.id),
    ...quiz.questions.map((question, index) =>
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
      )
    ),
  ];
  await env.DB.batch(statements);

  return json({ ok: true, slug: quiz.slug, questions: quiz.questions.length });
}

async function ensureSiteSchema(db) {
  if (siteSchemaReady) return;
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
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_quizzes_publish ON site_quizzes(status, publish_at)"),
  ]);

  const columns = await db.prepare("PRAGMA table_info(site_questions)").all();
  const hasImageColumn = (columns.results || []).some(column => column.name === "image_data_url");
  if (!hasImageColumn) {
    await db.prepare("ALTER TABLE site_questions ADD COLUMN image_data_url TEXT NOT NULL DEFAULT ''").run();
  }

  siteSchemaReady = true;
}

function validateQuizPayload(body) {
  if (!body || typeof body !== "object") return { error: "A quiz payload is required." };

  const slug = String(body.slug || "").trim().toLowerCase();
  if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug)) return { error: "Use a URL-safe quiz slug." };

  const title = String(body.title || "").trim();
  const category = String(body.category || "").trim();
  if (!title) return { error: "Quiz title is required." };
  if (!category) return { error: "Quiz category is required." };

  const youtubeUrl = validateYouTubeUrl(body.youtube_url);
  if (!youtubeUrl) return { error: "A valid HTTPS YouTube URL is required." };

  const questions = Array.isArray(body.questions) ? body.questions : [];
  if (questions.length < 1 || questions.length > 100)
    return { error: "A quiz needs between 1 and 100 questions." };

  const normalizedQuestions = [];
  for (let index = 0; index < questions.length; index++) {
    const item = questions[index] || {};
    const question = String(item.question || "").trim();
    const answers = Array.isArray(item.answers)
      ? item.answers.map(value => String(value || "").trim())
      : [];
    const correctAnswer = normalizeAnswer(item.correct_answer);
    const imageDataUrl = normalizeImageDataUrl(item.image_data_url);
    if (!question) return { error: `Question ${index + 1} is blank.` };
    if (answers.length !== 4 || answers.some(answer => !answer) || new Set(answers).size !== 4) {
      return { error: `Question ${index + 1} must have four distinct answers.` };
    }
    if (!correctAnswer)
      return { error: `Question ${index + 1} needs correct_answer A, B, C or D.` };
    if (imageDataUrl === null)
      return { error: `Question ${index + 1} has an invalid or oversized website image.` };
    normalizedQuestions.push({
      question,
      answers,
      correct_answer: correctAnswer,
      explanation: String(item.explanation || "").trim(),
      image_data_url: imageDataUrl,
    });
  }

  const status = String(body.status || "published").trim().toLowerCase();
  if (!new Set(["draft", "published"]).has(status))
    return { error: "Status must be draft or published." };

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
      youtube_url: youtubeUrl,
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

function validateYouTubeUrl(value) {
  try {
    const url = new URL(String(value || "").trim());
    if (url.protocol !== "https:") return "";
    const host = url.hostname.toLowerCase();
    if (!(host === "youtube.com" || host.endsWith(".youtube.com") || host === "youtu.be")) return "";
    return url.toString();
  } catch {
    return "";
  }
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
  return new Response(JSON.stringify(value, null, 2), { status, headers: JSON_HEADERS });
}
