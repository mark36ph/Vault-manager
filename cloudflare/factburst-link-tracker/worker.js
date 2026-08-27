import { listSiteQuizzes, upsertSiteQuiz } from "./site-quiz-admin.js";

const SOURCES = {
  fb: "facebook",
  facebook: "facebook",
  ig: "instagram",
  instagram: "instagram",
  yt: "youtube-promo",
  youtube: "youtube-promo",
  "youtube-promo": "youtube-promo",
};

const TRACKING_MODE = "filtered_unique_v2";
const DEDUPE_HOURS = 6;

export default {
  async fetch(request, env) {
    try {
      const url = new URL(request.url);
      const path = url.pathname.replace(/^\/+|\/+$/g, "");
      const parts = path ? path.split("/") : [];

      if (request.method === "GET" && path === "health") {
        return json({
          ok: true,
          service: "factburst-link-tracker",
          tracking_mode: TRACKING_MODE,
          dedupe_hours: DEDUPE_HOURS,
        });
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

      if (path === "api/site/quizzes" && request.method === "GET") {
        requireApiKey(request, env);
        return listSiteQuizzes(env);
      }

      if (path === "api/site/quizzes" && request.method === "POST") {
        requireApiKey(request, env);
        return upsertSiteQuiz(request, env);
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
  const userAgent = request.headers.get("User-Agent") || "";
  const deviceType = detectDevice(userAgent);

  // Raw hits deliberately preserve every redirect request, including retries,
  // link-preview fetches and obvious automated traffic, for audit/comparison.
  await env.DB.prepare(
    `INSERT INTO clicks (campaign_slug, source, clicked_at, device_type)
     VALUES (?, ?, CURRENT_TIMESTAMP, ?)`
  ).bind(slug, source, deviceType).run();

  await ensureFilteredClickStore(env);
  if (!isAutomatedTraffic(request)) {
    const visitorHash = await privacySafeVisitorHash(request, env);
    await env.DB.prepare(
      `INSERT INTO unique_clicks (campaign_slug, source, visitor_hash, clicked_at, device_type)
       SELECT ?, ?, ?, CURRENT_TIMESTAMP, ?
       WHERE NOT EXISTS (
         SELECT 1
         FROM unique_clicks
         WHERE campaign_slug = ?
           AND visitor_hash = ?
           AND clicked_at >= datetime('now', '-' || ? || ' hours')
       )`
    ).bind(
      slug,
      source,
      visitorHash,
      deviceType,
      slug,
      visitorHash,
      DEDUPE_HOURS
    ).run();
  }

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
  await ensureFilteredClickStore(env);
  const result = await env.DB.prepare(
    `SELECT
       c.slug,
       c.quiz_id,
       c.title,
       c.destination_url,
       c.created_at,
       c.active,
       (SELECT COUNT(*) FROM clicks cl WHERE cl.campaign_slug = c.slug) AS raw_hits,
       (SELECT COUNT(*) FROM unique_clicks uc WHERE uc.campaign_slug = c.slug) AS unique_visitors
     FROM campaigns c
     ORDER BY c.created_at DESC`
  ).all();

  return json({
    tracking_mode: TRACKING_MODE,
    dedupe_hours: DEDUPE_HOURS,
    campaigns: result.results || [],
  });
}

async function campaignStats(env, slugValue) {
  await ensureFilteredClickStore(env);
  const slug = cleanSlug(slugValue);
  const campaign = await env.DB.prepare(
    `SELECT slug, quiz_id, title, destination_url, created_at, active
     FROM campaigns
     WHERE slug = ?
     LIMIT 1`
  ).bind(slug).first();

  if (!campaign) return json({ error: "Campaign not found" }, 404);

  const uniqueResult = await env.DB.prepare(
    `SELECT source, COUNT(*) AS visitors
     FROM unique_clicks
     WHERE campaign_slug = ?
     GROUP BY source`
  ).bind(slug).all();

  const counts = {
    facebook: 0,
    instagram: 0,
    "youtube-promo": 0,
  };
  for (const row of uniqueResult.results || []) {
    counts[row.source] = Number(row.visitors || 0);
  }

  const raw = await env.DB.prepare(
    `SELECT COUNT(*) AS raw_hits
     FROM clicks
     WHERE campaign_slug = ?`
  ).bind(slug).first();

  return json({
    tracking_mode: TRACKING_MODE,
    dedupe_hours: DEDUPE_HOURS,
    campaign,
    visitors: {
      facebook: counts.facebook,
      instagram: counts.instagram,
      youtube_promo: counts["youtube-promo"],
      unique: counts.facebook + counts.instagram + counts["youtube-promo"],
    },
    raw_hits: Number(raw?.raw_hits || 0),
  });
}

async function overallStats(env) {
  await ensureFilteredClickStore(env);
  const result = await env.DB.prepare(
    `SELECT
       c.slug,
       c.quiz_id,
       c.title,
       (SELECT COUNT(*) FROM unique_clicks uc
        WHERE uc.campaign_slug = c.slug AND uc.source = 'facebook') AS facebook_visitors,
       (SELECT COUNT(*) FROM unique_clicks uc
        WHERE uc.campaign_slug = c.slug AND uc.source = 'instagram') AS instagram_visitors,
       (SELECT COUNT(*) FROM unique_clicks uc
        WHERE uc.campaign_slug = c.slug AND uc.source = 'youtube-promo') AS youtube_promo_visitors,
       (SELECT COUNT(*) FROM unique_clicks uc
        WHERE uc.campaign_slug = c.slug) AS unique_visitors,
       (SELECT COUNT(*) FROM clicks cl
        WHERE cl.campaign_slug = c.slug) AS raw_hits
     FROM campaigns c
     WHERE c.active = 1
     ORDER BY unique_visitors DESC, c.created_at DESC`
  ).all();

  return json({
    tracking_mode: TRACKING_MODE,
    dedupe_hours: DEDUPE_HOURS,
    campaigns: result.results || [],
  });
}

async function ensureFilteredClickStore(env) {
  await env.DB.prepare(
    `CREATE TABLE IF NOT EXISTS unique_clicks (
       id INTEGER PRIMARY KEY AUTOINCREMENT,
       campaign_slug TEXT NOT NULL,
       source TEXT NOT NULL,
       visitor_hash TEXT NOT NULL,
       clicked_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
       device_type TEXT NOT NULL DEFAULT '',
       FOREIGN KEY (campaign_slug) REFERENCES campaigns(slug)
     )`
  ).run();
  await env.DB.prepare(
    `CREATE INDEX IF NOT EXISTS idx_unique_clicks_campaign
     ON unique_clicks(campaign_slug)`
  ).run();
  await env.DB.prepare(
    `CREATE INDEX IF NOT EXISTS idx_unique_clicks_visitor_time
     ON unique_clicks(campaign_slug, visitor_hash, clicked_at)`
  ).run();
}

function isAutomatedTraffic(request) {
  const userAgent = (request.headers.get("User-Agent") || "").trim().toLowerCase();
  if (!userAgent) return true;

  const purpose = [
    request.headers.get("Purpose") || "",
    request.headers.get("Sec-Purpose") || "",
    request.headers.get("X-Purpose") || "",
  ].join(" ").toLowerCase();
  if (/prefetch|preview|prerender/.test(purpose)) return true;

  return /(facebookexternalhit|facebot|meta-externalagent|meta-externalfetcher|twitterbot|linkedinbot|slackbot|discordbot|telegrambot|whatsapp|googlebot|bingbot|duckduckbot|yandexbot|baiduspider|crawler|spider|headlesschrome|lighthouse|curl\/|wget\/|python-requests|postmanruntime|preview)/i.test(userAgent);
}

async function privacySafeVisitorHash(request, env) {
  const ip = request.headers.get("CF-Connecting-IP") || "unknown";
  const userAgent = request.headers.get("User-Agent") || "unknown";
  const language = request.headers.get("Accept-Language") || "";
  const salt = String(env.TRACKER_VISITOR_SALT || env.TRACKER_API_KEY || "factburst-link-tracker");
  const input = new TextEncoder().encode(`${salt}\n${ip}\n${userAgent}\n${language}`);
  const digest = await crypto.subtle.digest("SHA-256", input);
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, "0")).join("");
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
