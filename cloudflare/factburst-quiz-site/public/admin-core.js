(() => {
  "use strict";
  const KEY = "factburst_admin_session_key";
  const DIAG_KEY = "factburst_admin_auth_diagnostic";
  const NAV = [["Dashboard","/admin"],["Quizzes","/admin/quizzes"],["Social","/admin/social"],["Analytics","/admin/analytics"],["Settings","/admin/settings"]];
  const path = location.pathname.replace(/\/$/, "") || "/admin";
  const nativeFetch = window.fetch.bind(window);
  let session = null;

  function recordAuthDiagnostic(data) {
    const diagnostic = {
      time: new Date().toISOString(),
      page: location.pathname,
      ...data,
    };
    try { sessionStorage.setItem(DIAG_KEY, JSON.stringify(diagnostic)); } catch {}
    window.FactburstAdminAuthDiagnostic = diagnostic;
    document.dispatchEvent(new CustomEvent("factburst-admin-auth-diagnostic", { detail: diagnostic }));
    return diagnostic;
  }

  function clearAuthDiagnostic() {
    try { sessionStorage.removeItem(DIAG_KEY); } catch {}
    window.FactburstAdminAuthDiagnostic = null;
  }

  function addDiagnostics() {
    const header = document.querySelector(".admin-header-actions");
    if (!header || header.querySelector("[data-admin-auth-diagnostics]")) return;
    const details = document.createElement("details");
    details.dataset.adminAuthDiagnostics = "1";
    details.className = "admin-auth-diagnostics";
    details.innerHTML = `<summary>Auth diagnostics</summary><div class="admin-auth-diagnostics-body"></div>`;
    header.appendChild(details);
    const body = details.querySelector(".admin-auth-diagnostics-body");
    const render = event => {
      const d = event?.detail || (() => { try { return JSON.parse(sessionStorage.getItem(DIAG_KEY) || "null"); } catch { return null; } })();
      if (!d) { body.textContent = "No authentication errors recorded."; return; }
      body.textContent = `Time: ${d.time || "unknown"}\nPage: ${d.page || "unknown"}\nPhase: ${d.phase || "unknown"}\nEndpoint: ${d.endpoint || "unknown"}\nHTTP: ${d.status ?? "unknown"}\nResult: ${d.result || d.error || "authentication check failed"}`;
    };
    render();
    document.addEventListener("factburst-admin-auth-diagnostic", render);
  }

  function shell() {
    const app = document.querySelector("#admin-app");
    if (!app) return;
    let nav = app.querySelector(".admin-sidebar");
    if (!nav) {
      nav = document.createElement("aside");
      nav.className = "admin-sidebar";
      nav.setAttribute("aria-label", "Admin navigation");
      app.insertBefore(nav, app.firstElementChild);
    }
    nav.innerHTML = `<div class="admin-sidebar-label">Manage</div>${NAV.map(([label, href]) => `<a href="${href}" class="${href === path ? "active" : ""}">${label}</a>`).join("")}`;
  }

  function addIdentity() {
    const header = document.querySelector(".admin-header-actions");
    if (!header || header.querySelector("[data-admin-identity]")) return;
    const identity = document.createElement("span");
    identity.dataset.adminIdentity = "1";
    identity.className = "admin-status success";
    identity.textContent = `${session?.role === "moderator" ? "Moderator" : "Administrator"} signed in`;
    header.appendChild(identity);
  }

  function addSignOut() {
    const header = document.querySelector(".admin-header-actions");
    if (!header || header.querySelector("[data-admin-signout]")) return;
    const b = document.createElement("button");
    b.className = "button button-ghost";
    b.type = "button";
    b.dataset.adminSignout = "1";
    b.textContent = "Sign out";
    header.appendChild(b);
    b.onclick = async () => {
      try { await nativeFetch("/api/admin/auth/logout", { method: "POST", credentials: "same-origin" }); } catch {}
      sessionStorage.removeItem(KEY);
      clearAuthDiagnostic();
      location.href = "/admin";
    };
  }

  function showApp() {
    const app = document.querySelector("#admin-app");
    if (app && !document.querySelector("#login-form")) app.classList.remove("hidden");
  }

  function showLogin() {
    document.querySelector("#admin-app")?.classList.add("hidden");
    document.querySelector("#login-panel")?.classList.remove("hidden");
  }

  async function verifySession() {
    const endpoint = "/api/admin/auth/session";
    try {
      const response = await nativeFetch(endpoint, { credentials: "same-origin", cache: "no-store" });
      if (!response.ok) {
        const body = await response.clone().text().catch(() => "");
        recordAuthDiagnostic({ phase: "shared-session-check", endpoint, status: response.status, result: body || response.statusText || "session endpoint rejected request" });
        return false;
      }
      session = await response.json().catch(() => ({ ok: true, role: "admin" }));
      session = { ok: true, role: session?.role === "moderator" ? "moderator" : "admin" };
      sessionStorage.setItem(KEY, "session");
      window.FactburstAdminSession = session;
      clearAuthDiagnostic();
      return true;
    } catch (error) {
      recordAuthDiagnostic({ phase: "shared-session-check", endpoint, status: 0, result: error?.message || "network error" });
      return false;
    }
  }

  async function boot() {
    shell();
    addDiagnostics();
    const hasLoginForm = Boolean(document.querySelector("#login-form"));
    const valid = await verifySession();
    if (valid) {
      showApp();
      addIdentity();
      addSignOut();
      document.dispatchEvent(new CustomEvent("factburst-admin-session-ready", { detail: session }));
      return;
    }
    sessionStorage.removeItem(KEY);
    if (hasLoginForm || path === "/admin") {
      showLogin();
      document.dispatchEvent(new CustomEvent("factburst-admin-session-expired"));
      return;
    }
    const returnTo = `${location.pathname}${location.search}${location.hash}`;
    location.replace(`/admin?return=${encodeURIComponent(returnTo)}`);
  }

  window.FactburstAdmin = {
    session: () => session,
    fetch: async (input, options = {}) => {
      const request = new Request(input, options);
      const headers = new Headers(request.headers);
      const authorization = headers.get("Authorization") || "";
      if (/^Bearer\s+session$/i.test(authorization.trim())) headers.delete("Authorization");
      const endpoint = new URL(request.url, location.origin).pathname;
      let response;
      try {
        response = await nativeFetch(new Request(request, { headers, credentials: "same-origin", cache: "no-store" }));
      } catch (error) {
        recordAuthDiagnostic({ phase: "api-request", endpoint, status: 0, result: error?.message || "network error" });
        throw error;
      }
      if (response.status === 401 && endpoint.startsWith("/api/")) {
        const body = await response.clone().text().catch(() => "");
        recordAuthDiagnostic({ phase: "api-auth-check", endpoint, status: 401, result: body || response.statusText || "unauthorized" });
        sessionStorage.removeItem(KEY);
        session = null;
        document.dispatchEvent(new CustomEvent("factburst-admin-session-expired", { detail: { endpoint, status: 401 } }));
      }
      return response;
    }
  };

  window.fetch = window.FactburstAdmin.fetch;
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot, { once: true });
  else boot();
})();
