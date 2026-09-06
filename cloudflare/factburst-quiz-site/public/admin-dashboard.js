(() => {
  "use strict";
  const $ = selector => document.querySelector(selector);
  const list = $("#quiz-list");
  const search = $("#admin-search");
  const status = $("#admin-status-filter");
  const category = $("#admin-category-filter");
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
    } catch {}
  }

  search.addEventListener("input", applyFilters);
  status.addEventListener("change", applyFilters);
  category.addEventListener("change", applyFilters);
  new MutationObserver(() => setTimeout(applyFilters, 0)).observe(list, { childList: true });
  loadStats();
})();
