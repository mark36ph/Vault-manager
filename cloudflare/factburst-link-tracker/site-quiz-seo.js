const JSON_HEADERS = {
  "Content-Type": "application/json; charset=utf-8",
  "Cache-Control": "no-store",
  "X-Content-Type-Options": "nosniff",
};

let seoSchemaReady = false;

export async function handleSiteQuizSeoAdmin(request, env, url) {
  if (url.pathname === "/api/site/quiz-seo" && request.method === "GET") {
    return listSiteQuizSeo(env);
  }

  const match = url.pathname.match(/^\/api\/site\/quiz-seo\/([a-z0-9][a-z0-9-]{0,79})$/i);
  if (match && request.method === "PATCH") {
    return updateSiteQuizSeo(request, env, match[1].toLowerCase());
  }

  return null;
}

export async function listSiteQuizSeo(env) {
  await ensureSeoSchema(env.DB);
  const result = await env.DB.prepare(`
    SELECT
      q.slug,
      q.title,
      q.category,
      q.description,
      q.seo_title,
      q.seo_description,
      q.social_title,
      q.social_description,
      q.status,
      q.publish_at,
      q.updated_at,
      (SELECT COUNT(*) FROM site_questions sq WHERE sq.quiz_id = q.id) AS question_count
    FROM site_quizzes q
    ORDER BY COALESCE(q.publish_at, q.created_at) DESC, q.id DESC
  `).all();
  return json({ quizzes: result.results || [] });
}

export async function updateSiteQuizSeo(request, env, slug) {
  await ensureSeoSchema(env.DB);
  const existing = await env.DB.prepare(`
    SELECT slug FROM site_quizzes WHERE slug = ? LIMIT 1
  `).bind(slug).first();
  if (!existing) return json({ error: "Quiz not found" }, 404);

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "Request body must be valid JSON." }, 400);
  }

  const normalized = normalizeSeoUpdate(body);
  if (normalized.error) return json({ error: normalized.error }, 400);

  await env.DB.prepare(`
    UPDATE site_quizzes
    SET seo_title = ?,
        seo_description = ?,
        social_title = ?,
        social_description = ?,
        updated_at = CURRENT_TIMESTAMP
    WHERE slug = ?
  `).bind(
    normalized.value.seo_title,
    normalized.value.seo_description,
    normalized.value.social_title,
    normalized.value.social_description,
    slug,
  ).run();

  return json({
    ok: true,
    slug,
    seo: normalized.value,
  });
}

export function normalizeSeoUpdate(body) {
  if (!body || typeof body !== "object") return { error: "SEO metadata is required." };

  const seoTitle = normalizeText(body.seo_title, 120);
  const seoDescription = normalizeText(body.seo_description, 300);
  const socialTitle = normalizeText(body.social_title, 160);
  const socialDescription = normalizeText(body.social_description, 300);

  if (seoTitle === null) return { error: "SEO title must be 120 characters or fewer." };
  if (seoDescription === null) return { error: "SEO description must be 300 characters or fewer." };
  if (socialTitle === null) return { error: "Social title must be 160 characters or fewer." };
  if (socialDescription === null) return { error: "Social description must be 300 characters or fewer." };
  if (!seoTitle) return { error: "SEO title is required." };
  if (!seoDescription) return { error: "SEO description is required." };
  if (!socialTitle) return { error: "Social title is required." };
  if (!socialDescription) return { error: "Social description is required." };

  return {
    value: {
      seo_title: seoTitle,
      seo_description: seoDescription,
      social_title: socialTitle,
      social_description: socialDescription,
    },
  };
}

async function ensureSeoSchema(db) {
  if (seoSchemaReady) return;
  const columns = await db.prepare("PRAGMA table_info(site_quizzes)").all();
  const names = new Set((columns.results || []).map(column => String(column.name || "")));
  if (!names.size) throw new Error("The website quiz database has not been prepared yet.");

  if (!names.has("seo_title")) {
    await db.prepare("ALTER TABLE site_quizzes ADD COLUMN seo_title TEXT NOT NULL DEFAULT ''").run();
  }
  if (!names.has("seo_description")) {
    await db.prepare("ALTER TABLE site_quizzes ADD COLUMN seo_description TEXT NOT NULL DEFAULT ''").run();
  }
  if (!names.has("social_title")) {
    await db.prepare("ALTER TABLE site_quizzes ADD COLUMN social_title TEXT NOT NULL DEFAULT ''").run();
  }
  if (!names.has("social_description")) {
    await db.prepare("ALTER TABLE site_quizzes ADD COLUMN social_description TEXT NOT NULL DEFAULT ''").run();
  }

  seoSchemaReady = true;
}

function normalizeText(value, maxLength) {
  const text = String(value ?? "").trim().replace(/\s+/g, " ");
  return text.length <= maxLength ? text : null;
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value, null, 2), { status, headers: JSON_HEADERS });
}
