import test from "node:test";
import assert from "node:assert/strict";
import {
  normalizeEmail,
  normalizeLeaderboardLimit,
  normalizeUsername,
  totalPercentage,
  verificationExpiry,
} from "./accounts.js";
import { missingSiteUserUpgrades } from "./account-schema.js";
import { isReservedUsername, reservedUsernameReason } from "./account-policy.js";

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

test("reserves administration moderation support and Factburst names", () => {
  for (const username of [
    "admin",
    "Admin99",
    "administrator",
    "mod",
    "mod42",
    "moderator",
    "staff",
    "support",
    "Factburst",
    "factburst-support",
    "FactburstQuiz",
  ]) {
    assert.equal(isReservedUsername(username), true, username);
    assert.notEqual(reservedUsernameReason(username), "", username);
  }

  for (const username of ["Quiz Master", "Mark_36", "ModelFan", "SpaceAce", "HistoryBuff"]) {
    assert.equal(isReservedUsername(username), false, username);
  }
});

test("normalizes and validates account emails", () => {
  assert.equal(normalizeEmail("  Player@example.com "), "Player@example.com");
  assert.equal(normalizeEmail("player+quiz@example.co.uk"), "player+quiz@example.co.uk");
  assert.equal(normalizeEmail("not-an-email"), "");
  assert.equal(normalizeEmail("a@b"), "");
  assert.equal(normalizeEmail("bad @example.com"), "");
});

test("verification links expire after 24 hours", () => {
  assert.equal(
    verificationExpiry("2026-08-29T12:00:00.000Z"),
    "2026-08-30T12:00:00.000Z",
  );
});

test("legacy account tables receive every account upgrade in order", () => {
  const legacyColumns = [
    { name: "id" },
    { name: "username" },
    { name: "username_key" },
    { name: "password_hash" },
    { name: "password_salt" },
    { name: "password_iterations" },
    { name: "created_at" },
    { name: "last_login_at" },
  ];
  const expected = [
    "email",
    "email_key",
    "email_verified_at",
    "password_scheme",
    "status",
    "suspended_at",
    "suspension_reason",
  ];
  assert.deepEqual(
    missingSiteUserUpgrades(legacyColumns).map(upgrade => upgrade.name),
    expected,
  );
  assert.deepEqual(
    missingSiteUserUpgrades([
      ...legacyColumns,
      ...expected.map(name => ({ name })),
    ]),
    [],
  );
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
