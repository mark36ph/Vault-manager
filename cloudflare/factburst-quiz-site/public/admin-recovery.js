(() => {
  "use strict";
  const $ = s => document.querySelector(s);
  async function api(path, options = {}) {
    const response = await fetch(path, { credentials:"same-origin", cache:"no-store", ...options });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(data.error || `Request failed (${response.status}).`);
    return data;
  }
  function status(text, type = "") {
    const el = $("#recovery-status");
    if (!el) return;
    el.textContent = text;
    el.className = `admin-status ${type}`.trim();
  }
  function render(data) {
    const maintenance = data?.maintenance || {};
    const enabled = !!maintenance.enabled;
    $("#recovery-state").textContent = enabled ? "Maintenance mode is ON" : "Website is live";
    $("#recovery-message").textContent = maintenance.message || "No maintenance message is configured.";
    $("#recovery-updated").textContent = maintenance.updated_at ? `Last changed: ${new Date(maintenance.updated_at).toLocaleString()}` : "Last change time unavailable.";
    $("#disable-maintenance").disabled = !enabled;
  }
  async function load() {
    try {
      const data = await api("/api/admin/users/site-settings");
      render({ maintenance: { enabled:data.maintenance_enabled, message:data.maintenance_message, updated_at:data.updated_at } });
    } catch (error) {
      status(error.message, "error");
    }
  }
  async function disable() {
    if (!window.confirm("Turn maintenance mode OFF and make the public website live now?")) return;
    const button = $("#disable-maintenance");
    button.disabled = true;
    status("Turning maintenance mode off…");
    try {
      const data = await api("/api/admin/users/site-settings", {
        method:"PATCH",
        headers:{"content-type":"application/json"},
        body:JSON.stringify({ maintenance_enabled:false })
      });
      render({ maintenance:{ enabled:data.maintenance_enabled, message:data.maintenance_message, updated_at:data.updated_at } });
      status("Maintenance mode is OFF. The public website is live.", "success");
    } catch (error) {
      status(error.message, "error");
      await load();
    } finally {
      button.disabled = false;
    }
  }
  function init() {
    $("#disable-maintenance")?.addEventListener("click", disable);
    document.addEventListener("factburst-admin-session-ready", load);
    if (window.FactburstAdminSession) load();
  }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init, {once:true});
  else init();
})();
