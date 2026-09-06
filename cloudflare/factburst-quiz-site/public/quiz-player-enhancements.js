(() => {
  const player = document.querySelector("#quiz-player");
  const answerList = document.querySelector("#answer-list");
  const next = document.querySelector("#next-question");
  const progressText = document.querySelector("#quiz-progress-text");
  const questionText = document.querySelector("#question-text");

  if (!player || !answerList || !next) return;

  let lastQuestionText = "";

  const enhanceAnswers = () => {
    const buttons = [...answerList.querySelectorAll(".answer-button")];
    buttons.forEach((button, index) => {
      button.setAttribute("aria-pressed", button.classList.contains("selected") ? "true" : "false");
      button.setAttribute("aria-label", `${String.fromCharCode(65 + index)}. ${button.textContent.trim()}`);
      button.title = `Choose answer ${String.fromCharCode(65 + index)}`;
    });

    const selected = buttons.find((button) => button.classList.contains("selected"));
    if (selected) {
      buttons.forEach((button) => button.setAttribute("aria-pressed", button === selected ? "true" : "false"));
    }
  };

  const animateQuestion = () => {
    if (!questionText) return;
    const current = questionText.textContent.trim();
    if (!current || current === lastQuestionText) return;
    lastQuestionText = current;
    player.classList.remove("quiz-question-enter");
    requestAnimationFrame(() => player.classList.add("quiz-question-enter"));
  };

  const observeAnswers = new MutationObserver(() => {
    enhanceAnswers();
    animateQuestion();
  });
  observeAnswers.observe(answerList, { childList: true, subtree: true, attributes: true, attributeFilter: ["class"] });

  answerList.addEventListener("click", (event) => {
    const button = event.target.closest(".answer-button");
    if (!button) return;
    requestAnimationFrame(() => {
      const buttons = answerList.querySelectorAll(".answer-button");
      buttons.forEach((item) => item.setAttribute("aria-pressed", item === button ? "true" : "false"));
      button.focus({ preventScroll: true });
    });
  });

  document.addEventListener("keydown", (event) => {
    if (player.classList.contains("hidden")) return;
    if (event.ctrlKey || event.metaKey || event.altKey) return;

    const buttons = [...answerList.querySelectorAll(".answer-button")];
    if (event.key >= "1" && event.key <= "4") {
      const button = buttons[Number(event.key) - 1];
      if (button) {
        event.preventDefault();
        button.click();
      }
      return;
    }

    if (event.key.toLowerCase() === "n" && !next.disabled) {
      event.preventDefault();
      next.click();
    }
  });

  next.addEventListener("click", () => {
    next.classList.add("quiz-next-pressed");
    window.setTimeout(() => next.classList.remove("quiz-next-pressed"), 180);
  });

  const progressObserver = new MutationObserver(() => {
    if (progressText) progressText.setAttribute("aria-live", "polite");
    animateQuestion();
  });
  if (progressText) progressObserver.observe(progressText, { childList: true, characterData: true, subtree: true });

  enhanceAnswers();
  animateQuestion();
})();
