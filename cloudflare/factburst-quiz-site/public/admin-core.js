(() => {
  "use strict";
  const KEY = "factburst_admin_session_key";
  const NAV = [["Dashboard","/admin"],["Quizzes","/admin/quizzes"],["Social","/admin/social"],["Analytics","/admin/analytics"],["Settings","/admin/settings"]];
  const path = location.pathname.replace(/\/$/, "") || "/admin";
  const nativeFetch = window.fetch.bind(window);
  let session = null;

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
    try {
      const response = await nativeFetch("/api/admin/auth/session", { credentials: "same-origin", cache: "no-store" });
      if (!response.ok) return false;
      session = await response.json().catch(() => ({ ok: true, role: "admin" }));
      session = { ok: true, role: session?.role === "moderator" ? "moderator" : "admin" };
      sessionStorage.setItem(KEY, "session");
      window.FactburstAdminSession = session;
      return true;
    } catch { return false; }
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
      const response = await nativeFetch(new Request(request, { headers, credentials: "same-origin", cache: "no-store" }));
      if (response.status === 401 && String(request.url).includes("/api/")) {
        sessionStorage.removeItem(KEY);
        if (path !== "/admin") {
          const returnTo = `${location.pathname}${location.search}${location.hash}`;
          location.replace(`/admin?return=${encodeURIComponent(returnTo)}`);
        } else {
          showLogin();
        }
        document.dispatchEvent(new CustomEvent("factburst-admin-session-expired"));
      }
      return response;
    }
  };

  window.fetch = window.FactburstAdmin.fetch;
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot, { once: true });
  else boot();
})();
