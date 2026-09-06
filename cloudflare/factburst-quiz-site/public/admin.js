(() => {
  "use strict";

  const KEY_STORAGE = "factburst_admin_session_key";
  const state = { key: sessionStorage.getItem(KEY_STORAGE) || "", quizzes: [], editingSlug: "" };
  const $ = selector => document.querySelector(selector);
  const escapeHtml = value => String(value ?? "").replace(/[&<>'"]/g, char => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[char]));

  const loginPanel = $("#login-panel");
  const app = $("#admin-app");
  const loginForm = $("#login-form");
  const loginKey = $("#admin-key");
  const loginStatus = $("#login-status");
  const appStatus = $("#app-status");
  const saveStatus = $("#save-status");
  const quizList = $("#quiz-list");
  const emptyList = $("#quiz-list-empty");
  const editor = $("#editor-panel");
  const form = $("#quiz-form");
  const questionsEditor = $("#questions-editor");
  const questionCount = $("#question-count");

  function setStatus(element, message, type = "") {
    element.textContent = message;
    element.className = `admin-status ${type}`.trim();
  }

  function headers() {
    return { Authorization: `Bearer ${state.key}`, "Content-Type": "application/json" };
  }

  async function api(path, options = {}) {
    const response = await fetch(path, { ...options, headers: { ...headers(), ...(options.headers || {}) } });
    let data = null;
    try { data = await response.json(); } catch {}
    if (!response.ok) {
      const error = new Error(data?.error || `Request failed (${response.status}).`);
      error.status = response.status;
      throw error;
    }
    return data;
  }

  function showApp() {
    loginPanel.classList.add("hidden");
    app.classList.remove("hidden");
    $("#sign-out").classList.remove("hidden");
  }

  function showLogin() {
    loginPanel.classList.remove("hidden");
    app.classList.add("hidden");
    editor.classList.add("hidden");
    $("#sign-out").classList.add("hidden");
  }

  function signOut() {
    state.key = "";
    sessionStorage.removeItem(KEY_STORAGE);
    showLogin();
    loginKey.value = "";
    setStatus(loginStatus, "Signed out.");
  }

  function formatDate(value) {
    if (!value) return "Not scheduled";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "Invalid date";
    return date.toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
  }

  function toDateTimeLocal(value) {
    if (!value) return "";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";
    const pad = number => String(number).padStart(2, "0");
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }

  function toIso(value) {
    if (!value) return null;
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }

  function renderList() {
    emptyList.classList.toggle("hidden", state.quizzes.length > 0);
    quizList.innerHTML = state.quizzes.map(quiz => `
      <article class="admin-quiz-row">
        <div>
          <div class="admin-quiz-title">${escapeHtml(quiz.title || quiz.slug)}</div>
          <div class="admin-quiz-meta">
            <span class="admin-badge ${quiz.status === "published" ? "published" : "draft"}">${escapeHtml(quiz.status)}</span>
            <span>${escapeHtml(quiz.category || "Uncategorised")}</span>
            <span>${Number(quiz.question_count || 0)} questions</span>
            <span>${quiz.status === "published" && quiz.publish_at ? escapeHtml(formatDate(quiz.publish_at)) : quiz.status === "published" ? "Live now" : quiz.publish_at ? `Scheduled ${escapeHtml(formatDate(quiz.publish_at))}` : "Not scheduled"}</span>
          </div>
        </div>
        <div class="admin-row-actions"><button class="button button-secondary" type="button" data-edit="${escapeHtml(quiz.slug)}">Edit</button></div>
      </article>`).join("");
  }

  function blankQuestion() {
    return { question: "", answers: ["", "", "", ""], correct_answer: "A", explanation: "", image_data_url: "", image_key: "" };
  }

  function renderQuestions(questions) {
    questionsEditor.innerHTML = "";
    questions.forEach((question, index) => addQuestionCard(question, index + 1, false));
    updateQuestionCount();
  }

  function addQuestionCard(question = blankQuestion(), number = questionsEditor.children.length + 1, focus = true) {
    const details = document.createElement("details");
    details.className = "question-card";
    details.open = true;
    details.dataset.imageKey = question.image_key || "";
    details.innerHTML = `
      <summary><div class="question-summary"><strong>Question <span class="question-number">${number}</span></strong><span class="question-summary-text">${escapeHtml(question.question || "New question")}</span></div></summary>
      <div class="question-body">
        <label>Question<textarea class="q-text" rows="3" required maxlength="500">${escapeHtml(question.question || "")}</textarea></label>
        <div class="answers-grid">
          ${[0,1,2,3].map(i => `<label class="answer-label"><span>${String.fromCharCode(65+i)}</span>Answer<input class="q-answer" data-index="${i}" required maxlength="300" value="${escapeHtml(question.answers?.[i] || "")}"></label>`).join("")}
        </div>
        <label>Correct answer<select class="q-correct"><option value="A" ${question.correct_answer === "A" ? "selected" : ""}>A</option><option value="B" ${question.correct_answer === "B" ? "selected" : ""}>B</option><option value="C" ${question.correct_answer === "C" ? "selected" : ""}>C</option><option value="D" ${question.correct_answer === "D" ? "selected" : ""}>D</option></select></label>
        <label>Explanation<textarea class="q-explanation" rows="3" maxlength="800" placeholder="Optional explanation shown after the result.">${escapeHtml(question.explanation || "")}</textarea></label>
        <div class="question-image">
          ${question.image_key ? `<img src="/${escapeHtml(question.image_key).replace(/^\/+/, "")}" alt="Existing question image"><span class="image-note">Existing image will be kept unless you choose a replacement.</span>` : `<span class="image-note">No question image.</span>`}
          <label>Replace image (PNG)<input class="q-image" type="file" accept="image/png"></label>
        </div>
        <div class="question-tools"><span class="image-note">Question ${number} of up to 100</span><button class="button button-ghost danger-button remove-question" type="button">Remove</button></div>
      </div>`;
    questionsEditor.appendChild(details);

    const text = details.querySelector(".q-text");
    const summary = details.querySelector(".question-summary-text");
    text.addEventListener("input", () => { summary.textContent = text.value.trim() || "New question"; });
    details.querySelector(".remove-question").addEventListener("click", () => {
      if (questionsEditor.children.length <= 1) {
        setStatus(saveStatus, "A quiz needs at least one question.", "error");
        return;
      }
      details.remove();
      renumberQuestions();
    });
    if (focus) text.focus();
  }

  function renumberQuestions() {
    [...questionsEditor.children].forEach((card, index) => {
      card.querySelector(".question-number").textContent = String(index + 1);
      const note = card.querySelector(".question-tools .image-note");
      if (note) note.textContent = `Question ${index + 1} of up to 100`;
    });
    updateQuestionCount();
  }

  function updateQuestionCount() {
    const count = questionsEditor.children.length;
    questionCount.textContent = `${count} question${count === 1 ? "" : "s"}`;
  }

  async function fileToDataUrl(file) {
    if (!file) return "";
    if (file.type !== "image/png") throw new Error("Question images must be PNG files.");
    if (file.size > 900000) throw new Error("Please use a PNG image smaller than 900 KB.");
    return await new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result || ""));
      reader.onerror = () => reject(new Error("Could not read the image."));
      reader.readAsDataURL(file);
    });
  }

  async function collectQuestions() {
    const cards = [...questionsEditor.children];
    const questions = [];
    for (const [index, card] of cards.entries()) {
      const question = card.querySelector(".q-text").value.trim();
      const answers = [...card.querySelectorAll(".q-answer")].map(input => input.value.trim());
      const correct = card.querySelector(".q-correct").value;
      const explanation = card.querySelector(".q-explanation").value.trim();
      const file = card.querySelector(".q-image").files?.[0];
      if (!question) throw new Error(`Question ${index + 1} is blank.`);
      if (answers.some(answer => !answer) || new Set(answers).size !== 4) throw new Error(`Question ${index + 1} must have four distinct answers.`);
      const item = { question, answers, correct_answer: correct, explanation, image_data_url: "", image_key: card.dataset.imageKey || "" };
      if (file) {
        item.image_data_url = await fileToDataUrl(file);
        item.image_key = "";
      }
      questions.push(item);
    }
    return questions;
  }

  function openEditor(quiz = null) {
    state.editingSlug = quiz?.slug || "";
    $("#editor-title").textContent = quiz ? `Edit: ${quiz.title}` : "New quiz";
    $("#quiz-title").value = quiz?.title || "";
    $("#quiz-slug").value = quiz?.slug || "";
    $("#quiz-category").value = quiz?.category || "General Knowledge";
    $("#quiz-status").value = quiz?.status || "draft";
    $("#quiz-publish-at").value = toDateTimeLocal(quiz?.publish_at);
    $("#quiz-youtube").value = quiz?.youtube_url || "";
    $("#quiz-description").value = quiz?.description || "";
    renderQuestions(quiz?.questions?.length ? quiz.questions : [blankQuestion()]);
    setStatus(saveStatus, "");
    editor.classList.remove("hidden");
    editor.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  async function editQuiz(slug) {
    try {
      setStatus(appStatus, "Loading quiz…");
      const data = await api(`/api/admin/quizzes/${encodeURIComponent(slug)}`);
      setStatus(appStatus, "");
      openEditor(data.quiz);
    } catch (error) {
      if (error.status === 401) return signOut();
      setStatus(appStatus, error.message, "error");
    }
  }

  async function loadQuizzes() {
    setStatus(appStatus, "Loading quizzes…");
    try {
      const data = await api("/api/admin/quizzes");
      state.quizzes = data.quizzes || [];
      renderList();
      setStatus(appStatus, `${state.quizzes.length} quiz${state.quizzes.length === 1 ? "" : "zes"} loaded.`, "success");
    } catch (error) {
      if (error.status === 401) return signOut();
      setStatus(appStatus, error.message, "error");
    }
  }

  loginForm.addEventListener("submit", async event => {
    event.preventDefault();
    const key = loginKey.value.trim();
    if (!key) return;
    state.key = key;
    setStatus(loginStatus, "Checking key…");
    try {
      const response = await fetch("/api/admin/quizzes", { headers: { Authorization: `Bearer ${key}` } });
      if (!response.ok) throw new Error(response.status === 401 ? "That publishing key was not accepted." : `Could not connect (${response.status}).`);
      sessionStorage.setItem(KEY_STORAGE, key);
      loginKey.value = "";
      setStatus(loginStatus, "");
      showApp();
      await loadQuizzes();
    } catch (error) {
      state.key = "";
      setStatus(loginStatus, error.message, "error");
    }
  });

  $("#sign-out").addEventListener("click", signOut);
  $("#refresh-quizzes").addEventListener("click", loadQuizzes);
  $("#new-quiz").addEventListener("click", () => openEditor());
  $("#close-editor").addEventListener("click", () => editor.classList.add("hidden"));
  $("#cancel-edit").addEventListener("click", () => editor.classList.add("hidden"));
  $("#add-question").addEventListener("click", () => {
    if (questionsEditor.children.length >= 100) return setStatus(saveStatus, "A quiz can contain up to 100 questions.", "error");
    addQuestionCard();
    renumberQuestions();
  });
  quizList.addEventListener("click", event => {
    const button = event.target.closest("[data-edit]");
    if (button) editQuiz(button.dataset.edit);
  });

  form.addEventListener("submit", async event => {
    event.preventDefault();
    const saveButton = $("#save-quiz");
    saveButton.disabled = true;
    setStatus(saveStatus, "Saving quiz…");
    try {
      const questions = await collectQuestions();
      const slug = $("#quiz-slug").value.trim().toLowerCase();
      const title = $("#quiz-title").value.trim();
      const category = $("#quiz-category").value.trim();
      const status = $("#quiz-status").value;
      if (!slug || !title || !category) throw new Error("Title, slug and category are required.");
      const youtube = $("#quiz-youtube").value.trim();
      if (youtube && !/^https:\/\/(www\.)?(youtube\.com|youtu\.be)\//i.test(youtube)) throw new Error("YouTube URL must use an HTTPS YouTube address.");
      const payload = {
        slug,
        title,
        category,
        description: $("#quiz-description").value.trim(),
        youtube_url: youtube,
        publish_at: toIso($("#quiz-publish-at").value),
        status,
        questions,
      };
      await api("/api/admin/quizzes", { method: "POST", body: JSON.stringify(payload) });
      setStatus(saveStatus, "Quiz saved successfully.", "success");
      state.editingSlug = slug;
      await loadQuizzes();
      const refreshed = await api(`/api/admin/quizzes/${encodeURIComponent(slug)}`);
      openEditor(refreshed.quiz);
      setStatus(saveStatus, "Quiz saved successfully.", "success");
    } catch (error) {
      if (error.status === 401) return signOut();
      setStatus(saveStatus, error.message, "error");
    } finally {
      saveButton.disabled = false;
    }
  });

  async function start() {
    if (!state.key) return showLogin();
    showApp();
    await loadQuizzes();
  }

  start();
})();
