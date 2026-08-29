import test from "node:test";
import assert from "node:assert/strict";
import {
  normalizeLeaderboardLimit,
  normalizeUsername,
  totalPercentage,
} from "./accounts.js";

test("normalizes safe public usernames", () => {
  assert.equal(normalizeUsername("  Quiz   Master  "), "Quiz Master");
  assert.equal(normalizeUsername("Mark_36"), "Mark_36");
  assert.equal(normalizeUsername("A.B-C"), "A.B-C");
});

test("rejects unsafe or invalid public usernames", () => {
  assert.equal(normalizeUsername("ab"), "");
  assert.equal(normalizeUsername("name<script>"), "");
  assert.equal(normalizeUsername("-leading"), "");
  assert.equal(normalizeUsername("trailing_"), "");
  assert.equal(normalizeUsername("x".repeat(25)), "");
});

test("leaderboard limits are bounded", () => {
  assert.equal(normalizeLeaderboardLimit("10"), 10);
  assert.equal(normalizeLeaderboardLimit("0"), 1);
  assert.equal(normalizeLeaderboardLimit("999"), 50);
  assert.equal(normalizeLeaderboardLimit("nope"), 25);
});

test("total percentage uses aggregate best points", () => {
  assert.equal(totalPercentage(42, 60), 70);
  assert.equal(totalPercentage(7, 10), 70);
  assert.equal(totalPercentage(0, 0), 0);
});
