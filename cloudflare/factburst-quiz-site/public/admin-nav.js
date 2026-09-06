(() => {
  const ADMIN_LINK = ["Admin", "/admin.html"];
  let adminReady = false;
  let accountRole = "";

  async function checkAdmin() {
    try {
      const response = await fetch("/api/site/status", { credentials: "same-origin", headers: { accept: "application/json" } });
      if (!response.ok) return false;
      const payload = await response.json();
      accountRole = String(payload?.role || "").toLowerCase();
      return payload?.is_admin === true && accountRole === "admin";
    } catch {
      return false;
    }
  }

  function addDesktopAdminLink() {
    const nav = document.querySelector(".top-nav");
    if (!nav || !adminReady || nav.querySelector("[data-admin-nav]") || nav.classList.contains("desktop-navigation-source")) return;
    const slot = nav.querySelector(".notification-slot");
    const link = document.createElement("a");
    link.href = ADMIN_LINK[1];
    link.textContent = ADMIN_LINK[0];
    link.dataset.adminNav = "1";
    if (location.pathname === "/admin.html") link.setAttribute("aria-current", "page");
    if (slot) nav.insertBefore(link, slot); else nav.append(link);
  }

  function renderProfileRole() {
    const roleElement = document.querySelector("#profile-role");
    if (!roleElement || !["admin", "moderator"].includes(accountRole)) return;
    roleElement.textContent = accountRole === "admin" ? "ADMIN" : "MOD";
    roleElement.dataset.role = accountRole === "admin" ? "admin" : "mod";
    roleElement.classList.remove("hidden");
  }

  async function initialize() {
    const isAdmin = await checkAdmin();
    adminReady = isAdmin;
    if (isAdmin) window.factburstAdmin = true;
    addDesktopAdminLink();
    renderProfileRole();
    if (accountRole === "admin" || accountRole === "moderator") {
      const observer = new MutationObserver(renderProfileRole);
      observer.observe(document.body, { subtree: true, attributes: true, attributeFilter: ["class", "data-role"] });
      window.setTimeout(() => observer.disconnect(), 10000);
    }
    window.dispatchEvent(new CustomEvent("factburst:admin-nav-ready"));
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize, { once: true });
  else initialize();
})();
