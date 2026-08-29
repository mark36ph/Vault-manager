import { activeSessionUser } from "./account-access.js";

const CHANGE_HOURS = 24;
const RESEND_SECONDS = 60;

export async function handleVerifiedEmailChangeApi(request, env, url) {
  const pathname = url.pathname;
  if (!["/api/account/email", "/api/account/pending-email", "/api/account/resend-email-change", "/api/account/verify-email-change"].includes(pathname)) return null;
  await ensureEmailChangeSchema(env.DB);

  if (pathname === "/api/account/verify-email-change" && request.method === "GET") return confirmEmailChange(env.DB, url);

  const user = await activeSessionUser(request, env.DB);
  if (!user) return null;
  if (!user.email_verified_at) return null;

  if (pathname === "/api/account/email" && request.method === "POST") return startEmailChange(request, env, url, user, true);
  if (pathname === "/api/account/pending-email" && request.method === "GET") return pendingEmail(env.DB, user.id);
  if (pathname === "/api/account/resend-email-change" && request.method === "POST") return resendEmailChange(env, url, user);
  return null;
}

async function ensureEmailChangeSchema(db) {
  await db.prepare(`CREATE TABLE IF NOT EXISTS site_email_changes (
    token_hash TEXT PRIMARY KEY,
    user_id INTEGER NOT NULL,
    new_email TEXT NOT NULL,
    new_email_key TEXT NOT NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
  )`).run();
  await db.prepare("CREATE INDEX IF NOT EXISTS idx_site_email_changes_user ON site_email_changes(user_id,created_at DESC)").run();
}

async function startEmailChange(request, env, url, user, bypassRateLimit = false) {
  let body;
  try { body = await request.json(); } catch { return json({ error: "Request body must be valid JSON." }, 400); }
  const email = normalizeEmail(body?.email);
  if (!email) return json({ error: "Enter a valid email address." }, 400);
  const emailKey = email.toLowerCase();
  if (emailKey === String(user.email_key || "").toLowerCase()) return json({ error: "That is already your verified email address." }, 400);
  const existing = await env.DB.prepare("SELECT id FROM site_users WHERE email_key=? AND id<>? LIMIT 1").bind(emailKey,user.id).first();
  if (existing) return json({ error: "That email address is already in use." }, 409);

  if (!bypassRateLimit) {
    const latest = await env.DB.prepare("SELECT created_at FROM site_email_changes WHERE user_id=? ORDER BY created_at DESC LIMIT 1").bind(user.id).first();
    if (latest?.created_at) {
      const elapsed = Math.floor((Date.now() - Date.parse(String(latest.created_at))) / 1000);
      if (Number.isFinite(elapsed) && elapsed >= 0 && elapsed < RESEND_SECONDS)
        return json({ error: `Please wait ${RESEND_SECONDS - elapsed} seconds before requesting another confirmation email.`, retry_after: RESEND_SECONDS - elapsed }, 429);
    }
  }

  return issueChange(env, url, user, email, emailKey);
}

async function resendEmailChange(env, url, user) {
  const pending = await env.DB.prepare("SELECT new_email,new_email_key,created_at FROM site_email_changes WHERE user_id=? ORDER BY created_at DESC LIMIT 1").bind(user.id).first();
  if (!pending) return json({ error: "There is no pending email change to resend." }, 404);
  const elapsed = Math.floor((Date.now() - Date.parse(String(pending.created_at))) / 1000);
  if (Number.isFinite(elapsed) && elapsed >= 0 && elapsed < RESEND_SECONDS)
    return json({ error: `Please wait ${RESEND_SECONDS - elapsed} seconds before requesting another confirmation email.`, retry_after: RESEND_SECONDS - elapsed }, 429);
  return issueChange(env, url, user, String(pending.new_email), String(pending.new_email_key));
}

async function issueChange(env, url, user, email, emailKey) {
  if (!env.EMAIL || typeof env.EMAIL.send !== "function") return json({ error: "Email delivery is not configured yet." }, 503);
  const from = normalizeEmail(env.EMAIL_FROM);
  if (!from) return json({ error: "Verification email sender is not configured yet." }, 503);
  const token = randomToken(32);
  const hash = await sha256(token);
  const now = new Date();
  const expires = new Date(now.getTime() + CHANGE_HOURS * 60 * 60 * 1000).toISOString();
  await env.DB.prepare("DELETE FROM site_email_changes WHERE user_id=?").bind(user.id).run();
  await env.DB.prepare("INSERT INTO site_email_changes (token_hash,user_id,new_email,new_email_key,created_at,expires_at) VALUES (?,?,?,?,?,?)")
    .bind(hash,user.id,email,emailKey,now.toISOString(),expires).run();
  const verifyUrl = new URL("/api/account/verify-email-change", url.origin);
  verifyUrl.searchParams.set("token", token);
  const safeUser = escapeHtml(String(user.username || "Factburst player"));
  const safeUrl = escapeHtml(verifyUrl.toString());
  try {
    await env.EMAIL.send({
      from: { email: from, name: "Factburst Quiz" },
      to: [email],
      subject: "Confirm your new Factburst Quiz email",
      text: `Hi ${user.username || "Factburst player"},\n\nConfirm this new email address for your Factburst Quiz account:\n${verifyUrl}\n\nYour current verified email stays active until you confirm this change. This link expires in ${CHANGE_HOURS} hours.`,
      html: `<div style="font-family:Arial,sans-serif;line-height:1.6"><h2>Confirm your new email</h2><p>Hi ${safeUser},</p><p>Your current verified email remains active until you confirm this new address.</p><p><a href="${safeUrl}">Confirm new email address</a></p><p>This link expires in ${CHANGE_HOURS} hours.</p></div>`,
    });
  } catch (error) {
    return json({ error: error instanceof Error ? error.message : "Could not send the confirmation email." }, 503);
  }
  return json({
    authenticated: true,
    pending_email: email,
    current_email: String(user.email || ""),
    verification_sent: true,
    message: `Confirmation sent to ${email}. Your current email remains active until the new address is confirmed.`,
  });
}

async function pendingEmail(db, userId) {
  const row = await db.prepare("SELECT new_email,created_at,expires_at FROM site_email_changes WHERE user_id=? AND expires_at>? ORDER BY created_at DESC LIMIT 1").bind(userId,new Date().toISOString()).first();
  return json({ pending_email: row ? String(row.new_email || "") : "", created_at: row ? String(row.created_at || "") : "", expires_at: row ? String(row.expires_at || "") : "" });
}

async function confirmEmailChange(db, url) {
  const token = String(url.searchParams.get("token") || "").trim();
  if (token.length < 32 || token.length > 200) return confirmationPage(false, "That email confirmation link is not valid.");
  const hash = await sha256(token);
  const row = await db.prepare("SELECT user_id,new_email,new_email_key,expires_at FROM site_email_changes WHERE token_hash=? LIMIT 1").bind(hash).first();
  if (!row || Date.parse(String(row.expires_at || "")) <= Date.now()) return confirmationPage(false, "That email confirmation link has expired or has already been used.");
  const existing = await db.prepare("SELECT id FROM site_users WHERE email_key=? AND id<>? LIMIT 1").bind(row.new_email_key,row.user_id).first();
  if (existing) return confirmationPage(false, "That email address is already in use by another account.");
  const now = new Date().toISOString();
  await db.prepare("UPDATE site_users SET email=?,email_key=?,email_verified_at=? WHERE id=?").bind(row.new_email,row.new_email_key,now,row.user_id).run();
  await db.prepare("DELETE FROM site_email_changes WHERE user_id=?").bind(row.user_id).run();
  return confirmationPage(true, `Your Factburst email has been changed to ${String(row.new_email || "")}.`);
}

function confirmationPage(success, message) {
  const safe = escapeHtml(message);
  return new Response(`<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Email ${success ? "confirmed" : "change"} | Factburst Quiz</title><style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#07103d;color:#fff;font:16px system-ui}.card{width:min(560px,calc(100% - 32px));padding:30px;border:1px solid #3153a0;border-radius:20px;background:#0b1b59}a{display:inline-block;margin-top:14px;color:#4edcff;font-weight:800}</style></head><body><main class="card"><h1>${success ? "Email confirmed" : "Email change unavailable"}</h1><p>${safe}</p><a href="/profile.html">Return to your profile</a></main></body></html>`, { status: success ? 200 : 400, headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" } });
}

function normalizeEmail(value){const email=String(value||"").trim();if(!email||email.length>254||/\s/.test(email))return "";return /^[^@]+@[^@]+\.[^@]+$/.test(email)?email:"";}
function randomToken(byteCount){const bytes=new Uint8Array(byteCount);crypto.getRandomValues(bytes);return base64UrlEncode(bytes);}
async function sha256(value){const bytes=new TextEncoder().encode(String(value||""));const digest=await crypto.subtle.digest("SHA-256",bytes);return base64UrlEncode(new Uint8Array(digest));}
function base64UrlEncode(bytes){let binary="";for(const byte of bytes)binary+=String.fromCharCode(byte);return btoa(binary).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/g,"");}
function escapeHtml(value){return String(value||"").replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"})[c]);}
function json(value,status=200){return new Response(JSON.stringify(value),{status,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff"}});}
