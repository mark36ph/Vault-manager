(() => {
  "use strict";

  // Keep the private admin UI hidden until the server confirms that the
  // current Factburst account has the exact "admin" role.
  document.documentElement.style.visibility = "hidden";

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

      document.documentElement.style.visibility = "visible";
      return true;
    } catch {
      window.location.replace("/");
      return false;
    }
  }

  window.factburstAdminAccess = checkAdmin();
})();
