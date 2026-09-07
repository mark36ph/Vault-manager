(() => {
  "use strict";

  const PAGE_SIZE = 12;
  const CATEGORY_PAGE_SIZE = 50;
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
  const latestCard = $("#latest-card");

  function quizUrl(slug) { return `/quiz/${encodeURIComponent(slug)}`; }

  function createQuizCard(quiz) {
    const card = document.createElement("a");
    card.className = "quiz-card";
    card.href = quizUrl(quiz.slug);
    const category = document.createElement("span"); category.className = "category-pill"; category.textContent = quiz.category || "Quiz";
    const heading = document.createElement("h3"); heading.textContent = quiz.title;
    const copy = document.createElement("p"); copy.textContent = quiz.description || "Test yourself and see what score you can get.";
    const footer = document.createElement("div"); footer.className = "quiz-card-footer";
    const questions = document.createElement("span"); questions.textContent = `${quiz.question_count || 0} questions`;
    const play = document.createElement("span"); play.textContent = "Play →";
    footer.append(questions, play); card.append(category, heading, copy, footer); return card;
  }

  function renderLatest(quiz) {
    if (!latestCard) return;
    latestCard.classList.remove("loading-card");
    if (!quiz) { latestCard.textContent = "No live quizzes yet. New challenges will appear here."; return; }
    const copy = document.createElement("div");
    const category = document.createElement("span"); category.className = "category-pill"; category.textContent = quiz.category || "Quiz";
    const heading = document.createElement("h3"); heading.textContent = quiz.title;
    const description = document.createElement("p"); description.textContent = quiz.description || `${quiz.question_count || 10} questions. See how many you can get right.`;
    copy.append(category, heading, description);
    const action = document.createElement("a"); action.className = "button button-primary"; action.href = quizUrl(quiz.slug); action.textContent = "Play quiz";
    latestCard.replaceChildren(copy, action);
  }

  async function loadLatest() {
    try { const response = await fetch("/api/quizzes/latest", { cache: "no-store" }); if (!response.ok) return renderLatest(null); const data = await response.json(); renderLatest(data.quiz || null); }
    catch { renderLatest(null); }
  }

  async function loadCategories() {
    if (!filter) return;
    const previousValue = state.category;
    const categories = new Map();
    try {
      const firstResponse = await fetch(`/api/quizzes?limit=${CATEGORY_PAGE_SIZE}&live_only=1`, { cache: "no-store" });
      if (firstResponse.ok) {
        const first = await firstResponse.json();
        for (const quiz of Array.isArray(first.quizzes) ? first.quizzes : []) {
          const value = String(quiz.category || "").trim();
          if (value) categories.set(value.toLowerCase(), value);
        }
        const totalPages = Math.min(Number(first.total_pages || 1), 20);
        if (totalPages > 1) {
          const requests = [];
          for (let page = 2; page <= totalPages; page++) requests.push(fetch(`/api/quizzes?page=${page}&limit=${CATEGORY_PAGE_SIZE}&live_only=1`, { cache: "no-store" }).then(response => response.ok ? response.json() : null).catch(() => null));
          const pages = await Promise.all(requests);
          for (const data of pages) for (const quiz of Array.isArray(data?.quizzes) ? data.quizzes : []) {
            const value = String(quiz.category || "").trim();
            if (value) categories.set(value.toLowerCase(), value);
          }
        }
      }
    } catch {}

    const fallbackCategories = ["Science", "History", "Geography", "Space", "Nature & Animals", "Technology", "Arts & Literature", "Music", "Film", "Logos", "Sports", "Entertainment", "Mathematics", "General Knowledge"];
    for (const value of fallbackCategories) if (!categories.has(value.toLowerCase())) categories.set(value.toLowerCase(), value);

    const sorted = [...categories.values()].sort((a, b) => a.localeCompare(b));
    filter.replaceChildren(new Option("All categories", ""), ...sorted.map(value => new Option(value, value)));
    filter.value = [...filter.options].some(option => option.value.toLowerCase() === previousValue.toLowerCase()) ? previousValue : "";
  }

  function updateUrl() {
    const params = new URLSearchParams();
    if (state.page > 1) params.set("page", String(state.page));
    if (state.category) params.set("category", state.category);
    if (state.search) params.set("search", state.search);
    history.replaceState(null, "", `${location.pathname}${params.toString() ? `?${params}` : ""}#browse`);
  }

  function readUrl() {
    const params = new URLSearchParams(location.search);
    state.page = Math.max(1, Number.parseInt(params.get("page") || "1", 10) || 1);
    state.category = params.get("category") || "";
    state.search = params.get("search") || "";
    if (search) search.value = state.search;
  }

  async function loadPage() {
    grid.setAttribute("aria-busy", "true");
    try {
      const params = new URLSearchParams({ page: String(state.page), limit: String(PAGE_SIZE), live_only: "1" });
      if (state.category) params.set("category", state.category);
      if (state.search) params.set("search", state.search);
      const response = await fetch(`/api/quizzes?${params}`, { cache: "no-store" });
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || "The quiz library could not be loaded.");
      const quizzes = Array.isArray(data.quizzes) ? data.quizzes : [];
      grid.replaceChildren(...quizzes.map(createQuizCard));
      empty.classList.toggle("hidden", quizzes.length !== 0);
      const totalPages = Number(data.total_pages || 1);
      previous.disabled = !data.has_previous; next.disabled = !data.has_more;
      pageLabel.textContent = `Page ${data.page} of ${totalPages}`;
      countLabel.textContent = `${Number(data.total || 0)} quiz${Number(data.total || 0) === 1 ? "" : "zes"}`;
      pagination.classList.toggle("hidden", totalPages <= 1); updateUrl();
    } catch (error) {
      grid.replaceChildren(); empty.classList.remove("hidden");
      empty.querySelector("h3").textContent = "Quiz library unavailable";
      empty.querySelector("p").textContent = error.message || "Please try again shortly.";
    } finally { grid.removeAttribute("aria-busy"); }
  }

  filter.addEventListener("change", () => { state.category = filter.value; state.page = 1; loadPage(); });
  let searchTimer;
  search.addEventListener("input", () => { clearTimeout(searchTimer); searchTimer = setTimeout(() => { state.search = search.value.trim(); state.page = 1; loadPage(); }, 250); });
  previous.addEventListener("click", () => { if (state.page <= 1) return; state.page--; loadPage(); document.querySelector("#browse")?.scrollIntoView({ behavior: "smooth", block: "start" }); });
  next.addEventListener("click", () => { state.page++; loadPage(); document.querySelector("#browse")?.scrollIntoView({ behavior: "smooth", block: "start" }); });

  readUrl();
  loadCategories();
  loadLatest();
  loadPage();
})();
