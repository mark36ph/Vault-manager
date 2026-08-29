import quizWorker from "./worker.js";
import {
  handleAccountApi,
  recordAuthenticatedScore,
  requireVerifiedQuizAccess,
} from "./accounts.js";
import { handleAuthApi } from "./account-auth.js";
import { prepareAccountSchema } from "./account-schema.js";
import { createResendEmailAdapter } from "./resend-email.js";

let accountSchemaReady = false;

export default {
  async fetch(request, env, context) {
    const url = new URL(request.url);
    const accountRoute = isAccountRoute(url.pathname);

    if (accountRoute) {
      if (!env.DB) return quizWorker.fetch(request, env, context);
      const schemaFailure = await ensureSchemasSafely(env, url);
      if (schemaFailure) return schemaFailure;
      const accountEnv = {
        ...env,
        EMAIL: createResendEmailAdapter(env),
      };
      const authResponse = await handleAuthApi(request, accountEnv, url);
      if (authResponse) return authResponse;
      const response = await handleAccountApi(request, accountEnv, url);
      if (response) return response;
    }

    const scoreMatch = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/score$/i);
    const detailMatch = url.pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})$/i);
    const gatedDetail = detailMatch && detailMatch[1].toLowerCase() !== "latest" && request.method === "GET";

    if ((scoreMatch && request.method === "POST") || gatedDetail) {
      if (!env.DB) return quizWorker.fetch(request, env, context);
      const schemaFailure = await ensureSchemasSafely(env, url);
      if (schemaFailure) return schemaFailure;
      const blocked = await requireVerifiedQuizAccess(request, env.DB);
      if (blocked) return blocked;
    }

    if (scoreMatch && request.method === "POST") {
      const scored = await quizWorker.fetch(request, env, context);
      if (!scored.ok || !env.DB) return scored;

      const schemaFailure = await ensureAccountSchemaSafely(env.DB);
      if (schemaFailure) return schemaFailure;
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
      return new Response(JSON.stringify({ ...payload, account_score: accountScore }), {
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
    pathname === "/api/leaderboard" ||
    /^\/api\/quizzes\/[a-z0-9][a-z0-9-]{0,79}\/leaderboard$/i.test(pathname);
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

async function ensureAccountSchemaSafely(db) {
  try {
    await ensureAccountSchemaOnce(db);
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
