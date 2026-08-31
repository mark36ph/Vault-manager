import assert from "node:assert/strict";
import test from "node:test";

import {
  cleanRedirectLocation,
  rewritePublicPaths,
  seoAssetPath,
} from "./clean-public-routes.js";

test("clean public routes resolve to their existing HTML assets", () => {
  assert.equal(seoAssetPath("/"), "/index.html");
  assert.equal(seoAssetPath("/profile"), "/profile.html");
  assert.equal(seoAssetPath("/leaderboard"), "/leaderboard.html");
  assert.equal(seoAssetPath("/terms"), "/terms.html");
  assert.equal(seoAssetPath("/privacy"), "/privacy.html");
  assert.equal(seoAssetPath("/quiz/example"), "/quiz/example");
});

test("legacy HTML routes redirect to clean production URLs and preserve query strings", () => {
  assert.equal(
    cleanRedirectLocation(new URL("https://factburstquiz.com/profile.html?tab=friends")),
    "https://factburstquiz.com/profile?tab=friends",
  );
  assert.equal(
    cleanRedirectLocation(new URL("https://factburstquiz.com/leaderboard.html?category=Space")),
    "https://factburstquiz.com/leaderboard?category=Space",
  );
  assert.equal(
    cleanRedirectLocation(new URL("https://factburstquiz.com/index.html")),
    "https://factburstquiz.com/",
  );
  assert.equal(cleanRedirectLocation(new URL("https://factburstquiz.com/quiz/example")), "");
});

test("SEO HTML, sitemap and robots text use clean public paths", () => {
  const source = [
    '<a href="/profile.html">Profile</a>',
    '<a href="/leaderboard.html">Leaderboard</a>',
    '<a href="/terms.html">Terms</a>',
    '<a href="/privacy.html">Privacy</a>',
    '<loc>https://factburstquiz.com/leaderboard.html</loc>',
    '<link rel="canonical" href="https://factburstquiz.com/privacy.html">',
    'Disallow: /profile.html',
  ].join("\n");
  const rewritten = rewritePublicPaths(source);

  assert.match(rewritten, /href="\/profile"/);
  assert.match(rewritten, /href="\/leaderboard"/);
  assert.match(rewritten, /href="\/terms"/);
  assert.match(rewritten, /href="\/privacy"/);
  assert.match(rewritten, /<loc>https:\/\/factburstquiz\.com\/leaderboard<\/loc>/);
  assert.match(rewritten, /canonical" href="https:\/\/factburstquiz\.com\/privacy"/);
  assert.match(rewritten, /Disallow: \/profile/);
  assert.doesNotMatch(rewritten, /(?:profile|leaderboard|terms|privacy)\.html/);
});
