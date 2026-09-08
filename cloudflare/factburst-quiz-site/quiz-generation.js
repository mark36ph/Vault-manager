import { handleAdminSocialApi } from "./admin-social.js";

const JSON_HEADERS = { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "x-content-type-options": "nosniff" };
const GENERATION_SETTINGS_KEY = "quiz_generation_settings";
const DEFAULT_SETTINGS = { enabled: false, frequency: "daily", time_utc: "06:00", quizzes_per_run: 1, categories: [], auto_publish: false };

export async function scheduledQuizGeneration(env) {
  if (!env.DB) return { queued: 0, reason: "database-unavailable" };
  await ensureGenerationSchema(env.DB);
  const row = await env.DB.prepare("SELECT value FROM site_settings WHERE key=? LIMIT 1").bind(GENERATION_SETTINGS_KEY).first();
  let settings = { ...DEFAULT_SETTINGS };
  if (row?.value) { try { settings = { ...settings, ...JSON.parse(String(row.value)) }; } catch {} }
  if (settings.enabled !== true) return { queued: 0, reason: "disabled" };

  const now = new Date();
  if (!isDue(settings, now)) return { queued: 0, reason: "not-due" };
  const last = await env.DB.prepare("SELECT created_at FROM site_generation_jobs WHERE status IN ('queued','claimed','completed') ORDER BY id DESC LIMIT 1").first();
  if (last?.created_at && sameScheduleWindow(String(last.created_at), settings, now)) return { queued: 0, reason: "already-ran" };

  const count = Math.min(10, Math.max(1, Number.parseInt(settings.quizzes_per_run, 10) || 1));
  const categories = Array.isArray(settings.categories) ? settings.categories.map(value => String(value || "").trim()).filter(Boolean) : [];
  const statements = [];
  for (let i = 0; i < count; i++) {
    statements.push(env.DB.prepare("INSERT INTO site_generation_jobs(status,requested_category,questions_per_quiz,auto_publish,attempts,created_at) VALUES ('queued',?,?,?,0,?)").bind(categories.length ? categories[i % categories.length] : "", 10, settings.auto_publish === true ? 1 : 0, now.toISOString()));
  }
  await env.DB.batch(statements);
  return { queued: count, reason: "scheduled" };
}

export async function handleGeneratorApi(request, env, url) {
  if (url.pathname.startsWith("/api/admin/social")) return handleAdminSocialApi(request, env, url);
  if (!url.pathname.startsWith("/api/generator/")) return null;
  const expected = String(env.GENERATOR_API_KEY || "").trim();
  const supplied = String(request.headers.get("authorization") || "").replace(/^Bearer\s+/i, "").trim();
  if (!expected || supplied !== expected) return json({ error: "Generator access is not configured." }, 503);
  if (!env.DB) return json({ error: "Database unavailable." }, 503);
  await ensureGenerationSchema(env.DB);

  if (url.pathname === "/api/generator/jobs/claim" && request.method === "POST") {
    const body = await request.json().catch(() => ({}));
    const workerId = String(body?.worker_id || "csharp-generator").trim().slice(0, 100) || "csharp-generator";
    const job = await env.DB.prepare("SELECT id,status,requested_category,questions_per_quiz,auto_publish,attempts,created_at FROM site_generation_jobs WHERE status='queued' ORDER BY id ASC LIMIT 1").first();
    if (!job) return json({ job: null });
    const claimed = await env.DB.prepare("UPDATE site_generation_jobs SET status='claimed',claimed_at=?,worker_id=?,attempts=attempts+1 WHERE id=? AND status='queued'").bind(new Date().toISOString(), workerId, job.id).run();
    if (!claimed.meta?.changes) return json({ job: null });
    return json({ job: { ...job, status: "claimed", worker_id: workerId, attempts: Number(job.attempts || 0) + 1 } });
  }

  const match = url.pathname.match(/^\/api\/generator\/jobs\/(\d+)$/);
  if (match && request.method === "PATCH") {
    const id = Number(match[1]);
    const body = await request.json().catch(() => ({}));
    const status = ["completed", "failed", "queued"].includes(String(body?.status || "")) ? String(body.status) : "";
    if (!status) return json({ error: "Invalid job status." }, 400);
    const summary = String(body?.result_summary || "").trim().slice(0, 1000);
    const error = String(body?.error_message || "").trim().slice(0, 1000);
    const now = new Date().toISOString();
    const result = await env.DB.prepare("UPDATE site_generation_jobs SET status=?,completed_at=?,result_summary=?,error_message=? WHERE id=?").bind(status, status === "completed" || status === "failed" ? now : null, summary, error, id).run();
    if (!result.meta?.changes) return json({ error: "Generation job not found." }, 404);
    return json({ ok: true });
  }

  return json({ error: "Generator endpoint not found." }, 404);
}

async function ensureGenerationSchema(db) {
  await db.batch([
    db.prepare(`CREATE TABLE IF NOT EXISTS site_generation_jobs (id INTEGER PRIMARY KEY AUTOINCREMENT, status TEXT NOT NULL DEFAULT 'queued', requested_category TEXT NOT NULL DEFAULT '', questions_per_quiz INTEGER NOT NULL DEFAULT 10, auto_publish INTEGER NOT NULL DEFAULT 0, attempts INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, claimed_at TEXT, completed_at TEXT, worker_id TEXT NOT NULL DEFAULT '', result_summary TEXT NOT NULL DEFAULT '', error_message TEXT NOT NULL DEFAULT '')`),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_generation_jobs_status ON site_generation_jobs(status, created_at)"),
  ]);
}

function isDue(settings, now) {
  const [hour, minute] = String(settings.time_utc || "06:00").split(":").map(Number);
  if (now.getUTCHours() !== hour || now.getUTCMinutes() !== minute) return false;
  const frequency = String(settings.frequency || "daily");
  if (frequency === "hourly") return true;
  if (frequency === "weekly") return now.getUTCDay() === 1;
  return true;
}

function sameScheduleWindow(createdAt, settings, now) {
  const date = new Date(createdAt);
  if (Number.isNaN(date.getTime())) return false;
  if (String(settings.frequency || "daily") === "hourly") return date.getUTCFullYear() === now.getUTCFullYear() && date.getUTCMonth() === now.getUTCMonth() && date.getUTCDate() === now.getUTCDate() && date.getUTCHours() === now.getUTCHours();
  if (String(settings.frequency || "daily") === "weekly") return Math.abs(now.getTime() - date.getTime()) < 6 * 24 * 60 * 60 * 1000;
  return date.toISOString().slice(0, 10) === now.toISOString().slice(0, 10);
}

function json(value, status = 200) { return new Response(JSON.stringify(value), { status, headers: JSON_HEADERS }); }
