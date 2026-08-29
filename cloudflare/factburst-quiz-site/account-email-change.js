import { activeSessionUser } from "./account-access.js";

const VERIFICATION_HOURS = 24;
const RESEND_SECONDS = 60;

export async function handleEmailChangeApi(request, env, url) {
  const { pathname } = url;

  if (pathname === "/api/account/email-status" && request.method === "GET") {
    return emailStatus(request, env.DB);
  }
  if (pathname === "/api/account/email" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return requestEmailChange(request, env, url);
  }
  if (pathname === "/api/account/resend-verification" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "Request origin was not accepted." }, 403);
    return resendPendingEmailChange(request, env, url);
  }
  if (pathname === "/api/account/verify" && request.method === "GET") {
    return verifyEmailToken(env.DB, url);
  }

  return null;
}

export function normalizeEmailAddress(value) {
  const email = String(value || "").trim();
  if (!email || email.length > 254 || /\s/.test(email)) return "";
  if (!/^[^@]+@[^@]+\.[^@]+$/.test(email)) return "";
  return email;
}

export function verificationTarget(row) {
  const tokenEmailKey = String(row?.email_key || "");
  if (!tokenEmailKey) return "";
  if (String(row?.pending_email_key || "") && tokenEmailKey === String(row.pending_email_key)) return "pending";
  if (tokenEmailKey === String(row?.current_email_key || "")) return "current";
  return "";
}

async function emailStatus(request, db) {
  const user = await activeSessionUser(request, db);
  if (!user) return json({ error: "Log in to manage your email address." }, 401);

  const row = await accountEmailRow(db, user.id);
  return json({
    email: String(row?.email || ""),
    email_verified: Boolean(row?.email_verified_at),
    pending_email: String(row?.pending_email || ""),
  });
}

async function requestEmailChange(request, env, url) {
  const sessionUser = await activeSessionUser(request, env.DB);
  if (!sessionUser) return json({ error: "Log in before changing your email address." }, 401);

  const body = await readJson(request);
  const email = normalizeEmailAddress(body?.email);
  if (!email) return json({ error: "Enter a valid email address." }, 400);
  const emailKey = email.toLowerCase();
  const user = await accountEmailRow(env.DB, sessionUser.id);
  if (!user) return json({ error: "Account not found." }, 404);

  if (emailKey === String(user.email_key || "")) {
    if (user.pending_email_key) {
      await env.DB.prepare(`
        UPDATE site_users SET pending_email = '', pending_email_key = '' WHERE id = ?
      `).bind(user.id).run();
      await env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(user.id).run();
    }
    return json({
      email: String(user.email || ""),
      email_verified: Boolean(user.email_verified_at),
      pending_email: "",
      verification_sent: false,
      message: "That is already your current email address.",
    });
  }

  const existing = await env.DB.prepare(`
    SELECT id FROM site_users
    WHERE id <> ? AND (email_key = ? OR pending_email_key = ?)
    LIMIT 1
  `).bind(user.id, emailKey, emailKey).first();
  if (existing) return json({ error: "That email address is already in use." }, 409);

  await env.DB.prepare(`
    UPDATE site_users SET pending_email = ?, pending_email_key = ? WHERE id = ?
  `).bind(email, emailKey, user.id).run();

  const targetUser = {
    ...user,
    email,
    email_key: emailKey,
  };
  const delivery = await issueEmailChangeVerification(env, url, targetUser, { bypassRateLimit: true });
  return json({
    email: String(user.email || ""),
    email_verified: Boolean(user.email_verified_at),
    pending_email: email,
    verification_sent: delivery.sent,
    message: delivery.sent
      ? `Confirmation sent to ${email}. Your current email stays active until you confirm the new one.`
      : delivery.error,
  }, delivery.sent ? 200 : 503);
}

async function resendPendingEmailChange(request, env, url) {
  const sessionUser = await activeSessionUser(request, env.DB);
  if (!sessionUser) return json({ error: "Log in before requesting another verification email." }, 401);

  const user = await accountEmailRow(env.DB, sessionUser.id);
  if (!user?.pending_email || !user?.pending_email_key) return null;

  const targetUser = {
    ...user,
    email: user.pending_email,
    email_key: user.pending_email_key,
  };
  const delivery = await issueEmailChangeVerification(env, url, targetUser);
  if (delivery.retry_after) {
    return json({
      error: `Please wait ${delivery.retry_after} seconds before requesting another email.`,
      retry_after: delivery.retry_after,
    }, 429);
  }

  return json({
    pending_email: String(user.pending_email),
    verification_sent: delivery.sent,
    message: delivery.sent ? `Confirmation resent to ${user.pending_email}.` : delivery.error,
  }, delivery.sent ? 200 : 503);
}

async function verifyEmailToken(db, url) {
  const token = String(url.searchParams.get("token") || "").trim();
  if (token.length < 32 || token.length > 200) return json({ error: "That verification link is not valid." }, 400);

  const tokenHash = await sha256(token);
  const now = new Date().toISOString();
  const row = await db.prepare(`
    SELECT v.user_id, v.email_key, v.expires_at,
           u.email_key AS current_email_key,
           u.pending_email, u.pending_email_key
    FROM site_email_verifications v
    JOIN site_users u ON u.id = v.user_id
    WHERE v.token_hash = ? LIMIT 1
  `).bind(tokenHash).first();

  if (!row) return json({ error: "That verification link has already been used or is not valid." }, 400);
  if (String(row.expires_at || "") <= now) {
    await db.prepare("DELETE FROM site_email_verifications WHERE token_hash = ?").bind(tokenHash).run();
    return json({ error: "That verification link has expired. Request a new one from your account." }, 410);
  }

  const target = verificationTarget(row);
  if (target === "pending") {
    const pendingEmailKey = String(row.pending_email_key || "");
    const existing = await db.prepare(`
      SELECT id FROM site_users WHERE email_key = ? AND id <> ? LIMIT 1
    `).bind(pendingEmailKey, row.user_id).first();
    if (existing) {
      await db.prepare("DELETE FROM site_email_verifications WHERE token_hash = ?").bind(tokenHash).run();
      return json({ error: "That email address is already in use." }, 409);
    }

    await db.batch([
      db.prepare(`
        UPDATE site_users
        SET email = pending_email,
            email_key = pending_email_key,
            email_verified_at = ?,
            pending_email = '',
            pending_email_key = ''
        WHERE id = ?
      `).bind(now, row.user_id),
      db.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(row.user_id),
    ]);
    return json({
      verified: true,
      email_changed: true,
      message: "Your new email address is confirmed and is now active on your Factburst account.",
    });
  }

  if (target === "current") {
    await db.batch([
      db.prepare("UPDATE site_users SET email_verified_at = ? WHERE id = ?").bind(now, row.user_id),
      db.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(row.user_id),
    ]);
    return json({ verified: true, message: "Email verified. You can now play Factburst quizzes." });
  }

  await db.prepare("DELETE FROM site_email_verifications WHERE token_hash = ?").bind(tokenHash).run();
  return json({ error: "That verification link no longer matches your account email." }, 409);
}

async function issueEmailChangeVerification(env, url, user, options = {}) {
  if (!env.EMAIL || typeof env.EMAIL.send !== "function") {
    return { sent: false, error: "Verification email delivery is not configured yet." };
  }
  const from = String(env.EMAIL_FROM || "").trim();
  const email = normalizeEmailAddress(user.email);
  if (!normalizeEmailAddress(from) || !email) {
    return { sent: false, error: "The confirmation email could not be prepared." };
  }

  const now = new Date().toISOString();
  if (!options.bypassRateLimit) {
    const latest = await env.DB.prepare(`
      SELECT created_at FROM site_email_verifications
      WHERE user_id = ? ORDER BY created_at DESC LIMIT 1
    `).bind(user.id).first();
    if (latest?.created_at) {
      const elapsed = Math.floor((Date.parse(now) - Date.parse(latest.created_at)) / 1000);
      if (Number.isFinite(elapsed) && elapsed >= 0 && elapsed < RESEND_SECONDS) {
        return { sent: false, retry_after: RESEND_SECONDS - elapsed };
      }
    }
  }

  const token = randomToken(32);
  const tokenHash = await sha256(token);
  const expiresAt = new Date(Date.parse(now) + VERIFICATION_HOURS * 60 * 60 * 1000).toISOString();
  await env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(user.id).run();
  await env.DB.prepare(`
    INSERT INTO site_email_verifications (token_hash, user_id, email_key, created_at, expires_at)
    VALUES (?, ?, ?, ?, ?)
  `).bind(tokenHash, user.id, String(user.email_key || email.toLowerCase()), now, expiresAt).run();

  const verifyUrl = new URL("/", url.origin);
  verifyUrl.searchParams.set("verify_email", token);
  const username = String(user.username || "Factburst player");
  const text = [
    `Hi ${username},`,
    "",
    `Confirm ${email} as the new email address for your Factburst Quiz account:`,
    verifyUrl.toString(),
    "",
    `This link expires in ${VERIFICATION_HOURS} hours and can only be used once.`,
    "Your existing email remains active until this confirmation succeeds.",
    "If you did not request this change, you can ignore this email.",
  ].join("\n");
  const html = `
    <div style="font-family:Arial,sans-serif;line-height:1.55;color:#101828">
      <h2>Confirm your new Factburst Quiz email</h2>
      <p>Hi ${escapeHtml(username)},</p>
      <p>Confirm <strong>${escapeHtml(email)}</strong> as the new email address for your Factburst Quiz account.</p>
      <p><a href="${escapeHtml(verifyUrl.toString())}" style="display:inline-block;padding:12px 18px;background:#087ea4;color:white;text-decoration:none;border-radius:8px;font-weight:700">Confirm new email</a></p>
      <p>Your existing email remains active until this confirmation succeeds.</p>
      <p>This link expires in ${VERIFICATION_HOURS} hours and can only be used once.</p>
      <p>If you did not request this change, you can ignore this email.</p>
    </div>`;

  try {
    await env.EMAIL.send({
      from: { email: from, name: "Factburst Quiz" },
      to: [email],
      subject: "Confirm your new Factburst Quiz email",
      text,
      html,
    });
    return { sent: true };
  } catch (error) {
    console.error("Factburst email change confirmation failed", error);
    return { sent: false, error: "We could not send the confirmation email. Try again shortly." };
  }
}

async function accountEmailRow(db, userId) {
  return db.prepare(`
    SELECT id, username, email, email_key, email_verified_at,
           pending_email, pending_email_key
    FROM site_users WHERE id = ? LIMIT 1
  `).bind(userId).first();
}

async function readJson(request) {
  try {
    return await request.json();
  } catch {
    return null;
  }
}

function sameOrigin(request, url) {
  const origin = String(request.headers.get("origin") || "").trim();
  return origin === url.origin;
}

async function sha256(value) {
  const bytes = new TextEncoder().encode(String(value || ""));
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return base64UrlEncode(new Uint8Array(digest));
}

function randomToken(byteCount) {
  const bytes = new Uint8Array(byteCount);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes);
}

function base64UrlEncode(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function escapeHtml(value) {
  return String(value || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
    },
  });
}
