(() => {
  const section = document.querySelector("#quiz-high-scores"), list = document.querySelector("#quiz-leaderboard-list"), mine = document.querySelector("#quiz-leaderboard-mine");
  if (!section || !list) return;
  section.classList.add("hidden");
  const slugMatch = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})\/?$/i), slug = (slugMatch?.[1] || new URLSearchParams(location.search).get("slug") || "").toLowerCase();
  if (!slug) return;
  const escape = value => String(value ?? "").replace(/[&<>'"]/g, char => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;","\"":"&quot;"}[char]));
  const getLength = () => { try { const memory = Number(window.factburstQuizSelection?.refs?.length || window.factburstQuizSelection?.positions?.length || 0); if (memory) return memory; const saved = JSON.parse(sessionStorage.getItem("factburst_quiz_selection_v1") || "null"); return Number(saved?.refs?.length || saved?.positions?.length || 0); } catch { return 0; } };
  let controlsReady = false;
  let select;
  const ensureControls = () => {
    if (controlsReady) return;
    const heading = section.querySelector(".section-heading");
    if (!heading) return;
    const controls = document.createElement("div"); controls.className = "quiz-leaderboard-controls";
    const label = document.createElement("label"); label.textContent = "Leaderboard length"; label.htmlFor = "quiz-leaderboard-length";
    select = document.createElement("select"); select.id = "quiz-leaderboard-length"; select.setAttribute("aria-label", "Leaderboard length");
    controls.append(label, select); heading.append(controls);
    select.addEventListener("change", () => load(Number(select.value)));
    controlsReady = true;
  };
  const getAvailableLengths = current => {
    const max = Number(window.factburstQuizTotal || 0);
    const values = new Set([10, 15, 20, 25, 30, 35, 40, 50].filter(value => !max || value <= max));
    if (current > 0) values.add(current);
    return [...values].sort((a, b) => a - b);
  };
  const render = payload => {
    const rows = Array.isArray(payload?.leaderboard) ? payload.leaderboard : []; list.replaceChildren();
    if (!rows.length) { const empty = document.createElement("p"); empty.className = "leaderboard-message"; empty.textContent = `No verified scores yet for ${payload?.length || select.value}-question games.`; list.append(empty); }
    else { const fragment = document.createDocumentFragment(); rows.forEach(row => { const item = document.createElement("div"); item.className = `leaderboard-row${row.current_user ? " current-user" : ""}`; item.innerHTML = `<span class="leaderboard-rank">${Number(row.rank)}</span><span class="leaderboard-name">${escape(row.username)}</span><strong class="leaderboard-score">${Number(row.score)}/${Number(row.total)}</strong><span class="leaderboard-percent">${Number(row.percentage)}%</span>`; fragment.append(item); }); list.append(fragment); }
    if (mine) { mine.classList.add("hidden"); mine.textContent = ""; if (payload?.mine) { mine.textContent = `Your best: ${payload.mine.score}/${payload.mine.total} (${payload.mine.percentage}%) · Rank #${payload.mine.rank}`; mine.classList.remove("hidden"); } }
  };
  const load = async length => { if (!length) return; ensureControls(); if (!select) return; list.replaceChildren(Object.assign(document.createElement("p"), { className: "leaderboard-message", textContent: "Loading high scores…" })); try { const response = await fetch(`/api/quizzes/${encodeURIComponent(slug)}/leaderboard?limit=50&length=${encodeURIComponent(length)}`, { headers: { accept: "application/json" } }); if (!response.ok) throw new Error("Leaderboard unavailable"); render(await response.json()); } catch { list.replaceChildren(Object.assign(document.createElement("p"), { className: "leaderboard-message", textContent: "The leaderboard is not available right now. Please try again." })); } };
  const sync = () => {
    const errorPanel = document.querySelector("#quiz-error");
    if (errorPanel && !errorPanel.classList.contains("hidden")) { section.classList.add("hidden"); return false; }
    const length = getLength();
    if (!length) return false;
    ensureControls();
    if (!select) return false;
    const values = getAvailableLengths(length);
    select.replaceChildren(...values.map(value => { const option = document.createElement("option"); option.value = String(value); option.textContent = `${value} questions`; return option; }));
    select.value = String(length);
    section.classList.remove("hidden");
    load(length);
    return true;
  };
  window.addEventListener("factburst:quiz-selection-ready", sync);
  let attempts = 0; const timer = setInterval(() => { attempts++; if (sync() || attempts >= 20) clearInterval(timer); }, 500);
})();
