(() => {
  "use strict";
  const content = document.querySelector("#content");
  const slug = new URLSearchParams(location.search).get("slug");
  const escapeHtml = value => String(value ?? "").replace(/[&<>'"]/g, char => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[char]));
  if (!slug) { content.innerHTML = '<div class="error">No quiz was selected for preview.</div>'; return; }
  async function load() {
    try {
      const response = await fetch(`/api/admin/quizzes/${encodeURIComponent(slug)}`, { credentials: "same-origin", cache: "no-store" });
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || "The quiz could not be loaded.");
      const quiz = data.quiz;
      const questions = Array.isArray(quiz?.questions) ? quiz.questions : [];
      content.innerHTML = `<p class="eyebrow">Player preview</p><h1>${escapeHtml(quiz.title || "Quiz")}</h1><div class="meta"><span class="pill">${escapeHtml(quiz.category || "Uncategorised")}</span><span class="pill">${questions.length} questions</span><span class="pill">${escapeHtml(quiz.status || "draft")}</span></div><p>${escapeHtml(quiz.description || "Preview this quiz exactly as the player content will appear.")}</p><div id="questions"></div><div class="actions"><a class="button secondary" href="/admin#quizzes">Back to admin</a><a class="button primary" href="/quiz/${encodeURIComponent(quiz.slug)}">Open player page</a></div>`;
      const list = document.querySelector("#questions");
      if (!questions.length) { list.innerHTML = '<div class="error">This quiz has no questions yet.</div>'; return; }
      list.innerHTML = questions.map((q, index) => `<article class="question"><p class="eyebrow">Question ${index + 1}</p><h2>${escapeHtml(q.question)}</h2><div class="answers">${(q.answers || []).map((answer, i) => `<div class="answer ${String.fromCharCode(65+i) === q.correct_answer ? "correct" : ""}"><strong>${String.fromCharCode(65+i)}.</strong> ${escapeHtml(answer)}</div>`).join("")}</div>${q.explanation ? `<p class="explanation"><strong>Explanation:</strong> ${escapeHtml(q.explanation)}</p>` : ""}</article>`).join("");
    } catch (error) { content.innerHTML = `<div class="error">${escapeHtml(error.message || "Preview failed.")}<div class="actions"><a class="button secondary" href="/admin#quizzes">Back to admin</a></div></div>`; }
  }
  load();
})();
