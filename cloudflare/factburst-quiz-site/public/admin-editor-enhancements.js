(() => {
  "use strict";

  const editor = document.querySelector("#questions-editor");
  if (!editor) return;

  const toolbar = document.querySelector(".question-toolbar-actions");
  const expandButton = document.querySelector("#expand-all-questions");
  const collapseButton = document.querySelector("#collapse-all-questions");
  const saveStatus = document.querySelector("#save-status");

  function setStatus(message, type = "") {
    if (!saveStatus) return;
    saveStatus.textContent = message;
    saveStatus.className = `admin-status ${type}`.trim();
  }

  function getQuestionText(card) {
    return card.querySelector(".q-text")?.value.trim() || "";
  }

  function getAnswers(card) {
    return [...card.querySelectorAll(".q-answer")].map(input => input.value.trim());
  }

  function validateQuestions() {
    const cards = [...editor.children];
    const seen = new Map();
    const issues = [];

    cards.forEach((card, index) => {
      card.classList.remove("admin-question-invalid");
      const question = getQuestionText(card);
      const answers = getAnswers(card);
      const correct = card.querySelector(".q-correct")?.value?.trim() ?? "";
      const cardIssues = [];

      if (!question) cardIssues.push("missing question");
      if (answers.length === 0 || answers.some(answer => !answer)) cardIssues.push("missing answer");
      const nonEmpty = answers.filter(Boolean).map(answer => answer.toLowerCase());
      if (new Set(nonEmpty).size !== nonEmpty.length) cardIssues.push("duplicate answers");
      if (!correct) cardIssues.push("no correct answer");

      const key = question.toLowerCase();
      if (key) {
        if (seen.has(key)) {
          cardIssues.push(`duplicate question (same as Q${seen.get(key)})`);
          const first = cards[seen.get(key) - 1];
          first?.classList.add("admin-question-invalid");
        } else {
          seen.set(key, index + 1);
        }
      }

      if (cardIssues.length) {
        card.classList.add("admin-question-invalid");
        issues.push({ index: index + 1, text: cardIssues.join(", ") });
      }
    });

    return issues;
  }

  function updateValidation() {
    const panel = document.querySelector("#admin-validation-panel");
    if (!panel) return;
    const issues = validateQuestions();
    const summary = panel.querySelector(".admin-validation-summary");
    const list = panel.querySelector(".admin-validation-list");
    if (issues.length === 0) {
      panel.classList.remove("has-errors");
      if (summary) summary.textContent = "✓ Ready to publish — no question issues found.";
      if (list) list.innerHTML = "";
      return;
    }
    panel.classList.add("has-errors");
    if (summary) summary.textContent = `${issues.length} question${issues.length === 1 ? "" : "s"} need attention.`;
    if (list) {
      list.innerHTML = issues.map(issue => `<button type="button" class="admin-validation-item" data-question-index="${issue.index}"><strong>Q${issue.index}</strong> — ${issue.text}</button>`).join("");
    }
  }

  function renumber() {
    [...editor.children].forEach((card, index) => {
      const number = card.querySelector(".question-number");
      const note = card.querySelector(".question-tools .image-note");
      if (number) number.textContent = String(index + 1);
      if (note) note.textContent = `Question ${index + 1} of up to 100`;
    });
    const count = document.querySelector("#question-count");
    if (count) {
      const total = editor.children.length;
      count.textContent = `${total} question${total === 1 ? "" : "s"}`;
    }
    updateValidation();
  }

  function removeCard(card) {
    if (editor.children.length <= 1) {
      setStatus("A quiz needs at least one question.", "error");
      return;
    }
    card.remove();
    renumber();
    setStatus("Question removed. Save the quiz to keep this change.");
  }

  function duplicateCard(card) {
    const clone = card.cloneNode(true);
    clone.dataset.imageKey = card.dataset.imageKey || "";
    clone.open = true;

    const sourceText = card.querySelector(".q-text");
    const cloneText = clone.querySelector(".q-text");
    if (sourceText && cloneText) cloneText.value = sourceText.value;

    card.querySelectorAll(".q-answer").forEach((input, index) => {
      const target = clone.querySelectorAll(".q-answer")[index];
      if (target) target.value = input.value;
    });

    const sourceCorrect = card.querySelector(".q-correct");
    const cloneCorrect = clone.querySelector(".q-correct");
    if (sourceCorrect && cloneCorrect) cloneCorrect.value = sourceCorrect.value;

    const sourceExplanation = card.querySelector(".q-explanation");
    const cloneExplanation = clone.querySelector(".q-explanation");
    if (sourceExplanation && cloneExplanation) cloneExplanation.value = sourceExplanation.value;

    const oldFile = clone.querySelector(".q-image");
    if (oldFile) oldFile.value = "";

    const summary = clone.querySelector(".question-summary-text");
    if (summary) summary.textContent = cloneText?.value.trim() || "Copied question";

    card.after(clone);
    enhanceCard(clone);
    renumber();
    setStatus("Question duplicated. Edit it as needed, then save the quiz.");
    clone.scrollIntoView({ behavior: "smooth", block: "center" });
  }

  function enhanceCard(card) {
    card.draggable = true;
    const summary = card.querySelector("summary");
    const text = card.querySelector(".q-text");
    const summaryText = card.querySelector(".question-summary-text");

    if (text && summaryText && !text.dataset.enhanced) {
      text.dataset.enhanced = "1";
      text.addEventListener("input", () => {
        summaryText.textContent = text.value.trim() || "New question";
        updateValidation();
      });
    }

    card.querySelectorAll("input, textarea, select").forEach(input => {
      if (!input.dataset.validationEnhanced) {
        input.dataset.validationEnhanced = "1";
        input.addEventListener("input", updateValidation);
        input.addEventListener("change", updateValidation);
      }
    });

    if (!summary) return;
    let main = summary.querySelector(".question-summary-main");
    if (!main) {
      main = document.createElement("div");
      main.className = "question-summary-main";
      while (summary.firstChild) main.appendChild(summary.firstChild);
      summary.appendChild(main);
    }

    if (!main.querySelector(".question-drag-handle")) {
      const handle = document.createElement("span");
      handle.className = "question-drag-handle";
      handle.textContent = "⋮⋮";
      handle.title = "Drag to reorder";
      handle.setAttribute("aria-hidden", "true");
      main.insertBefore(handle, main.firstChild);
    }

    let actions = summary.querySelector(".question-actions");
    if (!actions) {
      actions = document.createElement("span");
      actions.className = "question-actions";
      summary.appendChild(actions);
    }
    if (!actions.querySelector(".duplicate-question")) {
      const duplicate = document.createElement("button");
      duplicate.type = "button";
      duplicate.className = "button button-ghost question-action duplicate-question";
      duplicate.textContent = "Duplicate";
      duplicate.title = "Duplicate this question";
      actions.insertBefore(duplicate, actions.firstChild);
    }
  }

  function enhanceExisting() {
    [...editor.children].forEach(enhanceCard);
    renumber();
  }

  function installAdminTools() {
    if (!toolbar || document.querySelector("#admin-question-tools")) return;
    const tools = document.createElement("div");
    tools.id = "admin-question-tools";
    tools.innerHTML = `
      <div class="admin-question-search-row">
        <input id="admin-question-search" type="search" placeholder="Search questions…" aria-label="Search questions">
        <button type="button" class="button button-ghost" id="admin-clear-question-search">Clear</button>
      </div>
      <div id="admin-validation-panel" class="admin-validation-panel" aria-live="polite">
        <div class="admin-validation-summary">Checking questions…</div>
        <div class="admin-validation-list"></div>
      </div>`;
    toolbar.after(tools);

    const search = tools.querySelector("#admin-question-search");
    const clear = tools.querySelector("#admin-clear-question-search");
    search?.addEventListener("input", () => {
      const term = search.value.trim().toLowerCase();
      [...editor.children].forEach(card => {
        const haystack = [getQuestionText(card), ...getAnswers(card)].join(" ").toLowerCase();
        card.hidden = Boolean(term && !haystack.includes(term));
      });
    });
    clear?.addEventListener("click", () => {
      search.value = "";
      [...editor.children].forEach(card => { card.hidden = false; });
      search.focus();
    });

    tools.querySelector(".admin-validation-list")?.addEventListener("click", event => {
      const button = event.target.closest(".admin-validation-item");
      if (!button) return;
      const index = Number(button.dataset.questionIndex);
      const card = editor.children[index - 1];
      if (!card) return;
      card.hidden = false;
      card.open = true;
      card.scrollIntoView({ behavior: "smooth", block: "center" });
    });

    const style = document.createElement("style");
    style.textContent = `
      #admin-question-tools { margin: 12px 0 16px; }
      .admin-question-search-row { display:flex; gap:8px; align-items:center; margin-bottom:10px; }
      #admin-question-search { flex:1; min-width:0; }
      .admin-validation-panel { border:1px solid rgba(120,120,120,.25); border-radius:10px; padding:10px 12px; }
      .admin-validation-panel.has-errors { border-color: rgba(190,70,70,.55); }
      .admin-validation-summary { font-weight:600; }
      .admin-validation-list { display:grid; gap:4px; margin-top:7px; }
      .admin-validation-item { border:0; background:none; text-align:left; padding:4px; cursor:pointer; }
      .admin-question-invalid { outline:2px solid rgba(190,70,70,.55); outline-offset:2px; }
      @media (max-width:600px) { .admin-question-search-row { flex-direction:column; align-items:stretch; } }
    `;
    document.head.appendChild(style);
  }

  editor.addEventListener("click", event => {
    const duplicate = event.target.closest(".duplicate-question");
    if (duplicate) {
      event.preventDefault();
      event.stopImmediatePropagation();
      const card = duplicate.closest(".question-card");
      if (card) duplicateCard(card);
      return;
    }

    const remove = event.target.closest(".remove-question");
    if (remove) {
      event.preventDefault();
      event.stopImmediatePropagation();
      const card = remove.closest(".question-card");
      if (!card) return;
      const questionText = getQuestionText(card);
      const label = questionText
        ? `\n\n“${questionText.slice(0, 80)}${questionText.length > 80 ? "…" : ""}”`
        : "";
      if (window.confirm(`Remove this question?${label}\n\nThis change is not permanent until you save the quiz.`)) {
        removeCard(card);
      }
    }
  }, true);

  editor.addEventListener("dragstart", event => {
    const card = event.target.closest(".question-card");
    if (!card) return;
    if (!event.target.closest(".question-drag-handle")) {
      event.preventDefault();
      return;
    }
    card.classList.add("dragging");
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", "question");
  });

  editor.addEventListener("dragover", event => {
    const target = event.target.closest(".question-card");
    const dragging = editor.querySelector(".question-card.dragging");
    if (!target || !dragging || target === dragging) return;
    event.preventDefault();
    const rect = target.getBoundingClientRect();
    const before = event.clientY < rect.top + rect.height / 2;
    editor.insertBefore(dragging, before ? target : target.nextSibling);
    [...editor.children].forEach(card => card.classList.remove("drop-target"));
    target.classList.add("drop-target");
  });

  editor.addEventListener("dragend", event => {
    const card = event.target.closest(".question-card");
    if (!card) return;
    card.classList.remove("dragging");
    [...editor.children].forEach(item => item.classList.remove("drop-target"));
    renumber();
    setStatus("Question order changed. Save the quiz to keep the new order.");
  });

  const observer = new MutationObserver(() => enhanceExisting());
  observer.observe(editor, { childList: true });

  expandButton?.addEventListener("click", () => {
    [...editor.children].forEach(card => { card.open = true; });
  });

  collapseButton?.addEventListener("click", () => {
    [...editor.children].forEach(card => { card.open = false; });
  });

  if (toolbar) toolbar.setAttribute("aria-label", "Question editor controls");
  installAdminTools();
  enhanceExisting();
})();
