(() => {
  const originalFetch = window.fetch.bind(window);
  const selectionKey = "factburst_quiz_selection_v1";
  let quizSelection = null;
  let selectionReady = null;

  const getSlug = () => {
    const match = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})\/?$/i);
    if (match) return match[1].toLowerCase();
    const value = new URLSearchParams(location.search).get("slug") || "";
    return /^[a-z0-9][a-z0-9-]{0,79}$/i.test(value) ? value.toLowerCase() : "";
  };

  const saveSelection = (slug, positions) => {
    quizSelection = { slug, positions: [...positions] };
    window.factburstQuizSelection = quizSelection;
    try { sessionStorage.setItem(selectionKey, JSON.stringify({ ...quizSelection, created_at: Date.now() })); } catch {}
  };

  const loadSelection = slug => {
    if (quizSelection?.slug === slug && Array.isArray(quizSelection.positions)) return quizSelection;
    try {
      const value = JSON.parse(sessionStorage.getItem(selectionKey) || "null");
      if (value?.slug === slug && Array.isArray(value.positions)) {
        quizSelection = value;
        window.factburstQuizSelection = value;
        return value;
      }
    } catch {}
    return null;
  };

  const shuffle = items => {
    const copy = [...items];
    for (let i = copy.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [copy[i], copy[j]] = [copy[j], copy[i]];
    }
    return copy;
  };

  const createOptionValues = total => {
    const values = [5, 10, 15, 20].filter(value => value < total);
    values.push(total);
    return [...new Set(values)].sort((a, b) => a - b);
  };

  const setupSelection = (quiz, slug) => {
    const setup = document.querySelector("#quiz-setup");
    const select = document.querySelector("#quiz-question-count");
    const start = document.querySelector("#start-quiz");
    const copy = document.querySelector("#quiz-setup-copy");
    if (!setup || !select || !start) return Promise.resolve(Math.min(10, quiz.questions.length));
    setup.classList.remove("hidden");
    select.replaceChildren();
    const options = createOptionValues(quiz.questions.length);
    for (const value of options) {
      const option = document.createElement("option");
      option.value = String(value);
      option.textContent = value === quiz.questions.length ? `All ${value} questions` : `${value} questions`;
      select.append(option);
    }
    const previous = loadSelection(slug);
    const previousCount = previous?.positions?.length;
    const defaultCount = options.includes(previousCount) ? previousCount : (options.includes(10) ? 10 : options[options.length - 1]);
    select.value = String(defaultCount);
    if (copy) copy.textContent = `This quiz has ${quiz.questions.length} questions. Choose how many you want to play, and we’ll randomly select them for you.`;
    return new Promise(resolve => {
      selectionReady = resolve;
      start.onclick = () => {
        const count = Math.max(1, Math.min(Number(select.value), quiz.questions.length));
        const selected = shuffle(quiz.questions).slice(0, count);
        const positions = selected.map(question => Number(question.position));
        saveSelection(slug, positions);
        setup.classList.add("hidden");
        resolve(count);
        selectionReady = null;
      };
    });
  };

  window.fetch = async (input, init) => {
    const url = typeof input === "string" ? input : input?.url || "";
    const method = String(init?.method || (input instanceof Request ? input.method : "GET")).toUpperCase();
    const parsed = new URL(url, location.origin);
    if (method === "POST" && /^\/api\/quizzes\/[a-z0-9][a-z0-9-]{0,79}\/score$/i.test(parsed.pathname)) {
      try {
        const body = typeof init?.body === "string" ? JSON.parse(init.body) : await input.clone().json();
        const slug = parsed.pathname.split("/")[3].toLowerCase();
        const selection = loadSelection(slug);
        if (selection?.positions?.length && Array.isArray(body?.answers) && !Array.isArray(body?.question_positions)) {
          return originalFetch(input, { ...(init || {}), body: JSON.stringify({ ...body, question_positions: selection.positions }) });
        }
      } catch {}
    }
    if (method === "POST" && parsed.pathname === "/api/account/claim-score") {
      try {
        const body = typeof init?.body === "string" ? JSON.parse(init.body) : await input.clone().json();
        const slug = String(body?.slug || getSlug()).toLowerCase();
        const selection = loadSelection(slug);
        if (slug && selection?.positions?.length && Array.isArray(body?.answers)) {
          const response = await originalFetch(`/api/quizzes/${encodeURIComponent(slug)}/score`, {
            method: "POST", credentials: "same-origin",
            headers: { "content-type": "application/json", accept: "application/json" },
            body: JSON.stringify({ answers: body.answers, question_positions: selection.positions }),
          });
          const payload = await response.json().catch(() => ({}));
          if (!response.ok) return new Response(JSON.stringify(payload), { status: response.status, headers: { "content-type": "application/json" } });
          const account = await originalFetch("/api/account", { credentials: "same-origin", headers: { accept: "application/json" }).then(result => result.json()).catch(() => ({}));
          return new Response(JSON.stringify({ ...payload, user: account?.user || null }), { status: response.status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" } });
        }
      } catch {}
    }
    if (method !== "GET" || !/^\/api\/quizzes\/[a-z0-9][a-z0-9-]{0,79}$/i.test(parsed.pathname)) return originalFetch(input, init);
    const slug = parsed.pathname.split("/").pop().toLowerCase();
    if (selectionReady) return originalFetch(input, init);
    const response = await originalFetch(input, init);
    if (!response.ok) return response;
    try {
      const payload = await response.clone().json();
      const quiz = payload?.quiz;
      if (!quiz || !Array.isArray(quiz.questions) || quiz.questions.length === 0) return response;
      await setupSelection(quiz, slug);
      const selection = loadSelection(slug);
      const positions = new Set(selection?.positions || quiz.questions.map(question => Number(question.position)));
      const questions = quiz.questions.filter(question => positions.has(Number(question.position)));
      return new Response(JSON.stringify({ ...payload, quiz: { ...quiz, questions } }), { status: response.status, statusText: response.statusText, headers: new Headers(response.headers) });
    } catch { return response; }
  };

  const leaderboardScript = document.createElement("script");
  leaderboardScript.src = "/quiz-leaderboard-length.js?v=1";
  document.head.append(leaderboardScript);
})();
