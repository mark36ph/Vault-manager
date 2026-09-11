(() => {
  "use strict";
  const KEY = "factburst_admin_session_key";
  const DIAG_KEY = "factburst_admin_auth_diagnostic";
  const NAV = [
    ["Dashboard", "/admin", "dashboard"],
    ["Quizzes", "/admin/quizzes", "quiz"],
    ["Social", "/admin/social", "social"],
    ["Analytics", "/admin/analytics", "analytics"],
    ["Settings", "/admin/settings", "settings"],
  ];
  const path = location.pathname.replace(/\/$/, "") || "/admin";
  const nativeFetch = window.fetch.bind(window);
  let session = null;

  function icon(name) {
    const icons = {
      dashboard: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/></svg>',
      quiz: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 3.5h9l3 3V20.5H6z"/><path d="M14 3.5v4h4M9 12h6M9 16h4"/></svg>',
      social: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="6" cy="12" r="2.5"/><circle cx="18" cy="6" r="2.5"/><circle cx="18" cy="18" r="2.5"/><path d="m8.2 10.8 7.5-3.6M8.2 13.2l7.5 3.6"/></svg>',
      analytics: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 19V9M10 19V5M16 19v-7M22 19H2"/></svg>',
      settings: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Z"/><path d="m19.4 15 .1.1a2 2 0 0 1-2.8 2.8l-.1-.1a2 2 0 0 0-3.4 1.4v.2a2 2 0 0 1-4 0v-.2a2 2 0 0 0-3.4-1.4l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1A2 2 0 0 0 3.7 11H3.5a2 2 0 0 1 0-4h.2a2 2 0 0 0 1.4-3.4L5 3.5a2 2 0 1 1 2.8-2.8l.1.1A2 2 0 0 0 11.3 2h.2a2 2 0 0 1 4 0v.2a2 2 0 0 0 3.4 1.4l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1A2 2 0 0 0 23.1 10h.2a2 2 0 0 1 0 4h-.2a2 2 0 0 0-3.7-1.4Z"/></svg>'
    };
    return icons[name] || icons.dashboard;
  }

  function installNavStyles() {
    if (document.getElementById("admin-sidebar-svg-styles")) return;
    const style = document.createElement("style");
    style.id = "admin-sidebar-svg-styles";
    style.textContent = `.admin-sidebar{gap:6px}.admin-sidebar-label{padding:8px 10px 6px}.admin-sidebar a{position:relative;gap:11px}.admin-sidebar a svg{width:18px;height:18px;flex:0 0 18px;fill:none;stroke:currentColor;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round;opacity:.82}.admin-sidebar a.active svg{opacity:1}.admin-sidebar-section-divider{height:1px;margin:7px 8px;background:rgba(255,255,255,.08)}.admin-sidebar-footer{margin-top:auto;padding-top:6px}.admin-sidebar-footer-label{padding:5px 10px;color:var(--admin-muted,#9ca8ba);font-size:10px;font-weight:800;letter-spacing:.08em;text-transform:uppercase}@media(max-width:700px){.admin-sidebar a{gap:7px}.admin-sidebar a svg{width:16px;height:16px;flex-basis:16px}.admin-sidebar-section-divider{margin:5px 4px}}`;
    document.head.appendChild(style);
  }

  function recordAuthDiagnostic(data) {
    const diagnostic = { time: new Date().toISOString(), page: location.pathname, ...data };
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
    installNavStyles();
    let nav = app.querySelector(".admin-sidebar");
    if (!nav) {
      nav = document.createElement("aside");
      nav.className = "admin-sidebar";
      nav.setAttribute("aria-label", "Admin navigation");
      app.insertBefore(nav, app.firstElementChild);
    }
    nav.innerHTML = `<div class="admin-sidebar-label">Overview</div>${NAV.slice(0,1).map(([label,href,ico]) => `<a href="${href}" class="${href === path ? "active" : ""}">${icon(ico)}<span>${label}</span></a>`).join("")}<div class="admin-sidebar-label">Manage</div>${NAV.slice(1,3).map(([label,href,ico]) => `<a href="${href}" class="${href === path ? "active" : ""}">${icon(ico)}<span>${label}</span></a>`).join("")}<div class="admin-sidebar-label">Insights</div>${NAV.slice(3,4).map(([label,href,ico]) => `<a href="${href}" class="${href === path ? "active" : ""}">${icon(ico)}<span>${label}</span></a>`).join("")}<div class="admin-sidebar-footer"><div class="admin-sidebar-section-divider"></div><div class="admin-sidebar-footer-label">System</div>${NAV.slice(4).map(([label,href,ico]) => `<a href="${href}" class="${href === path ? "active" : ""}">${icon(ico)}<span>${label}</span></a>`).join("")}</div>`;
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
