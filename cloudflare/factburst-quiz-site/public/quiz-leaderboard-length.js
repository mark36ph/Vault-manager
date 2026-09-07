(() => {
  const section = document.querySelector("#quiz-high-scores");
  const list = document.querySelector("#quiz-leaderboard-list");
  const mine = document.querySelector("#quiz-leaderboard-mine");
  if (!section || !list) return;

  const slugMatch = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})\/?$/i);
  const slug = (slugMatch?.[1] || new URLSearchParams(location.search).get("slug") || "").toLowerCase();
  if (!slug) return;

  const heading = section.querySelector(".section-heading");
  const controls = document.createElement("div");
  controls.className = "quiz-leaderboard-controls";
  const label = document.createElement("label");
  label.textContent = "Leaderboard length";
  label.htmlFor = "quiz-leaderboard-length";
  const select = document.createElement("select");
  select.id = "quiz-leaderboard-length";
  select.setAttribute("aria-label", "Leaderboard length");
  controls.append(label, select);
  if (heading) heading.append(controls);

  const escape = value => String(value ?? "").replace(/[&<>'"]/g, char => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;","\"":"&quot;"}[char]));
  const getLength = () => Number(window.factburstQuizSelection?.positions?.length || sessionStorage.getItem("factburst_quiz_selection_v1") && JSON.parse(sessionStorage.getItem("factburst_quiz_selection_v1"))?.positions?.length || 0);

  const render = payload => {
    const rows = Array.isArray(payload?.leaderboard) ? payload.leaderboard : [];
    list.replaceChildren();
    if (!rows.length) {
      const empty = document.createElement("p");
      empty.className = "leaderboard-message";
      empty.textContent = `No verified scores yet for ${payload?.length || select.value}-question games.`;
      list.append(empty);
    } else {
      const fragment = document.createDocumentFragment();
      rows.forEach(row => {
        const item = document.createElement("div");
        item.className = `leaderboard-row${row.current_user ? " current-user" : ""}`;
        item.innerHTML = `<span class="leaderboard-rank">${Number(row.rank)}</span><span class="leaderboard-name">${escape(row.username)}</span><strong class="leaderboard-score">${Number(row.score)}/${Number(row.total)}</strong><span class="leaderboard-percent">${Number(row.percentage)}%</span>`;
        fragment.append(item);
      });
      list.append(fragment);
    }
    if (mine) {
      mine.classList.add("hidden");
      mine.textContent = "";
      if (payload?.mine) {
        mine.textContent = `Your best: ${payload.mine.score}/${payload.mine.total} (${payload.mine.percentage}%) · Rank #${payload.mine.rank}`;
        mine.classList.remove("hidden");
      }
    }
  };

  const load = async length => {
    if (!length) return;
    list.replaceChildren(Object.assign(document.createElement("p"), { className: "leaderboard-message", textContent: "Loading high scores…" }));
    try {
      const response = await fetch(`/api/quizzes/${encodeURIComponent(slug)}/leaderboard?limit=50&length=${encodeURIComponent(length)}`, { headers: { accept: "application/json" } });
      if (!response.ok) throw new Error("Leaderboard unavailable");
      render(await response.json());
    } catch {
      list.replaceChildren(Object.assign(document.createElement("p"), { className: "leaderboard-message", textContent: "The leaderboard is not available right now. Please try again." }));
    }
  };

  const sync = () => {
    let length = 0;
    try { length = getLength(); } catch {}
    if (!length) return;
    if (![...select.options].some(option => Number(option.value) === length)) {
      const option = document.createElement("option");
      option.value = String(length);
      option.textContent = `${length} questions`;
      select.append(option);
    }
    select.value = String(length);
    load(length);
  };

  select.addEventListener("change", () => load(Number(select.value)));
  const observer = new MutationObserver(sync);
  observer.observe(document.body, { childList: true, subtree: true });
  setTimeout(sync, 250);
  setTimeout(sync, 1000);
  setTimeout(sync, 2500);
})();
