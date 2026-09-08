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
    const values = { "#stat-published": stats.published, "#stat-drafts": stats.drafts, "#stat-questions": stats.questions, "#stat-attempts": stats.attempts };
    for (const [selector, value] of Object.entries(values)) { const element = $(selector); if (element) element.textContent = Number(value || 0).toLocaleString(); }
  }

  function populateCategories(data) {
    const categories = [...new Set((data.quizzes || []).map(quiz => quiz.category).filter(Boolean))].sort((a, b) => a.localeCompare(b));
    const current = category.value;
    category.replaceChildren(new Option("All categories", ""), ...categories.map(value => new Option(value, value)));
    category.value = current;
  }

  function healthFor(quiz) {
    const questions = Array.isArray(quiz.questions) ? quiz.questions : [];
    const issues = [];
    const seenQuestions = new Set();
    if (questions.length < 10) issues.push(`Only ${questions.length} question${questions.length === 1 ? "" : "s"} (10 recommended)`);
    questions.forEach((item, index) => {
      const text = String(item.question || "").trim();
      const key = text.toLowerCase().replace(/\s+/g, " ");
      if (!text) issues.push(`Question ${index + 1} is empty`);
      if (key && seenQuestions.has(key)) issues.push(`Question ${index + 1} duplicates another question`);
      if (key) seenQuestions.add(key);
      const answers = Array.isArray(item.answers) ? item.answers.map(answer => String(answer || "").trim()) : [];
      if (answers.length < 4 || answers.some(answer => !answer)) issues.push(`Question ${index + 1} has a missing answer`);
      const normalizedAnswers = answers.filter(Boolean).map(answer => answer.toLowerCase().replace(/\s+/g, " "));
      if (new Set(normalizedAnswers).size !== normalizedAnswers.length) issues.push(`Question ${index + 1} has duplicate answers`);
      const correct = String(item.correct_answer || "").trim().toUpperCase();
      if (!["A", "B", "C", "D"].includes(correct)) issues.push(`Question ${index + 1} has no valid correct answer`);
      if (!String(item.explanation || "").trim()) issues.push(`Question ${index + 1} is missing an explanation`);
    });
    if (!String(quiz.category || "").trim()) issues.push("Category is missing");
    return issues;
  }

  function healthScore(issues, questionCount) {
    let score = 100;
    if (questionCount < 10) score -= 20;
    for (const issue of issues) {
      if (/empty|duplicate|missing answer|valid correct/.test(issue)) score -= 10;
      else if (/explanation|category/.test(issue)) score -= 5;
    }
    return Math.max(0, score);
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
      (dashboard.querySelector(".admin-dashboard-panels") || dashboard).appendChild(panel);
    }

    const source = catalogue.length ? catalogue : rows.map(row => ({ status: row.querySelector(".admin-badge")?.textContent || "", question_count: Number.parseInt((row.querySelector(".admin-quiz-meta")?.textContent.match(/(\d+) questions?/) || [])[1] || "0", 10), attempts: 0 }));
    const detailed = source.filter(quiz => Array.isArray(quiz.questions));
    const health = detailed.map(quiz => ({ quiz, issues: healthFor(quiz), score: healthScore(healthFor(quiz), quiz.questions.length) }));
    const attention = health.filter(item => item.issues.length > 0);
    const ready = health.filter(item => item.score === 100).length;
    const drafts = source.filter(quiz => String(quiz.status || "").toLowerCase() === "draft").length;
    const totalAttempts = source.reduce((sum, quiz) => sum + Number(quiz.attempts || 0), 0);
    const activeQuizzes = source.filter(quiz => Number(quiz.attempts || 0) > 0).length;
    const averageAttempts = source.length ? totalAttempts / source.length : 0;

    const attentionRows = attention.slice(0, 6).map(({ quiz, issues, score }) => {
      const slug = encodeURIComponent(quiz.slug || "");
      const title = escapeHtml(quiz.title || quiz.slug || "Untitled quiz");
      const summary = escapeHtml(issues.slice(0, 2).join(" · "));
      return `<li><div><strong>${title}</strong><small>${score}/100 · ${summary}${issues.length > 2 ? ` · +${issues.length - 2} more` : ""}</small></div><a class="button button-secondary" href="/admin?edit=${slug}">Fix</a></li>`;
    }).join("") || `<li class="admin-insight-empty">No quiz health issues found.</li>`;

    const topPlayed = [...source].sort((a, b) => Number(b.attempts || 0) - Number(a.attempts || 0)).slice(0, 5);
    const topRows = topPlayed.length ? topPlayed.map((quiz, index) => `<li><span><b>${index + 1}</b>${escapeHtml(quiz.title || quiz.slug || "Untitled quiz")}</span><strong>${Number(quiz.attempts || 0).toLocaleString()}</strong></li>`).join("") : `<li class="admin-insight-empty">No quiz play data yet.</li>`;
    const headline = attention.length ? `${attention.length} quiz${attention.length === 1 ? "" : "zes"} need attention` : "Your quiz catalogue looks healthy";
    panel.innerHTML = `<div class="admin-health-summary"><div><p class="eyebrow">Quiz health</p><h3>${headline}</h3><p>${ready} ready · ${drafts} draft${drafts === 1 ? "" : "s"} · ${attention.length} with issues.</p></div><div class="admin-health-metrics"><span><b>${activeQuizzes}</b> active</span><span><b>${averageAttempts.toFixed(1)}</b> plays/quiz</span></div></div><div class="admin-insight-grid"><div><p class="eyebrow">Needs attention</p><ul class="admin-health-issues">${attentionRows}</ul></div><div><p class="eyebrow">Most played</p><ol class="admin-top-quizzes">${topRows}</ol><div class="admin-activity-stats"><span><b>${totalAttempts.toLocaleString()}</b>Total plays</span><span><b>${activeQuizzes}</b>Quizzes played</span></div></div></div>`;
  }

  function escapeHtml(value) { return String(value).replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]); }

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
      await Promise.all(catalogue.map(async quiz => {
        if (!quiz.slug) return;
        try {
          const detail = await fetch(`/api/admin/quizzes/${encodeURIComponent(quiz.slug)}`, { credentials: "same-origin", cache: "no-store" });
          if (detail.ok) {
            const payload = await detail.json();
            if (payload.quiz) {
              Object.assign(quiz, payload.quiz);
              quiz.question_count = Array.isArray(payload.quiz.questions) ? payload.quiz.questions.length : Number(quiz.question_count || 0);
            }
          }
        } catch {}
      }));
      renderHealth();
    } catch {}
  }

  search.addEventListener("input", applyFilters);
  status.addEventListener("change", applyFilters);
  category.addEventListener("change", applyFilters);
  new MutationObserver(() => setTimeout(applyFilters, 0)).observe(list, { childList: true });
  loadStats();
  window.setInterval(() => { if (document.visibilityState === "visible") loadStats(); }, 10000);
})();
