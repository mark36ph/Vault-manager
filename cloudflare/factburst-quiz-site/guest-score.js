import quizWorker from "./worker.js";

const originalQuizWorkerFetch = quizWorker.fetch.bind(quizWorker);
quizWorker.fetch = async (request, env, context) => {
  const url = new URL(request.url);
  const match = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/score$/i);
  if (match && request.method === "POST") {
    try {
      const body = await request.clone().json();
      if (Array.isArray(body?.question_positions) && body.question_positions.length > 0) {
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
  const requestedPositions = Array.isArray(body?.question_positions)
    ? body.question_positions.map(Number).filter(Number.isInteger)
    : [];
  const now = new Date().toISOString();
  const playable = await loadPlayableQuiz(db, slug, now);
  if (!playable) return json({ error: "Quiz not found." }, 404);

  const { quiz, launchQuiz } = playable;
  const questionResult = await db.prepare(`
    SELECT position, correct_answer, explanation
    FROM site_questions WHERE quiz_id = ? ORDER BY position ASC
  `).bind(quiz.id).all();
  const allQuestions = questionResult.results || [];
  const positions = requestedPositions.length > 0 ? requestedPositions : allQuestions.map(question => Number(question.position));
  const uniquePositions = [...new Set(positions)];
  const questionByPosition = new Map(allQuestions.map(question => [Number(question.position), question]));
  const questions = uniquePositions.map(position => questionByPosition.get(position)).filter(Boolean);

  if (allQuestions.length === 0) return json({ error: "This quiz has no questions yet." }, 409);
  if (questions.length !== uniquePositions.length) return json({ error: "One or more selected questions are invalid." }, 400);
  if (answers.length !== questions.length) return json({ error: `Submit exactly ${questions.length} answers.` }, 400);
  if (answers.some(answer => !answer)) return json({ error: "Every answer must be A, B, C or D." }, 400);

  let score = 0;
  const results = questions.map((question, index) => {
    const correct = normalizeAnswer(question.correct_answer);
    const selected = answers[index];
    const isCorrect = selected === correct;
    if (isCorrect) score++;
    return {
      position: Number(question.position),
      selected,
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
  if (positions.length === 0) return null;
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
  return new Response(JSON.stringify({ ...payload, guest: false, saved: false, question_count: positions.length }), {
    status: response.status,
    headers,
  });
}

async function loadPlayableQuiz(db, slug, now) {
  const quiz = await db.prepare(`
    SELECT id, slug, title, youtube_url, publish_at
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
