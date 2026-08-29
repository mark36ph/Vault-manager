const loading = document.querySelector("#profile-loading");
const content = document.querySelector("#profile-content");
const errorPanel = document.querySelector("#profile-error");
const errorCopy = document.querySelector("#profile-error-copy");
const friendStatus = document.querySelector("#friend-status");
const emailStatus = document.querySelector("#email-change-status");
let profileQuizzes = [];

initialize().catch(error => showError(error.message || "Could not load your profile."));

document.querySelector("#profile-logout")?.addEventListener("click", async () => {
  const button = document.querySelector("#profile-logout");
  button.disabled = true;
  button.textContent = "Logging out…";
  try {
    await api("/api/account/logout", { method: "POST" });
  } finally {
    location.href = "/";
  }
});

document.querySelector("#email-change-form")?.addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const input = document.querySelector("#profile-email-input");
  const submit = form.querySelector("button[type='submit']");
  submit.disabled = true;
  setEmailStatus("Sending confirmation…", "");
  try {
    const response = await api("/api/account/email", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email: input.value }),
    });
    input.value = "";
    setEmailStatus(response.message || "Confirmation sent.", response.verification_sent === false ? "" : "success");
    await refreshEmailSettings();
  } catch (error) {
    setEmailStatus(error.message || "Could not start the email change.", "error");
  } finally {
    submit.disabled = false;
  }
});

document.querySelector("#email-resend-confirmation")?.addEventListener("click", async event => {
  const button = event.currentTarget;
  button.disabled = true;
  button.textContent = "Sending…";
  setEmailStatus("Resending confirmation…", "");
  try {
    const response = await api("/api/account/resend-verification", { method: "POST" });
    setEmailStatus(response.message || "Confirmation resent.", "success");
    await refreshEmailSettings();
  } catch (error) {
    setEmailStatus(error.message || "Could not resend the confirmation.", "error");
  } finally {
    button.disabled = false;
    button.textContent = "Resend confirmation";
  }
});

document.querySelector("#friend-add-form")?.addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const input = document.querySelector("#friend-username");
  const submit = form.querySelector("button[type='submit']");
  submit.disabled = true;
  setFriendStatus("Sending friend request…", "");
  try {
    const response = await api("/api/friends", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username: input.value }),
    });
    input.value = "";
    setFriendStatus(response.message || "Friend request sent.", "success");
    await refreshFriends();
  } catch (error) {
    setFriendStatus(error.message || "Could not send friend request.", "error");
  } finally {
    submit.disabled = false;
  }
});

async function initialize() {
  const account = await api("/api/account");
  if (!account.authenticated || !account.user) {
    throw new Error("Log in to your Factburst account to view this page.");
  }
  if (!account.user.email_verified) {
    throw new Error("Verify your email before viewing your full Factburst profile.");
  }

  const [history, friends, emailSettings] = await Promise.all([
    api("/api/account/history"),
    api("/api/friends"),
    api("/api/account/email-status"),
  ]);
  renderProfile(account.user, history);
  renderEmailSettings(emailSettings);
  renderFriends(friends);
  loading.classList.add("hidden");
  content.classList.remove("hidden");
}

function renderProfile(user, history) {
  text("#profile-username", user.username || "Player");
  text("#profile-email", `✓ Verified email: ${user.email || ""}`);
  text("#profile-rank", history.overall_rank ? `#${history.overall_rank}` : "—");
  text("#profile-score", `${Number(user.total_score || 0)}/${Number(user.total_possible || 0)}`);
  text("#profile-quizzes", String(Number(user.quizzes_completed || 0)));
  text("#profile-attempts", String(Number(user.attempts || 0)));
  text("#profile-accuracy", `${Number(user.percentage || 0)}%`);

  const host = document.querySelector("#profile-history");
  host.replaceChildren();
  profileQuizzes = Array.isArray(history.quizzes) ? history.quizzes : [];
  if (profileQuizzes.length === 0) {
    const empty = document.createElement("div");
    empty.className = "profile-history-empty";
    empty.innerHTML = "<h3>No completed quizzes yet</h3><p>Finish your first quiz and your score history will appear here.</p>";
    host.append(empty);
    return;
  }

  for (const quiz of profileQuizzes) host.append(historyCard(quiz));
}

async function refreshEmailSettings() {
  renderEmailSettings(await api("/api/account/email-status"));
}

function renderEmailSettings(settings) {
  const current = String(settings?.email || "");
  const pending = String(settings?.pending_email || "");
  text("#profile-current-email", current || "—");
  text("#email-pending-address", pending || "—");
  document.querySelector("#email-change-pending")?.classList.toggle("hidden", !pending);
}

async function refreshFriends() {
  renderFriends(await api("/api/friends"));
}

function renderFriends(payload) {
  const friends = Array.isArray(payload.friends) ? payload.friends : [];
  const incoming = Array.isArray(payload.incoming) ? payload.incoming : [];
  const outgoing = Array.isArray(payload.outgoing) ? payload.outgoing : [];

  text("#friend-count", `${friends.length} ${friends.length === 1 ? "friend" : "friends"}`);
  renderFriendCollection(document.querySelector("#friend-list"), friends, "friend");
  renderFriendCollection(document.querySelector("#friend-incoming"), incoming, "incoming");
  renderFriendCollection(document.querySelector("#friend-outgoing"), outgoing, "outgoing");

  document.querySelector("#friend-requests-wrap")?.classList.toggle("hidden", incoming.length === 0 && outgoing.length === 0);
  if (friends.length === 0) {
    const host = document.querySelector("#friend-list");
    const empty = document.createElement("div");
    empty.className = "friend-empty";
    empty.textContent = "No friends yet. Add someone by their public Factburst username.";
    host.append(empty);
  }
}

function renderFriendCollection(host, items, kind) {
  if (!host) return;
  host.replaceChildren();
  for (const friend of items) host.append(friendCard(friend, kind));
}

function friendCard(friend, kind) {
  const card = document.createElement("article");
  card.className = "friend-card";

  const identity = document.createElement("div");
  identity.className = "friend-identity";
  const name = document.createElement("strong");
  name.textContent = friend.username || "Player";
  const stats = document.createElement("span");
  stats.textContent = `${Number(friend.quizzes_completed || 0)} quizzes • ${Number(friend.percentage || 0)}% accuracy`;
  identity.append(name, stats);

  const actions = document.createElement("div");
  actions.className = "friend-actions";
  if (kind === "friend") {
    const challenge = actionButton("Challenge", "button-primary", () => showChallengePicker(card, friend));
    const remove = actionButton("Remove", "button-secondary", () => removeFriend(friend));
    actions.append(challenge, remove);
  } else if (kind === "incoming") {
    actions.append(
      actionButton("Accept", "button-primary", () => respondFriend(friend, "accept")),
      actionButton("Decline", "button-secondary", () => respondFriend(friend, "decline")),
    );
  } else {
    actions.append(actionButton("Cancel request", "button-secondary", () => removeFriend(friend)));
  }

  card.append(identity, actions);
  return card;
}

function showChallengePicker(card, friend) {
  card.querySelector(".friend-challenge-picker")?.remove();
  const picker = document.createElement("div");
  picker.className = "friend-challenge-picker";

  if (profileQuizzes.length === 0) {
    picker.textContent = "Complete a quiz first, then you can challenge this friend to beat your best score.";
    card.append(picker);
    return;
  }

  const select = document.createElement("select");
  select.setAttribute("aria-label", `Choose a quiz to challenge ${friend.username}`);
  for (const quiz of profileQuizzes) {
    const option = document.createElement("option");
    option.value = quiz.slug;
    option.textContent = `${quiz.title} — ${quiz.best_score}/${quiz.total}`;
    select.append(option);
  }

  const send = document.createElement("button");
  send.type = "button";
  send.className = "button button-primary";
  send.textContent = "Create challenge";
  send.addEventListener("click", async () => {
    send.disabled = true;
    send.textContent = "Creating…";
    try {
      const response = await api("/api/challenges", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ slug: select.value, friend_user_id: friend.user_id }),
      });
      const challenge = response.challenge;
      const shareText = `${challenge.challenger} challenged ${friend.username} to beat ${challenge.score}/${challenge.total} on “${challenge.quiz_title}” at Factburst Quiz.`;
      if (navigator.share) {
        await navigator.share({ title: "Factburst Quiz challenge", text: shareText, url: challenge.url });
      } else {
        await navigator.clipboard.writeText(`${shareText} ${challenge.url}`);
        setFriendStatus(`Challenge for ${friend.username} copied to your clipboard.`, "success");
      }
      picker.remove();
    } catch (error) {
      setFriendStatus(error.message || "Could not create challenge.", "error");
      send.disabled = false;
      send.textContent = "Create challenge";
    }
  });

  picker.append(select, send);
  card.append(picker);
}

async function respondFriend(friend, action) {
  setFriendStatus(`${action === "accept" ? "Accepting" : "Declining"} ${friend.username}…`, "");
  try {
    await api(`/api/friends/${friend.friendship_id}`, {
      method: "PATCH",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ action }),
    });
    setFriendStatus(action === "accept" ? `${friend.username} is now your friend.` : "Friend request declined.", "success");
    await refreshFriends();
  } catch (error) {
    setFriendStatus(error.message || "Could not update friend request.", "error");
  }
}

async function removeFriend(friend) {
  const label = friend.username || "this user";
  if (!confirm(`Remove or cancel the connection with ${label}?`)) return;
  try {
    await api(`/api/friends/${friend.friendship_id}`, { method: "DELETE" });
    setFriendStatus(`Connection with ${label} removed.`, "success");
    await refreshFriends();
  } catch (error) {
    setFriendStatus(error.message || "Could not remove friend.", "error");
  }
}

function actionButton(label, className, handler) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = `button ${className}`;
  button.textContent = label;
  button.addEventListener("click", handler);
  return button;
}

function historyCard(quiz) {
  const article = document.createElement("article");
  article.className = "profile-history-card";

  const main = document.createElement("div");
  main.className = "profile-history-main";
  const title = document.createElement("h3");
  title.textContent = quiz.title || quiz.slug || "Quiz";
  const dates = document.createElement("p");
  dates.textContent = `Last played ${formatDate(quiz.last_completed_at)} • First played ${formatDate(quiz.first_completed_at)}`;
  main.append(title, dates);

  const metrics = document.createElement("div");
  metrics.className = "profile-history-metrics";
  metrics.append(
    metric("Best", `${quiz.best_score}/${quiz.total}`),
    metric("Accuracy", `${quiz.percentage}%`),
    metric("Rank", quiz.leaderboard_rank ? `#${quiz.leaderboard_rank}` : "—"),
    metric("Attempts", String(quiz.attempts || 0)),
  );

  const play = document.createElement("a");
  play.className = "button button-secondary profile-play-again";
  play.href = `/quiz.html?slug=${encodeURIComponent(quiz.slug || "")}`;
  play.textContent = "Play again";

  article.append(main, metrics, play);
  return article;
}

function metric(label, value) {
  const item = document.createElement("div");
  const name = document.createElement("span");
  name.textContent = label;
  const number = document.createElement("strong");
  number.textContent = value;
  item.append(name, number);
  return item;
}

function setEmailStatus(message, kind) {
  if (!emailStatus) return;
  emailStatus.textContent = message;
  emailStatus.dataset.kind = kind || "";
}

function setFriendStatus(message, kind) {
  if (!friendStatus) return;
  friendStatus.textContent = message;
  friendStatus.dataset.kind = kind || "";
}

function formatDate(value) {
  const parsed = new Date(value || "");
  if (Number.isNaN(parsed.getTime())) return "—";
  return new Intl.DateTimeFormat(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(parsed);
}

async function api(url, options = {}) {
  const response = await fetch(url, {
    credentials: "same-origin",
    ...options,
  });
  let payload = null;
  try {
    payload = await response.json();
  } catch {
    payload = null;
  }
  if (!response.ok) {
    throw new Error(payload?.error || `Request failed (${response.status}).`);
  }
  return payload || {};
}

function text(selector, value) {
  const element = document.querySelector(selector);
  if (element) element.textContent = value;
}

function showError(message) {
  loading.classList.add("hidden");
  content.classList.add("hidden");
  errorCopy.textContent = message;
  errorPanel.classList.remove("hidden");
}
