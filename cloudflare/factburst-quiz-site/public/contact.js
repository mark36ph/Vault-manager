(() => {
  const form = document.querySelector("#contact-form");
  const topic = document.querySelector("#contact-topic");
  const quizFields = document.querySelector("#contact-quiz-fields");
  const formSection = document.querySelector("#contact-form-section");
  const optionsSection = document.querySelector("#contact-options-section");
  const backButton = document.querySelector("#contact-back");
  const message = document.querySelector("#contact-message");
  const status = document.querySelector("#contact-status");

  if (!form || !topic || !quizFields || !formSection) return;

  function updateQuizFields() {
    const needsQuiz = ["quiz-problem", "question-correction", "quiz-comment"].includes(topic.value);
    quizFields.classList.toggle("hidden", !needsQuiz);
    const quizInput = quizFields.querySelector("input[name='quiz']");
    if (quizInput) quizInput.required = ["quiz-problem", "question-correction"].includes(topic.value);
  }

  function openForm(selectedTopic) {
    topic.value = selectedTopic || "feedback";
    updateQuizFields();
    formSection.classList.remove("hidden");
    formSection.scrollIntoView({ behavior: "smooth", block: "start" });
    window.setTimeout(() => {
      if (topic) topic.focus({ preventScroll: true });
    }, 250);
  }

  function closeForm() {
    formSection.classList.add("hidden");
    if (optionsSection) optionsSection.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  document.querySelectorAll("[data-contact-topic]").forEach(button => {
    button.addEventListener("click", () => openForm(button.dataset.contactTopic));
  });

  topic.addEventListener("change", updateQuizFields);
  if (backButton) backButton.addEventListener("click", closeForm);
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
      quiz ? `Quiz / question: ${quiz}` : "",
      "",
      messageText,
    ].filter(Boolean).join("\n");

    status.textContent = "Opening your email app with the message prepared...";
    const mailto = `mailto:contact@factburstquiz.com?subject=${encodeURIComponent(`Factburst Quiz: ${subject}`)}&body=${encodeURIComponent(body)}`;
    window.location.href = mailto;
  });
})();
