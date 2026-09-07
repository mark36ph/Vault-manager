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

  const saveSelection = (slug, questions) => {
    const refs = questions.map(question => ({
      quiz_id: Number(question.quiz_id),
      position: Number(question.position),
    }));
    quizSelection = {
      slug,
      refs,
      positions: questions.map(question => Number(question.position)),
      count: questions.length,
    };
    window.factburstQuizSelection = quizSelection;
    try {
      sessionStorage.setItem(selectionKey, JSON.stringify({ ...quizSelection, created_at: Date.now() }));
    } catch {}
  };

  const loadSelection = slug => {
    if (quizSelection?.slug === slug && Array.isArray(quizSelection.refs)) return quizSelection;
    try {
      const value = JSON.parse(sessionStorage.getItem(selectionKey) || "null");
      if (value?.slug === slug && (Array.isArray(value.refs) || Array.isArray(value.positions))) {
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

  const createOptionValues = (baseCount, poolCount) => {
    const values = new Set();
    if (poolCount >= baseCount) values.add(baseCount);
    for (const value of [15, 20, 25, 30, 35, 40, 50]) {
      if (value > baseCount && value <= poolCount) values.add(value);
    }
    if (poolCount > baseCount && !values.has(poolCount)) values.add(poolCount);
    return [...values].sort((a, b) => a - b);
  };

  const fetchPool = async slug => {
    const response = await originalFetch(`/api/quizzes/${encodeURIComponent(slug)}/question-pool`, {
      credentials: "same-origin",
      headers: { accept: "application/json" },
      cache: "no-store",
    });
    if (!response.ok) return null;
    return response.json().catch(() => null);
  };

  const setupSelection = async (quiz, slug) => {
    const setup = document.querySelector("#quiz-setup");
    const select = document.querySelector("#quiz-question-count");
    const start = document.querySelector("#start-quiz");
    const copy = document.querySelector("#quiz-setup-copy");
    if (!setup || !select || !start) return { questions: quiz.questions, count: quiz.questions.length };

    const baseQuestions = quiz.questions.map(question => ({
      ...question,
      quiz_id: Number(question.quiz_id || quiz.id || 0),
    }));
    let pool = null;
    try {
      pool = await fetchPool(slug);
    } catch {}

    const poolQuestions = Array.isArray(pool?.questions) ? pool.questions : baseQuestions;
    const baseCount = baseQuestions.length;
    const poolCount = Math.max(baseCount, Number(pool?.question_count || poolQuestions.length || baseCount));
    const options = createOptionValues(baseCount, poolCount);
    const previous = loadSelection(slug);
    const previousCount = Number(previous?.refs?.length || previous?.positions?.length || 0);

    setup.classList.remove("hidden");
    select.replaceChildren();
    for (const value of options) {
      const option = document.createElement("option");
      option.value = String(value);
      option.textContent = value === baseCount
        ? `Standard ${value}-question quiz`
        : (value === poolCount ? `All ${value} ${String(quiz.category || "").toLowerCase()} questions` : `${value} questions from ${quiz.category || "this category"}`);
      select.append(option);
    }
    select.value = String(options.includes(previousCount) ? previousCount : baseCount);

    if (copy) {
      copy.textContent = poolCount > baseCount
        ? `This quiz has ${baseCount} questions. The standard game uses those questions. Choose more to draw additional questions from the ${quiz.category || "same"} category in the database.`
        : `This quiz has ${baseCount} questions. The standard game uses all of them.`;
    }

    return new Promise(resolve => {
      selectionReady = resolve;
      start.onclick = () => {
        const count = Math.max(baseCount, Math.min(Number(select.value), poolCount));
        const selected = count === baseCount
          ? shuffle(baseQuestions).slice(0, baseCount)
          : shuffle(poolQuestions).slice(0, count);
        if (selected.length < count) return;
        saveSelection(slug, selected);
        window.factburstQuizTotal = poolCount;
        setup.classList.add("hidden");
        resolve({ questions: selected, count });
        selectionReady = null;
        window.dispatchEvent(new CustomEvent("factburst:quiz-selection-ready", { detail: { count } }));
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
        if (selection?.refs?.length && Array.isArray(body?.answers) && !Array.isArray(body?.question_refs)) {
          return originalFetch(input, {
            ...(init || {}),
            body: JSON.stringify({ ...body, question_refs: selection.refs }),
          });
        }
      } catch {}
    }

    if (method === "POST" && parsed.pathname === "/api/account/claim-score") {
      try {
        const body = typeof init?.body === "string" ? JSON.parse(init.body) : await input.clone().json();
        const slug = String(body?.slug || getSlug()).toLowerCase();
        const selection = loadSelection(slug);
        if (slug && selection?.refs?.length && Array.isArray(body?.answers)) {
          const response = await originalFetch(`/api/quizzes/${encodeURIComponent(slug)}/score`, {
            method: "POST",
            credentials: "same-origin",
            headers: { "content-type": "application/json", accept: "application/json" },
            body: JSON.stringify({ answers: body.answers, question_refs: selection.refs }),
          });
          const payload = await response.json().catch(() => ({}));
          if (!response.ok) return new Response(JSON.stringify(payload), { status: response.status, headers: { "content-type": "application/json" } });
          const account = await originalFetch("/api/account", {
            credentials: "same-origin",
            headers: { accept: "application/json" },
          }).then(result => result.json()).catch(() => ({}));
          return new Response(JSON.stringify({ ...payload, user: account?.user || null }), {
            status: response.status,
            headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
          });
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
      const selection = await setupSelection(quiz, slug);
      const questions = Array.isArray(selection?.questions) && selection.questions.length ? selection.questions : quiz.questions;
      return new Response(JSON.stringify({ ...payload, quiz: { ...quiz, questions } }), {
        status: response.status,
        statusText: response.statusText,
        headers: new Headers(response.headers),
      });
    } catch {
      return response;
    }
  };

  const loadLeaderboardScript = () => {
    if (!document.querySelector('link[data-factburst-leaderboard-style]')) {
      const style = document.createElement("link");
      style.rel = "stylesheet";
      style.href = "/quiz-leaderboard-length.css?v=1";
      style.dataset.factburstLeaderboardStyle = "1";
      document.head.append(style);
    }
    const script = document.createElement("script");
    script.src = "/quiz-leaderboard-length.js?v=2";
    document.body.append(script);
  };

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", loadLeaderboardScript, { once: true });
  else loadLeaderboardScript();
})();
