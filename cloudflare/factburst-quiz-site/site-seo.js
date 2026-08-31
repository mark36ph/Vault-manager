const SITE_ORIGIN = "https://factburstquiz.com";
const SOCIAL_IMAGE = `${SITE_ORIGIN}/brand-icon.png?v=2`;
const QUIZ_PATH = "/quiz.html";
const DIRECTORY_PATH = "/quizzes.html";

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
  if (!STATIC_PAGES.has(url.pathname) && url.pathname !== QUIZ_PATH) return null;
  if (!env.ASSETS) return null;
  if (request.method === "HEAD") return env.ASSETS.fetch(request);

  const asset = await env.ASSETS.fetch(request);
  if (!asset.ok || !(asset.headers.get("content-type") || "").toLowerCase().includes("text/html")) return asset;

  const meta = url.pathname === QUIZ_PATH
    ? await quizPageMeta(env, url, quizWorker)
    : url.pathname === DIRECTORY_PATH
      ? await directoryPageMeta(env, url, quizWorker)
      : staticPageMeta(url.pathname);
  const headers = new Headers(asset.headers);
  headers.set("content-type", "text/html; charset=utf-8");
  headers.set("cache-control", "public, max-age=300, stale-while-revalidate=600");
  headers.delete("content-length");
  headers.delete("etag");
  return new Response(enhanceHtml(await asset.text(), meta), {
    status: meta?.status || asset.status,
    headers,
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

async function directoryPageMeta(env, url, quizWorker) {
  const base = staticPageMeta(DIRECTORY_PATH);
  const requested = String(url.searchParams.get("category") || "").trim();
  const quizzes = await loadQuizSummaries(env, url, quizWorker);
  if (!requested) {
    return {
      ...base,
      listing: Array.isArray(quizzes) ? publishedQuizzes(quizzes) : [],
      breadcrumbs: [
        ["Home", `${SITE_ORIGIN}/`],
        ["Quizzes", `${SITE_ORIGIN}${DIRECTORY_PATH}`],
      ],
    };
  }

  if (!Array.isArray(quizzes)) {
    return { ...base, noindex: true };
  }

  const category = uniqueCategories(quizzes)
    .find(value => value.localeCompare(requested, undefined, { sensitivity: "accent" }) === 0 || value.toLowerCase() === requested.toLowerCase());
  if (!category) {
    return {
      ...base,
      noindex: true,
      title: "Quiz Category Not Found | Factburst Quiz",
      description: "Browse all available Factburst quiz categories.",
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
    listing,
    breadcrumbs: [
      ["Home", `${SITE_ORIGIN}/`],
      ["Quizzes", `${SITE_ORIGIN}${DIRECTORY_PATH}`],
      [category, canonical],
    ],
  };
}

async function quizPageMeta(env, url, quizWorker) {
  const slug = String(url.searchParams.get("slug") || "").toLowerCase();
  const valid = /^[a-z0-9][a-z0-9-]{0,79}$/.test(slug);
  const canonical = valid ? `${SITE_ORIGIN}${QUIZ_PATH}?slug=${encodeURIComponent(slug)}` : `${SITE_ORIGIN}${QUIZ_PATH}`;
  const fallback = {
    title: "Play Quiz | Factburst Quiz",
    description: "Play a Factburst quiz and see how many questions you can get right.",
    canonical,
    image: SOCIAL_IMAGE,
    imageAlt: "Factburst Quiz — fast questions, factual answers",
    noindex: true,
    schemaType: "WebPage",
  };
  if (!valid) return { ...fallback, status: 404 };

  const quizzes = await loadQuizSummaries(env, url, quizWorker);
  if (!Array.isArray(quizzes)) return fallback;
  const quiz = quizzes.find(item => String(item?.slug || "").toLowerCase() === slug);
  const publishAt = quiz?.publish_at ? Date.parse(quiz.publish_at) : NaN;
  if (!quiz || (!quiz.launch_quiz && Number.isFinite(publishAt) && publishAt > Date.now())) {
    return { ...fallback, status: 404 };
  }

  const title = String(quiz.title || "Factburst Quiz").trim();
  const category = String(quiz.category || "Quiz").trim();
  const questionCount = Number(quiz.question_count) || 10;
  const description = String(quiz.description || `${questionCount} questions on ${category}. See how many you can get right.`).trim();
  return {
    title: `${title} | Factburst Quiz`,
    description,
    canonical,
    image: SOCIAL_IMAGE,
    imageAlt: `Factburst Quiz — ${title}`,
    schemaType: "Quiz",
    quiz: {
      title,
      category,
      questionCount,
      description,
      publishAt: quiz.publish_at || "",
    },
    breadcrumbs: [
      ["Home", `${SITE_ORIGIN}/`],
      ["Quizzes", `${SITE_ORIGIN}${DIRECTORY_PATH}`],
      [category, categoryUrl(category)],
      [title, canonical],
    ],
  };
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

function categoryUrl(category) {
  return `${SITE_ORIGIN}${DIRECTORY_PATH}?category=${encodeURIComponent(String(category || "").trim())}`;
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
    loc: `${SITE_ORIGIN}${QUIZ_PATH}?slug=${encodeURIComponent(String(quiz.slug).toLowerCase())}`,
    lastmod: sitemapLastmod(quiz.publish_at),
  }));

  const entries = dedupeSitemapEntries([
    { loc: `${SITE_ORIGIN}/` },
    { loc: `${SITE_ORIGIN}${DIRECTORY_PATH}` },
    ...categoryEntries,
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
    `<meta property="og:image:alt" content="${escapeHtml(imageAlt)}">`,
    `<meta name="twitter:card" content="summary">`,
    `<meta name="twitter:title" content="${escapeHtml(title)}">`,
    `<meta name="twitter:description" content="${escapeHtml(description)}">`,
    `<meta name="twitter:image" content="${escapeHtml(image)}">`,
    `<script type="application/ld+json" data-factburst-seo>${schemaJson(meta)}</script>`,
  ].filter(Boolean).join("\n  ");
  output = output.replace(/<\/head>/i, `  ${tags}\n</head>`);

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
  }

  if (meta?.category) {
    output = output.replace(
      /<h1>Find your next challenge\.<\/h1>/i,
      `<h1>${escapeHtml(meta.category)} quizzes</h1>`,
    );
    output = output.replace(
      /<p class="hero-text">Browse every live Factburst quiz, filter by category, or see which quizzes are scheduled next\.<\/p>/i,
      `<p class="hero-text">Test your knowledge with live ${escapeHtml(meta.category)} quizzes, compare your score and find another challenge when you finish.</p>`,
    );
  }

  return output;
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
      inLanguage: "en",
    });
    graph.push({
      "@type": "Quiz",
      "@id": `${canonical}#quiz`,
      name: meta.quiz.title,
      description: meta.quiz.description,
      url: canonical,
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
        url: `${SITE_ORIGIN}${QUIZ_PATH}?slug=${encodeURIComponent(String(quiz?.slug || "").toLowerCase())}`,
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
