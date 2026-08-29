import test from "node:test";
import assert from "node:assert/strict";
import { PASSWORD_POLICY, derivePasswordHash } from "./account-auth.js";

test("password policy stays within the Worker CPU budget", () => {
  assert.equal(PASSWORD_POLICY.scheme, "pbkdf2-sha256-pepper-v1");
  assert.equal(PASSWORD_POLICY.iterations, 25_000);
});

test("peppered password hashes are deterministic and pepper-specific", async () => {
  const salt = "MDEyMzQ1Njc4OWFiY2RlZg";
  const first = await derivePasswordHash("correct horse battery staple", salt, "p".repeat(64));
  const again = await derivePasswordHash("correct horse battery staple", salt, "p".repeat(64));
  const otherPepper = await derivePasswordHash("correct horse battery staple", salt, "q".repeat(64));

  assert.equal(first, again);
  assert.notEqual(first, otherPepper);
  assert.match(first, /^[A-Za-z0-9_-]+$/);
});
