import quizWorker from "./worker.js";
import {
  handleAccountApi,
  recordAuthenticatedScore,
} from "./accounts.js";
import { handleAuthApi } from "./account-auth.js";
import { handleEmailChangeApi } from "./account-email-change.js";
import { prepareAccountSchema } from "./account-schema.js";
import {
  activeSessionUser,
  enforceAccountRequestPolicy,
  enforceActiveSession,
} from "./account-access.js";
import { handleChallengeApi } from "./account-challenges.js";
import { handleFriendsApi } from "./account-friends.js";
import { handleFilteredLeaderboardApi } from "./account-leaderboards.js";
import { handleProfileApi } from "./account-profile.js";
import { handleCommentsApi } from "./account-comments.js";
import { handlePublicAdsConfig } from "./site-ads.js";
import { scoreGuestQuiz } from "./guest-score.js";
import { createResendEmailAdapter } from "./resend-email.js";

let accountSchemaReady = false;

export default {
  async fetch(request, env, context) {
    const url = new URL(request.url);

    if (url.pathname === "/api/site/ads" && request.method === "GET") {
      if (!env.DB) {
        return new Response(JSON.stringify({ enabled: false, client: "", left_slot: "", right_slot: "" }), {
          headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
        });
      }
      return handlePublicAdsConfig(request, env.DB, url);
    }

    const accountRoute = isAccountRoute(url.pathname);
    if (accountRoute) {
      if (!env.DB) return quizWorker.fetch(request, env, context);
      const schemaFailure = await ensureSchemasSafely(env, url);
      if (schemaFailure) return schemaFailure;

      const policyResponse = await enforceAccountRequestPolicy(request, env.DB, url);
      if (policyResponse) return policyResponse;

      if (isCommentRoute(url.pathname) && request.method === "POST") {
        const statusBlocked = await enforceActiveSession(request, env.DB);
        if (statusBlocked) return statusBlocked;
      }

      const commentsResponse = await handleCommentsApi(request, env.DB, url);
      if (commentsResponse) return commentsResponse;

      const challengeResponse = await handleChallengeApi(request, env.DB, url);
      if (challengeResponse) return challengeResponse;

      const friendsResponse = await handleFriendsApi(request, env.DB, url);
      if (friendsResponse) return friendsResponse;

      const accountEnv = {
        ...env,
        EMAIL: createResendEmailAdapter(env),
      };

      const emailChangeResponse = await handleEmailChangeApi(request, accountEnv, url);
      if (emailChangeResponse) return emailChangeResponse;

      const authResponse = await handleAuthApi(request, accountEnv, url);
      if (authResponse) return authResponse;

      const profileResponse = await handleProfileApi(request, env.DB, url);
      if (profileResponse) return profileResponse;

      const leaderboardResponse = await handleFilteredLeaderboardApi(request, env.DB, url);
      if (leaderboardResponse) return leaderboardResponse;

      const response = await handleAccountApi(request, accountEnv, url);
      if (response) return response;
    }

    const scoreMatch = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/score$/i);
    if (scoreMatch && request.method === "POST") {
      if (!env.DB) return quizWorker.fetch(request, env, context);
      const schemaFailure = await ensureSchemasSafely(env, url);
      if (schemaFailure) return schemaFailure;

      const statusBlocked = await enforceActiveSession(request, env.DB);
      if (statusBlocked) return statusBlocked;

      const currentUser = await activeSessionUser(request, env.DB);
      if (!currentUser?.email_verified_at) {
        return scoreGuestQuiz(request, env.DB, scoreMatch[1].toLowerCase());
      }

      const scored = await quizWorker.fetch(request, env, context);
      if (!scored.ok) return scored;

      const quiz = await env.DB.prepare("SELECT id FROM site_quizzes WHERE slug = ? LIMIT 1")
        .bind(scoreMatch[1].toLowerCase()).first();
      if (!quiz) return scored;

      let payload;
      try {
        payload = await scored.clone().json();
      } catch {
        return scored;
      }

      const score = Number(payload?.score);
      const total = Number(payload?.total);
      if (!Number.isInteger(score) || !Number.isInteger(total) || total <= 0 || score < 0 || score > total) {
        return scored;
      }

      const accountScore = await recordAuthenticatedScore(
        request,
        env.DB,
        Number(quiz.id),
        score,
        total,
        new Date().toISOString(),
      );
      if (!accountScore) return scored;

      const headers = new Headers(scored.headers);
      headers.set("content-type", "application/json; charset=utf-8");
      headers.set("cache-control", "no-store");
      return new Response(JSON.stringify({ ...payload, account_score: accountScore, guest: false, saved: true }), {
        status: scored.status,
        headers,
      });
    }

    return quizWorker.fetch(request, env, context);
  },
};

function isAccountRoute(pathname) {
  return pathname === "/api/account" ||
    pathname.startsWith("/api/account/") ||
    pathname === "/api/friends" ||
    pathname.startsWith("/api/friends/") ||
    pathname === "/api/challenges" ||
    pathname.startsWith("/api/challenges/") ||
    pathname === "/api/leaderboard" ||
    /^\/api\/quizzes\/[a-z0-9][a-z0-9-]{0,79}\/leaderboard$/i.test(pathname) ||
    isCommentRoute(pathname);
}

function isCommentRoute(pathname) {
  return /^\/api\/quizzes\/[a-z0-9][a-z0-9-]{0,79}\/comments$/i.test(pathname);
}

async function ensureSchemas(env, url) {
  if (accountSchemaReady) return;

  const bootstrapUrl = new URL("/api/quizzes?limit=1", url);
  const bootstrap = await quizWorker.fetch(new Request(bootstrapUrl, { method: "GET" }), env);
  if (!bootstrap.ok && bootstrap.status >= 500) {
    throw new Error("The quiz database could not be prepared for accounts.");
  }
  await ensureAccountSchemaOnce(env.DB);
}

async function ensureAccountSchemaOnce(db) {
  if (accountSchemaReady) return;
  await prepareAccountSchema(db);
  accountSchemaReady = true;
}

async function ensureSchemasSafely(env, url) {
  try {
    await ensureSchemas(env, url);
    return null;
  } catch (error) {
    console.error("Factburst account schema preparation failed", error);
    return accountSetupFailure();
  }
}

function accountSetupFailure() {
  return new Response(JSON.stringify({
    error: "Account setup is temporarily unavailable. Please try again shortly.",
    code: "account_schema_error",
  }), {
    status: 503,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
    },
  });
}
