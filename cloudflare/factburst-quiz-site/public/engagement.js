(() => {
  const nativeFetch = window.fetch.bind(window);
  let latestScore = null;
  let quizStartedAt = Date.now();

  window.fetch = async function factburstEngagementFetch(input, init) {
    const response = await nativeFetch(input, init);
    try {
      const url = typeof input === "string" ? input : input?.url || "";
      const method = String(init?.method || input?.method || "GET").toUpperCase();
      if (method === "POST" && /\/api\/quizzes\/[a-z0-9-]+\/score(?:\?|$)/i.test(url) && response.ok) {
        response.clone().json().then(payload => {
          latestScore = payload;
          setTimeout(() => enhanceQuizResult(payload), 140);
        }).catch(() => {});
      }
    } catch {}
    return response;
  };

  document.addEventListener("DOMContentLoaded", () => {
    const page = document.body.dataset.page;
    if (page === "home") initHomeEngagement();
    if (page === "profile") initProfileEngagement();
    if (page === "quiz") initQuizEngagement();
  });

  async function initHomeEngagement() {
    try {
      const dashboard = await api("/api/engagement/dashboard");
      renderHomeDashboard(dashboard);
      installNotifications(dashboard.notifications?.unread || 0);
    } catch {}
  }

  function renderHomeDashboard(data) {
    const anchor = document.querySelector("#latest");
    if (!anchor || document.querySelector("#personal-dashboard")) return;
    const player = data.player || {};
    const section = el("section", "shell section engagement-section");
    section.id = "personal-dashboard";
    section.append(sectionHeading("For you", `Welcome back, ${player.username || "Player"}`));

    const grid = el("div", "engagement-grid");
    grid.append(
      metricCard("Daily streak", `${player.streak || 0} days`, `Longest: ${player.longest_streak || 0} days`),
      metricCard("Level", String(player.level || 1), `${Number(player.xp || 0).toLocaleString()} XP`),
      metricCard("Questions answered", Number(player.questions_answered || 0).toLocaleString(), `${player.accuracy || 0}% accuracy`),
      metricCard("Achievements", String((data.achievements || []).length), "Keep playing to unlock more"),
    );
    section.append(grid);

    const content = el("div", "engagement-mini-grid");
    if (data.daily) content.append(dailyCard(data.daily, player));
    content.append(continueCard(data.recent || []));
    content.append(recommendationCard(data.recommendations || []));
    section.append(content);

    if (data.tournament) section.append(tournamentCard(data.tournament));
    anchor.before(section);
  }

  async function initProfileEngagement() {
    try {
      const dashboard = await api("/api/engagement/dashboard");
      installNotifications(dashboard.notifications?.unread || 0);
      renderProfileEngagement(dashboard);
      renderEmailChangePanel();
      renderFriendsLeaderboard();
      renderLiveMatchCreator(dashboard);
    } catch {}
  }

  function renderProfileEngagement(data) {
    const content = document.querySelector("#profile-content");
    const history = document.querySelector(".profile-history-panel");
    if (!content || !history || document.querySelector("#profile-engagement")) return;
    const player = data.player || {};
    const section = el("section", "profile-panel profile-engagement-panel");
    section.id = "profile-engagement";
    section.append(sectionHeading("Progress", "Your quiz progress"));

    const stats = el("div", "engagement-grid");
    stats.append(
      metricCard("Level", String(player.level || 1), `${player.xp || 0} XP`),
      metricCard("Daily streak", `${player.streak || 0} days`, `Best ${player.longest_streak || 0}`),
      metricCard("Questions", String(player.questions_answered || 0), `${player.correct_answers || 0} correct`),
      metricCard("Accuracy", `${player.accuracy || 0}%`, `${player.attempts || 0} attempts`),
    );
    section.append(stats);

    const categories = el("div", "engagement-card");
    categories.append(title3("Category performance"));
    const categoryList = el("div", "engagement-list");
    for (const category of data.categories || []) {
      const row = el("div", "engagement-row");
      const copy = el("div");
      copy.append(strong(category.category), small(`${category.quizzes} quizzes • ${category.rank}`));
      const value = strong(`${category.percentage}%`);
      const bar = el("div", "category-progress");
      const fill = el("span"); fill.style.width = `${Math.max(0, Math.min(100, category.percentage || 0))}%`; bar.append(fill); copy.append(bar);
      row.append(copy, value); categoryList.append(row);
    }
    if (!(data.categories || []).length) categoryList.append(paragraph("Play quizzes in a few categories and your strengths will appear here."));
    categories.append(categoryList);

    const achievements = el("div", "engagement-card");
    achievements.append(title3("Achievements"));
    const achievementGrid = el("div", "achievement-grid");
    for (const achievement of data.achievements || []) {
      const card = el("div", "achievement-card");
      card.append(strong(`🏆 ${achievement.title}`), span(achievement.description)); achievementGrid.append(card);
    }
    if (!(data.achievements || []).length) achievementGrid.append(paragraph("Your first achievement is waiting for you."));
    achievements.append(achievementGrid);

    const saved = listQuizCard("Saved quizzes", data.saved || [], "You have not saved any quizzes yet.");
    const recent = listQuizCard("Recently played", data.recent || [], "Your recent quizzes will appear here.");
    const recommendations = listQuizCard("Recommended next", data.recommendations || [], "Play more quizzes to build recommendations.");

    const layout = el("div", "engagement-mini-grid");
    layout.append(categories, achievements, saved, recent, recommendations);
    if (data.daily) layout.append(dailyCard(data.daily, player));
    section.append(layout);
    if (data.tournament) section.append(tournamentCard(data.tournament));
    history.before(section);
  }

  function renderEmailChangePanel() {
    const hero = document.querySelector(".profile-hero");
    if (!hero || document.querySelector("#email-change-panel")) return;
    const panel = el("section", "profile-panel profile-engagement-panel");
    panel.id = "email-change-panel";
    panel.append(sectionHeading("Account", "Change email address"));
    panel.append(paragraph("Your current verified email stays active until the replacement address is confirmed."));
    const form = el("form", "email-change-form");
    const input = document.createElement("input"); input.type = "email"; input.required = true; input.placeholder = "New email address"; input.autocomplete = "email";
    const button = buttonEl("Send confirmation", "button button-primary"); button.type = "submit";
    const status = el("p", "email-change-status");
    form.append(input, button); panel.append(form, status);
    form.addEventListener("submit", async event => {
      event.preventDefault(); button.disabled = true; status.textContent = "Sending confirmation…";
      try {
        const result = await api("/api/account/email", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ email: input.value }) });
        status.textContent = result.message || "Confirmation sent."; input.value = ""; await showPendingEmail(status);
      } catch (error) { status.textContent = error.message; }
      finally { button.disabled = false; }
    });
    hero.after(panel); showPendingEmail(status).catch(() => {});
  }

  async function showPendingEmail(status) {
    const pending = await api("/api/account/pending-email");
    if (!pending.pending_email) return;
    status.replaceChildren(document.createTextNode(`Awaiting confirmation: ${pending.pending_email} `));
    const resend = buttonEl("Resend", "button button-secondary"); resend.type = "button";
    resend.addEventListener("click", async () => {
      resend.disabled = true;
      try { const result = await api("/api/account/resend-email-change", { method: "POST" }); status.textContent = result.message || "Confirmation resent."; }
      catch (error) { status.textContent = error.message; }
      finally { resend.disabled = false; }
    });
    status.append(resend);
  }

  async function renderFriendsLeaderboard() {
    const anchor = document.querySelector(".profile-friends-panel");
    if (!anchor || document.querySelector("#friends-leaderboard-panel")) return;
    const panel = el("section", "profile-panel profile-engagement-panel"); panel.id = "friends-leaderboard-panel";
    panel.append(sectionHeading("Competition", "Friends leaderboard"));
    const actions = el("div", "engagement-actions");
    const tableHost = el("div");
    for (const period of ["week", "month", "all"]) {
      const b = buttonEl(period === "all" ? "All time" : period[0].toUpperCase() + period.slice(1), "button button-secondary");
      b.addEventListener("click", () => loadFriendRanking(period, tableHost)); actions.append(b);
    }
    panel.append(actions, tableHost); anchor.after(panel); await loadFriendRanking("week", tableHost);
  }

  async function loadFriendRanking(period, host) {
    host.replaceChildren(paragraph("Loading friends leaderboard…"));
    try {
      const data = await api(`/api/engagement/leaderboard?scope=friends&period=${encodeURIComponent(period)}`);
      const table = el("table", "rank-table");
      table.innerHTML = "<thead><tr><th>#</th><th>Player</th><th>Quizzes</th><th>Score</th><th>Accuracy</th></tr></thead>";
      const body = document.createElement("tbody");
      for (const row of data.leaderboard || []) {
        const tr = document.createElement("tr"); if (row.current_user) tr.className = "current";
        tr.innerHTML = `<td>${row.rank}</td><td></td><td>${row.quizzes}</td><td>${row.score}/${row.total}</td><td>${row.percentage}%</td>`;
        tr.children[1].textContent = row.username; body.append(tr);
      }
      table.append(body); host.replaceChildren(table);
    } catch (error) { host.replaceChildren(paragraph(error.message)); }
  }

  async function renderLiveMatchCreator(dashboard) {
    const anchor = document.querySelector("#friends-leaderboard-panel");
    if (!anchor || document.querySelector("#live-match-panel")) return;
    let friends;
    try { friends = (await api("/api/friends")).friends || []; } catch { return; }
    if (!friends.length) return;
    let quizzes = [];
    try { quizzes = (await api("/api/quizzes?limit=60")).quizzes || []; } catch {}
    quizzes = quizzes.filter(q => !q.publish_at || Date.parse(q.publish_at) <= Date.now());
    if (!quizzes.length) return;

    const panel = el("section", "profile-panel profile-engagement-panel"); panel.id = "live-match-panel";
    panel.append(sectionHeading("Head to head", "Start a live friend quiz"));
    panel.append(paragraph("Choose a friend and quiz. Both players answer the same quiz and the match updates when each score is submitted."));
    const actions = el("div", "engagement-actions");
    const friendSelect = document.createElement("select"); friendSelect.className = "button button-secondary";
    for (const friend of friends) { const option = document.createElement("option"); option.value = friend.user_id; option.textContent = friend.username; friendSelect.append(option); }
    const quizSelect = document.createElement("select"); quizSelect.className = "button button-secondary";
    for (const quiz of quizzes) { const option = document.createElement("option"); option.value = quiz.slug; option.textContent = quiz.title; quizSelect.append(option); }
    const start = buttonEl("Start live match", "button button-primary");
    const status = paragraph("");
    start.addEventListener("click", async () => {
      start.disabled = true; status.textContent = "Creating match…";
      try {
        const result = await api("/api/live-matches", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ slug: quizSelect.value, friend_user_id: Number(friendSelect.value) }) });
        const match = result.match; status.textContent = "Live match created. Share the link with your friend.";
        await shareOrCopy("Factburst live quiz", `${match.host} challenged ${match.guest} to a live Factburst quiz.`, match.url);
      } catch (error) { status.textContent = error.message; }
      finally { start.disabled = false; }
    });
    actions.append(friendSelect, quizSelect, start); panel.append(actions, status); anchor.after(panel);
  }

  function initQuizEngagement() {
    quizStartedAt = Date.now();
    installQuestionReportButton();
    api("/api/engagement/dashboard").then(data => installNotifications(data.notifications?.unread || 0)).catch(() => {});
    const live = new URLSearchParams(location.search).get("live");
    if (live) pollLiveMatch(live).catch(() => {});
  }

  function installQuestionReportButton() {
    const card = document.querySelector(".question-card");
    if (!card || card.querySelector(".report-question-button")) return;
    const button = buttonEl("Report this question", "report-question-button");
    button.addEventListener("click", async () => {
      const slug = new URLSearchParams(location.search).get("slug") || "";
      const text = document.querySelector("#question-number")?.textContent || "";
      const position = Number((text.match(/\d+/) || [])[0] || 0);
      if (!position) return;
      const reason = prompt("Report reason: incorrect, typo, duplicate, outdated, or other", "incorrect");
      if (!reason) return;
      const detail = prompt("Optional details about the problem", "") || "";
      try {
        await api(`/api/quizzes/${encodeURIComponent(slug)}/questions/${position}/report`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ reason: reason.trim().toLowerCase(), detail }) });
        button.textContent = "Question reported — thank you"; button.disabled = true;
      } catch (error) { alert(error.message); }
    });
    card.append(button);
  }

  async function enhanceQuizResult(score) {
    const panel = document.querySelector("#quiz-results");
    if (!panel || panel.classList.contains("hidden") || panel.querySelector("#quiz-engagement-tools")) return;
    const slug = new URLSearchParams(location.search).get("slug") || "";
    const title = document.querySelector("#quiz-title")?.textContent || document.title.replace(" | Factburst Quiz", "");
    const tools = el("div", "quiz-engagement-tools"); tools.id = "quiz-engagement-tools";
    tools.append(title3("Keep the score going"));
    const actions = el("div", "quiz-reaction-row");
    const save = buttonEl("☆ Save quiz", "button button-secondary");
    const up = buttonEl("👍 Like", "button button-secondary");
    const down = buttonEl("👎 Not for me", "button button-secondary");
    const cardShare = buttonEl("Share result card", "button button-secondary");
    save.addEventListener("click", () => toggleSave(slug, save));
    up.addEventListener("click", () => react(slug, "up", up, down));
    down.addEventListener("click", () => react(slug, "down", up, down));
    cardShare.addEventListener("click", () => shareResultCard(title, score));
    actions.append(save, up, down, cardShare); tools.append(actions);
    if (score.engagement) {
      const progress = paragraph(`+${score.engagement.xp_earned || 0} XP • Level ${score.engagement.level || 1} • ${Number(score.engagement.xp || 0).toLocaleString()} total XP`);
      tools.append(progress);
      for (const achievement of score.engagement.achievements_unlocked || []) {
        const badge = el("span", "engagement-badge"); badge.textContent = `🏆 ${achievement.title}`; tools.append(badge);
      }
    }
    panel.append(tools);
    await hydrateSavedState(slug, save);
    await addResultRecommendations(tools);

    const params = new URLSearchParams(location.search);
    const challenge = params.get("challenge");
    const live = params.get("live");
    if (challenge) await submitAndRenderChallenge(challenge, slug, score, tools);
    if (live) await submitAndRenderLive(live, score, tools);
  }

  async function hydrateSavedState(slug, button) {
    try {
      const data = await api("/api/engagement/dashboard");
      const isSaved = (data.saved || []).some(q => q.slug === slug); button.dataset.saved = isSaved ? "1" : "0"; button.textContent = isSaved ? "★ Saved" : "☆ Save quiz";
    } catch {}
  }

  async function toggleSave(slug, button) {
    const saved = button.dataset.saved === "1";
    button.disabled = true;
    try {
      await api(saved ? `/api/engagement/saved?slug=${encodeURIComponent(slug)}` : "/api/engagement/saved", saved ? { method: "DELETE" } : { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ slug }) });
      button.dataset.saved = saved ? "0" : "1"; button.textContent = saved ? "☆ Save quiz" : "★ Saved";
    } catch (error) { alert(error.message); }
    finally { button.disabled = false; }
  }

  async function react(slug, reaction, up, down) {
    try {
      const result = await api(`/api/quizzes/${encodeURIComponent(slug)}/reaction`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ reaction }) });
      up.textContent = `👍 Like ${result.up || 0}`; down.textContent = `👎 Not for me ${result.down || 0}`;
    } catch (error) { alert(error.message); }
  }

  async function addResultRecommendations(host) {
    try {
      const data = await api("/api/engagement/dashboard");
      const items = data.recommendations || [];
      if (!items.length) return;
      host.append(title3("Recommended next"));
      const actions = el("div", "engagement-actions");
      for (const quiz of items) actions.append(linkButton(quiz.title, `/quiz.html?slug=${encodeURIComponent(quiz.slug)}`));
      host.append(actions);
    } catch {}
  }

  async function submitAndRenderChallenge(token, slug, score, host) {
    const card = el("div", "challenge-result-card"); card.append(title3("Friend challenge"), paragraph("Saving your challenge result…")); host.append(card);
    try {
      const response = await api("/api/engagement/challenge-result", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ token, score: score.score, total: score.total, duration_ms: Date.now() - quizStartedAt }) });
      renderChallengeResult(card, response.result, slug);
    } catch (error) {
      try { const response = await api(`/api/engagement/challenge-result?token=${encodeURIComponent(token)}`); renderChallengeResult(card, response.result, slug); }
      catch { card.replaceChildren(title3("Friend challenge"), paragraph(error.message || "Challenge result is unavailable.")); }
    }
  }

  function renderChallengeResult(card, result, slug) {
    card.replaceChildren(title3(result.winner === "pending" ? "Waiting for challenge result" : result.winner === "draw" ? "Challenge draw" : "Challenge complete"));
    const grid = matchScores(result.challenger, result.challenged); card.append(grid);
    const currentId = result.challenger.user_id;
    const other = result.challenged;
    if (result.winner !== "pending" && other?.user_id) {
      const rematch = buttonEl("Rematch", "button button-primary");
      rematch.addEventListener("click", async () => {
        rematch.disabled = true;
        try {
          const account = await api("/api/account");
          const me = account.user?.id;
          const friendId = Number(me) === Number(result.challenger.user_id) ? result.challenged.user_id : result.challenger.user_id;
          const response = await api("/api/challenges", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ slug, friend_user_id: friendId }) });
          await shareOrCopy("Factburst rematch", "Can you win the rematch?", response.challenge.url);
        } catch (error) { alert(error.message); }
        finally { rematch.disabled = false; }
      });
      card.append(rematch);
    }
  }

  async function submitAndRenderLive(token, score, host) {
    let card = document.querySelector("#live-match-result");
    if (!card) { card = el("div", "live-match-card"); card.id = "live-match-result"; host.append(card); }
    card.replaceChildren(title3("Live head-to-head"), paragraph("Submitting your score…"));
    try {
      const response = await api(`/api/live-matches/${encodeURIComponent(token)}/score`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ score: score.score, total: score.total, duration_ms: Date.now() - quizStartedAt }) });
      renderLiveResult(card, response.match); if (response.match.status !== "complete") pollLiveMatch(token, card);
    } catch (error) { card.append(paragraph(error.message)); }
  }

  async function pollLiveMatch(token, existingCard = null) {
    const response = await api(`/api/live-matches/${encodeURIComponent(token)}`);
    const match = response.match;
    let card = existingCard || document.querySelector("#live-match-status");
    if (!card) {
      const player = document.querySelector("#quiz-player"); if (!player) return;
      card = el("div", "live-match-card"); card.id = "live-match-status"; player.prepend(card);
    }
    renderLiveResult(card, match);
    if (match.status !== "complete") setTimeout(() => pollLiveMatch(token, card).catch(() => {}), 3500);
  }

  function renderLiveResult(card, match) {
    const title = match.status === "complete" ? (match.winner === "draw" ? "Live match: draw" : "Live match complete") : "Live match in progress";
    card.replaceChildren(title3(title), matchScores(match.host, match.guest));
    if (match.status !== "complete") card.append(paragraph("Waiting for both players to finish. This updates automatically."));
  }

  function matchScores(left, right) {
    const grid = el("div", "match-score-grid");
    const a = el("div"); a.append(strong(left?.username || "Player"), span(left?.score == null ? "Playing…" : `${left.score}/${left.total}`));
    const versus = span("VS");
    const b = el("div"); b.append(strong(right?.username || "Player"), span(right?.score == null ? "Playing…" : `${right.score}/${right.total}`));
    grid.append(a, versus, b); return grid;
  }

  async function shareResultCard(title, score) {
    const canvas = document.createElement("canvas"); canvas.width = 1200; canvas.height = 630;
    const ctx = canvas.getContext("2d");
    const gradient = ctx.createLinearGradient(0, 0, 1200, 630); gradient.addColorStop(0, "#061447"); gradient.addColorStop(1, "#392078"); ctx.fillStyle = gradient; ctx.fillRect(0, 0, 1200, 630);
    ctx.fillStyle = "#39d9ff"; ctx.font = "700 34px Arial"; ctx.fillText("FACTBURST QUIZ", 70, 90);
    ctx.fillStyle = "#ffffff"; ctx.font = "800 58px Arial"; wrapCanvasText(ctx, title, 70, 180, 1050, 72);
    ctx.font = "900 120px Arial"; ctx.fillText(`${score.score}/${score.total}`, 70, 455);
    ctx.font = "600 32px Arial"; ctx.fillStyle = "#bdd1ff"; ctx.fillText("Can you beat my score?", 70, 525);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/png"));
    if (!blob) return;
    const file = new File([blob], "factburst-result.png", { type: "image/png" });
    const text = `I scored ${score.score}/${score.total} on “${title}” at Factburst Quiz. Can you beat me?`;
    try {
      if (navigator.canShare?.({ files: [file] })) await navigator.share({ title: "Factburst Quiz result", text, url: location.href, files: [file] });
      else await shareOrCopy("Factburst Quiz result", text, location.href);
    } catch {}
  }

  function wrapCanvasText(ctx, text, x, y, maxWidth, lineHeight) {
    const words = String(text || "Quiz result").split(/\s+/); let line = ""; let row = 0;
    for (const word of words) { const test = line ? `${line} ${word}` : word; if (ctx.measureText(test).width > maxWidth && line) { ctx.fillText(line, x, y + row * lineHeight); line = word; row++; } else line = test; }
    if (line) ctx.fillText(line, x, y + row * lineHeight);
  }

  function installNotifications(unread) {
    if (document.querySelector("#notification-button")) return;
    const button = buttonEl("🔔", "notification-button"); button.id = "notification-button"; button.setAttribute("aria-label", "Notifications");
    if (Number(unread) > 0) button.dataset.unread = String(unread);
    const panel = el("div", "notification-panel hidden"); panel.id = "notification-panel";
    button.addEventListener("click", async () => {
      panel.classList.toggle("hidden"); if (panel.classList.contains("hidden")) return;
      panel.replaceChildren(paragraph("Loading notifications…"));
      try {
        const data = await api("/api/notifications"); panel.replaceChildren();
        const heading = el("div", "engagement-row"); heading.append(title3("Notifications"));
        const read = buttonEl("Mark all read", "button button-secondary"); read.addEventListener("click", async () => { await api("/api/notifications/read-all", { method: "PATCH" }); button.removeAttribute("data-unread"); loadNotificationItems(panel, data.notifications || [], true); }); heading.append(read); panel.append(heading);
        loadNotificationItems(panel, data.notifications || [], false);
      } catch (error) { panel.replaceChildren(paragraph(error.message)); }
    });
    document.body.append(panel, button);
  }

  function loadNotificationItems(panel, items, markRead) {
    panel.querySelectorAll(".notification-item").forEach(node => node.remove());
    if (!items.length) { panel.append(paragraph("No notifications yet.")); return; }
    for (const item of items) {
      const link = document.createElement(item.url ? "a" : "div"); link.className = `notification-item ${(!item.read && !markRead) ? "unread" : ""}`; if (item.url) link.href = item.url;
      link.append(strong(item.title), span(item.message || "")); panel.append(link);
    }
  }

  function dailyCard(daily, player) {
    const card = el("div", "engagement-card"); card.append(spanBadge("Daily Quiz"), title3(daily.title));
    card.append(paragraph(daily.completed ? `Completed today: ${daily.score}/${daily.total}` : `${daily.category || "Quiz"} • Keep your ${player?.streak || 0}-day streak moving.`));
    card.append(linkButton(daily.completed ? "Play again" : "Play Daily Quiz", `/quiz.html?slug=${encodeURIComponent(daily.slug)}`)); return card;
  }

  function continueCard(items) {
    const card = el("div", "engagement-card"); card.append(spanBadge("Continue"), title3("Recently played"));
    if (!items.length) card.append(paragraph("Your latest quiz will appear here."));
    else { const item = items[0]; card.append(paragraph(`${item.best_score}/${item.total} • ${item.percentage}%`), linkButton("Play again", `/quiz.html?slug=${encodeURIComponent(item.slug)}`)); }
    return card;
  }

  function recommendationCard(items) {
    const card = el("div", "engagement-card"); card.append(spanBadge("For you"), title3("Recommended quiz"));
    if (!items.length) card.append(paragraph("Play a few quizzes and recommendations will appear here."));
    else { const item = items[0]; card.append(paragraph(item.category || "Quiz"), linkButton("Play recommendation", `/quiz.html?slug=${encodeURIComponent(item.slug)}`)); }
    return card;
  }

  function tournamentCard(tournament) {
    const card = el("div", "engagement-card engagement-section"); card.append(spanBadge("Weekly tournament"), title3("This week’s Factburst challenge"), paragraph("Play the featured quizzes this week. Your best scores count toward the tournament table."));
    const links = el("div", "tournament-quiz-links"); for (const quiz of tournament.quizzes || []) links.append(linkButton(quiz.title, `/quiz.html?slug=${encodeURIComponent(quiz.slug)}`)); card.append(links);
    const leaders = (tournament.leaderboard || []).slice(0, 5); if (leaders.length) { const list = el("div", "engagement-list"); for (const leader of leaders) { const row = el("div", "engagement-row"); row.append(strong(`#${leader.rank} ${leader.username}`), span(`${leader.score}/${leader.total} • ${leader.percentage}%`)); list.append(row); } card.append(list); }
    return card;
  }

  function listQuizCard(title, items, emptyText) {
    const card = el("div", "engagement-card"); card.append(title3(title));
    if (!items.length) { card.append(paragraph(emptyText)); return card; }
    const list = el("div", "engagement-list");
    for (const item of items.slice(0, 5)) { const row = el("div", "engagement-row"); const a = document.createElement("a"); a.href = `/quiz.html?slug=${encodeURIComponent(item.slug)}`; a.textContent = item.title; a.style.color = "white"; a.style.fontWeight = "800"; const detail = span(item.percentage != null ? `${item.percentage}%` : item.category || "Quiz"); row.append(a, detail); list.append(row); }
    card.append(list); return card;
  }

  function metricCard(label, value, copy) { const card = el("div", "engagement-card"); card.append(spanBadge(label), strongBig(value), paragraph(copy)); return card; }
  function sectionHeading(eyebrow, heading) { const root = el("div", "section-heading"); const inner = el("div"); const eye = el("p", "eyebrow"); eye.textContent = eyebrow; const h = document.createElement("h2"); h.textContent = heading; inner.append(eye, h); root.append(inner); return root; }
  function linkButton(label, href) { const a = document.createElement("a"); a.className = "button button-primary"; a.href = href; a.textContent = label; return a; }
  function buttonEl(label, className) { const b = document.createElement("button"); b.type = "button"; b.className = className; b.textContent = label; return b; }
  function title3(text) { const h = document.createElement("h3"); h.textContent = text; return h; }
  function paragraph(text) { const p = document.createElement("p"); p.textContent = text; return p; }
  function strong(text) { const node = document.createElement("strong"); node.textContent = text; return node; }
  function strongBig(text) { const node = strong(text); node.className = "big"; return node; }
  function span(text) { const node = document.createElement("span"); node.textContent = text; return node; }
  function small(text) { const node = document.createElement("small"); node.textContent = text; return node; }
  function spanBadge(text) { const node = span(text); node.className = "engagement-badge"; return node; }
  function el(tag, className = "") { const node = document.createElement(tag); if (className) node.className = className; return node; }

  async function shareOrCopy(title, text, url) {
    if (navigator.share) { try { await navigator.share({ title, text, url }); return; } catch (error) { if (error?.name === "AbortError") return; } }
    await navigator.clipboard.writeText(`${text} ${url}`.trim());
  }

  async function api(url, options = {}) {
    const response = await nativeFetch(url, { credentials: "same-origin", cache: "no-store", ...options });
    let payload = null; try { payload = await response.json(); } catch {}
    if (!response.ok) throw new Error(payload?.error || `Request failed (${response.status}).`);
    return payload || {};
  }
})();
