import test from "node:test";
import assert from "node:assert/strict";
import { suspendedAccountMessage } from "./account-access.js";
import { normalizeCommentBody } from "./account-comments.js";
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
