import { buildQuizSocialCardPng } from "./social-card.js";

const SITE_ORIGIN = "https://factburstquiz.com";
const SOCIAL_IMAGE = `${SITE_ORIGIN}/brand-icon.png?v=2`;
const DIRECTORY_PATH = "/quizzes";
const DIRECTORY_ASSET = "/quizzes.html";
const LEGACY_QUIZ_PATH = "/quiz.html";
const QUIZ_PREFIX = "/quiz/";
const CATEGORY_PREFIX = "/quizzes/";

const CATEGORY_TOPICS = new Map([
  ["science", "biology, chemistry, physics, discoveries and the ideas that explain how the world works"],
  ["history", "ancient civilisations, turning points, leaders, conflicts, inventions and events from across the centuries"],
  ["geography", "countries, capitals, landmarks, borders, landscapes, oceans and places around the world"],
  ["space", "planets, stars, astronomy, spacecraft, missions and discoveries beyond Earth"],
  ["nature & animals", "wildlife, habitats, evolution, plants, ecosystems and remarkable behaviour in the natural world"],
  ["technology", "computing, inventions, software, the internet, devices and the people behind modern technology"],
  ["arts & literature", "authors, novels, poetry, painting, sculpture, movements and influential creative works"],
  ["music", "artists, bands, songs, albums, instruments, genres and music history"],
  ["film", "movies, directors, actors, characters, awards and memorable moments from cinema"],
  ["logos", "recognisable brands, symbols, visual identities and the companies behind familiar marks"],
  ["sports", "teams, athletes, competitions, records, rules and sporting history from around the world"],
  ["entertainment", "television, celebrities, games, popular culture and well-known entertainment moments"],
  ["mathematics", "numbers, arithmetic, geometry, patterns, probability and mathematical reasoning"],
  ["general knowledge", "a broad mix of science, history, geography, culture, everyday facts and surprising trivia"],
]);

const STATIC_PAGES = new Map([
  ["/", ["/", "Factburst Quiz | Fast 10-Question Quizzes", "Play fast factual quizzes across science, history, space, technology, entertainment and more.", "WebSite"]],
  ["/index.html", ["/", "Factburst Quiz | Fast 10-Question Quizzes", "Play fast factual quizzes across science, history, space, technology, entertainment and more.", "WebSite"]],
  [DIRECTORY_PATH, [DIRECTORY_PATH, "Quiz Library | Factburst Quiz", "Browse Factburst quizzes by category and find your next 10-question challenge.", "CollectionPage"]],
  ["/leaderboard.html", ["/leaderboard.html", "Leaderboard | Factburst Quiz", "See the top Factburst Quiz players and compare the best saved quiz scores.", "WebPage"]],
  ["/profile.html", ["/profile.html", "Your Profile | Factburst Quiz", "View your Factburst Quiz scores, rankings, friends and quiz history.", "ProfilePage", true]],
  ["/terms.html", ["/terms.html", "Terms of Use | Factburst Quiz", "Terms of use for Factburst Quiz.", "WebPage"]],
  ["/privacy.html", ["/privacy.html", "Privacy Notice | Factburst Quiz", "Privacy information for Factburst Quiz, including account data, quiz activity and cookie choices.", "WebPage"]],
]);

export async function handleSeoRequest(request, env, url, quizWorker) {
  if (request.method !== "GET" && request.method !== "HEAD") return null;
  if (url.pathname === "/robots.txt") return robotsResponse(request.method === "HEAD");
  if (url.pathname === "/sitemap.xml") return sitemapResponse(request, env, url, quizWorker);

  const socialMatch = url.pathname.match(/^\/social\/quiz\/([a-z0-9][a-z0-9-]{0,79})\.png$/i);
  if (socialMatch) return socialCardResponse(request, env, url, quizWorker, socialMatch[1].toLowerCase());

  const legacyRedirect = legacyRouteRedirect(url);
  if (legacyRedirect) return legacyRedirect;

  if (url.pathname === `${DIRECTORY_PATH}/`) {
    return redirect(`${SITE_ORIGIN}${DIRECTORY_PATH}${url.search}`, 301);
  }

  const quizMatch = url.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})(\/?)$/i);
  if (quizMatch?.[2]) return redirect(`${SITE_ORIGIN}${quizPath(quizMatch[1])}${url.search}`, 301);

  const categoryMatch = url.pathname.match(/^\/quizzes\/([a-z0-9][a-z0-9-]{0,79})(\/?)$/i);
  if (categoryMatch?.[2]) return redirect(`${SITE_ORIGIN}${categoryPathFromSlug(categoryMatch[1])}${url.search}`, 301);

  const isQuiz = Boolean(quizMatch);
  const isCategory = Boolean(categoryMatch);
  const isDirectory = url.pathname === DIRECTORY_PATH;
  const isStatic = STATIC_PAGES.has(url.pathname);
  if (!isQuiz && !isCategory && !isDirectory && !isStatic) return null;
  if (!env.ASSETS) return null;

  const meta = isQuiz
    ? await quizPageMeta(env, url, quizWorker, quizMatch[1].toLowerCase())
    : isCategory
      ? await directoryPageMeta(env, url, quizWorker, categoryMatch[1].toLowerCase())
      : isDirectory
        ? await directoryPageMeta(env, url, quizWorker, "")
        : staticPageMeta(url.pathname);

  const assetPath = isQuiz ? LEGACY_QUIZ_PATH : (isCategory || isDirectory) ? DIRECTORY_ASSET : url.pathname;
  const asset = await fetchHtmlAsset(env, request, url, assetPath);
  if (!asset.ok || !(asset.headers.get("content-type") || "").toLowerCase().includes("text/html")) return asset;

  const headers = new Headers(asset.headers);
  headers.set("content-type", "text/html; charset=utf-8");
  headers.set("cache-control", "public, max-age=300, stale-while-revalidate=600");
  headers.delete("content-length");
  headers.delete("etag");

  if (request.method === "HEAD") {
    return new Response(null, { status: meta?.status || asset.status, headers });
  }

  return new Response(enhanceHtml(await asset.text(), meta), {
    status: meta?.status || asset.status,
    headers,
  });
}

async function fetchHtmlAsset(env, request, url, assetPath) {
  const assetUrl = new URL(assetPath, url.origin);
  return env.ASSETS.fetch(new Request(assetUrl, request));
}

function legacyRouteRedirect(url) {
  if (url.pathname === LEGACY_QUIZ_PATH) {
    const slug = String(url.searchParams.get("slug") || "").toLowerCase();
    if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug)) return redirect(`${SITE_ORIGIN}${DIRECTORY_PATH}`, 301);
    const target = new URL(quizPath(slug), SITE_ORIGIN);
    copyQueryExcept(url, target, new Set(["slug"]));
    return redirect(target.toString(), 301);
  }

  if (url.pathname === DIRECTORY_ASSET) {
    const category = String(url.searchParams.get("category") || "").trim();
    const target = new URL(category ? categoryPath(category) : DIRECTORY_PATH, SITE_ORIGIN);
    copyQueryExcept(url, target, new Set(["category"]));
    return redirect(target.toString(), 301);
  }
  return null;
}

function copyQueryExcept(source, target, excluded) {
  for (const [key, value] of source.searchParams.entries()) {
    if (!excluded.has(key)) target.searchParams.append(key, value);
  }
}

function redirect(location, status = 301) {
  return new Response(null, {
    status,
    headers: {
      location,
      "cache-control": "public, max-age=3600",
    },
  });
}

function staticPageMeta(pathname) {
  const [canonicalPath, title, description, schemaType, noindex = false] = STATIC_PAGES.get(pathname) || STATIC_PAGES.get("/");
  return {
    title,
    description,
    schemaType,
    noindex,
    canonical: `${SITE_ORIGIN}${canonicalPath}`,
    image: SOCIAL_IMAGE,
    imageAlt: "Factburst Quiz — fast questions, factual answers",
  };
}

async function directoryPageMeta(env, url, quizWorker, categorySlug) {
  const base = staticPageMeta(DIRECTORY_PATH);
  const quizzes = await loadQuizSummaries(env, url, quizWorker);
  if (!categorySlug) {
    const listing = Array.isArray(quizzes) ? publishedQuizzes(quizzes) : [];
    return {
      ...base,
      listing,
      breadcrumbs: [
        ["Home", `${SITE_ORIGIN}/`],
        ["Quizzes", `${SITE_ORIGIN}${DIRECTORY_PATH}`],
      ],
    };
  }

  if (!Array.isArray(quizzes)) return { ...base, noindex: true };

  const category = uniqueCategories(quizzes).find(value => slugifyCategory(value) === categorySlug);
  if (!category) {
    return {
      ...base,
      status: 404,
      noindex: true,
      title: "Quiz Category Not Found | Factburst Quiz",
      description: "Browse all available Factburst quiz categories.",
      canonical: `${SITE_ORIGIN}${DIRECTORY_PATH}`,
    };
  }

  const canonical = categoryUrl(category);
  const listing = publishedQuizzes(quizzes)
    .filter(quiz => String(quiz.category || "").toLowerCase() === category.toLowerCase());
  return {
    title: `${category} Quizzes | Factburst Quiz`,
    description: `Play fast ${category} quizzes on Factburst Quiz. Pick a challenge, test your knowledge and compare your score.`,
    canonical,
    image: SOCIAL_IMAGE,
    imageAlt: `Factburst Quiz — ${category} quizzes`,
    schemaType: "CollectionPage",
    category,
    categoryGuide: buildCategoryGuide(category, listing.length),
    listing,
    breadcrumbs: [
      ["Home", `${SITE_ORIGIN}/`],
      ["Quizzes", `${SITE_ORIGIN}${DIRECTORY_PATH}`],
      [category, canonical],
    ],
  };
}

async function quizPageMeta(env, url, quizWorker, slug) {
  const canonical = `${SITE_ORIGIN}${quizPath(slug)}`;
  const fallback = {
    title: "Play Quiz | Factburst Quiz",
    description: "Play a Factburst quiz and see how many questions you can get right.",
    canonical,
    image: SOCIAL_IMAGE,
    imageAlt: "Factburst Quiz — fast questions, factual answers",
    noindex: true,
    schemaType: "WebPage",
  };

  const quizzes = await loadQuizSummaries(env, url, quizWorker);
  if (!Array.isArray(quizzes)) return fallback;
  const quiz = publishedQuizzes(quizzes).find(item => String(item?.slug || "").toLowerCase() === slug);
  if (!quiz) return { ...fallback, status: 404 };

  const title = String(quiz.title || "Factburst Quiz").trim();
  const category = String(quiz.category || "Quiz").trim();
  const questionCount = Number(quiz.question_count) || 10;
  const description = String(quiz.description || `${questionCount} questions on ${category}. See how many you can get right.`).trim();
  const related = publishedQuizzes(quizzes)
    .filter(item => String(item?.slug || "").toLowerCase() !== slug)
    .filter(item => String(item?.category || "").toLowerCase() === category.toLowerCase())
    .slice(0, 4);
  const image = `${SITE_ORIGIN}/social/quiz/${encodeURIComponent(slug)}.png`;

  return {
    title: `${title} | Factburst Quiz`,
    description,
    canonical,
    image,
    imageAlt: `Factburst Quiz — ${title}`,
    imageWidth: 1200,
    imageHeight: 630,
    imageType: "image/png",
    twitterCard: "summary_large_image",
    schemaType: "Quiz",
    quiz: {
      slug,
      title,
      category,
      questionCount,
      description,
      publishAt: quiz.publish_at || "",
    },
    related,
    breadcrumbs: [
      ["Home", `${SITE_ORIGIN}/`],
      ["Quizzes", `${SITE_ORIGIN}${DIRECTORY_PATH}`],
      [category, categoryUrl(category)],
      [title, canonical],
    ],
  };
}

async function socialCardResponse(request, env, url, quizWorker, slug) {
  const quizzes = await loadQuizSummaries(env, url, quizWorker);
  if (!Array.isArray(quizzes)) return new Response("Social preview unavailable.", { status: 503 });
  const quiz = publishedQuizzes(quizzes).find(item => String(item?.slug || "").toLowerCase() === slug);
  if (!quiz) return new Response("Social preview not found.", { status: 404 });

  const headers = {
    "content-type": "image/png",
    "cache-control": "public, max-age=3600, s-maxage=86400, stale-while-revalidate=86400",
    "x-content-type-options": "nosniff",
  };
  if (request.method === "HEAD") return new Response(null, { headers });

  const png = await buildQuizSocialCardPng({
    title: String(quiz.title || "Factburst Quiz"),
    category: String(quiz.category || "Quiz"),
    questionCount: Number(quiz.question_count) || 10,
  });
  return new Response(png, { headers });
}

async function loadQuizSummaries(env, url, quizWorker) {
  if (!env.DB) return null;
  try {
    const apiUrl = new URL("/api/quizzes?limit=100", url);
    const response = await quizWorker.fetch(new Request(apiUrl, { method: "GET" }), env);
    if (!response.ok) return null;
    const payload = await response.json();
    return Array.isArray(payload?.quizzes) ? payload.quizzes : [];
  } catch (error) {
    console.error("Could not load quiz summaries for SEO", error);
    return null;
  }
}

function publishedQuizzes(quizzes, now = Date.now()) {
  return quizzes.filter(quiz => {
    if (!/^[a-z0-9][a-z0-9-]{0,79}$/i.test(String(quiz?.slug || ""))) return false;
    if (quiz?.launch_quiz || !quiz?.publish_at) return true;
    const published = Date.parse(quiz.publish_at);
    return Number.isFinite(published) && published <= now;
  });
}

function uniqueCategories(quizzes) {
  const categories = new Map();
  for (const quiz of publishedQuizzes(quizzes)) {
    const value = String(quiz?.category || "").trim();
    if (value && !categories.has(value.toLowerCase())) categories.set(value.toLowerCase(), value);
  }
  return [...categories.values()].sort((a, b) => a.localeCompare(b));
}

function buildCategoryGuide(category, quizCount) {
  const topics = CATEGORY_TOPICS.get(String(category || "").toLowerCase())
    || `facts, people, places, events and ideas connected with ${category}`;
  const countCopy = quizCount === 1
    ? `There is currently 1 live ${category} quiz to play, with more challenges added as new Factburst quizzes are published.`
    : `There are currently ${quizCount} live ${category} quizzes to play, with more challenges added as new Factburst quizzes are published.`;
  return {
    heading: `Explore ${category} quizzes`,
    paragraphs: [
      `Factburst ${category} quizzes turn ${topics} into quick, factual challenges. Each quiz is designed to be easy to start, fast to finish and useful for discovering what you know — and what might surprise you.`,
      `${countCopy} Pick any quiz below, compare your score, then follow the related-quiz links to keep testing the same subject.`,
    ],
  };
}

function slugifyCategory(value) {
  return String(value || "")
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/&/g, " and ")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .replace(/-and-/g, "-")
    .slice(0, 80);
}

function categoryPath(category) {
  return categoryPathFromSlug(slugifyCategory(category));
}

function categoryPathFromSlug(slug) {
  return `${CATEGORY_PREFIX}${String(slug || "").toLowerCase()}`;
}

function categoryUrl(category) {
  return `${SITE_ORIGIN}${categoryPath(category)}`;
}

function quizPath(slug) {
  return `${QUIZ_PREFIX}${String(slug || "").toLowerCase()}`;
}

function quizUrl(slug) {
  return `${SITE_ORIGIN}${quizPath(slug)}`;
}

function robotsResponse(head = false) {
  const body = [
    "User-agent: *",
    "Allow: /",
    "Disallow: /profile.html",
    "Disallow: /api/",
    `Sitemap: ${SITE_ORIGIN}/sitemap.xml`,
    "",
  ].join("\n");
  return new Response(head ? null : body, {
    headers: { "content-type": "text/plain; charset=utf-8", "cache-control": "public, max-age=86400" },
  });
}

async function sitemapResponse(request, env, url, quizWorker) {
  const loaded = await loadQuizSummaries(env, url, quizWorker);
  const quizzes = Array.isArray(loaded) ? publishedQuizzes(loaded) : [];
  const categoryEntries = Array.isArray(loaded)
    ? uniqueCategories(loaded).map(category => ({ loc: categoryUrl(category) }))
    : [];
  const quizEntries = quizzes.map(quiz => ({
    loc: quizUrl(quiz.slug),
    lastmod: sitemapLastmod(quiz.publish_at),
  }));

  const entries = dedupeSitemapEntries([
    { loc: `${SITE_ORIGIN}/` },
    { loc: `${SITE_ORIGIN}${DIRECTORY_PATH}` },
    ...categoryEntries,
    { loc: `${SITE_ORIGIN}/about.html` },
    { loc: `${SITE_ORIGIN}/faq.html` },
    { loc: `${SITE_ORIGIN}/contact.html` },
    { loc: `${SITE_ORIGIN}/leaderboard.html` },
    { loc: `${SITE_ORIGIN}/terms.html` },
    { loc: `${SITE_ORIGIN}/privacy.html` },
    ...quizEntries,
  ]);
  const xml = buildSitemapXml(entries);
  return new Response(request.method === "HEAD" ? null : xml, {
    headers: { "content-type": "application/xml; charset=utf-8", "cache-control": "public, max-age=1800, stale-while-revalidate=3600" },
  });
}

function sitemapLastmod(value) {
  if (!value) return "";
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? new Date(parsed).toISOString() : "";
}

function dedupeSitemapEntries(entries) {
  const seen = new Set();
  return entries.filter(entry => {
    const loc = typeof entry === "string" ? entry : entry?.loc;
    if (!loc || seen.has(loc)) return false;
    seen.add(loc);
    return true;
  });
}

export function buildSitemapXml(entries) {
  const items = entries.map(entry => {
    const value = typeof entry === "string" ? { loc: entry } : entry || {};
    const lastmod = value.lastmod ? `<lastmod>${escapeXml(value.lastmod)}</lastmod>` : "";
    return `  <url><loc>${escapeXml(value.loc || "")}</loc>${lastmod}</url>`;
  }).join("\n");
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${items}\n</urlset>\n`;
}

export function enhanceHtml(html, meta) {
  let output = String(html || "");
  const title = String(meta?.title || "Factburst Quiz");
  const description = String(meta?.description || "Fast factual quizzes from Factburst Quiz.");
  const canonical = String(meta?.canonical || `${SITE_ORIGIN}/`);
  const image = String(meta?.image || SOCIAL_IMAGE);
  const imageAlt = String(meta?.imageAlt || "Factburst Quiz");

  output = output.replace(/<title>[\s\S]*?<\/title>/i, `<title>${escapeHtml(title)}</title>`);
  output = output.replace(/<meta\s+name=["']description["'][^>]*>/i, `<meta name="description" content="${escapeHtml(description)}">`);
  output = output.replace(/\s*<link\s+rel=["']preload["']\s+href=["']\/brand-icon\.png\?v=2["']\s+as=["']image["']\s*\/?>\s*/i, "\n");
  output = output.replace(/\s*<link\s+rel=["']canonical["'][^>]*>\s*/gi, "\n");
  output = output.replace(/\s*<meta\s+(?:property|name)=["'](?:og:|twitter:)[^"']+["'][^>]*>\s*/gi, "\n");
  output = output.replace(/\s*<meta\s+name=["']robots["'][^>]*>\s*/gi, "\n");
  output = output.replace(/\s*<script\s+type=["']application\/ld\+json["'][^>]*data-factburst-seo[^>]*>[\s\S]*?<\/script>\s*/gi, "\n");

  const tags = [
    `<link rel="canonical" href="${escapeHtml(canonical)}">`,
    meta?.noindex
      ? `<meta name="robots" content="noindex,follow">`
      : `<meta name="robots" content="index,follow,max-image-preview:large,max-snippet:-1,max-video-preview:-1">`,
    `<meta property="og:site_name" content="Factburst Quiz">`,
    `<meta property="og:type" content="website">`,
    `<meta property="og:title" content="${escapeHtml(title)}">`,
    `<meta property="og:description" content="${escapeHtml(description)}">`,
    `<meta property="og:url" content="${escapeHtml(canonical)}">`,
    `<meta property="og:image" content="${escapeHtml(image)}">`,
    meta?.imageType ? `<meta property="og:image:type" content="${escapeHtml(meta.imageType)}">` : "",
    meta?.imageWidth ? `<meta property="og:image:width" content="${Number(meta.imageWidth)}">` : "",
    meta?.imageHeight ? `<meta property="og:image:height" content="${Number(meta.imageHeight)}">` : "",
    `<meta property="og:image:alt" content="${escapeHtml(imageAlt)}">`,
    `<meta name="twitter:card" content="${escapeHtml(meta?.twitterCard || "summary")}">`,
    `<meta name="twitter:title" content="${escapeHtml(title)}">`,
    `<meta name="twitter:description" content="${escapeHtml(description)}">`,
    `<meta name="twitter:image" content="${escapeHtml(image)}">`,
    `<script type="application/ld+json" data-factburst-seo>${schemaJson(meta)}</script>`,
  ].filter(Boolean).join("\n  ");
  output = output.replace(/<\/head>/i, `  ${tags}\n</head>`);

  output = output.replace(/href="\/quizzes\.html"/gi, 'href="/quizzes"');
  output = preRenderPageContent(output, meta);
  if (!output.includes('src="/growth.js"')) output = output.replace(/<\/body>/i, `  <script src="/growth.js" defer></script>\n</body>`);
  output = output.replace(/<img\s+src=["']\/brand-icon\.png\?v=2["']\s+alt=["']["'](?![^>]*decoding=)/gi, '<img src="/brand-icon.png?v=2" alt="" decoding="async"');
  return output;
}

function preRenderPageContent(html, meta) {
  let output = html;
  if (meta?.quiz) {
    output = output.replace(
      /<span class="category-pill" id="quiz-category"><\/span>/i,
      `<span class="category-pill" id="quiz-category">${escapeHtml(meta.quiz.category)}</span>`,
    );
    output = output.replace(
      /<h1 id="quiz-title" class="quiz-title"><\/h1>/i,
      `<h1 id="quiz-title" class="quiz-title">${escapeHtml(meta.quiz.title)}</h1>`,
    );
    output = output.replace(
      /<span id="quiz-progress-text">[\s\S]*?<\/span>/i,
      `<span id="quiz-progress-text">Question 1 of ${Number(meta.quiz.questionCount) || 10}</span>`,
    );
    output = injectQuizDiscovery(output, meta);
  }

  if (meta?.category) {
    output = output.replace(
      /<body data-page="home" data-layout="directory">/i,
      `<body data-page="home" data-layout="directory" data-category="${escapeHtml(meta.category)}">`,
    );
    output = output.replace(
      /<h1>Find your next challenge\.<\/h1>/i,
      `<h1>${escapeHtml(meta.category)} quizzes</h1>`,
    );
    output = output.replace(
      /<p class="hero-text">Browse every live Factburst quiz, filter by category, or see which quizzes are scheduled next\.<\/p>/i,
      `<p class="hero-text">Test your knowledge with live ${escapeHtml(meta.category)} quizzes, compare your score and find another challenge when you finish.</p>`,
    );
    output = injectCategoryGuide(output, meta);
  }

  return output;
}

function injectCategoryGuide(html, meta) {
  const guide = meta?.categoryGuide;
  if (!guide || !Array.isArray(guide.paragraphs) || !guide.paragraphs.length) return html;
  const section = `
    <section class="shell section seo-category-guide" aria-labelledby="seo-category-guide-title">
      <div class="section-heading"><div><p class="eyebrow">About this category</p><h2 id="seo-category-guide-title">${escapeHtml(guide.heading)}</h2></div></div>
      <div class="quiz-feature">
        <div>${guide.paragraphs.map(text => `<p>${escapeHtml(text)}</p>`).join("")}</div>
        <a class="button button-secondary" href="${DIRECTORY_PATH}#browse">Browse all categories</a>
      </div>
    </section>
`;
  return html.replace(/(\s*<section class="shell section" id="latest">)/i, `${section}$1`);
}

function injectQuizDiscovery(html, meta) {
  const about = `
      <section class="quiz-leaderboard-section seo-quiz-about" aria-labelledby="seo-quiz-about-title">
        <div class="section-heading"><div><p class="eyebrow">About this quiz</p><h2 id="seo-quiz-about-title">${escapeHtml(meta.quiz.title)}</h2></div></div>
        <div class="quiz-feature">
          <div>
            <p>${escapeHtml(meta.quiz.description)}</p>
            <p>This ${Number(meta.quiz.questionCount) || 10}-question ${escapeHtml(meta.quiz.category)} challenge is free to play. Finish the quiz to see your score, review the factual explanations and compare your result with the Factburst leaderboard.</p>
          </div>
          <a class="button button-secondary" href="${escapeHtml(categoryPath(meta.quiz.category))}">More ${escapeHtml(meta.quiz.category)}</a>
        </div>
      </section>
`;

  const related = Array.isArray(meta.related) && meta.related.length
    ? `
      <section class="quiz-leaderboard-section seo-related-quizzes" aria-labelledby="seo-related-title">
        <div class="section-heading"><div><p class="eyebrow">Keep playing</p><h2 id="seo-related-title">Related ${escapeHtml(meta.quiz.category)} quizzes</h2></div></div>
        <div class="quiz-grid">
          ${meta.related.map(quiz => `
          <a class="quiz-card" href="${escapeHtml(quizPath(quiz.slug))}">
            <span class="category-pill">${escapeHtml(quiz.category || meta.quiz.category)}</span>
            <h3>${escapeHtml(quiz.title || "Factburst Quiz")}</h3>
            <p>${escapeHtml(quiz.description || `Try another ${meta.quiz.category} challenge.`)}</p>
            <div class="quiz-card-footer"><span>${Number(quiz.question_count) || 10} questions</span><span>Play →</span></div>
          </a>`).join("")}
        </div>
      </section>
`
    : "";

  return html.replace(
    /(\s*<section class="quiz-leaderboard-section" id="quiz-high-scores">)/i,
    `${about}${related}$1`,
  );
}

function schemaJson(meta) {
  const canonical = String(meta?.canonical || `${SITE_ORIGIN}/`);
  const graph = [
    {
      "@type": "WebSite",
      "@id": `${SITE_ORIGIN}/#website`,
      name: "Factburst Quiz",
      url: `${SITE_ORIGIN}/`,
      inLanguage: "en",
    },
  ];

  if (Array.isArray(meta?.breadcrumbs) && meta.breadcrumbs.length > 0) {
    graph.push({
      "@type": "BreadcrumbList",
      "@id": `${canonical}#breadcrumbs`,
      itemListElement: meta.breadcrumbs.map(([name, item], index) => ({
        "@type": "ListItem",
        position: index + 1,
        name,
        item,
      })),
    });
  }

  if (meta?.quiz) {
    graph.push({
      "@type": "WebPage",
      "@id": `${canonical}#webpage`,
      name: meta.quiz.title,
      description: meta.quiz.description,
      url: canonical,
      isPartOf: { "@id": `${SITE_ORIGIN}/#website` },
      ...(meta?.breadcrumbs ? { breadcrumb: { "@id": `${canonical}#breadcrumbs` } } : {}),
      mainEntity: { "@id": `${canonical}#quiz` },
      ...(Array.isArray(meta.related) && meta.related.length ? { relatedLink: meta.related.map(item => quizUrl(item.slug)) } : {}),
      inLanguage: "en",
    });
    graph.push({
      "@type": "Quiz",
      "@id": `${canonical}#quiz`,
      name: meta.quiz.title,
      description: meta.quiz.description,
      url: canonical,
      image: meta.image || undefined,
      educationalUse: "assessment",
      numberOfQuestions: meta.quiz.questionCount,
      about: { "@type": "Thing", name: meta.quiz.category },
      isAccessibleForFree: true,
      inLanguage: "en",
      ...(meta.quiz.publishAt ? { datePublished: meta.quiz.publishAt } : {}),
    });
  } else if (Array.isArray(meta?.listing)) {
    const listId = `${canonical}#quiz-list`;
    graph.push({
      "@type": meta?.schemaType || "CollectionPage",
      "@id": `${canonical}#webpage`,
      name: String(meta?.title || "Factburst Quiz"),
      description: String(meta?.description || "Fast factual quizzes from Factburst Quiz."),
      url: canonical,
      isPartOf: { "@id": `${SITE_ORIGIN}/#website` },
      ...(meta?.breadcrumbs ? { breadcrumb: { "@id": `${canonical}#breadcrumbs` } } : {}),
      mainEntity: { "@id": listId },
      inLanguage: "en",
    });
    graph.push({
      "@type": "ItemList",
      "@id": listId,
      name: meta?.category ? `${meta.category} quizzes` : "Factburst quizzes",
      numberOfItems: meta.listing.length,
      itemListElement: meta.listing.map((quiz, index) => ({
        "@type": "ListItem",
        position: index + 1,
        name: String(quiz?.title || "Factburst Quiz"),
        url: quizUrl(quiz?.slug || ""),
      })),
    });
  } else {
    graph.push({
      "@type": meta?.schemaType || "WebPage",
      "@id": `${canonical}#webpage`,
      name: String(meta?.title || "Factburst Quiz"),
      description: String(meta?.description || "Fast factual quizzes from Factburst Quiz."),
      url: canonical,
      ...(meta?.schemaType === "WebSite" ? {} : { isPartOf: { "@id": `${SITE_ORIGIN}/#website` } }),
      inLanguage: "en",
    });
  }

  return JSON.stringify({ "@context": "https://schema.org", "@graph": graph }).replace(/</g, "\\u003c");
}

function escapeHtml(value) {
  return String(value || "").replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
function escapeXml(value) { return escapeHtml(value).replace(/'/g, "&apos;"); }