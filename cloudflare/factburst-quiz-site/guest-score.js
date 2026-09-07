import quizWorker from "./worker.js";

const originalQuizWorkerFetch = quizWorker.fetch.bind(quizWorker);
quizWorker.fetch = async (request, env, context) => {
  const url = new URL(request.url);
  const poolMatch = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/question-pool$/i);
  if (poolMatch && request.method === "GET") {
    return questionPoolRequest(env, poolMatch[1].toLowerCase());
  }

  const match = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/score$/i);
  if (match && request.method === "POST") {
    try {
      const body = await request.clone().json();
      if ((Array.isArray(body?.question_refs) && body.question_refs.length > 0) ||
          (Array.isArray(body?.question_positions) && body.question_positions.length > 0)) {
        return scoreSelectedQuizRequest(request, env, match[1].toLowerCase());
      }
    } catch {
      // Fall through to the normal quiz worker for invalid requests.
    }
  }
  return originalQuizWorkerFetch(request, env, context);
};

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

export async function scoreGuestQuiz(request, db, slug) {
  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }

  const answers = Array.isArray(body?.answers) ? body.answers.map(normalizeAnswer) : [];
  const requestedRefs = normalizeQuestionRefs(body?.question_refs);
  const requestedPositions = Array.isArray(body?.question_positions)
    ? body.question_positions.map(Number).filter(Number.isInteger)
    : [];
  const now = new Date().toISOString();
  const playable = await loadPlayableQuiz(db, slug, now);
  if (!playable) return json({ error: "Quiz not found." }, 404);

  const { quiz, launchQuiz } = playable;
  const allQuestions = requestedRefs.length > 0
    ? await loadCategoryQuestions(db, quiz.category, now, launchQuiz ? slug : "")
    : await loadQuizQuestions(db, quiz.id);

  const selected = requestedRefs.length > 0
    ? selectQuestionsByRefs(allQuestions, requestedRefs)
    : selectQuestionsByPositions(allQuestions, requestedPositions);

  if (allQuestions.length === 0) return json({ error: "This quiz has no questions yet." }, 409);
  if (selected.invalid) return json({ error: "One or more selected questions are invalid." }, 400);
  const questions = selected.questions;
  if (questions.length === 0) return json({ error: "This quiz has no questions yet." }, 409);
  if (answers.length !== questions.length) return json({ error: `Submit exactly ${questions.length} answers.` }, 400);
  if (answers.some(answer => !answer)) return json({ error: "Every answer must be A, B, C or D." }, 400);

  let score = 0;
  const results = questions.map((question, index) => {
    const correct = normalizeAnswer(question.correct_answer);
    const selectedAnswer = answers[index];
    const isCorrect = selectedAnswer === correct;
    if (isCorrect) score++;
    return {
      position: Number(question.position),
      quiz_id: Number(question.quiz_id || quiz.id),
      selected: selectedAnswer,
      correct_answer: correct,
      correct: isCorrect,
      explanation: String(question.explanation || ""),
    };
  });

  return json({
    score,
    total: questions.length,
    percentage: Math.round((score / questions.length) * 100),
    results,
    youtube_url: launchQuiz ? "" : String(quiz.youtube_url || ""),
    guest: true,
    saved: false,
    question_count: questions.length,
  });
}

export async function scoreSelectedQuizRequest(request, env, slug) {
  if (!env?.DB) return null;
  let body;
  try {
    body = await request.clone().json();
  } catch {
    return null;
  }
  const positions = Array.isArray(body?.question_positions)
    ? body.question_positions.map(Number).filter(Number.isInteger)
    : [];
  const refs = normalizeQuestionRefs(body?.question_refs);
  if (positions.length === 0 && refs.length === 0) return null;
  const response = await scoreGuestQuiz(request, env.DB, slug);
  if (!response.ok) return response;
  const headers = new Headers(response.headers);
  headers.set("content-type", "application/json; charset=utf-8");
  headers.set("cache-control", "no-store");
  const payload = await response.clone().json();
  const completedAt = new Date().toISOString();
  const playable = await loadPlayableQuiz(env.DB, slug, completedAt);
  if (playable?.quiz?.id && Number.isInteger(Number(payload.score)) && Number.isInteger(Number(payload.total))) {
    await env.DB.prepare(`INSERT INTO site_attempts (quiz_id, score, total, completed_at) VALUES (?, ?, ?, ?)`).bind(playable.quiz.id, Number(payload.score), Number(payload.total), completedAt).run();
  }
  return new Response(JSON.stringify({ ...payload, guest: false, saved: false, question_count: Number(payload.total || 0) }), {
    status: response.status,
    headers,
  });
}

async function questionPoolRequest(env, slug) {
  if (!env?.DB) return json({ error: "The quiz database is not configured yet." }, 503);
  const now = new Date().toISOString();
  const playable = await loadPlayableQuiz(env.DB, slug, now);
  if (!playable) return json({ error: "Quiz not found." }, 404);
  const { quiz, launchQuiz } = playable;
  const questions = await loadCategoryQuestions(env.DB, quiz.category, now, launchQuiz ? slug : "");
  const mapped = questions.map(question => ({
    quiz_id: Number(question.quiz_id),
    quiz_slug: String(question.quiz_slug || ""),
    position: Number(question.position),
    question: String(question.question || ""),
    answers: [question.answer_a, question.answer_b, question.answer_c, question.answer_d].map(value => String(value || "")),
    image_url: question.image_key ? `/quiz-images/${String(question.image_key).replace(/^\/+/, "")}` : "",
    image_data_url: String(question.image_data_url || ""),
  }));
  return json({
    slug,
    category: String(quiz.category || ""),
    base_question_count: mapped.filter(question => Number(question.quiz_id) === Number(quiz.id)).length,
    question_count: mapped.length,
    questions: mapped,
  });
}

async function loadQuizQuestions(db, quizId) {
  const result = await db.prepare(`
    SELECT quiz_id, position, question, answer_a, answer_b, answer_c, answer_d, correct_answer, explanation, image_key, image_data_url
    FROM site_questions WHERE quiz_id = ? ORDER BY position ASC
  `).bind(quizId).all();
  return result.results || [];
}

async function loadCategoryQuestions(db, category, now, launchSlug = "") {
  const result = await db.prepare(`
    SELECT q.id AS quiz_id, q.slug AS quiz_slug, q.category, q.publish_at,
           sq.position, sq.question, sq.answer_a, sq.answer_b, sq.answer_c, sq.answer_d,
           sq.correct_answer, sq.explanation, sq.image_key, sq.image_data_url
    FROM site_quizzes q
    JOIN site_questions sq ON sq.quiz_id = q.id
    WHERE q.status = 'published'
      AND lower(q.category) = lower(?)
      AND ((q.publish_at IS NULL OR q.publish_at <= ?) OR q.slug = ?)
    ORDER BY q.id DESC, sq.position ASC
  `).bind(String(category || ""), now, String(launchSlug || "")).all();
  return result.results || [];
}

function selectQuestionsByPositions(allQuestions, positions) {
  const requested = positions.length > 0 ? [...new Set(positions)] : allQuestions.map(question => Number(question.position));
  const byPosition = new Map(allQuestions.map(question => [Number(question.position), question]));
  const questions = requested.map(position => byPosition.get(position)).filter(Boolean);
  return { questions, invalid: questions.length !== requested.length };
}

function selectQuestionsByRefs(allQuestions, refs) {
  const key = question => `${Number(question.quiz_id)}:${Number(question.position)}`;
  const byRef = new Map(allQuestions.map(question => [key(question), question]));
  const unique = [];
  const seen = new Set();
  for (const ref of refs) {
    const refKey = `${ref.quiz_id}:${ref.position}`;
    if (seen.has(refKey)) continue;
    seen.add(refKey);
    unique.push(byRef.get(refKey));
  }
  return { questions: unique.filter(Boolean), invalid: unique.some(question => !question) };
}

function normalizeQuestionRefs(value) {
  if (!Array.isArray(value)) return [];
  const refs = [];
  for (const item of value) {
    const quizId = Number(item?.quiz_id);
    const position = Number(item?.position);
    if (Number.isInteger(quizId) && quizId > 0 && Number.isInteger(position) && position > 0) {
      refs.push({ quiz_id: quizId, position });
    }
  }
  return refs;
}

async function loadPlayableQuiz(db, slug, now) {
  const quiz = await db.prepare(`
    SELECT id, slug, title, category, youtube_url, publish_at
    FROM site_quizzes
    WHERE slug = ? AND status = 'published'
    LIMIT 1
  `).bind(slug).first();
  if (!quiz) return null;
  if (!quiz.publish_at || String(quiz.publish_at) <= now) return { quiz, launchQuiz: false };

  const alreadyLive = await db.prepare(`
    SELECT id FROM site_quizzes
    WHERE status = 'published' AND (publish_at IS NULL OR publish_at <= ?)
    LIMIT 1
  `).bind(now).first();
  if (alreadyLive) return null;

  const launch = await db.prepare(`
    SELECT slug FROM site_quizzes
    WHERE status = 'published' AND publish_at > ?
    ORDER BY publish_at ASC, id ASC
    LIMIT 1
  `).bind(now).first();
  return String(launch?.slug || "") === String(slug) ? { quiz, launchQuiz: true } : null;
}

export function normalizeAnswer(value) {
  const answer = String(value || "").trim().toUpperCase();
  return /^[A-D]$/.test(answer) ? answer : "";
}
