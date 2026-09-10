(() => {
  "use strict";
  const $ = s => document.querySelector(s);
  const toggle = $("#maintenance-toggle");
  const message = $("#maintenance-message");
  const save = $("#save-website-settings");
  const status = $("#website-settings-status");
  const siteStatus = $("#settings-site-status");
  const adsEnabled = $("#ads-enabled");
  const adsClient = $("#adsense-client");
  const adsLeft = $("#adsense-left-slot");
  const adsRight = $("#adsense-right-slot");
  const adsSave = $("#save-ads-settings");
  const adsStatus = $("#ads-settings-status");
  const adsState = $("#ads-settings-state");
  const adsBadge = $("#ads-settings-badge");
  const adsValidation = $("#ads-settings-validation");
  const publisherCheck = $("#ads-check-publisher");
  const slotCheck = $("#ads-check-slot");
  const adsOverview = $("#settings-ads-status");
  const backupOverview = $("#settings-backup-status");
  const backupStatus = $("#api-settings-backup-status");
  const backupState = $("#api-settings-backup-state");
  const backupDate = $("#api-settings-backup-date");
  const services = $("#api-settings-services");

  const setStatus = (element, text, type = "") => {
    if (!element) return;
    element.textContent = text;
    element.className = `admin-status ${type}`.trim();
  };

  async function api(path, options = {}) {
    const response = await fetch(path, { credentials: "same-origin", cache: "no-store", ...options });
    let data = {};
    try { data = await response.json(); } catch {}
    if (!response.ok) {
      const error = new Error(data.error || `Request failed (${response.status})`);
      error.status = response.status;
      throw error;
    }
    return data;
  }

  function validClient(value) {
    return /^ca-pub-\d{10,24}$/.test(String(value || "").trim());
  }

  function validSlot(value) {
    return /^\d{4,20}$/.test(String(value || "").trim());
  }

  function slotMatchesPublisher(slot, client) {
    const publisherNumber = String(client || "").replace(/^ca-pub-/, "");
    return Boolean(slot && publisherNumber && String(slot).trim() === publisherNumber);
  }

  function updateAdsValidation() {
    const client = String(adsClient?.value || "").trim();
    const left = String(adsLeft?.value || "").trim();
    const right = String(adsRight?.value || "").trim();
    const publisherOk = validClient(client);
    const leftOk = validSlot(left);
    const rightOk = validSlot(right);
    const anySlot = leftOk || rightOk;
    const leftLooksWrong = leftOk && slotMatchesPublisher(left, client);
    const rightLooksWrong = rightOk && slotMatchesPublisher(right, client);
    const realSlot = (leftOk && !leftLooksWrong) || (rightOk && !rightLooksWrong);

    if (publisherCheck) {
      publisherCheck.textContent = publisherOk ? "✓" : "!";
      publisherCheck.classList.toggle("warn", !publisherOk);
    }
    if (slotCheck) {
      slotCheck.textContent = realSlot ? "✓" : "!";
      slotCheck.classList.toggle("warn", !realSlot);
    }

    if (!adsValidation) return { publisherOk, anySlot, realSlot, leftLooksWrong, rightLooksWrong };

    adsValidation.className = "admin-settings-validation";
    if (!publisherOk) {
      adsValidation.textContent = "Enter a valid AdSense Publisher ID in the format ca-pub-1234567890123456.";
      adsValidation.classList.add("error");
    } else if (!anySlot) {
      adsValidation.textContent = "Add at least one numeric AdSense ad-slot ID before enabling advertising.";
      adsValidation.classList.add("error");
    } else if (leftLooksWrong || rightLooksWrong) {
      adsValidation.textContent = "The slot ID currently matches your Publisher ID. Ad slot IDs are separate numeric IDs created for individual ad units. Replace it with the actual slot ID from AdSense.";
      adsValidation.classList.add("error");
    } else {
      adsValidation.textContent = "Publisher and ad-slot formats look valid.";
      adsValidation.classList.add("success");
    }

    return { publisherOk, anySlot, realSlot, leftLooksWrong, rightLooksWrong };
  }

  function updateAdsBadge() {
    const result = updateAdsValidation();
    const ready = result.publisherOk && result.realSlot;
    const active = Boolean(adsEnabled?.checked) && ready;
    if (adsBadge) {
      adsBadge.textContent = active ? "Active" : ready ? "Configured" : "Needs attention";
      adsBadge.className = `admin-section-badge ${active ? "success" : ""}`.trim();
    }
    if (adsOverview) adsOverview.textContent = active ? "Active" : ready ? "Configured" : "Needs attention";
    return result;
  }

  async function loadWebsite() {
    try {
      const data = await api("/api/admin/users/site-settings");
      toggle.checked = !!data.maintenance_enabled;
      message.value = data.maintenance_message || "";
      if (siteStatus) siteStatus.textContent = toggle.checked ? "Maintenance mode" : "Live";
    } catch (error) {
      setStatus(status, error.message, "error");
      if (siteStatus) siteStatus.textContent = "Unavailable";
    }
  }

  async function loadAds() {
    try {
      const data = await api("/api/admin/site/ads");
      adsEnabled.checked = !!data.enabled;
      adsClient.value = data.client || "";
      adsLeft.value = data.left_slot || "";
      adsRight.value = data.right_slot || "";
      const result = updateAdsBadge();
      const active = !!data.active && result.realSlot;
      setStatus(adsState, active ? "Google Ads are active on the public site." : result.realSlot ? "Advertising is configured but currently disabled." : "Google Ads need attention before they should be enabled.", active ? "success" : "");
      if (adsOverview) adsOverview.textContent = active ? "Active" : result.realSlot ? "Configured" : "Needs attention";
    } catch (error) {
      setStatus(adsStatus, error.message, "error");
      if (adsOverview) adsOverview.textContent = "Unavailable";
    }
  }

  async function loadBackup() {
    try {
      const data = await api("/api/admin/api-settings");
      if (!data.configured) {
        backupState.textContent = "No Cloudflare API settings backup has been created yet.";
        backupDate.textContent = "Use the desktop app's Back up API settings to Cloudflare button.";
        services.textContent = "No backup available.";
        if (backupOverview) backupOverview.textContent = "Not configured";
        return;
      }
      backupState.textContent = "Encrypted API settings backup is configured.";
      backupDate.textContent = data.backed_up_at ? `Last backup: ${new Date(data.backed_up_at).toLocaleString()}` : "Last backup time unavailable.";
      const entries = Object.entries(data.settings || {});
      services.innerHTML = entries.length
        ? entries.map(([key, value]) => `<div class="admin-settings-service"><span>${escapeHtml(labelFor(key))}</span><code>${escapeHtml(String(value))}</code></div>`).join("")
        : "No configured API values were included in the backup.";
      if (backupOverview) backupOverview.textContent = "Configured";
    } catch (error) {
      setStatus(backupStatus, error.message, "error");
      if (backupOverview) backupOverview.textContent = "Unavailable";
    }
  }

  function labelFor(key) {
    return String(key).replace(/_/g, " ").replace(/\b\w/g, match => match.toUpperCase());
  }

  function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>\"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[character]));
  }

  save?.addEventListener("click", async () => {
    save.disabled = true;
    setStatus(status, "Saving…");
    try {
      const data = await api("/api/admin/users/site-settings", {
        method: "PATCH",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ maintenance_enabled: toggle.checked, maintenance_message: message.value })
      });
      toggle.checked = !!data.maintenance_enabled;
      message.value = data.maintenance_message || "";
      setStatus(status, toggle.checked ? "Maintenance mode enabled." : "Website is live.", "success");
      if (siteStatus) siteStatus.textContent = toggle.checked ? "Maintenance mode" : "Live";
    } catch (error) {
      setStatus(status, error.message, "error");
    } finally {
      save.disabled = false;
    }
  });

  adsSave?.addEventListener("click", async () => {
    const validation = updateAdsValidation();
    if (!validation.publisherOk || !validation.realSlot) {
      setStatus(adsStatus, "Fix the AdSense configuration before enabling Google Ads.", "error");
      return;
    }
    adsSave.disabled = true;
    setStatus(adsStatus, "Saving…");
    try {
      const data = await api("/api/admin/site/ads", {
        method: "PATCH",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ enabled: adsEnabled.checked, client: adsClient.value, left_slot: adsLeft.value, right_slot: adsRight.value })
      });
      adsEnabled.checked = !!data.enabled;
      adsClient.value = data.client || "";
      adsLeft.value = data.left_slot || "";
      adsRight.value = data.right_slot || "";
      updateAdsBadge();
      setStatus(adsStatus, "Google Ads settings saved.", "success");
      setStatus(adsState, data.active ? "Google Ads are active on the public site." : "Advertising is configured but currently disabled.", data.active ? "success" : "");
      if (adsOverview) adsOverview.textContent = data.active ? "Active" : "Configured";
    } catch (error) {
      setStatus(adsStatus, error.message, "error");
    } finally {
      adsSave.disabled = false;
    }
  });

  [adsClient, adsLeft, adsRight, adsEnabled].forEach(element => element?.addEventListener("input", updateAdsBadge));
  toggle?.addEventListener("change", () => { if (siteStatus) siteStatus.textContent = toggle.checked ? "Maintenance mode" : "Live"; });

  if (toggle && message) loadWebsite();
  if (adsEnabled && adsClient && adsLeft && adsRight) loadAds();
  if (backupStatus && backupState && backupDate && services) loadBackup();
})();
