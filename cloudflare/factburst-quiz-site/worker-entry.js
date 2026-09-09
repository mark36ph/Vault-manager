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
import { handlePublicAdsConfig, handleAdminAdsConfig } from "./site-ads.js";
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

    const adsAdminResponse = await handleAdminAdsConfig(request, env, url);
    if (adsAdminResponse) return adsAdminResponse;

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
    if (seoResponse) return await rewriteSeoResponse(seoResponse, request.method);
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