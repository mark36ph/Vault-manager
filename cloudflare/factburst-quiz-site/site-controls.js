import { activeSessionUser } from "./account-access.js";

const DEFAULT_MAINTENANCE_MESSAGE = "Factburst Quiz is currently undergoing maintenance. Please check back shortly.";
let siteControlSchemaPromise = null;

export async function ensureSiteControlSchema(db) {
  if (!siteControlSchemaPromise) {
    siteControlSchemaPromise = prepareSiteControlSchema(db).catch(error => {
      siteControlSchemaPromise = null;
      throw error;
    });
  }
  return siteControlSchemaPromise;
}

async function prepareSiteControlSchema(db) {
  await db.prepare(`
    CREATE TABLE IF NOT EXISTS site_settings (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL DEFAULT '',
      updated_at TEXT NOT NULL
    )
  `).run();

  const columns = await db.prepare("PRAGMA table_info(site_users)").all();
  const names = new Set((columns.results || []).map(column => String(column?.name || "")));
  if (names.size > 0 && !names.has("role")) {
    try {
      await db.prepare("ALTER TABLE site_users ADD COLUMN role TEXT NOT NULL DEFAULT 'user'").run();
    } catch (error) {
      if (!/duplicate column/i.test(String(error?.message || ""))) throw error;
    }
  }
}

export async function handleSiteStatusApi(request, db, url) {
  if (request.method !== "GET" || url.pathname !== "/api/site/status") return null;
  await ensureSiteControlSchema(db);
  const maintenance = await maintenanceSettings(db);
  const user = await activeSessionUser(request, db);
  const role = user ? await roleForUser(db, user.id) : "guest";
  return json({
    maintenance: maintenance.enabled,
    message: maintenance.message,
    role,
    is_admin: role === "admin",
    can_moderate: role === "admin" || role === "moderator",
  });
}

export async function enforceMaintenanceMode(request, db, url) {
  await ensureSiteControlSchema(db);
  const maintenance = await maintenanceSettings(db);
  if (!maintenance.enabled || maintenanceExempt(url.pathname)) return null;

  const user = await activeSessionUser(request, db);
  const role = user ? await roleForUser(db, user.id) : "guest";
  if (role === "admin") return null;

  if (wantsHtml(request, url)) {
    return maintenancePage(maintenance.message);
  }

  return json({
    error: maintenance.message,
    code: "maintenance_mode",
    maintenance: true,
  }, 503, { "retry-after": "900" });
}

export async function roleForUser(db, userId) {
  if (!userId) return "guest";
  try {
    const row = await db.prepare("SELECT role FROM site_users WHERE id = ? LIMIT 1").bind(userId).first();
    return normalizeRole(row?.role);
  } catch {
    return "user";
  }
}

export function normalizeRole(value) {
  const role = String(value || "user").trim().toLowerCase();
  return role === "admin" || role === "moderator" ? role : "user";
}

async function maintenanceSettings(db) {
  const rows = await db.prepare(`
    SELECT key, value FROM site_settings
    WHERE key IN ('maintenance_enabled', 'maintenance_message')
  `).all();
  const values = Object.fromEntries((rows.results || []).map(row => [String(row.key || ""), String(row.value || "")]));
  return {
    enabled: values.maintenance_enabled === "1",
    message: String(values.maintenance_message || "").trim() || DEFAULT_MAINTENANCE_MESSAGE,
  };
}

function maintenanceExempt(pathname) {
  return pathname === "/api/site/status" ||
    pathname === "/api/account" ||
    pathname === "/api/account/login" ||
    pathname === "/api/account/logout" ||
    pathname === "/api/account/verify" ||
    pathname === "/api/account/verify-email-change";
}

function wantsHtml(request, url) {
  if (request.method !== "GET") return false;
  if (url.pathname.startsWith("/api/")) return false;
  return (request.headers.get("accept") || "").includes("text/html") || !url.pathname.includes(".");
}

function maintenancePage(message) {
  const safe = escapeHtml(message);
  const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Maintenance | Factburst Quiz</title><style>
:root{color-scheme:dark}*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;background:linear-gradient(135deg,#061447,#28166c);font:16px system-ui,sans-serif;color:#eef5ff}.card{width:min(620px,calc(100% - 32px));padding:34px;border:1px solid #3556a6;border-radius:22px;background:#0b1c59;box-shadow:0 24px 70px #0006}h1{margin:0 0 12px;font-size:34px}.bar{position:fixed;inset:0 0 auto;background:#ffd43b;color:#111;padding:10px 18px;text-align:center;font-weight:800}.copy{color:#bdd1ff;line-height:1.6}.admin{margin-top:28px;padding-top:24px;border-top:1px solid #ffffff1c}label{display:grid;gap:6px;margin:10px 0;font-weight:700}input{width:100%;padding:12px;border-radius:10px;border:1px solid #5270b7;background:#06123d;color:white}button{margin-top:10px;padding:12px 18px;border:0;border-radius:10px;background:#38d5ff;color:#03102d;font-weight:800;cursor:pointer}.status{min-height:22px;margin-top:12px;color:#ffb7c9}
</style></head><body><div class="bar">Factburst Quiz is in maintenance mode</div><main class="card"><h1>We’ll be back shortly</h1><p class="copy">${safe}</p><section class="admin"><strong>Administrator access</strong><p class="copy">Admins can sign in below to continue using the site while maintenance is active.</p><form id="admin-login"><label>Username<input id="u" autocomplete="username" required></label><label>Password<input id="p" type="password" autocomplete="current-password" required></label><button>Admin sign in</button><p class="status" id="s"></p></form></section></main><script>
document.querySelector('#admin-login').addEventListener('submit',async e=>{e.preventDefault();const s=document.querySelector('#s');s.textContent='Signing in…';try{const r=await fetch('/api/account/login',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({username:document.querySelector('#u').value,password:document.querySelector('#p').value})});const j=await r.json();if(!r.ok)throw new Error(j.error||'Sign in failed.');const t=await fetch('/api/site/status').then(x=>x.json());if(t.is_admin){location.reload();return}await fetch('/api/account/logout',{method:'POST'});throw new Error('This account is not an administrator.')}catch(err){s.textContent=err.message||'Sign in failed.'}});
</script></body></html>`;
  return new Response(html, {
    status: 503,
    headers: {
      "content-type": "text/html; charset=utf-8",
      "cache-control": "no-store",
      "retry-after": "900",
      "x-content-type-options": "nosniff",
    },
  });
}

function escapeHtml(value) {
  return String(value || "").replace(/[&<>"']/g, character => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;",
  })[character]);
}

function json(value, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(value), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
      ...extraHeaders,
    },
  });
}
