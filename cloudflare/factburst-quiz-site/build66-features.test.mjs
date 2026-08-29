import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { suspendedAccountMessage } from "./account-access.js";
import { normalizeCommentBody } from "./account-comments.js";
import { normalizeCommentBody as normalizeCommunityCommentBody, handleCommunityApi } from "./account-community.js";
import { handleEngagementApi, ensureEngagementSchema, recordEngagementAttempt } from "./account-engagement.js";
import { handleVerifiedEmailChangeApi } from "./account-email-change.js";
import { normalizeRole, handleSiteStatusApi, enforceMaintenanceMode } from "./site-controls.js";
import { normalizeAnswer } from "./guest-score.js";
import { normalizeClient, normalizeSlot } from "./site-ads.js";

test("suspended accounts receive an explicit suspension message", () => {
  const message = suspendedAccountMessage();
  assert.match(message, /suspended/i);
  assert.match(message, /Factburst/i);
});

test("comments are normalized and bounded", () => {
  assert.equal(normalizeCommentBody("  Great   quiz!\r\n\r\n\r\nLoved it.  "), "Great quiz!\n\nLoved it.");
  assert.equal(normalizeCommentBody("x"), "");
  assert.equal(normalizeCommentBody("x".repeat(601)), "");
  assert.equal(normalizeCommunityCommentBody("  Reply   here  "), "Reply here");
});

test("guest score answers only accept A through D", () => {
  assert.equal(normalizeAnswer(" a "), "A");
  assert.equal(normalizeAnswer("D"), "D");
  assert.equal(normalizeAnswer("E"), "");
  assert.equal(normalizeAnswer(""), "");
});

test("AdSense identifiers are validated before use", () => {
  assert.equal(normalizeClient("ca-pub-1234567890123456"), "ca-pub-1234567890123456");
  assert.equal(normalizeClient("pub-123"), "");
  assert.equal(normalizeSlot("1234567890"), "1234567890");
  assert.equal(normalizeSlot("slot-123"), "");
});

test("quiz content stays in the centre column when ad rails are hidden", () => {
  const css = readFileSync(new URL("./public/ads.css", import.meta.url), "utf8");
  assert.match(css, /\.quiz-page-grid \.quiz-shell\s*\{[^}]*grid-column:\s*2;/s);
  assert.match(css, /@media \(max-width: 1179px\)[\s\S]*\.quiz-page-grid \.quiz-shell\s*\{[^}]*grid-column:\s*1;/s);
});

test("website roles are limited to user moderator and admin", () => {
  assert.equal(normalizeRole("ADMIN"), "admin");
  assert.equal(normalizeRole("moderator"), "moderator");
  assert.equal(normalizeRole("anything-else"), "user");
});

test("engagement and maintenance handlers are exported for the Worker", () => {
  assert.equal(typeof handleEngagementApi, "function");
  assert.equal(typeof ensureEngagementSchema, "function");
  assert.equal(typeof recordEngagementAttempt, "function");
  assert.equal(typeof handleCommunityApi, "function");
  assert.equal(typeof handleVerifiedEmailChangeApi, "function");
  assert.equal(typeof handleSiteStatusApi, "function");
  assert.equal(typeof enforceMaintenanceMode, "function");
});

test("new public engagement scripts parse and quiz page loads the v2 comments UI", () => {
  for (const file of ["engagement.js", "site-status.js", "comments-v2.js", "challenge-ui.js"]) {
    const source = readFileSync(new URL(`./public/${file}`, import.meta.url), "utf8");
    assert.doesNotThrow(() => new Function(source), `${file} should parse as JavaScript`);
  }
  const html = readFileSync(new URL("./public/quiz.html", import.meta.url), "utf8");
  assert.match(html, /engagement\.js/);
  assert.match(html, /comments-v2\.js/);
});

test("direct friend challenges use Factburst notifications instead of share links", () => {
  const apiSource = readFileSync(new URL("./account-challenges.js", import.meta.url), "utf8");
  const uiSource = readFileSync(new URL("./public/challenge-ui.js", import.meta.url), "utf8");
  const workerSource = readFileSync(new URL("./worker-entry.js", import.meta.url), "utf8");

  assert.match(apiSource, /INSERT INTO site_notifications/);
  assert.match(apiSource, /email_sent/);
  assert.match(apiSource, /env\.EMAIL\.send/);
  assert.match(workerSource, /handleChallengeApi\(request, accountEnv, url\)/);
  assert.doesNotMatch(uiSource, /navigator\.share/);
  assert.doesNotMatch(uiSource, /clipboard\.writeText/);
  assert.doesNotMatch(uiSource, /Share challenge link/);
  assert.match(uiSource, /Factburst notifications/);
});

test("empty comment state keeps padding away from the card edge", () => {
  const css = readFileSync(new URL("./public/comments.css", import.meta.url), "utf8");
  assert.match(css, /\.comments-message\s*\{[^}]*padding:\s*18px 20px;/s);
});
