const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

export async function listDailySchedule(env, url) {
  await ensureDailySchedule(env);
  const from = validDay(url.searchParams.get("from")) ? url.searchParams.get("from") : todayKey();
  const toCandidate = url.searchParams.get("to");
  const to = validDay(toCandidate) ? toCandidate : addDays(from, 14);
  const result = await env.DB.prepare(`
    SELECT d.day_key, d.quiz_id, q.slug, q.title, q.category, q.status, q.publish_at
    FROM site_daily_schedule d
    JOIN site_quizzes q ON q.id = d.quiz_id
    WHERE d.day_key >= ? AND d.day_key <= ?
    ORDER BY d.day_key ASC
  `).bind(from, to).all();
  return json({
    from,
    to,
    schedule: (result.results || []).map(row => ({
      day_key: String(row.day_key),
      quiz_id: Number(row.quiz_id),
      slug: String(row.slug),
      title: String(row.title),
      category: String(row.category || ""),
      status: String(row.status || ""),
      publish_at: row.publish_at == null ? null : String(row.publish_at),
    })),
  });
}

export async function setDailyQuiz(request, env) {
  await ensureDailySchedule(env);
  const body = await request.json();
  const dayKey = String(body?.day_key || "").trim();
  const quizId = Number(body?.quiz_id);
  if (!validDay(dayKey)) throw badRequest("Choose a valid daily quiz date.");
  if (!Number.isInteger(quizId) || quizId <= 0) throw badRequest("Choose a quiz.");

  const quiz = await env.DB.prepare(`
    SELECT id, slug, title, category, status, publish_at
    FROM site_quizzes WHERE id = ? LIMIT 1
  `).bind(quizId).first();
  if (!quiz) throw notFound("Quiz not found.");
  if (String(quiz.status || "") !== "published") {
    throw badRequest("The daily quiz must be published before it can be scheduled.");
  }

  const dayStart = `${dayKey}T00:00:00.000Z`;
  if (quiz.publish_at && String(quiz.publish_at) > dayStart) {
    throw badRequest("This quiz is published after the selected day starts. Schedule it for a later day or publish it earlier.");
  }

  const now = new Date().toISOString();
  await env.DB.prepare(`
    INSERT INTO site_daily_schedule (day_key, quiz_id, created_at, updated_at)
    VALUES (?, ?, ?, ?)
    ON CONFLICT(day_key) DO UPDATE SET quiz_id = excluded.quiz_id, updated_at = excluded.updated_at
  `).bind(dayKey, quizId, now, now).run();

  return json({
    ok: true,
    schedule: {
      day_key: dayKey,
      quiz_id: Number(quiz.id),
      slug: String(quiz.slug),
      title: String(quiz.title),
      category: String(quiz.category || ""),
    },
  }, 201);
}

export async function clearDailyQuiz(request, env, dayKey) {
  await ensureDailySchedule(env);
  if (!validDay(dayKey)) throw badRequest("Daily quiz date is not valid.");
  await env.DB.prepare("DELETE FROM site_daily_schedule WHERE day_key = ?").bind(dayKey).run();
  return json({ ok: true, day_key: dayKey, cleared: true });
}

async function ensureDailySchedule(env) {
  await env.DB.prepare(`
    CREATE TABLE IF NOT EXISTS site_daily_schedule (
      day_key TEXT PRIMARY KEY,
      quiz_id INTEGER NOT NULL,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
    )
  `).run();
  await env.DB.prepare("CREATE INDEX IF NOT EXISTS idx_site_daily_schedule_quiz ON site_daily_schedule(quiz_id)").run();
}

function validDay(value) {
  const day = String(value || "").trim();
  if (!DATE_RE.test(day)) return false;
  return !Number.isNaN(Date.parse(`${day}T00:00:00.000Z`)) && new Date(`${day}T00:00:00.000Z`).toISOString().slice(0, 10) === day;
}

function todayKey() {
  return new Date().toISOString().slice(0, 10);
}

function addDays(day, count) {
  const date = new Date(`${day}T00:00:00.000Z`);
  date.setUTCDate(date.getUTCDate() + count);
  return date.toISOString().slice(0, 10);
}

function badRequest(message) {
  const error = new Error(message);
  error.status = 400;
  return error;
}

function notFound(message) {
  const error = new Error(message);
  error.status = 404;
  return error;
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value, null, 2), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
