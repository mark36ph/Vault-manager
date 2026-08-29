import { upsertSiteQuiz } from "./site-quiz-admin.js";

export async function upsertSiteQuizPreservingVisibility(request, env) {
  let existing = null;
  let slug = "";

  try {
    const body = await request.clone().json();
    slug = cleanQuizSlug(body?.slug);
    existing = await env.DB.prepare(
      `SELECT status, publish_at
       FROM site_quizzes
       WHERE slug = ?
       LIMIT 1`
    ).bind(slug).first();
  } catch {
    // Let the existing full upsert path report malformed payloads in its normal way.
  }

  const response = await upsertSiteQuiz(request, env);
  if (!response.ok || !existing || !slug) return response;

  // Full content/question sync must never silently undo a manual Website-page
  // visibility decision. Existing quizzes keep their current website state and
  // release time until the dedicated visibility endpoint changes them.
  await env.DB.prepare(
    `UPDATE site_quizzes
     SET status = ?, publish_at = ?, updated_at = CURRENT_TIMESTAMP
     WHERE slug = ?`
  ).bind(
    normalizeStoredStatus(existing.status),
    existing.publish_at ?? null,
    slug
  ).run();

  return response;
}

export async function updateSiteQuizVisibility(request, env, slugValue) {
  const slug = cleanQuizSlug(slugValue);
  const existing = await env.DB.prepare(
    `SELECT slug, status, publish_at
     FROM site_quizzes
     WHERE slug = ?
     LIMIT 1`
  ).bind(slug).first();

  if (!existing) return json({ error: "Quiz not found" }, 404);

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }

  const status = String(body?.status || "").trim().toLowerCase();
  if (status !== "draft" && status !== "published") {
    return json({ error: "status must be either draft or published." }, 400);
  }

  const hasPublishAt = Object.prototype.hasOwnProperty.call(body || {}, "publish_at");
  let publishAt = existing.publish_at ?? null;
  if (hasPublishAt) {
    publishAt = normalizePublishAt(body.publish_at);
    if (publishAt instanceof Response) return publishAt;
  }

  await env.DB.prepare(
    `UPDATE site_quizzes
     SET status = ?, publish_at = ?, updated_at = CURRENT_TIMESTAMP
     WHERE slug = ?`
  ).bind(status, publishAt, slug).run();

  return json({
    ok: true,
    quiz: {
      slug,
      status,
      publish_at: publishAt,
    },
  });
}

function cleanQuizSlug(value) {
  const slug = String(value || "").trim().toLowerCase();
  if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug)) {
    const error = new Error(
      "Quiz slug may contain only lowercase letters, numbers and hyphens."
    );
    error.status = 400;
    throw error;
  }
  return slug;
}

function normalizeStoredStatus(value) {
  return String(value || "").trim().toLowerCase() === "draft" ? "draft" : "published";
}

function normalizePublishAt(value) {
  if (value === null || value === undefined || String(value).trim() === "") return null;
  const text = String(value).trim();
  const parsed = Date.parse(text);
  if (!Number.isFinite(parsed)) {
    return json({ error: "publish_at must be an ISO-8601 date/time or null." }, 400);
  }
  return new Date(parsed).toISOString();
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
