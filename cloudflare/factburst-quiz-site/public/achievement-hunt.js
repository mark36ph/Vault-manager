(() => {
  const achievements = [
    ["first_quiz", "First Steps", "Complete your first Factburst quiz.", "🎯"],
    ["perfect_score", "Perfect Score", "Get every question right in a quiz.", "💯"],
    ["ten_quizzes", "Quiz Regular", "Complete 10 different quizzes.", "🏅"],
    ["hundred_questions", "Century", "Answer 100 quiz questions.", "🧠"],
    ["seven_day_streak", "On Fire", "Complete the Daily Challenge 7 days in a row.", "🔥"],
    ["category_master", "Category Master", "Reach 80% across 5 quizzes in one category.", "👑"],
  ];

  document.addEventListener("DOMContentLoaded", () => {
    const section = document.querySelector("#achievement-hunt");
    const grid = document.querySelector("#achievement-hunt-grid");
    const status = document.querySelector("#achievement-hunt-status");
    if (!section || !grid) return;

    for (const [key, title, description, icon] of achievements) {
      const card = document.createElement("article");
      card.className = "achievement-hunt-card locked";
      card.dataset.achievement = key;
      card.innerHTML = `<div class="achievement-hunt-icon" aria-hidden="true">${icon}</div><div class="achievement-hunt-copy"><strong>${title}</strong><span>${description}</span></div><span class="achievement-hunt-state">Locked</span>`;
      grid.append(card);
    }

    fetch("/api/engagement/dashboard", { credentials: "same-origin" })
      .then(async response => {
        if (!response.ok) return null;
        return response.json();
      })
      .then(data => {
        if (!data) {
          if (status) status.textContent = "Sign in to track your achievements and unlock badges.";
          return;
        }
        const unlocked = new Set((data.achievements || []).map(item => String(item.key)));
        let count = 0;
        for (const card of grid.querySelectorAll("[data-achievement]")) {
          if (!unlocked.has(card.dataset.achievement)) continue;
          count++;
          card.classList.remove("locked");
          card.classList.add("unlocked");
          const state = card.querySelector(".achievement-hunt-state");
          if (state) state.textContent = "Unlocked ✓";
        }
        if (status) status.textContent = count === achievements.length
          ? "🏆 All achievements unlocked — you're a Factburst legend!"
          : `${count}/${achievements.length} unlocked · Keep playing to collect them all.`;
      })
      .catch(() => {
        if (status) status.textContent = "Play quizzes to start collecting achievements.";
      });
  });
})();
