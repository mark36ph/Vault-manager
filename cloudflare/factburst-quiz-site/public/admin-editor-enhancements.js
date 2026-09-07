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
      });
    }

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
      const questionText = card.querySelector(".q-text")?.value.trim() || "";
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
  enhanceExisting();
})();
