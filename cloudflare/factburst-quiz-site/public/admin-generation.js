(() => {
  "use strict";
  const $ = selector => document.querySelector(selector);
  const form = $("#generation-settings-form");
  const status = $("#generation-status");
  const jobs = $("#generation-jobs");
  const runNow = $("#generation-run-now");
  if (!form || !status || !jobs || !runNow) return;

  const fields = {
    enabled: $("#generation-enabled"),
    frequency: $("#generation-frequency"),
    time: $("#generation-time"),
    perRun: $("#generation-per-run"),
    categories: $("#generation-categories"),
    autoPublish: $("#generation-auto-publish")
  };

  function setStatus(message, error = false) {
    status.textContent = message;
    status.classList.toggle("error", error);
  }

  function categoriesValue() {
    return fields.categories.value.split(/[,\n]/).map(value => value.trim()).filter(Boolean).slice(0, 30);
  }

  function renderJobs(items) {
    if (!items.length) {
      jobs.innerHTML = '<p class="admin-empty-inline">No generation jobs have been queued yet.</p>';
      return;
    }
    jobs.innerHTML = items.map(job => {
      const title = String(job.requested_category || "Automatic category mix");
      const state = String(job.status || "queued");
      const created = job.created_at ? new Date(job.created_at).toLocaleString() : "";
      const detail = job.error_message || job.result_summary || "Waiting for the quiz generator.";
      return `<article class="generation-job"><div><strong>${escapeHtml(title)}</strong><small>${escapeHtml(state)} · ${escapeHtml(created)}</small></div><p>${escapeHtml(detail)}</p></article>`;
    }).join("");
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
  }

  async function load() {
    try {
      const response = await fetch("/api/admin/users/generation", { credentials: "same-origin", cache: "no-store" });
      if (!response.ok) throw new Error("Could not load generation settings.");
      const data = await response.json();
      fields.enabled.checked = data.settings?.enabled === true;
      fields.frequency.value = data.settings?.frequency || "daily";
      fields.time.value = data.settings?.time_utc || "06:00";
      fields.perRun.value = String(data.settings?.quizzes_per_run || 1);
      fields.categories.value = Array.isArray(data.settings?.categories) ? data.settings.categories.join(", ") : "";
      fields.autoPublish.checked = data.settings?.auto_publish === true;
      renderJobs(Array.isArray(data.jobs) ? data.jobs : []);
    } catch (error) {
      setStatus(error.message || "Could not load generation settings.", true);
    }
  }

  async function save(event) {
    event.preventDefault();
    setStatus("Saving…");
    const payload = {
      enabled: fields.enabled.checked,
      frequency: fields.frequency.value,
      time_utc: fields.time.value || "06:00",
      quizzes_per_run: Math.min(10, Math.max(1, Number.parseInt(fields.perRun.value || "1", 10))),
      categories: categoriesValue(),
      auto_publish: fields.autoPublish.checked
    };
    try {
      const response = await fetch("/api/admin/users/generation", {
        method: "PATCH",
        credentials: "same-origin",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(payload)
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(data.error || "Could not save generation settings.");
      setStatus("Generation settings saved.");
      await load();
    } catch (error) {
      setStatus(error.message || "Could not save generation settings.", true);
    }
  }

  async function queueNow() {
    runNow.disabled = true;
    setStatus("Queuing generation job…");
    try {
      const response = await fetch("/api/admin/users/generation/run", {
        method: "POST",
        credentials: "same-origin",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ categories: categoriesValue(), quizzes_per_run: Number.parseInt(fields.perRun.value || "1", 10), auto_publish: fields.autoPublish.checked })
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(data.error || "Could not queue generation.");
      setStatus(`Queued ${Number(data.queued || 0).toLocaleString()} generation job${data.queued === 1 ? "" : "s"}.`);
      await load();
    } catch (error) {
      setStatus(error.message || "Could not queue generation.", true);
    } finally {
      runNow.disabled = false;
    }
  }

  form.addEventListener("submit", save);
  runNow.addEventListener("click", queueNow);
  load();
  window.setInterval(() => { if (document.visibilityState === "visible") load(); }, 15000);
})();
