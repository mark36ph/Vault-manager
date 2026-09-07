(() => {
  const section = document.querySelector("#quiz-high-scores"), list = document.querySelector("#quiz-leaderboard-list"), mine = document.querySelector("#quiz-leaderboard-mine");
  if (!section || !list) return;
  const slugMatch = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})\/?$/i), slug = (slugMatch?.[1] || new URLSearchParams(location.search).get("slug") || "").toLowerCase();
  if (!slug) return;
  const heading = section.querySelector(".section-heading"), controls = document.createElement("div"); controls.className = "quiz-leaderboard-controls";
  const label = document.createElement("label"); label.textContent = "Leaderboard length"; label.htmlFor = "quiz-leaderboard-length";
  const select = document.createElement("select"); select.id = "quiz-leaderboard-length"; select.setAttribute("aria-label", "Leaderboard length");
  controls.append(label, select); if (heading) heading.append(controls);
  const escape = value => String(value ?? "").replace(/[&<>'"]/g, char => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;","\"":"&quot;"}[char]));
  const getLength = () => { try { const memory = Number(window.factburstQuizSelection?.positions?.length || 0); if (memory) return memory; const saved = JSON.parse(sessionStorage.getItem("factburst_quiz_selection_v1") || "null"); return Number(saved?.positions?.length || 0); } catch { return 0; } };
  const render = payload => {
    const rows = Array.isArray(payload?.leaderboard) ? payload.leaderboard : []; list.replaceChildren();
    if (!rows.length) { const empty = document.createElement("p"); empty.className = "leaderboard-message"; empty.textContent = `No verified scores yet for ${payload?.length || select.value}-question games.`; list.append(empty); }
    else { const fragment = document.createDocumentFragment(); rows.forEach(row => { const item = document.createElement("div"); item.className = `leaderboard-row${row.current_user ? " current-user" : ""}`; item.innerHTML = `<span class="leaderboard-rank">${Number(row.rank)}</span><span class="leaderboard-name">${escape(row.username)}</span><strong class="leaderboard-score">${Number(row.score)}/${Number(row.total)}</strong><span class="leaderboard-percent">${Number(row.percentage)}%</span>`; fragment.append(item); }); list.append(fragment); }
    if (mine) { mine.classList.add("hidden"); mine.textContent = ""; if (payload?.mine) { mine.textContent = `Your best: ${payload.mine.score}/${payload.mine.total} (${payload.mine.percentage}%) · Rank #${payload.mine.rank}`; mine.classList.remove("hidden"); } }
  };
  const load = async length => { if (!length) return; list.replaceChildren(Object.assign(document.createElement("p"), { className: "leaderboard-message", textContent: "Loading high scores…" })); try { const response = await fetch(`/api/quizzes/${encodeURIComponent(slug)}/leaderboard?limit=50&length=${encodeURIComponent(length)}`, { headers: { accept: "application/json" } }); if (!response.ok) throw new Error("Leaderboard unavailable"); render(await response.json()); } catch { list.replaceChildren(Object.assign(document.createElement("p"), { className: "leaderboard-message", textContent: "The leaderboard is not available right now. Please try again." })); } };
  const sync = () => { const length = getLength(); if (!length) return false; if (!select.options.length) for (const value of [5, 10, 15, 20]) { const option = document.createElement("option"); option.value = String(value); option.textContent = `${value} questions`; select.append(option); } if (![...select.options].some(option => Number(option.value) === length)) { const option = document.createElement("option"); option.value = String(length); option.textContent = `${length} questions`; select.append(option); } select.value = String(length); load(length); return true; };
  select.addEventListener("change", () => load(Number(select.value)));
  let attempts = 0; const timer = setInterval(() => { attempts++; if (sync() || attempts >= 10) clearInterval(timer); }, 500);
})();
