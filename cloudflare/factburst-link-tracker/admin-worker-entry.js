import trackerWorker from "./worker.js";
import { handleSiteUserAdmin } from "./site-user-admin.js";
import { handleSiteUserFriendsAdmin } from "./site-user-friends-admin.js";
import { handleSiteAdAdmin } from "./site-ad-admin.js";
import { handleSiteAccessAdmin } from "./site-access-admin.js";

export default {
  async fetch(request, env, context) {
    const url = new URL(request.url);
    if (isSiteAdminRoute(url.pathname)) {
      try {
        requireApiKey(request, env);

        const accessResponse = await handleSiteAccessAdmin(request, env, url);
        if (accessResponse) return accessResponse;

        const adResponse = await handleSiteAdAdmin(request, env, url);
        if (adResponse) return adResponse;

        const friendsResponse = await handleSiteUserFriendsAdmin(request, env, url);
        if (friendsResponse) return friendsResponse;

        const userResponse = await handleSiteUserAdmin(request, env, url);
        if (userResponse) return userResponse;
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

function isSiteAdminRoute(pathname) {
  return pathname === "/api/site/ads" ||
    pathname === "/api/site/settings" ||
    pathname === "/api/site/users" ||
    pathname.startsWith("/api/site/users/");
}

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
