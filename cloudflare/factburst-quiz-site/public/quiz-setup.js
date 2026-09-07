(() => {
  const params = new URLSearchParams(location.search);
  const slug = String(params.get("quiz") || params.get("slug") || "").trim().toLowerCase();
  const type = document.querySelector("#quiz-type");
  const count = document.querySelector("#quiz-question-count");
  const start = document.querySelector("#start-selected-quiz");
  const status = document.querySelector("#setup-status");
  const description = document.querySelector("#setup-description");
  const typeHelp = document.querySelector("#quiz-type-help");
  const countHelp = document.querySelector("#quiz-count-help");

  if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug)) {
    status.textContent = "Choose a quiz from the quiz library to begin.";
    start.disabled = true;
    return;
  }

  const selectionKey = "factburst_quiz_selection_v1";
  let quiz = null;
  let pool = [];

  const shuffle = items => {
    const copy = [...items];
    for (let i = copy.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [copy[i], copy[j]] = [copy[j], copy[i]];
    }
    return copy;
  };

  const optionsFor = (base, available) => {
    const values = new Set([base]);
    for (const n of [15, 20, 25, 30, 35, 40, 50]) if (n > base && n <= available) values.add(n);
    if (available > base) values.add(available);
    return [...values].sort((a, b) => a - b);
  };

  function refreshCopy() {
    const categoryMode = type.value === "category";
    typeHelp.textContent = categoryMode
      ? `Build a longer challenge using questions from the ${quiz?.category || "selected"} category.`
      : "Play the questions that belong to this specific quiz.";
    countHelp.textContent = categoryMode
      ? "Longer challenges draw additional questions from the category."
      : "The standard quiz uses its own questions.";
  }

  function refreshCounts() {
    if (!quiz) return;
    const base = Array.isArray(quiz.questions) ? quiz.questions.length : 0;
    const available = Math.max(base, pool.length);
    count.replaceChildren();
    const values = type.value === "category" ? optionsFor(base, available) : [base];
    for (const value of values) {
      const option = document.createElement("option");
      option.value = String(value);
      option.textContent = value === base && type.value !== "category" ? `Standard ${value}-question quiz` : `${value} questions`;
      count.append(option);
    }
    refreshCopy();
  }

  type.addEventListener("change", refreshCounts);

  async function load() {
    status.textContent = "Loading quiz options…";
    try {
      const response = await fetch(`/api/quizzes/${encodeURIComponent(slug)}`, { credentials: "same-origin", cache: "no-store" });
      const payload = await response.json();
      if (!response.ok || !payload?.quiz) throw new Error("Quiz unavailable");
      quiz = payload.quiz;
      description.textContent = `${quiz.title || "Quiz"}${quiz.category ? ` · ${quiz.category}` : ""}`;
      const poolResponse = await fetch(`/api/quizzes/${encodeURIComponent(slug)}/question-pool`, { credentials: "same-origin", cache: "no-store" });
      if (poolResponse.ok) {
        const poolPayload = await poolResponse.json().catch(() => null);
        pool = Array.isArray(poolPayload?.questions) ? poolPayload.questions : [];
      }
      refreshCounts();
      status.textContent = "";
      start.disabled = false;
    } catch {
      status.textContent = "This quiz could not be loaded. Please return to the quiz library.";
      start.disabled = true;
    }
  }

  start.addEventListener("click", () => {
    if (!quiz) return;
    const baseQuestions = Array.isArray(quiz.questions) ? quiz.questions.map(q => ({ ...q, quiz_id: Number(q.quiz_id || quiz.id || 0) })) : [];
    const selectedCount = Number(count.value || baseQuestions.length);
    const questions = type.value === "category" && selectedCount > baseQuestions.length
      ? shuffle(pool).slice(0, selectedCount)
      : shuffle(baseQuestions).slice(0, baseQuestions.length);
    if (questions.length < selectedCount) {
      status.textContent = "There are not enough questions available for that challenge yet.";
      return;
    }
    const selection = { slug, refs: questions.map(q => ({ quiz_id: Number(q.quiz_id), position: Number(q.position) })), positions: questions.map(q => Number(q.position)), count: questions.length, type: type.value, created_at: Date.now() };
    try { sessionStorage.setItem(selectionKey, JSON.stringify(selection)); } catch {}
    location.href = `/quiz/${encodeURIComponent(slug)}?selected=1`;
  });

  load();
})();
