const SOURCES = {
  fb: "facebook",
  facebook: "facebook",
  ig: "instagram",
  instagram: "instagram",
  yt: "youtube-promo",
  youtube: "youtube-promo",
  "youtube-promo": "youtube-promo",
};

export default {
  async fetch(request, env) {
    try {
      const url = new URL(request.url);
      const path = url.pathname.replace(/^\/+|\/+$/g, "");
      const parts = path ? path.split("/") : [];

      if (request.method === "GET" && path === "health") {
        return json({ ok: true, service: "factburst-link-tracker" });
      }

      if (
        request.method === "GET" &&
        parts.length === 2 &&
        SOURCES[parts[0].toLowerCase()]
      ) {
        return handleTrackedRedirect(request, env, parts[0], parts[1]);
      }

      if (path === "api/campaigns" && request.method === "POST") {
        requireApiKey(request, env);
        return createCampaign(request, env);
      }

      if (path === "api/campaigns" && request.method === "GET") {
        requireApiKey(request, env);
        return listCampaigns(env);
      }

      if (path === "api/stats" && request.method === "GET") {
        requireApiKey(request, env);
        return overallStats(env);
      }

      if (
        parts.length === 3 &&
        parts[0] === "api" &&
        parts[1] === "stats" &&
        request.method === "GET"
      ) {
        requireApiKey(request, env);
        return campaignStats(env, parts[2]);
      }

      return json({ error: "Not found" }, 404);
    } catch (error) {
      console.error(error);
      return json(
        { error: error instanceof Error ? error.message : "Unexpected error" },
        error?.status || 500
      );
    }
  },
};

async function handleTrackedRedirect(request, env, sourceKey, slugValue) {
  const source = SOURCES[sourceKey.toLowerCase()];
  const slug = cleanSlug(slugValue);
  const campaign = await env.DB.prepare(
    `SELECT slug, destination_url, active FROM campaigns WHERE slug = ? LIMIT 1`
  ).bind(slug).first();

  if (!campaign || Number(campaign.active) !== 1) {
    return new Response("Campaign not found.", {
      status: 404,
      headers: { "Content-Type": "text/plain; charset=utf-8" },
    });
  }

  const destination = validateDestination(campaign.destination_url);
  const deviceType = detectDevice(request.headers.get("User-Agent") || "");
  await env.DB.prepare(
    `INSERT INTO clicks (campaign_slug, source, clicked_at, device_type)
     VALUES (?, ?, CURRENT_TIMESTAMP, ?)`
  ).bind(slug, source, deviceType).run();

  return Response.redirect(destination, 302);
}

async function createCampaign(request, env) {
  const body = await request.json();
  const slug = cleanSlug(body.slug);
  const title = String(body.title || "").trim().slice(0, 300);
  const quizId =
    Number.isInteger(Number(body.quiz_id)) && Number(body.quiz_id) > 0
      ? Number(body.quiz_id)
      : null;
  const destination = validateDestination(body.destination_url);

  await env.DB.prepare(
    `INSERT INTO campaigns (slug, quiz_id, title, destination_url, active)
     VALUES (?, ?, ?, ?, 1)
     ON CONFLICT(slug) DO UPDATE SET
       quiz_id = excluded.quiz_id,
       title = excluded.title,
       destination_url = excluded.destination_url,
       active = 1`
  ).bind(slug, quizId, title, destination).run();

  return json({
    ok: true,
    slug,
    links: {
      facebook: `/fb/${slug}`,
      instagram: `/ig/${slug}`,
      youtube_promo: `/yt/${slug}`,
    },
  });
}

async function listCampaigns(env) {
  const result = await env.DB.prepare(
    `SELECT
       c.slug,
       c.quiz_id,
       c.title,
       c.destination_url,
       c.created_at,
       c.active,
       COUNT(cl.id) AS total_clicks
     FROM campaigns c
     LEFT JOIN clicks cl ON cl.campaign_slug = c.slug
     GROUP BY c.id
     ORDER BY c.created_at DESC`
  ).all();

  return json({ campaigns: result.results || [] });
}

async function campaignStats(env, slugValue) {
  const slug = cleanSlug(slugValue);
  const campaign = await env.DB.prepare(
    `SELECT slug, quiz_id, title, destination_url, created_at, active
     FROM campaigns
     WHERE slug = ?
     LIMIT 1`
  ).bind(slug).first();

  if (!campaign) return json({ error: "Campaign not found" }, 404);

  const result = await env.DB.prepare(
    `SELECT source, COUNT(*) AS clicks
     FROM clicks
     WHERE campaign_slug = ?
     GROUP BY source`
  ).bind(slug).all();

  const counts = {
    facebook: 0,
    instagram: 0,
    "youtube-promo": 0,
  };
  for (const row of result.results || []) {
    counts[row.source] = Number(row.clicks || 0);
  }

  return json({
    campaign,
    clicks: {
      facebook: counts.facebook,
      instagram: counts.instagram,
      youtube_promo: counts["youtube-promo"],
      total: counts.facebook + counts.instagram + counts["youtube-promo"],
    },
  });
}

async function overallStats(env) {
  const result = await env.DB.prepare(
    `SELECT
       c.slug,
       c.quiz_id,
       c.title,
       SUM(CASE WHEN cl.source = 'facebook' THEN 1 ELSE 0 END) AS facebook_clicks,
       SUM(CASE WHEN cl.source = 'instagram' THEN 1 ELSE 0 END) AS instagram_clicks,
       SUM(CASE WHEN cl.source = 'youtube-promo' THEN 1 ELSE 0 END) AS youtube_promo_clicks,
       COUNT(cl.id) AS total_clicks
     FROM campaigns c
     LEFT JOIN clicks cl ON cl.campaign_slug = c.slug
     WHERE c.active = 1
     GROUP BY c.id
     ORDER BY total_clicks DESC, c.created_at DESC`
  ).all();

  return json({ campaigns: result.results || [] });
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

function cleanSlug(value) {
  const slug = String(value || "").trim().toLowerCase();
  if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug)) {
    const error = new Error(
      "Campaign slug may contain only lowercase letters, numbers and hyphens."
    );
    error.status = 400;
    throw error;
  }
  return slug;
}

function validateDestination(value) {
  let url;
  try {
    url = new URL(String(value || "").trim());
  } catch {
    const error = new Error("Invalid destination URL.");
    error.status = 400;
    throw error;
  }

  if (url.protocol !== "https:") {
    const error = new Error("Destination must use HTTPS.");
    error.status = 400;
    throw error;
  }

  const host = url.hostname.toLowerCase();
  const youtube =
    host === "youtube.com" || host.endsWith(".youtube.com") || host === "youtu.be";
  if (!youtube) {
    const error = new Error("Factburst campaign destinations must point to YouTube.");
    error.status = 400;
    throw error;
  }

  return url.toString();
}

function detectDevice(userAgent) {
  const ua = userAgent.toLowerCase();
  if (/ipad|tablet/.test(ua)) return "tablet";
  if (/iphone|android|mobile/.test(ua)) return "mobile";
  return "desktop";
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
