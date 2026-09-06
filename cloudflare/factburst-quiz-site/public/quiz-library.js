(() => {
  "use strict";

  const PAGE_SIZE = 12;
  const state = { page: 1, category: "", search: "" };
  const $ = selector => document.querySelector(selector);

  const grid = $("#quiz-grid");
  const empty = $("#empty-state");
  const filter = $("#category-filter");
  const search = $("#quiz-search");
  const pagination = $("#quiz-pagination");
  const previous = $("#quiz-previous");
  const next = $("#quiz-next");
  const pageLabel = $("#quiz-page-label");
  const countLabel = $("#quiz-count-label");

  function quizUrl(slug) {
    return `/quiz/${encodeURIComponent(slug)}`;
  }

  function createQuizCard(quiz) {
    const card = document.createElement("a");
    card.className = "quiz-card";
    card.href = quizUrl(quiz.slug);

    const category = document.createElement("span");
    category.className = "category-pill";
    category.textContent = quiz.category || "Quiz";

    const heading = document.createElement("h3");
    heading.textContent = quiz.title;

    const copy = document.createElement("p");
    copy.textContent = quiz.description || "Test yourself and see what score you can get.";

    const footer = document.createElement("div");
    footer.className = "quiz-card-footer";
    const questions = document.createElement("span");
    questions.textContent = `${quiz.question_count || 0} questions`;
    const play = document.createElement("span");
    play.textContent = "Play →";
    footer.append(questions, play);

    card.append(category, heading, copy, footer);
    return card;
  }

  function updateUrl() {
    const params = new URLSearchParams();
    if (state.page > 1) params.set("page", String(state.page));
    if (state.category) params.set("category", state.category);
    if (state.search) params.set("search", state.search);
    const target = `${location.pathname}${params.toString() ? `?${params}` : ""}#browse`;
    history.replaceState(null, "", target);
  }

  function readUrl() {
    const params = new URLSearchParams(location.search);
    state.page = Math.max(1, Number.parseInt(params.get("page") || "1", 10) || 1);
    state.category = params.get("category") || "";
    state.search = params.get("search") || "";
    if (filter) filter.value = state.category;
    if (search) search.value = state.search;
  }

  async function loadCategories() {
    try {
      const response = await fetch("/api/quizzes?limit=1", { cache: "no-store" });
      if (!response.ok) return;
      const data = await response.json();
      const categories = [...new Set((data.quizzes || []).map(quiz => quiz.category).filter(Boolean))].sort((a, b) => a.localeCompare(b));
      for (const category of categories) {
        if ([...filter.options].some(option => option.value.toLowerCase() === String(category).toLowerCase())) continue;
        const option = document.createElement("option");
        option.value = category;
        option.textContent = category;
        filter.append(option);
      }
    } catch {}
  }

  async function loadPage() {
    grid.setAttribute("aria-busy", "true");
    try {
      const params = new URLSearchParams({ page: String(state.page), limit: String(PAGE_SIZE) });
      if (state.category) params.set("category", state.category);
      if (state.search) params.set("search", state.search);
      const response = await fetch(`/api/quizzes?${params}`, { cache: "no-store" });
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || "The quiz library could not be loaded.");

      const quizzes = Array.isArray(data.quizzes) ? data.quizzes : [];
      grid.replaceChildren(...quizzes.map(createQuizCard));
      empty.classList.toggle("hidden", quizzes.length !== 0);

      const totalPages = Number(data.total_pages || 1);
      previous.disabled = !data.has_previous;
      next.disabled = !data.has_more;
      pageLabel.textContent = `Page ${data.page} of ${totalPages}`;
      countLabel.textContent = `${Number(data.total || 0)} quiz${Number(data.total || 0) === 1 ? "" : "zes"}`;
      pagination.classList.toggle("hidden", totalPages <= 1);
      updateUrl();
    } catch (error) {
      grid.replaceChildren();
      empty.classList.remove("hidden");
      empty.querySelector("h3").textContent = "Quiz library unavailable";
      empty.querySelector("p").textContent = error.message || "Please try again shortly.";
    } finally {
      grid.removeAttribute("aria-busy");
    }
  }

  filter.addEventListener("change", () => {
    state.category = filter.value;
    state.page = 1;
    loadPage();
  });

  let searchTimer;
  search.addEventListener("input", () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      state.search = search.value.trim();
      state.page = 1;
      loadPage();
    }, 250);
  });

  previous.addEventListener("click", () => {
    if (state.page <= 1) return;
    state.page--;
    loadPage();
    document.querySelector("#browse")?.scrollIntoView({ behavior: "smooth", block: "start" });
  });

  next.addEventListener("click", () => {
    state.page++;
    loadPage();
    document.querySelector("#browse")?.scrollIntoView({ behavior: "smooth", block: "start" });
  });

  readUrl();
  loadCategories();
  loadPage();
})();
