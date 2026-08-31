import test from "node:test";
import assert from "node:assert/strict";
import { buildSitemapXml, enhanceHtml, handleSeoRequest } from "./site-seo.js";
import { buildQuizSocialCardPng } from "./social-card.js";

const quizHtml = `<!doctype html><html><head><meta name="description" content="old"><title>Old</title><link rel="preload" href="/brand-icon.png?v=2" as="image"></head><body data-page="quiz"><main class="quiz-shell"><section id="quiz-player"><span class="category-pill" id="quiz-category"></span><span id="quiz-progress-text">Question 1 of 10</span><h1 id="quiz-title" class="quiz-title"></h1></section><section class="quiz-leaderboard-section" id="quiz-high-scores"></section></main></body></html>`;
const directoryHtml = `<!doctype html><html><head><meta name="description" content="old"><title>Old</title></head><body data-page="home" data-layout="directory"><main><section class="shell page-intro"><h1>Find your next challenge.</h1><p class="hero-text">Browse every live Factburst quiz, filter by category, or see which quizzes are scheduled next.</p></section><section class="shell section" id="latest"></section><section id="browse"></section></main></body></html>`;

const quizzes = [
  {
    slug: "space-quiz",
    title: "Space Quiz",
    category: "Space",
    description: "Ten questions about planets, missions and astronomy.",
    question_count: 10,
    publish_at: "2026-08-01T10:00:00.000Z",
  },
  {
    slug: "space-missions",
    title: "Space Missions Quiz",
    category: "Space",
    description: "Test your knowledge of famous space missions.",
    question_count: 10,
    publish_at: "2026-08-02T10:00:00.000Z",
  },
  {
    slug: "history-quiz",
    title: "History Quiz",
    category: "History",
    description: "Ten history questions.",
    question_count: 10,
    publish_at: "2026-08-03T10:00:00.000Z",
  },
];

function mockWorker() {
  return {
    async fetch(request) {
      const url = new URL(request.url);
      if (url.pathname === "/api/quizzes") {
        return Response.json({ quizzes });
      }
      return Response.json({ error: "not found" }, { status: 404 });
    },
  };
}

function mockEnv() {
  return {
    DB: {},
    ASSETS: {
      async fetch(request) {
        const path = new URL(request.url).pathname;
        const html = path === "/quiz.html" ? quizHtml : directoryHtml;
        return new Response(html, { headers: { "content-type": "text/html; charset=utf-8" } });
      },
    },
  };
}

test("enhanceHtml injects clean canonical social metadata and growth runtime", () => {
  const output = enhanceHtml(quizHtml, {
    title: "Space Quiz | Factburst Quiz",
    description: "Ten questions about space.",
    canonical: "https://factburstquiz.com/quiz/space-quiz",
    image: "https://factburstquiz.com/social/quiz/space-quiz.png",
    imageAlt: "Factburst Quiz — Space Quiz",
    imageType: "image/png",
    imageWidth: 1200,
    imageHeight: 630,
    twitterCard: "summary_large_image",
    quiz: {
      title: "Space Quiz",
      description: "Ten questions about space.",
      category: "Space",
      questionCount: 10,
      slug: "space-quiz",
    },
    related: [quizzes[1]],
  });

  assert.match(output, /<title>Space Quiz \| Factburst Quiz<\/title>/);
  assert.match(output, /rel="canonical" href="https:\/\/factburstquiz\.com\/quiz\/space-quiz"/);
  assert.match(output, /property="og:image" content="https:\/\/factburstquiz\.com\/social\/quiz\/space-quiz\.png"/);
  assert.match(output, /property="og:image:width" content="1200"/);
  assert.match(output, /name="twitter:card" content="summary_large_image"/);
  assert.match(output, /type="application\/ld\+json" data-factburst-seo/);
  assert.match(output, /About this quiz/);
  assert.match(output, /href="\/quiz\/space-missions"/);
  assert.match(output, /src="\/growth\.js" defer/);
  assert.doesNotMatch(output, /rel="preload" href="\/brand-icon\.png\?v=2"/);
});

test("legacy quiz URLs permanently redirect and preserve challenge parameters", async () => {
  const response = await handleSeoRequest(
    new Request("https://factburstquiz.com/quiz.html?slug=space-quiz&challenge=abc123"),
    mockEnv(),
    new URL("https://factburstquiz.com/quiz.html?slug=space-quiz&challenge=abc123"),
    mockWorker(),
  );
  assert.equal(response.status, 301);
  assert.equal(response.headers.get("location"), "https://factburstquiz.com/quiz/space-quiz?challenge=abc123");
});

test("clean quiz route renders unique metadata and crawlable related links", async () => {
  const request = new Request("https://factburstquiz.com/quiz/space-quiz");
  const response = await handleSeoRequest(request, mockEnv(), new URL(request.url), mockWorker());
  assert.equal(response.status, 200);
  const html = await response.text();
  assert.match(html, /rel="canonical" href="https:\/\/factburstquiz\.com\/quiz\/space-quiz"/);
  assert.match(html, /Space Missions Quiz/);
  assert.match(html, /href="\/quiz\/space-missions"/);
  assert.match(html, /social\/quiz\/space-quiz\.png/);
});

test("clean category route renders category guide and canonical URL", async () => {
  const request = new Request("https://factburstquiz.com/quizzes/space");
  const response = await handleSeoRequest(request, mockEnv(), new URL(request.url), mockWorker());
  assert.equal(response.status, 200);
  const html = await response.text();
  assert.match(html, /<title>Space Quizzes \| Factburst Quiz<\/title>/);
  assert.match(html, /rel="canonical" href="https:\/\/factburstquiz\.com\/quizzes\/space"/);
  assert.match(html, /About this category/);
  assert.match(html, /astronomy/);
  assert.match(html, /data-category="Space"/);
});

test("enhanceHtml supports noindex pages", () => {
  const output = enhanceHtml("<html><head><title>X</title><meta name=\"description\" content=\"x\"></head><body></body></html>", {
    title: "Profile",
    description: "Private profile shell.",
    canonical: "https://factburstquiz.com/profile.html",
    noindex: true,
  });
  assert.match(output, /<meta name="robots" content="noindex,follow">/);
});

test("buildSitemapXml emits clean URLs and escapes query separators", () => {
  const xml = buildSitemapXml([
    "https://factburstquiz.com/quiz/space-quiz",
    "https://factburstquiz.com/quizzes/space?source=a&mode=b",
  ]);
  assert.match(xml, /quiz\/space-quiz/);
  assert.match(xml, /source=a&amp;mode=b/);
});

test("generated quiz social card is a real 1200x630 PNG", async () => {
  const png = await buildQuizSocialCardPng({ title: "Space Quiz", category: "Space", questionCount: 10 });
  assert.ok(png instanceof Uint8Array);
  assert.ok(png.length > 1000);
  assert.deepEqual(Array.from(png.slice(0, 8)), [137, 80, 78, 71, 13, 10, 26, 10]);
  assert.equal(png[24], 8, "PNG bit depth should be 8");
  assert.equal(png[25], 2, "PNG colour type should be RGB");
});
