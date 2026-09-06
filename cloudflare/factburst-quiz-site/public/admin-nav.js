(() => {
  const ADMIN_LINK = ["Admin", "/admin.html"];
  let adminReady = false;

  async function checkAdmin() {
    try {
      const response = await fetch("/api/site/status", { credentials: "same-origin", headers: { accept: "application/json" } });
      if (!response.ok) return false;
      const payload = await response.json();
      return payload?.is_admin === true && String(payload?.role || "").toLowerCase() === "admin";
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

  async function initialize() {
    adminReady = await checkAdmin();
    if (!adminReady) return;
    window.factburstAdmin = true;
    addDesktopAdminLink();
    window.dispatchEvent(new CustomEvent("factburst:admin-nav-ready"));
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize, { once: true });
  else initialize();
})();
