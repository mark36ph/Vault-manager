(() => {
  "use strict";
  const $ = selector => document.querySelector(selector);
  const list = $("#quiz-list");
  const search = $("#admin-search");
  const status = $("#admin-status-filter");
  const category = $("#admin-category-filter");
  const dashboard = $("#admin-section-dashboard");
  if (!list || !search || !status || !category) return;

  let rows = [];

  function renderStats(stats = {}) {
    const values = {
      "#stat-total": stats.total_quizzes,
      "#stat-published": stats.published,
      "#stat-drafts": stats.drafts,
      "#stat-questions": stats.questions,
      "#stat-attempts": stats.attempts,
    };
    for (const [selector, value] of Object.entries(values)) {
      const element = $(selector);
      if (element) element.textContent = Number(value || 0).toLocaleString();
    }
  }

  function populateCategories(data) {
    const categories = [...new Set((data.quizzes || []).map(quiz => quiz.category).filter(Boolean))].sort((a, b) => a.localeCompare(b));
    const current = category.value;
    category.replaceChildren(new Option("All categories", ""), ...categories.map(value => new Option(value, value)));
    category.value = current;
  }

  function refreshRows() {
    rows = [...list.querySelectorAll(".admin-quiz-row")];
    for (const row of rows) {
      const actions = row.querySelector(".admin-row-actions");
      const edit = actions?.querySelector("[data-edit]");
      if (!actions || !edit || actions.querySelector("[data-preview]") || !edit.dataset.edit) continue;
      const preview = document.createElement("a");
      preview.className = "button button-secondary";
      preview.href = `/admin-preview?slug=${encodeURIComponent(edit.dataset.edit)}`;
      preview.target = "_blank";
      preview.rel = "noopener";
      preview.dataset.preview = "true";
      preview.textContent = "Preview";
      actions.insertBefore(preview, edit);
    }
    renderHealth();
  }

  function renderHealth() {
    if (!dashboard) return;
    let panel = dashboard.querySelector("#admin-quiz-health");
    if (!panel) {
      panel = document.createElement("article");
      panel.id = "admin-quiz-health";
      panel.className = "admin-dashboard-card admin-quiz-health";
      const panels = dashboard.querySelector(".admin-dashboard-panels");
      (panels || dashboard).appendChild(panel);
    }
    const counts = { attention: 0, ready: 0, drafts: 0 };
    for (const row of rows) {
      const meta = [...(row.querySelector(".admin-quiz-meta")?.children || [])].map(element => element.textContent.trim());
      const questionMatch = meta.find(value => / questions?$/.test(value));
      const questions = Number.parseInt(questionMatch || "0", 10) || 0;
      const isDraft = meta[0]?.toLowerCase() === "draft";
      if (isDraft) counts.drafts += 1;
      if (questions < 10) counts.attention += 1; else counts.ready += 1;
    }
    panel.innerHTML = `<p class="eyebrow">Quiz health</p><h3>${counts.attention ? `${counts.attention} quiz${counts.attention === 1 ? "" : "zes"} need attention` : "Your quiz catalogue looks healthy"}</h3><p>${counts.ready} quiz${counts.ready === 1 ? "" : "zes"} have 10+ questions · ${counts.drafts} draft${counts.drafts === 1 ? "" : "s"} · Preview any quiz directly from the catalogue.</p>`;
  }

  function applyFilters() {
    refreshRows();
    const term = search.value.trim().toLowerCase();
    const wantedStatus = status.value.toLowerCase();
    const wantedCategory = category.value.toLowerCase();
    for (const row of rows) {
      const meta = row.querySelector(".admin-quiz-meta");
      const title = row.querySelector(".admin-quiz-title")?.textContent?.toLowerCase() || "";
      const values = [...(meta?.children || [])].map(element => element.textContent.trim().toLowerCase());
      const matchesSearch = !term || `${title} ${values.join(" ")}`.includes(term);
      const matchesStatus = !wantedStatus || values[0] === wantedStatus;
      const matchesCategory = !wantedCategory || values[1] === wantedCategory;
      row.classList.toggle("filtered-out", !(matchesSearch && matchesStatus && matchesCategory));
    }
  }

  async function loadStats() {
    try {
      const response = await fetch("/api/admin/quizzes", { credentials: "same-origin", cache: "no-store" });
      if (!response.ok) return;
      const data = await response.json();
      renderStats(data.stats || {});
      populateCategories(data);
      refreshRows();
    } catch {}
  }

  search.addEventListener("input", applyFilters);
  status.addEventListener("change", applyFilters);
  category.addEventListener("change", applyFilters);
  new MutationObserver(() => setTimeout(applyFilters, 0)).observe(list, { childList: true });

  loadStats();
  window.setInterval(() => {
    if (document.visibilityState === "visible") loadStats();
  }, 10000);
})();
