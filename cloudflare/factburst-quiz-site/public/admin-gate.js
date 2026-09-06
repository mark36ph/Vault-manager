(() => {
  "use strict";

  // The website admin UI is available only to an authenticated Factburst
  // account whose database role is exactly "admin". Normal users and guests
  // are sent straight back to the public home page.
  async function checkAdmin() {
    try {
      const response = await fetch("/api/site/status", {
        method: "GET",
        credentials: "same-origin",
        cache: "no-store",
        headers: { Accept: "application/json" },
      });
      if (!response.ok) {
        window.location.replace("/");
        return false;
      }

      const status = await response.json();
      if (status?.is_admin !== true || status?.role !== "admin") {
        window.location.replace("/");
        return false;
      }
      return true;
    } catch {
      window.location.replace("/");
      return false;
    }
  }

  window.factburstAdminAccess = checkAdmin();
})();
