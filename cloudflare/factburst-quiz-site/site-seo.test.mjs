import test from "node:test";
import assert from "node:assert/strict";
import { buildSitemapXml, enhanceHtml } from "./site-seo.js";

test("enhanceHtml injects canonical social metadata and growth runtime", () => {
  const input = `<!doctype html><html><head><meta name="description" content="old"><title>Old</title><link rel="preload" href="/brand-icon.png?v=2" as="image"></head><body><img src="/brand-icon.png?v=2" alt=""></body></html>`;
  const output = enhanceHtml(input, {
    title: "Space Quiz | Factburst Quiz",
    description: "Ten questions about space.",
    canonical: "https://factburstquiz.com/quiz.html?slug=space-quiz",
    image: "https://factburstquiz.com/brand-icon.png?v=2",
    imageAlt: "Factburst Quiz — Space Quiz",
    quiz: {
      title: "Space Quiz",
      description: "Ten questions about space.",
      category: "Space",
      questionCount: 10,
    },
  });

  assert.match(output, /<title>Space Quiz \| Factburst Quiz<\/title>/);
  assert.match(output, /rel="canonical" href="https:\/\/factburstquiz\.com\/quiz\.html\?slug=space-quiz"/);
  assert.match(output, /property="og:title" content="Space Quiz \| Factburst Quiz"/);
  assert.match(output, /type="application\/ld\+json" data-factburst-seo/);
  assert.match(output, /src="\/growth\.js" defer/);
  assert.match(output, /decoding="async"/);
  assert.doesNotMatch(output, /rel="preload" href="\/brand-icon\.png\?v=2"/);
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

test("buildSitemapXml escapes query separators", () => {
  const xml = buildSitemapXml(["https://factburstquiz.com/quiz.html?slug=a&source=b"]);
  assert.match(xml, /slug=a&amp;source=b/);
});
