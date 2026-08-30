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

  function decorateCategoryCards(root = document) {
    const cards = [];
    if (root instanceof Element && root.matches(".quiz-card, .quiz-feature")) cards.push(root);
    if (root.querySelectorAll) cards.push(...root.querySelectorAll(".quiz-card, .quiz-feature"));

    for (const card of cards) {
      if (card.classList.contains("skeleton-card")) continue;
      const pill = card.querySelector(".category-pill");
      if (!pill) continue;
      card.dataset.categoryKey = categoryKey(pill.textContent);
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
        for (const node of record.addedNodes) {
          if (node instanceof Element) decorateCategoryCards(node);
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
