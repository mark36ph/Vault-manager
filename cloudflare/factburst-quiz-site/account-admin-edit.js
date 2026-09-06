import { derivePasswordHash, PASSWORD_POLICY } from "./account-auth.js";

const MAX_TOKEN_LENGTH = 200;

export async function handleAdminAccountEditApi(request, env, url) {
  if (url.pathname !== "/api/account/admin-edit" || request.method !== "POST") return null;

  try {
    const body = await request.json();
    const token = String(body?.edit_token || "").trim();
    if (token.length < 32 || token.length > MAX_TOKEN_LENGTH) {
      return json({ error: "The account edit authorization is invalid or expired." }, 401);
    }

    const tokenHash = await sha256(token);
    const now = new Date().toISOString();
    const tokenRow = await env.DB.prepare(`
      SELECT user_id, expires_at
      FROM site_admin_user_edit_tokens
      WHERE token_hash = ? AND expires_at > ?
      LIMIT 1
    `).bind(tokenHash, now).first();
    if (!tokenRow) return json({ error: "The account edit authorization is invalid or expired." }, 401);

    const userId = Number(tokenRow.user_id || 0);
    const existing = await env.DB.prepare(`
      SELECT id, username, username_key, email, email_key
      FROM site_users WHERE id = ? LIMIT 1
    `).bind(userId).first();
    if (!existing) return json({ error: "User not found." }, 404);

    const username = normalizeUsername(body?.username);
    const email = normalizeEmail(body?.email);
    const password = String(body?.password || "");
    if (!username) return json({ error: "Choose a username 3–24 characters long using letters, numbers, spaces, dots, dashes or underscores." }, 400);
    if (!email) return json({ error: "Enter a valid email address." }, 400);
    if (password.length > 0 && (password.length < 10 || password.length > 128)) {
      return json({ error: password.length < 10 ? "Use a password with at least 10 characters." : "Password is too long." }, 400);
    }

    const usernameKey = username.toLowerCase();
    const emailKey = email.toLowerCase();
    const duplicate = await env.DB.prepare(`
      SELECT username_key, email_key
      FROM site_users
      WHERE id <> ? AND (username_key = ? OR email_key = ?)
      LIMIT 1
    `).bind(userId, usernameKey, emailKey).first();
    if (duplicate?.username_key === usernameKey) return json({ error: "That username is already taken." }, 409);
    if (duplicate?.email_key === emailKey) return json({ error: "That email address is already in use." }, 409);

    const statements = [];
    const emailChanged = emailKey !== String(existing.email_key || "").toLowerCase();
    const usernameChanged = usernameKey !== String(existing.username_key || "").toLowerCase();
    const passwordChanged = password.length > 0;

    if (passwordChanged) {
      const pepper = String(env.PASSWORD_PEPPER || "").trim();
      if (pepper.length < 32) {
        return json({ error: "Account password security is temporarily unavailable. Please try again shortly.", code: "password_security_not_configured" }, 503);
      }
      const salt = randomToken(18);
      const hash = await derivePasswordHash(password, salt, pepper, PASSWORD_POLICY.iterations);
      statements.push(env.DB.prepare(`
        UPDATE site_users
        SET username = ?, username_key = ?, email = ?, email_key = ?,
            email_verified_at = CASE WHEN ? THEN NULL ELSE email_verified_at END,
            password_hash = ?, password_salt = ?, password_iterations = ?, password_scheme = ?
        WHERE id = ?
      `).bind(
        username,
        usernameKey,
        email,
        emailKey,
        emailChanged ? 1 : 0,
        hash,
        salt,
        PASSWORD_POLICY.iterations,
        PASSWORD_POLICY.scheme,
        userId,
      ));
    } else {
      statements.push(env.DB.prepare(`
        UPDATE site_users
        SET username = ?, username_key = ?, email = ?, email_key = ?,
            email_verified_at = CASE WHEN ? THEN NULL ELSE email_verified_at END
        WHERE id = ?
      `).bind(username, usernameKey, email, emailKey, emailChanged ? 1 : 0, userId));
    }

    if (emailChanged) {
      statements.push(env.DB.prepare("DELETE FROM site_email_verifications WHERE user_id = ?").bind(userId));
    }
    if (passwordChanged) {
      statements.push(env.DB.prepare("DELETE FROM site_sessions WHERE user_id = ?").bind(userId));
    }
    statements.push(env.DB.prepare("DELETE FROM site_admin_user_edit_tokens WHERE token_hash = ?").bind(tokenHash));
    await env.DB.batch(statements);

    const updated = await env.DB.prepare(`
      SELECT id, username, email, email_verified_at, status
      FROM site_users WHERE id = ? LIMIT 1
    `).bind(userId).first();

    return json({
      updated: true,
      username_changed: usernameChanged,
      email_changed: emailChanged,
      password_changed: passwordChanged,
      user: {
        id: Number(updated?.id || userId),
        username: String(updated?.username || username),
        email: String(updated?.email || email),
        email_verified: Boolean(updated?.email_verified_at),
        email_verified_at: updated?.email_verified_at ? String(updated.email_verified_at) : null,
        status: String(updated?.status || "active"),
      },
    });
  } catch (error) {
    console.error("Factburst administrator account edit failed", error);
    return json({ error: "The account could not be updated. Please try again.", code: "account_edit_failed" }, 503);
  }
}

function normalizeUsername(value) {
  const username = String(value || "").trim().replace(/\s+/g, " ");
  if (username.length < 3 || username.length > 24) return "";
  if (!/^[A-Za-z0-9][A-Za-z0-9 _.-]*[A-Za-z0-9]$/.test(username)) return "";
  return username;
}

function normalizeEmail(value) {
  const email = String(value || "").trim();
  if (!email || email.length > 254 || /\s/.test(email)) return "";
  return /^[^@]+@[^@]+\.[^@]+$/.test(email) ? email : "";
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
