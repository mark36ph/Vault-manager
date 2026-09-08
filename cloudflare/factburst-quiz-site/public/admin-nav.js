(() => {
  const ADMIN_LINK = ["Admin", "/admin"];
  const SOCIAL_LINK = ["Social", "/admin#social"];
  let adminReady = false;

  async function checkAdmin() {
    try {
      const response = await fetch("/api/site/status", { credentials: "same-origin", headers: { accept: "application/json" } });
      if (!response.ok) return false;
      const payload = await response.json();
      return payload?.is_admin === true && String(payload?.role || "").toLowerCase() === "admin";
    } catch { return false; }
  }

  function addDesktopAdminLink() {
    const nav = document.querySelector(".top-nav");
    if (!nav || !adminReady || nav.classList.contains("desktop-navigation-source")) return;
    const slot = nav.querySelector(".notification-slot");
    for (const [label, href] of [ADMIN_LINK, SOCIAL_LINK]) {
      if (nav.querySelector(`[data-admin-nav="${label.toLowerCase()}"]`)) continue;
      const link = document.createElement("a");
      link.href = href;
      link.textContent = label;
      link.dataset.adminNav = label.toLowerCase();
      if (label === "Admin" && location.pathname === "/admin") link.setAttribute("aria-current", "page");
      if (label === "Social" && location.pathname === "/admin" && location.hash === "#social") link.setAttribute("aria-current", "page");
      if (slot) nav.insertBefore(link, slot); else nav.append(link);
    }
  }

  async function initialize() {
    adminReady = await checkAdmin();
    if (adminReady) window.factburstAdmin = true;
    addDesktopAdminLink();
    window.dispatchEvent(new CustomEvent("factburst:admin-nav-ready"));
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize, { once: true });
  else initialize();
})();
