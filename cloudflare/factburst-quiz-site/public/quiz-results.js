(() => {
  const results = document.querySelector("#quiz-results");
  if (!results) return;

  const getSlug = () => {
    const match = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})\/?$/i);
    if (match) return match[1].toLowerCase();
    const value = new URLSearchParams(location.search).get("slug") || "";
    return /^[a-z0-9][a-z0-9-]{0,79}$/i.test(value) ? value.toLowerCase() : "";
  };

  const enhance = async () => {
    if (results.classList.contains("hidden") || results.dataset.enhanced === "true") return;
    results.dataset.enhanced = "true";

    const score = document.querySelector("#result-score");
    const percentage = document.querySelector("#result-percentage");
    const heading = document.querySelector("#result-heading");
    const copy = document.querySelector("#result-copy");
    const playAgain = document.querySelector("#play-again");
    const nextQuiz = document.querySelector("#next-quiz");

    if (score && percentage) {
      const match = score.textContent.match(/(\d+)\s*\/\s*(\d+)/);
      if (match) {
        const correct = Number(match[1]);
        const total = Number(match[2]);
        const percent = total > 0 ? Math.round((correct / total) * 100) : 0;
        percentage.textContent = `${percent}%`;
        percentage.setAttribute("aria-label", `${percent}% correct`);

        if (copy && total !== 10 && /10\/10/.test(copy.textContent)) {
          copy.textContent = `You got every question right. That is a ${correct}/${total} performance.`;
        }
      }
    }

    if (playAgain) {
      playAgain.addEventListener("click", () => {
        const url = new URL(location.href);
        url.searchParams.set("restart", Date.now().toString());
        location.replace(url.pathname + url.search);
      }, { once: true });
    }

    if (!nextQuiz) return;
    nextQuiz.textContent = "Finding next quiz…";
    nextQuiz.setAttribute("aria-busy", "true");

    try {
      const currentSlug = getSlug();
      const category = document.querySelector("#quiz-category")?.textContent?.trim() || "";
      const params = new URLSearchParams({ limit: "6" });
      if (category && category.toLowerCase() !== "quiz") params.set("category", category);

      const response = await fetch(`/api/quizzes?${params.toString()}`, { headers: { accept: "application/json" } });
      if (!response.ok) throw new Error("Quiz list unavailable");
      const payload = await response.json();
      const quizzes = Array.isArray(payload.quizzes) ? payload.quizzes : [];
      const candidate = quizzes.find((quiz) => quiz?.slug && String(quiz.slug).toLowerCase() !== currentSlug);

      if (candidate?.slug) {
        nextQuiz.href = `/quiz/${encodeURIComponent(String(candidate.slug).toLowerCase())}`;
        nextQuiz.textContent = category ? "Next quiz" : "Next challenge";
        nextQuiz.classList.remove("hidden");
      } else {
        nextQuiz.href = "/quizzes.html";
        nextQuiz.textContent = "Browse quizzes";
      }
    } catch {
      nextQuiz.href = "/quizzes.html";
      nextQuiz.textContent = "Browse quizzes";
    } finally {
      nextQuiz.removeAttribute("aria-busy");
    }
  };

  const observer = new MutationObserver(enhance);
  observer.observe(results, { attributes: true, attributeFilter: ["class"] });
  enhance();
})();
