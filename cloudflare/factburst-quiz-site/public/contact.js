(() => {
  const form = document.querySelector("#contact-form");
  const topic = document.querySelector("#contact-topic");
  const quizFields = document.querySelector("#contact-quiz-fields");
  const status = document.querySelector("#contact-status");

  if (!form || !topic || !quizFields) return;

  function updateQuizFields() {
    const needsQuiz = ["quiz-problem", "question-correction"].includes(topic.value);
    quizFields.classList.toggle("hidden", !needsQuiz);
    for (const field of quizFields.querySelectorAll("input, textarea")) {
      field.required = needsQuiz && field.name === "quiz";
    }
  }

  topic.addEventListener("change", updateQuizFields);
  updateQuizFields();

  form.addEventListener("submit", event => {
    event.preventDefault();
    const data = new FormData(form);
    const subject = String(data.get("topic") || "General feedback");
    const name = String(data.get("name") || "").trim();
    const email = String(data.get("email") || "").trim();
    const message = String(data.get("message") || "").trim();
    const quiz = String(data.get("quiz") || "").trim();

    const body = [
      name ? `Name: ${name}` : "",
      email ? `Email: ${email}` : "",
      quiz ? `Quiz: ${quiz}` : "",
      "",
      message,
    ].filter(Boolean).join("\n");

    const mailto = `mailto:contact@factburstquiz.com?subject=${encodeURIComponent(`Factburst Quiz: ${subject}`)}&body=${encodeURIComponent(body)}`;
    window.location.href = mailto;
    status.textContent = "Your email app should now open with the message prepared. If it does not, email contact@factburstquiz.com directly.";
  });
})();
