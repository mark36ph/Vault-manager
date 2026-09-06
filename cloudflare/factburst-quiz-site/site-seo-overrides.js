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
    let html = applySeoHtml(await response.text(), seo);
    html = injectQuizQuestionPreviews(html, quiz);
    return new Response(html, {
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
  const suffix = " | Factburst Quiz";
  const generatedTitle = title.toLowerCase().endsWith("factburst quiz")
    ? trimAtWord(title, 65)
    : `${trimAtWord(title, Math.max(18, 65 - suffix.length))}${suffix}`;
  const generatedDescription = trimAtWord(
    `Take this ${questionCount}-question ${category} quiz from Factburst Quiz. Test your knowledge, see your score and discover the facts behind each answer.`,
    160,
  );

  const seoTitle = compactText(quiz?.seo_title) || generatedTitle;
  const seoDescription = compactText(quiz?.seo_description) ||
    (baseDescription.length >= 80 ? trimAtWord(baseDescription, 160) : generatedDescription);
  const socialTitle = compactText(quiz?.social_title) || trimAtWord(title, 100);
  const socialDescription = compactText(quiz?.social_description) || trimAtWord(
    `${questionCount} questions on ${category}. Can you score ${questionCount}/${questionCount}? Play the Factburst Quiz and compare your result.`,
    200,
  );

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
  const quiz = await db.prepare(`
    SELECT
      q.id,
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
  if (!quiz) return null;

  const previewResult = await db.prepare(`
    SELECT position, question
    FROM site_questions
    WHERE quiz_id = ?
    ORDER BY position ASC
    LIMIT 3
  `).bind(quiz.id).all();

  return {
    ...quiz,
    question_previews: (previewResult.results || []).map(row => ({
      position: Number(row.position) || 0,
      question: compactText(row.question),
    })).filter(row => row.question),
  };
}

function injectQuizQuestionPreviews(html, quiz) {
  const previews = Array.isArray(quiz?.question_previews)
    ? quiz.question_previews.filter(item => item?.question).slice(0, 3)
    : [];
  if (!previews.length) return html;

  const section = `
      <section class="quiz-leaderboard-section seo-quiz-previews" aria-labelledby="seo-quiz-previews-title">
        <div class="section-heading"><div><p class="eyebrow">Sample questions</p><h2 id="seo-quiz-previews-title">A look inside this ${escapeHtml(quiz.category || "quiz")} quiz</h2></div></div>
        <div class="quiz-feature">
          <div>
            ${previews.map(item => `<p><strong>Question ${item.position || ""}:</strong> ${escapeHtml(item.question)}</p>`).join("")}
            <p>These previews show the style and subject of the challenge without revealing the answers. Start the quiz to see the full set of questions and find out how you score.</p>
          </div>
        </div>
      </section>
`;

  return html.replace(/(\s*<section class="quiz-leaderboard-section" id="quiz-high-scores">)/i, `${section}$1`);
}

function replaceMeta(html, attribute, key, value) {
  const pattern = new RegExp(`<meta\\s+${attribute}=["']${escapeRegex(key)}["'][^>]*>`, "i");
  return html.replace(pattern, `<meta ${attribute}="${key}" content="${value}">`);
}

function compactText(value) {
  return String(value ?? "").trim().replace(/\s+/g, " ");
}

function trimAtWord(value, maxLength) {
  const text = compactText(value);
  if (text.length <= maxLength) return text;
  if (maxLength < 4) return text.slice(0, Math.max(0, maxLength));
  let candidate = text.slice(0, maxLength).trimEnd();
  const lastSpace = candidate.lastIndexOf(" ");
  if (lastSpace >= Math.max(12, Math.floor(maxLength / 2))) candidate = candidate.slice(0, lastSpace);
  return candidate.replace(/[\s\-:;,.]+$/g, "") + "…";
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
