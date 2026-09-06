(() => {
  const form = document.querySelector("#contact-form");
  const formSection = document.querySelector("#contact-form-section");
  const topic = document.querySelector("#contact-topic");
  const quizFields = document.querySelector("#contact-quiz-fields");
  const status = document.querySelector("#contact-status");
  const message = document.querySelector("#contact-message");

  if (!form || !formSection || !topic || !quizFields) return;

  function updateQuizFields() {
    const needsQuiz = ["quiz-problem", "question-correction", "quiz-feedback"].includes(topic.value);
    quizFields.hidden = !needsQuiz;
    const quiz = quizFields.querySelector("input[name=quiz]");
    if (quiz) quiz.required = needsQuiz;
  }

  function openContactForm(selectedTopic) {
    topic.value = selectedTopic;
    updateQuizFields();
    formSection.hidden = false;
    formSection.scrollIntoView({ behavior: "smooth", block: "start" });
    window.setTimeout(() => {
      if (topic.value === "quiz-problem" || topic.value === "question-correction" || topic.value === "quiz-feedback") {
        const quiz = document.querySelector("#contact-quiz");
        if (quiz) quiz.focus({ preventScroll: true });
      } else if (message) {
        message.focus({ preventScroll: true });
      }
    }, 250);
  }

  document.querySelectorAll("[data-contact-topic]").forEach(card => {
    card.addEventListener("click", () => openContactForm(card.dataset.contactTopic));
  });

  topic.addEventListener("change", updateQuizFields);
  updateQuizFields();

  form.addEventListener("submit", event => {
    event.preventDefault();
    if (!form.reportValidity()) return;

    const data = new FormData(form);
    const subject = String(data.get("topic") || "General feedback");
    const name = String(data.get("name") || "").trim();
    const email = String(data.get("email") || "").trim();
    const messageText = String(data.get("message") || "").trim();
    const quiz = String(data.get("quiz") || "").trim();

    const body = [
      name ? `Name: ${name}` : "",
      email ? `Email: ${email}` : "",
      quiz ? `Quiz: ${quiz}` : "",
      "",
      messageText,
    ].filter(Boolean).join("\n");

    status.textContent = "Opening your email app with the message prepared…";
    const mailto = `mailto:contact@factburstquiz.com?subject=${encodeURIComponent(`Factburst Quiz: ${subject}`)}&body=${encodeURIComponent(body)}`;
    window.location.href = mailto;
  });
})();
