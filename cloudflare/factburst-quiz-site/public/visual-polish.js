(() => {
  const CATEGORY_KEYS = new Map([
    ["science", "science"],
    ["history", "history"],
    ["geography", "geography"],
    ["space", "space"],
    ["nature & animals", "nature-animals"],
    ["nature and animals", "nature-animals"],
    ["technology", "technology"],
    ["arts & literature", "arts-literature"],
    ["arts and literature", "arts-literature"],
    ["music", "music"],
    ["film", "film"],
    ["logos", "logos"],
    ["sports", "sports"],
    ["entertainment", "entertainment"],
    ["mathematics", "mathematics"],
    ["general knowledge", "general-knowledge"],
  ]);

  function categoryKey(label) {
    const normalized = String(label || "").trim().toLowerCase();
    return CATEGORY_KEYS.get(normalized) || "general-knowledge";
  }

  function decorateCard(card) {
    if (!(card instanceof Element) || !card.matches(".quiz-card, .quiz-feature")) return;
    if (card.classList.contains("skeleton-card")) return;
    const pill = card.querySelector(".category-pill");
    if (!pill) return;
    card.dataset.categoryKey = categoryKey(pill.textContent);
  }

  function decorateCategoryCards(root = document) {
    if (root instanceof Element) decorateCard(root);
    if (root.querySelectorAll) {
      for (const card of root.querySelectorAll(".quiz-card, .quiz-feature")) decorateCard(card);
    }
  }

  function updateProfileAvatar() {
    const avatar = document.querySelector("#profile-avatar");
    const username = document.querySelector("#profile-username");
    if (!avatar || !username) return;
    const initial = String(username.textContent || "Player").trim().charAt(0).toUpperCase() || "P";
    if (avatar.textContent !== initial) avatar.textContent = initial;
  }

  function initialize() {
    decorateCategoryCards();
    updateProfileAvatar();

    if (!document.querySelector(".quiz-grid, #latest-card, #profile-content")) return;

    const observer = new MutationObserver(records => {
      for (const record of records) {
        if (record.target instanceof Element) decorateCard(record.target.closest(".quiz-card, .quiz-feature"));
        for (const node of record.addedNodes) {
          if (!(node instanceof Element)) continue;
          decorateCategoryCards(node);
          decorateCard(node.parentElement?.closest(".quiz-card, .quiz-feature"));
        }
      }
      updateProfileAvatar();
    });

    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }
})();
