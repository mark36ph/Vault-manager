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

    const socialAgentResponse = await handleSocialCommandApi(request, env, url);
    if (socialAgentResponse) return socialAgentResponse;

    const adminSocialCommentsResponse = await handleAdminSocialCommentsApi(request, env, url);
    if (adminSocialCommentsResponse) return adminSocialCommentsResponse;

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
    const scoreMatch = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/score$/i);
    if (scoreMatch && request.method === "POST") {
      if (!env.DB) return quizWorker.fetch(request, env, context);
      const schemaFailure = await ensureSchemasSafely(env, url); if (schemaFailure) return schemaFailure;
      const currentUser = await activeSessionUser(request, env.DB);
      if (!currentUser?.email_verified_at) return scoreGuestQuiz(request, env.DB, scoreMatch[1].toLowerCase());
      const scored = await quizWorker.fetch(request, env, context); if (!scored.ok) return scored;
      const quiz = await env.DB.prepare("SELECT id FROM site_quizzes WHERE slug = ? LIMIT 1").bind(scoreMatch[1].toLowerCase()).first(); if (!quiz) return scored;
      let payload; try { payload = await scored.clone().json(); } catch { return scored; }
      const score = Number(payload?.score), total = Number(payload?.total); if (!Number.isInteger(score)||!Number.isInteger(total)||total<=0||score<0||score>total)return scored;
      const completedAt=new Date().toISOString(); const accountScore=await recordAuthenticatedScore(request,env.DB,Number(quiz.id),score,total,completedAt); if(!accountScore)return scored;
      const engagement=await recordEngagementAttempt(request,env.DB,Number(quiz.id),score,total,completedAt); const headers=new Headers(scored.headers); headers.set("content-type","application/json; charset=utf-8");headers.set("cache-control","no-store");
      return new Response(JSON.stringify({...payload,account_score:accountScore,engagement,guest:false,saved:true}),{status:scored.status,headers});
    }
    const assetRequest = seoUrl.pathname !== url.pathname ? new Request(seoUrl, request) : request;
    return quizWorker.fetch(assetRequest, env, context);
  },
  async scheduled(controller, env, context) {
    try { const result=await scheduledQuizGeneration(env); console.log("Factburst scheduled quiz generation",{scheduled_time:controller.scheduledTime,...result}); }
    catch(error){ console.error("Factburst scheduled quiz generation failed",error); }
  },
};
async function claimGuestScore(request,db,url){let body;try{body=await request.json();}catch{return jsonResponse({error:"Request body must be valid JSON."},400);}const slug=String(body?.slug||"").trim().toLowerCase();const answers=Array.isArray(body?.answers)?body.answers.map(normalizeClaimAnswer):[];if(!/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug))return jsonResponse({error:"That quiz link is not valid."},400);if(answers.length===0||answers.some(answer=>!answer))return jsonResponse({error:"A complete quiz result is required."},400);const user=await activeSessionUser(request,db);if(!user)return jsonResponse({error:"Create or log in to an account before saving this score."},401);const scoringRequest=new Request(url.origin+`/api/quizzes/${encodeURIComponent(slug)}/score`,{method:"POST",headers:{"content-type":"application/json",origin:url.origin},body:JSON.stringify({answers})});const scored=await scoreGuestQuiz(scoringRequest,db,slug);if(!scored.ok)return scored;let payload;try{payload=await scored.clone().json();}catch{return jsonResponse({error:"The score could not be checked."},500);}const quiz=await db.prepare("SELECT id FROM site_quizzes WHERE slug = ? LIMIT 1").bind(slug).first();if(!quiz)return jsonResponse({error:"Quiz not found."},404);const score=Number(payload?.score),total=Number(payload?.total);if(!Number.isInteger(score)||!Number.isInteger(total)||total<=0||score<0||score>total)return jsonResponse({error:"The score could not be validated."},400);const completedAt=new Date().toISOString();await db.prepare(`INSERT INTO site_user_scores (user_id,quiz_id,best_score,total,attempts,first_completed_at,last_completed_at) VALUES (?,?,?,?,1,?,?) ON CONFLICT(user_id,quiz_id) DO UPDATE SET best_score=CASE WHEN site_user_scores.total=excluded.total THEN MAX(site_user_scores.best_score,excluded.best_score) ELSE excluded.best_score END,total=excluded.total,attempts=CASE WHEN site_user_scores.total=excluded.total THEN site_user_scores.attempts+1 ELSE 1 END,first_completed_at=CASE WHEN site_user_scores.total=excluded.total THEN site_user_scores.first_completed_at ELSE excluded.first_completed_at END,last_completed_at=excluded.last_completed_at`).bind(user.id,quiz.id,score,total,completedAt,completedAt).run();const saved=await db.prepare("SELECT best_score,total,attempts FROM site_user_scores WHERE user_id=? AND quiz_id=? LIMIT 1").bind(user.id,quiz.id).first();return jsonResponse({saved:true,guest:false,score,total,percentage:Number(payload?.percentage||0),user:{id:Number(user.id),username:String(user.username||""),email_verified:Boolean(user.email_verified_at)},account_score:saved?{username:String(user.username||""),best_score:Number(saved.best_score||0),total:Number(saved.total||total),attempts:Number(saved.attempts||0)}:null});}
function normalizeClaimAnswer(value){const answer=String(value||"").trim().toUpperCase();return/^[A-D]$/.test(answer)?answer:"";}
function jsonResponse(value,status=200){return new Response(JSON.stringify(value),{status,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff"});}
async function rewriteSeoResponse(response,method){const headers=new Headers(response.headers);const location=headers.get("location");if(location)headers.set("location",rewritePublicPaths(location));if(method==="HEAD"||response.status===204||response.status===304)return new Response(null,{status:response.status,statusText:response.statusText,headers});const contentType=headers.get("content-type")||"";if(!/(?:text\/html|application\/xml|text\/plain)/i.test(contentType))return new Response(response.body,{status:response.status,statusText:response.statusText,headers});const body=rewritePublicPaths(await response.text());headers.delete("content-length");headers.delete("etag");return new Response(body,{status:response.status,statusText:response.statusText,headers});}
function isAccountRoute(pathname){return pathname==="/api/account"||pathname.startsWith("/api/account/")||pathname==="/api/friends"||pathname.startsWith("/api/friends/")||pathname==="/api/challenges"||pathname.startsWith("/api/challenges/")||pathname==="/api/leaderboard"||/^\/api\/quizzes\/[a-z0-9][a-z0-9-]{0,79}\/leaderboard$/i.test(pathname)||isCommentRoute(pathname);}
function isCommentRoute(pathname){return/^\/api\/quizzes\/[a-z0-9][a-z0-9-]{0,79}\/comments$/i.test(pathname);}
function shouldCheckSiteControls(pathname){if(pathname==="/robots.txt"||pathname==="/sitemap.xml")return false;if(pathname==="/api/site/status")return true;if(pathname.startsWith("/api/"))return true;return!/\.(?:css|js|ico|png|jpg|jpeg|gif|webp|svg|woff2?)$/i.test(pathname);}
async function ensureSchemas(env,url){if(accountSchemaReady)return;const bootstrapUrl=new URL("/api/quizzes?limit=1",url);const bootstrap=await quizWorker.fetch(new Request(bootstrapUrl,{method:"GET"}),env);if(!bootstrap.ok&&bootstrap.status>=500)throw new Error("The quiz database could not be prepared for accounts.");await ensureAccountSchemaOnce(env.DB);}
async function ensureAccountSchemaOnce(db){if(accountSchemaReady)return;await prepareAccountSchema(db);accountSchemaReady=true;}
async function ensureSchemasSafely(env,url){try{await ensureSchemas(env,url);return null;}catch(error){console.error("Factburst account schema preparation failed",error);return accountSetupFailure();}}
function accountSetupFailure(){return new Response(JSON.stringify({error:"Account setup is temporarily unavailable. Please try again shortly.",code:"account_schema_error"}),{status:503,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff"}});}
