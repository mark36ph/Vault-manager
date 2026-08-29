const loading = document.querySelector("#profile-loading");
const content = document.querySelector("#profile-content");
const errorPanel = document.querySelector("#profile-error");
const errorCopy = document.querySelector("#profile-error-copy");

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

async function initialize() {
  const account = await api("/api/account");
  if (!account.authenticated || !account.user) {
    throw new Error("Log in to your Factburst account to view this page.");
  }
  if (!account.user.email_verified) {
    throw new Error("Verify your email before viewing your full Factburst profile.");
  }

  const history = await api("/api/account/history");
  renderProfile(account.user, history);
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
  const quizzes = Array.isArray(history.quizzes) ? history.quizzes : [];
  if (quizzes.length === 0) {
    const empty = document.createElement("div");
    empty.className = "profile-history-empty";
    empty.innerHTML = "<h3>No completed quizzes yet</h3><p>Finish your first quiz and your score history will appear here.</p>";
    host.append(empty);
    return;
  }

  for (const quiz of quizzes) host.append(historyCard(quiz));
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
