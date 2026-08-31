import quizWorker from "./worker.js";
import {
  handleAccountApi,
  recordAuthenticatedScore,
} from "./accounts.js";
import { handleAuthApi } from "./account-auth.js";
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
import { handleCommunityApi } from "./account-community.js";
import { handleEngagementApi, recordEngagementAttempt } from "./account-engagement.js";
import { handleVerifiedEmailChangeApi } from "./account-email-change.js";
import { enforceMaintenanceMode, handleSiteStatusApi } from "./site-controls.js";
import { handlePublicAdsConfig } from "./site-ads.js";
import { scoreGuestQuiz } from "./guest-score.js";
import { createResendEmailAdapter } from "./resend-email.js";
import { handleSeoRequest } from "./site-seo.js";
import { handleAnalyticsApi } from "./site-analytics.js";

let accountSchemaReady = false;

export default {
  async fetch(request, env, context) {
    const url = new URL(request.url);

    if (env.DB && shouldCheckSiteControls(url.pathname)) {
      const schemaFailure = await ensureSchemasSafely(env, url);
      if (schemaFailure) return schemaFailure;

      const statusResponse = await handleSiteStatusApi(request, env.DB, url);
      if (statusResponse) return statusResponse;

      const maintenanceResponse = await enforceMaintenanceMode(request, env.DB, url);
      if (maintenanceResponse) return maintenanceResponse;
    }

    const seoUrl = new URL(url);
    if (seoUrl.pathname === "/") seoUrl.pathname = "/index.html";
    const seoResponse = await handleSeoRequest(request, env, seoUrl, quizWorker);
    if (seoResponse) return seoResponse;

    const analyticsResponse = await handleAnalyticsApi(request, env, url);
    if (analyticsResponse) return analyticsResponse;

    if (url.pathname === "/api/site/ads" && request.method === "GET") {
      if (!env.DB) {
        return new Response(JSON.stringify({ enabled: false, client: "", left_slot: "", right_slot: "" }), {
          headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
        });
      }
      return handlePublicAdsConfig(request, env.DB, url);
    }

    if (env.DB) {
      const communityResponse = await handleCommunityApi(request, env.DB, url);
      if (communityResponse) return communityResponse;

      const engagementResponse = await handleEngagementApi(request, env.DB, url);
      if (engagementResponse) return engagementResponse;
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

      const accountEnv = {
        ...env,
        EMAIL: createResendEmailAdapter(env),
      };

      const challengeResponse = await handleChallengeApi(request, accountEnv, url);
      if (challengeResponse) return challengeResponse;

      const friendsResponse = await handleFriendsApi(request, env.DB, url);
      if (friendsResponse) return friendsResponse;

      const verifiedEmailChange = await handleVerifiedEmailChangeApi(request, accountEnv, url);
      if (verifiedEmailChange) return verifiedEmailChange;

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

      const completedAt = new Date().toISOString();
      const accountScore = await recordAuthenticatedScore(
        request,
        env.DB,
        Number(quiz.id),
        score,
        total,
        completedAt,
      );
      if (!accountScore) return scored;

      const engagement = await recordEngagementAttempt(
        request,
        env.DB,
        Number(quiz.id),
        score,
        total,
        completedAt,
      );

      const headers = new Headers(scored.headers);
      headers.set("content-type", "application/json; charset=utf-8");
      headers.set("cache-control", "no-store");
      return new Response(JSON.stringify({ ...payload, account_score: accountScore, engagement, guest: false, saved: true }), {
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

function shouldCheckSiteControls(pathname) {
  if (pathname === "/robots.txt" || pathname === "/sitemap.xml") return false;
  if (pathname === "/api/site/status") return true;
  if (pathname.startsWith("/api/")) return true;
  return !/\.(?:css|js|ico|png|jpg|jpeg|gif|webp|svg|woff2?)$/i.test(pathname);
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
