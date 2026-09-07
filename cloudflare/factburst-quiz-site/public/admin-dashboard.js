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
  let catalogue = [];

  function renderStats(stats = {}) {
    const values = {
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
    const source = catalogue.length ? catalogue : rows.map(row => ({
      status: row.querySelector(".admin-badge")?.textContent || "",
      question_count: Number.parseInt((row.querySelector(".admin-quiz-meta")?.textContent.match(/(\d+) questions?/) || [])[1] || "0", 10),
      attempts: 0,
    }));
    const attention = source.filter(quiz => Number(quiz.question_count || 0) < 10).length;
    const ready = source.filter(quiz => Number(quiz.question_count || 0) >= 10).length;
    const drafts = source.filter(quiz => String(quiz.status || "").toLowerCase() === "draft").length;
    const totalAttempts = source.reduce((sum, quiz) => sum + Number(quiz.attempts || 0), 0);
    const activeQuizzes = source.filter(quiz => Number(quiz.attempts || 0) > 0).length;
    const averageAttempts = source.length ? totalAttempts / source.length : 0;
    const topPlayed = [...source].sort((a, b) => Number(b.attempts || 0) - Number(a.attempts || 0)).slice(0, 5);
    const topRows = topPlayed.length
      ? topPlayed.map((quiz, index) => `<li><span><b>${index + 1}</b>${escapeHtml(quiz.title || quiz.slug || "Untitled quiz")}</span><strong>${Number(quiz.attempts || 0).toLocaleString()}</strong></li>`).join("")
      : `<li class="admin-insight-empty">No quiz play data yet.</li>`;
    panel.innerHTML = `<div class="admin-health-summary"><div><p class="eyebrow">Quiz health</p><h3>${attention ? `${attention} quiz${attention === 1 ? "" : "zes"} need attention` : "Your quiz catalogue looks healthy"}</h3><p>${ready} quiz${ready === 1 ? "" : "zes"} have 10+ questions · ${drafts} draft${drafts === 1 ? "" : "s"}.</p></div><div class="admin-health-metrics"><span><b>${activeQuizzes}</b> active</span><span><b>${averageAttempts.toFixed(1)}</b> plays/quiz</span></div></div><div class="admin-insight-grid"><div><p class="eyebrow">Most played</p><ol class="admin-top-quizzes">${topRows}</ol></div><div><p class="eyebrow">Activity snapshot</p><div class="admin-activity-stats"><span><b>${totalAttempts.toLocaleString()}</b>Total plays</span><span><b>${activeQuizzes}</b>Quizzes played</span><span><b>${drafts}</b>Drafts</span></div></div></div>`;
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
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
      catalogue = Array.isArray(data.quizzes) ? data.quizzes : [];
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
