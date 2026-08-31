import { handleSeoRequest as handleBaseSeoRequest } from "./site-seo.js";
import { buildQuizSocialCardPng } from "./social-card.js";

let seoSchemaReady = false;

export async function handleSeoRequest(request, env, url, quizWorker) {
  const response = await handleBaseSeoRequest(request, env, url, quizWorker);
  if (!response || !env.DB) return response;

  const quizMatch = url.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})$/i);
  const socialMatch = url.pathname.match(/^\/social\/quiz\/([a-z0-9][a-z0-9-]{0,79})\.png$/i);
  const slug = String(quizMatch?.[1] || socialMatch?.[1] || "").toLowerCase();
  if (!slug || !response.ok) return response;

  try {
    await ensureSeoSchema(env.DB);
    const quiz = await loadQuizSeo(env.DB, slug);
    if (!quiz) return response;
    const seo = effectiveSeoMetadata(quiz);

    if (socialMatch) {
      if (request.method === "HEAD") return response;
      const png = await buildQuizSocialCardPng({
        title: seo.socialTitle,
        category: String(quiz.category || "Quiz"),
        questionCount: Number(quiz.question_count) || 10,
      });
      const headers = new Headers(response.headers);
      headers.set("content-type", "image/png");
      headers.set("cache-control", "public, max-age=300, s-maxage=3600, stale-while-revalidate=3600");
      headers.set("x-content-type-options", "nosniff");
      headers.delete("content-length");
      headers.delete("etag");
      return new Response(png, { status: response.status, headers });
    }

    if (request.method === "HEAD") return response;
    const contentType = response.headers.get("content-type") || "";
    if (!/text\/html/i.test(contentType)) return response;

    const headers = new Headers(response.headers);
    headers.delete("content-length");
    headers.delete("etag");
    return new Response(applySeoHtml(await response.text(), seo), {
      status: response.status,
      statusText: response.statusText,
      headers,
    });
  } catch (error) {
    console.error("Could not apply saved quiz SEO metadata", error);
    return response;
  }
}

export function effectiveSeoMetadata(quiz) {
  const title = compactText(quiz?.title) || "Factburst Quiz";
  const category = compactText(quiz?.category) || "General Knowledge";
  const questionCount = Math.max(1, Number(quiz?.question_count) || 10);
  const baseDescription = compactText(quiz?.description);

  const seoTitle = compactText(quiz?.seo_title) || `${title} | Factburst Quiz`;
  const seoDescription = compactText(quiz?.seo_description) ||
    baseDescription ||
    `Take this ${questionCount}-question ${category} quiz from Factburst Quiz. Test your knowledge, see your score and discover the facts behind each answer.`;
  const socialTitle = compactText(quiz?.social_title) || title;
  const socialDescription = compactText(quiz?.social_description) ||
    `${questionCount} questions on ${category}. Can you score ${questionCount}/${questionCount}?`;

  return { seoTitle, seoDescription, socialTitle, socialDescription };
}

export function applySeoHtml(html, seo) {
  let output = String(html || "");
  const seoTitle = escapeHtml(seo?.seoTitle || "Factburst Quiz");
  const seoDescription = escapeHtml(seo?.seoDescription || "Fast factual quizzes from Factburst Quiz.");
  const socialTitle = escapeHtml(seo?.socialTitle || seo?.seoTitle || "Factburst Quiz");
  const socialDescription = escapeHtml(seo?.socialDescription || seo?.seoDescription || "Fast factual quizzes from Factburst Quiz.");

  output = output.replace(/<title>[\s\S]*?<\/title>/i, `<title>${seoTitle}</title>`);
  output = replaceMeta(output, "name", "description", seoDescription);
  output = replaceMeta(output, "property", "og:title", socialTitle);
  output = replaceMeta(output, "property", "og:description", socialDescription);
  output = replaceMeta(output, "name", "twitter:title", socialTitle);
  output = replaceMeta(output, "name", "twitter:description", socialDescription);
  return output;
}

async function ensureSeoSchema(db) {
  if (seoSchemaReady) return;
  const columns = await db.prepare("PRAGMA table_info(site_quizzes)").all();
  const names = new Set((columns.results || []).map(column => String(column.name || "")));
  if (!names.size) return;

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

async function loadQuizSeo(db, slug) {
  return db.prepare(`
    SELECT
      q.slug,
      q.title,
      q.category,
      q.description,
      q.seo_title,
      q.seo_description,
      q.social_title,
      q.social_description,
      (SELECT COUNT(*) FROM site_questions sq WHERE sq.quiz_id = q.id) AS question_count
    FROM site_quizzes q
    WHERE q.slug = ?
    LIMIT 1
  `).bind(slug).first();
}

function replaceMeta(html, attribute, key, value) {
  const pattern = new RegExp(`<meta\\s+${attribute}=["']${escapeRegex(key)}["'][^>]*>`, "i");
  return html.replace(pattern, `<meta ${attribute}="${key}" content="${value}">`);
}

function compactText(value) {
  return String(value ?? "").trim().replace(/\s+/g, " ");
}

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function escapeRegex(value) {
  return String(value ?? "").replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
