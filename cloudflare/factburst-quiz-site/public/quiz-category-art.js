(() => {
  const ART = {
    science: { symbol: "⚗", label: "Science" },
    history: { symbol: "⌛", label: "History" },
    geography: { symbol: "◎", label: "Geography" },
    space: { symbol: "✦", label: "Space" },
    "nature & animals": { symbol: "♧", label: "Nature & Animals" },
    technology: { symbol: "⌘", label: "Technology" },
    "arts & literature": { symbol: "✎", label: "Arts & Literature" },
    music: { symbol: "♫", label: "Music" },
    film: { symbol: "▶", label: "Film" },
    logos: { symbol: "◇", label: "Logos" },
    sports: { symbol: "◉", label: "Sports" },
    entertainment: { symbol: "★", label: "Entertainment" },
    mathematics: { symbol: "∑", label: "Mathematics" },
    "general knowledge": { symbol: "?", label: "General Knowledge" },
  };

  const normalize = (value) => String(value || "")
    .trim()
    .toLowerCase()
    .replace(/\s+/g, " ");

  function categoryArt(category) {
    const key = normalize(category);
    const item = ART[key] || { symbol: "?", label: category || "Quiz" };
    const wrapper = document.createElement("div");
    wrapper.className = "quiz-category-art";
    wrapper.dataset.category = key;
    wrapper.setAttribute("role", "img");
    wrapper.setAttribute("aria-label", `${item.label} quiz category`);
    wrapper.innerHTML = `
      <svg viewBox="0 0 640 220" aria-hidden="true" focusable="false">
        <defs>
          <linearGradient id="quiz-art-gradient" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stop-color="#172033" />
            <stop offset="1" stop-color="#273b5b" />
          </linearGradient>
          <pattern id="quiz-art-grid" width="28" height="28" patternUnits="userSpaceOnUse">
            <path d="M 28 0 L 0 0 0 28" fill="none" stroke="#ffffff" stroke-opacity=".08" />
          </pattern>
        </defs>
        <rect width="640" height="220" rx="24" fill="url(#quiz-art-gradient)" />
        <rect width="640" height="220" rx="24" fill="url(#quiz-art-grid)" />
        <circle cx="530" cy="48" r="82" fill="#ffffff" fill-opacity=".055" />
        <circle cx="570" cy="168" r="54" fill="#ffffff" fill-opacity=".04" />
        <text x="46" y="132" font-size="92" font-family="system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif" font-weight="700" fill="#ffffff" fill-opacity=".94">${item.symbol}</text>
        <text x="154" y="102" font-size="25" font-family="system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif" font-weight="700" fill="#ffffff" fill-opacity=".92">${item.label}</text>
        <text x="154" y="137" font-size="16" font-family="system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif" fill="#ffffff" fill-opacity=".68">Fast questions · factual answers</text>
      </svg>`;
    return wrapper;
  }

  function decorateCard(card) {
    if (!(card instanceof Element) || card.querySelector(":scope > .quiz-category-art")) return;
    const pill = card.querySelector(":scope > .category-pill");
    if (!pill) return;
    card.insertBefore(categoryArt(pill.textContent), card.firstChild);
    card.classList.add("has-category-art");
  }

  function decorate(root = document) {
    root.querySelectorAll("#quiz-grid > .quiz-card, #upcoming-grid > .quiz-card").forEach(decorateCard);
  }

  const style = document.createElement("style");
  style.textContent = `
    .quiz-card.has-category-art { overflow: hidden; padding-top: 0; }
    .quiz-category-art { margin: 0 -1px 1rem; border-radius: 18px 18px 0 0; overflow: hidden; background: #172033; }
    .quiz-category-art svg { display: block; width: 100%; height: auto; aspect-ratio: 640 / 220; }
    .quiz-card.has-category-art > .category-pill,
    .quiz-card.has-category-art > h3,
    .quiz-card.has-category-art > p,
    .quiz-card.has-category-art > .quiz-card-footer { margin-left: 0; margin-right: 0; }
    @media (max-width: 640px) {
      .quiz-category-art { margin-bottom: .85rem; }
      .quiz-category-art svg { aspect-ratio: 640 / 250; }
    }
  `;
  document.head.append(style);

  decorate();
  const observer = new MutationObserver(() => decorate());
  observer.observe(document.querySelector("#quiz-grid") || document.body, { childList: true, subtree: true });
  const upcoming = document.querySelector("#upcoming-grid");
  if (upcoming) observer.observe(upcoming, { childList: true, subtree: true });
})();
