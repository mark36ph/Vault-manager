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
import { handleAdminAccountEditApi } from "./account-admin-edit.js";
import { handleAdminUsersApi } from "./admin-users.js";
import { handleGeneratorApi, scheduledQuizGeneration } from "./quiz-generation.js";
import { handleSocialStatsApi } from "./social-stats.js";
import { handleSocialCommandApi, handleAdminSocialCommentsApi } from "./social-command-api.js";
import { handleAdminPhase1Api } from "./admin-phase1-api.js";
import { handleApiSettingsBackupApi } from "./api-settings-backup.js";
import { enforceMaintenanceMode, handleSiteStatusApi } from "./site-controls.js";
import { handlePublicAdsConfig } from "./site-ads.js";
import { scoreGuestQuiz } from "./guest-score.js";
import { createResendEmailAdapter } from "./resend-email.js";
import { handleSeoRequest } from "./site-seo-overrides.js";
import { handleAnalyticsApi } from "./site-analytics.js";
import {
  cleanRedirectLocation,
  rewritePublicPaths,
  seoAssetPath,
} from "./clean-public-routes.js";

let accountSchemaReady = false;

export default {
  async fetch(request, env, context) {
    const url = new URL(request.url);

    const apiSettingsResponse = await handleApiSettingsBackupApi(request, env, url);
    if (apiSettingsResponse) return apiSettingsResponse;

    const socialAgentResponse = await handleSocialCommandApi(request, env, url);
    if (socialAgentResponse) return socialAgentResponse;

    const adminSocialCommentsResponse = await handleAdminSocialCommentsApi(request, env, url);
    if (adminSocialCommentsResponse) return adminSocialCommentsResponse;

    const phase1Response = await handleAdminPhase1Api(request, env, url);
    if (phase1Response) return phase1Response;

    if (env.DB && shouldCheckSiteControls(url.pathname)) {
      const schemaFailure = await ensureSchemasSafely(env, url);
      if (schemaFailure) return schemaFailure;
      const statusResponse = await handleSiteStatusApi(request, env.DB, url);
      if (statusResponse) return statusResponse;
      const maintenanceResponse = await enforceMaintenanceMode(request, env.DB, url);
      if (maintenanceResponse) return maintenanceResponse;
    }
    const legacyRedirect = cleanRedirectLocation(url);
    if (legacyRedirect) return new Response(null,{status:301,headers:{location:legacyRedirect,"cache-control":"public, max-age=3600"}});
    const seoUrl = new URL(url); seoUrl.pathname = seoAssetPath(seoUrl.pathname);
    const seoResponse = await handleSeoRequest(request, env, seoUrl, quizWorker);
    if (seoResponse) return rewriteSeoResponse(seoResponse, request.method);
    const analyticsResponse = await handleAnalyticsApi(request, env, url); if (analyticsResponse) return analyticsResponse;
    const socialStatsResponse = await handleSocialStatsApi(request, env, url); if (socialStatsResponse) return socialStatsResponse;
    if (url.pathname === "/api/site/ads" && request.method === "GET") {
      if (!env.DB) return new Response(JSON.stringify({enabled:false,client:"",left_slot:"",right_slot:""}),{headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store"}});
      return handlePublicAdsConfig(request, env.DB, url);
    }
    if (env.DB) {
      const communityResponse = await handleCommunityApi(request, env.DB, url); if (communityResponse) return communityResponse;
      const engagementResponse = await handleEngagementApi(request, env.DB, url); if (engagementResponse) return engagementResponse;
    }
    if (url.pathname === "/api/account/claim-score" && request.method === "POST") {
      if (!env.DB) return quizWorker.fetch(request, env, context);
      const schemaFailure = await ensureSchemasSafely(env, url); if (schemaFailure) return schemaFailure;
      const origin = String(request.headers.get("origin") || "").trim(); if (origin !== url.origin) return jsonResponse({error:"Request origin was not accepted."},403);
      return claimGuestScore(request, env.DB, url);
    }
    const generatorResponse = await handleGeneratorApi(request, env, url); if (generatorResponse) return generatorResponse;
    if (url.pathname.startsWith("/api/admin/users")) return handleAdminUsersApi(request, env, url);
    const accountRoute = isAccountRoute(url.pathname);
    if (accountRoute) {
      if (!env.DB) return quizWorker.fetch(request, env, context);
      const schemaFailure = await ensureSchemasSafely(env, url); if (schemaFailure) return schemaFailure;
      const policyResponse = await enforceAccountRequestPolicy(request, env.DB, url); if (policyResponse) return policyResponse;
      if (isCommentRoute(url.pathname) && request.method === "POST") { const statusBlocked = await enforceActiveSession(request, env.DB); if (statusBlocked) return statusBlocked; }
      const commentsResponse = await handleCommentsApi(request, env.DB, url); if (commentsResponse) return commentsResponse;
      const accountEnv = {...env,EMAIL:createResendEmailAdapter(env)};
      const challengeResponse = await handleChallengeApi(request, accountEnv, url); if (challengeResponse) return challengeResponse;
      const friendsResponse = await handleFriendsApi(request, env.DB, url); if (friendsResponse) return friendsResponse;
      const verifiedEmailChange = await handleVerifiedEmailChangeApi(request, accountEnv, url); if (verifiedEmailChange) return verifiedEmailChange;
      const adminEditResponse = await handleAdminAccountEditApi(request, env, url); if (adminEditResponse) return adminEditResponse;
      const authResponse = await handleAuthApi(request, accountEnv, url); if (authResponse) return authResponse;
      const profileResponse = await handleProfileApi(request, env.DB, url); if (profileResponse) return profileResponse;
      const leaderboardResponse = await handleFilteredLeaderboardApi(request, env.DB, url); if (leaderboardResponse) return leaderboardResponse;
      const response = await handleAccountApi(request, accountEnv, url); if (response) return response;
    }
    return quizWorker.fetch(request, env, context);
  },

  async scheduled(controller, env, context) {
    await scheduledQuizGeneration(controller, env, context);
  },
};

function isAccountRoute(pathname) {
  return pathname === "/api/account" || pathname.startsWith("/api/account/");
}

function isCommentRoute(pathname) {
  return pathname === "/api/account/comments" || pathname.startsWith("/api/account/comments/");
}

function shouldCheckSiteControls(pathname) {
  return pathname === "/" || pathname.startsWith("/quizzes") || pathname.startsWith("/quiz/") || pathname.startsWith("/api/");
}

async function ensureSchemasSafely(env, url) {
  if (accountSchemaReady || !env.DB) return null;
  try {
    await prepareAccountSchema(env.DB);
    accountSchemaReady = true;
    return null;
  } catch (error) {
    console.error("Factburst account schema preparation failed", error);
    return jsonResponse({error:"Database schema is unavailable."},503);
  }
}

async function claimGuestScore(request, db, url) {
  let body;
  try { body = await request.json(); } catch { return jsonResponse({error:"Request body must be valid JSON."},400); }
  const slug = String(body?.slug || "").trim();
  const score = Number(body?.score);
  if (!slug || !Number.isFinite(score)) return jsonResponse({error:"Quiz slug and score are required."},400);
  return recordAuthenticatedScore(db, slug, score, null);
}

function rewriteSeoResponse(response, method) {
  if (method === "HEAD") return response;
  return response;
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {status,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store"}});
}
