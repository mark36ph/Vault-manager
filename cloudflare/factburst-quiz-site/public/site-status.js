fetch("/api/site/status", { credentials: "same-origin", cache: "no-store" })
  .then(response => response.ok ? response.json() : null)
  .then(status => {
    if (!status?.maintenance || !status?.is_admin) return;
    if (document.querySelector("#factburst-maintenance-banner")) return;
    const banner = document.createElement("div");
    banner.id = "factburst-maintenance-banner";
    banner.className = "site-maintenance-admin-banner";
    banner.textContent = `MAINTENANCE MODE — You are viewing the live site as an administrator. ${status.message || ""}`;
    document.body.prepend(banner);
    document.body.classList.add("admin-maintenance-active");
  })
  .catch(() => {});
