(() => {
  "use strict";
  // Quiz generation belongs to the desktop application. Keep this compatibility
  // shim only long enough to remove any stale website generation UI from older
  // cached admin.html pages.
  function removeWebsiteGenerationUi() {
    document.querySelectorAll('[data-admin-section="generation"]').forEach(el => el.remove());
    document.querySelectorAll('[data-admin-section-panel="generation"]').forEach(el => el.remove());
    if (location.hash === "#generation") {
      history.replaceState(null, "", "#dashboard");
      document.querySelector('[data-admin-section="dashboard"]')?.click();
    }
  }
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", removeWebsiteGenerationUi, { once: true });
  } else {
    removeWebsiteGenerationUi();
  }
})();
