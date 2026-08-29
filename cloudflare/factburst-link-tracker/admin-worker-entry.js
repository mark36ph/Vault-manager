import trackerWorker from "./worker.js";
import { handleSiteUserAdmin } from "./site-user-admin.js";

export default {
  async fetch(request, env, context) {
    const url = new URL(request.url);
    if (url.pathname === "/api/site/users" || url.pathname.startsWith("/api/site/users/")) {
      try {
        requireApiKey(request, env);
        const response = await handleSiteUserAdmin(request, env, url);
        if (response) return response;
        return json({ error: "Not found" }, 404);
      } catch (error) {
        console.error(error);
        return json(
          { error: error instanceof Error ? error.message : "Unexpected error" },
          error?.status || 500,
        );
      }
    }

    return trackerWorker.fetch(request, env, context);
  },
};

function requireApiKey(request, env) {
  if (!env.TRACKER_API_KEY) {
    const error = new Error("TRACKER_API_KEY has not been configured.");
    error.status = 500;
    throw error;
  }

  const authorization = request.headers.get("Authorization") || "";
  if (authorization !== `Bearer ${env.TRACKER_API_KEY}`) {
    const error = new Error("Unauthorized");
    error.status = 401;
    throw error;
  }
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value, null, 2), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
