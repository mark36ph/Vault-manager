const page = document.body.dataset.page || "";

const state = {
  authenticated: false,
  user: null,
  mode: "login",
};

const trigger = buildAccountTrigger();
const modal = buildAccountModal();
document.body.append(modal.backdrop);

document.addEventListener("click", (event) => {
  const link = event.target.closest?.("a");
  if (!link) return;
  const href = link.getAttribute("href") || "";
  if (!isQuizLink(href)) return;
  if (canPlay()) return;
  event.preventDefault();
  openAccountModal(state.authenticated ? "account" : "signup");
}, true);

loadAccount().then(async () => {
  await handleVerificationLink();
  if (page === "home") refreshOverallLeaderboard();
  if (page === "quiz") {
    initializeQuizLeaderboard();
    if (!canPlay()) openAccountModal(state.authenticated ? "account" : "login");
  }
}).catch(error => console.error("Could not initialize Factburst account", error));

function buildAccountTrigger() {
  const header = document.querySelector(".site-header");
  if (!header) return null;

  const button = document.createElement("button");
  button.type = "button";
  button.className = "account-trigger";
  button.textContent = "Sign up / Log in";
  button.addEventListener("click", () => openAccountModal(state.authenticated ? "account" : "login"));
  header.append(button);
  return button;
}

function buildAccountModal() {
  const backdrop = document.createElement("div");
  backdrop.className = "account-backdrop hidden";
  backdrop.setAttribute("role", "presentation");

  const dialog = document.createElement("section");
  dialog.className = "account-dialog";
  dialog.setAttribute("role", "dialog");
  dialog.setAttribute("aria-modal", "true");
  dialog.setAttribute("aria-labelledby", "account-dialog-title");

  const close = document.createElement("button");
  close.type = "button";
  close.className = "account-close";
  close.setAttribute("aria-label", "Close account window");
  close.textContent = "×";
  close.addEventListener("click", closeAccountModal);

  const content = document.createElement("div");
  content.id = "account-dialog-content";
  dialog.append(close, content);
  backdrop.append(dialog);

  backdrop.addEventListener("click", (event) => {
    if (event.target === backdrop) closeAccountModal();
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !backdrop.classList.contains("hidden")) closeAccountModal();
  });

  return { backdrop, content };
}

function openAccountModal(mode = "login", message = "", messageKind = "") {
  state.mode = mode;
  renderAccountModal(message, messageKind);
  modal.backdrop.classList.remove("hidden");
  document.body.classList.add("modal-open");
  setTimeout(() => modal.content.querySelector("input, button")?.focus(), 0);
}

function closeAccountModal() {
  modal.backdrop.classList.add("hidden");
  document.body.classList.remove("modal-open");
}

function renderAccountModal(message = "", messageKind = "") {
  modal.content.replaceChildren();

  if (state.authenticated && state.user) {
    renderSignedInAccount(message, messageKind);
    return;
  }

  const eyebrow = document.createElement("p");
  eyebrow.className = "eyebrow";
  eyebrow.textContent = "Factburst account";
  const title = document.createElement("h2");
  title.id = "account-dialog-title";
  title.textContent = state.mode === "signup" ? "Create your account" : "Welcome back";
  const copy = document.createElement("p");
  copy.className = "account-copy";
  copy.textContent = state.mode === "signup"
    ? "Create an account and verify your email to unlock quizzes, save high scores and build your overall total."
    : "Log in with your verified Factburst account to play quizzes and save scores.";

  const tabs = document.createElement("div");
  tabs.className = "account-tabs";
  tabs.append(tabButton("Log in", "login"), tabButton("Sign up", "signup"));

  const form = document.createElement("form");
  form.className = "account-form";
  form.autocomplete = "on";

  const usernameLabel = document.createElement("label");
  usernameLabel.textContent = "Username";
  const username = document.createElement("input");
  username.name = "username";
  username.type = "text";
  username.required = true;
  username.minLength = 3;
  username.maxLength = 24;
  username.autocomplete = "username";
  username.placeholder = "Your public leaderboard name";
  usernameLabel.append(username);

  let email = null;
  let emailLabel = null;
  if (state.mode === "signup") {
    emailLabel = document.createElement("label");
    emailLabel.textContent = "Email";
    email = document.createElement("input");
    email.name = "email";
    email.type = "email";
    email.required = true;
    email.maxLength = 254;
    email.autocomplete = "email";
    email.placeholder = "Used to verify your account";
    emailLabel.append(email);
  }

  const passwordLabel = document.createElement("label");
  passwordLabel.textContent = "Password";
  const password = document.createElement("input");
  password.name = "password";
  password.type = "password";
  password.required = true;
  password.minLength = 10;
  password.maxLength = 128;
  password.autocomplete = state.mode === "signup" ? "new-password" : "current-password";
  password.placeholder = state.mode === "signup" ? "At least 10 characters" : "Your password";
  passwordLabel.append(password);

  const honeypot = document.createElement("input");
  honeypot.name = "website";
  honeypot.type = "text";
  honeypot.tabIndex = -1;
  honeypot.autocomplete = "off";
  honeypot.className = "account-honeypot";
  honeypot.setAttribute("aria-hidden", "true");

  const status = statusLine(message, messageKind);

  const submit = document.createElement("button");
  submit.type = "submit";
  submit.className = "button button-primary account-submit";
  submit.textContent = state.mode === "signup" ? "Create account" : "Log in";

  const privacy = document.createElement("p");
  privacy.className = "account-fine-print";
  privacy.textContent = "Your username appears publicly on leaderboards. Your email is private and is used for account verification and access.";

  form.append(usernameLabel);
  if (emailLabel) form.append(emailLabel);
  form.append(passwordLabel, honeypot, status, submit);

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    submit.disabled = true;
    submit.textContent = state.mode === "signup" ? "Creating…" : "Logging in…";
    status.textContent = "";

    try {
      const response = await api(`/api/account/${state.mode}`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          username: username.value,
          email: email?.value || "",
          password: password.value,
          website: honeypot.value,
        }),
      });
      applyAccount(response);
      if (canPlay() && page === "quiz") {
        location.reload();
        return;
      }
      const defaultMessage = state.mode === "signup"
        ? "Account created. Check your email and verify it before playing."
        : (state.user?.email_verified ? "Logged in." : "Logged in. Verify your email before playing.");
      renderAccountModal(response.message || defaultMessage, response.verification_sent === false ? "error" : "success");
      await refreshLeaderboards();
    } catch (error) {
      status.textContent = error.message || "Could not continue.";
      status.dataset.kind = "error";
      submit.disabled = false;
      submit.textContent = state.mode === "signup" ? "Create account" : "Log in";
    }
  });

  modal.content.append(eyebrow, title, copy, tabs, form, privacy);
}

function tabButton(label, mode) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "account-tab" + (state.mode === mode ? " active" : "");
  button.textContent = label;
  button.addEventListener("click", () => {
    state.mode = mode;
    renderAccountModal();
  });
  return button;
}

function renderSignedInAccount(message = "", messageKind = "") {
  const user = state.user;
  const eyebrow = document.createElement("p");
  eyebrow.className = "eyebrow";
  eyebrow.textContent = "Your Factburst account";
  const title = document.createElement("h2");
  title.id = "account-dialog-title";
  title.textContent = user.username;
  modal.content.append(eyebrow, title);

  if (!user.email_verified) {
    renderVerificationRequired(user, message, messageKind);
    renderLogout();
    return;
  }

  const verified = document.createElement("p");
  verified.className = "account-status";
  verified.dataset.kind = "success";
  verified.textContent = `✓ Email verified: ${user.email}`;

  const stats = document.createElement("div");
  stats.className = "account-stats";
  stats.append(
    statCard("Total score", `${user.total_score}/${user.total_possible}`),
    statCard("Quizzes", String(user.quizzes_completed)),
    statCard("Accuracy", `${user.percentage}%`),
  );

  const rule = document.createElement("p");
  rule.className = "account-copy";
  rule.textContent = "Your total uses your best score from each unique quiz. Retakes can improve a quiz score but never add the same quiz twice.";

  modal.content.append(verified, stats, rule);
  if (message) modal.content.append(statusLine(message, messageKind));
  renderLogout();
}

function renderVerificationRequired(user, message = "", messageKind = "") {
  const warning = document.createElement("p");
  warning.className = "account-status";
  warning.dataset.kind = "error";
  warning.textContent = user.email
    ? `Verify ${user.email} before you can play quizzes.`
    : "Add and verify an email address before you can play quizzes.";
  modal.content.append(warning);

  if (message) modal.content.append(statusLine(message, messageKind));

  if (!user.email) {
    const form = document.createElement("form");
    form.className = "account-form";
    const label = document.createElement("label");
    label.textContent = "Email";
    const input = document.createElement("input");
    input.type = "email";
    input.required = true;
    input.maxLength = 254;
    input.autocomplete = "email";
    input.placeholder = "Your email address";
    label.append(input);

    const status = statusLine();
    const submit = document.createElement("button");
    submit.type = "submit";
    submit.className = "button button-primary";
    submit.textContent = "Send verification email";

    form.append(label, status, submit);
    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      submit.disabled = true;
      submit.textContent = "Sending…";
      try {
        const response = await api("/api/account/email", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ email: input.value }),
        });
        applyAccount(response);
        renderAccountModal(response.message || "Verification email sent.", response.verification_sent ? "success" : "error");
      } catch (error) {
        status.textContent = error.message || "Could not send verification email.";
        status.dataset.kind = "error";
        submit.disabled = false;
        submit.textContent = "Send verification email";
      }
    });
    modal.content.append(form);
    return;
  }

  const resend = document.createElement("button");
  resend.type = "button";
  resend.className = "button button-primary";
  resend.textContent = "Resend verification email";
  resend.addEventListener("click", async () => {
    resend.disabled = true;
    resend.textContent = "Sending…";
    try {
      const response = await api("/api/account/resend-verification", { method: "POST" });
      applyAccount(response);
      renderAccountModal(response.message || "Verification email sent.", response.verification_sent ? "success" : "error");
    } catch (error) {
      renderAccountModal(error.message || "Could not send verification email.", "error");
    }
  });

  const change = document.createElement("button");
  change.type = "button";
  change.className = "button button-secondary";
  change.textContent = "Use a different email";
  change.addEventListener("click", () => renderEmailChangeForm());

  const actions = document.createElement("div");
  actions.className = "result-actions";
  actions.append(resend, change);
  modal.content.append(actions);
}

function renderEmailChangeForm() {
  modal.content.replaceChildren();
  const eyebrow = document.createElement("p");
  eyebrow.className = "eyebrow";
  eyebrow.textContent = "Email verification";
  const title = document.createElement("h2");
  title.id = "account-dialog-title";
  title.textContent = "Use a different email";

  const form = document.createElement("form");
  form.className = "account-form";
  const label = document.createElement("label");
  label.textContent = "New email";
  const input = document.createElement("input");
  input.type = "email";
  input.required = true;
  input.maxLength = 254;
  input.autocomplete = "email";
  label.append(input);
  const status = statusLine();
  const submit = document.createElement("button");
  submit.type = "submit";
  submit.className = "button button-primary";
  submit.textContent = "Update and send verification";
  const cancel = document.createElement("button");
  cancel.type = "button";
  cancel.className = "button button-secondary";
  cancel.textContent = "Back";
  cancel.addEventListener("click", () => renderAccountModal());

  form.append(label, status, submit, cancel);
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    submit.disabled = true;
    try {
      const response = await api("/api/account/email", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ email: input.value }),
      });
      applyAccount(response);
      renderAccountModal(response.message || "Verification email sent.", response.verification_sent ? "success" : "error");
    } catch (error) {
      status.textContent = error.message || "Could not update email.";
      status.dataset.kind = "error";
      submit.disabled = false;
    }
  });

  modal.content.append(eyebrow, title, form);
}

function renderLogout() {
  const logout = document.createElement("button");
  logout.type = "button";
  logout.className = "button button-secondary";
  logout.textContent = "Log out";
  logout.addEventListener("click", async () => {
    logout.disabled = true;
    try {
      await api("/api/account/logout", { method: "POST" });
      applyAccount({ authenticated: false, user: null });
      state.mode = "login";
      if (page === "quiz") {
        location.href = "/";
        return;
      }
      renderAccountModal("Logged out.", "success");
      await refreshLeaderboards();
    } catch {
      logout.disabled = false;
    }
  });
  modal.content.append(logout);
}

function statCard(label, value) {
  const card = document.createElement("div");
  card.className = "account-stat";
  const number = document.createElement("strong");
  number.textContent = value;
  const caption = document.createElement("span");
  caption.textContent = label;
  card.append(number, caption);
  return card;
}

function statusLine(message = "", kind = "") {
  const status = document.createElement("p");
  status.className = "account-status";
  status.textContent = message;
  if (kind) status.dataset.kind = kind;
  return status;
}

async function loadAccount() {
  try {
    applyAccount(await api("/api/account"));
  } catch (error) {
    applyAccount({ authenticated: false, user: null });
    console.error("Could not load Factburst account", error);
  }
}

async function handleVerificationLink() {
  const params = new URLSearchParams(location.search);
  const token = params.get("verify_email") || "";
  if (!token) return;

  try {
    const response = await api(`/api/account/verify?token=${encodeURIComponent(token)}`);
    params.delete("verify_email");
    const query = params.toString();
    history.replaceState({}, "", `${location.pathname}${query ? `?${query}` : ""}${location.hash}`);
    await loadAccount();
    openAccountModal(state.authenticated ? "account" : "login", response.message || "Email verified.", "success");
  } catch (error) {
    params.delete("verify_email");
    const query = params.toString();
    history.replaceState({}, "", `${location.pathname}${query ? `?${query}` : ""}${location.hash}`);
    openAccountModal(state.authenticated ? "account" : "login", error.message || "Could not verify that email.", "error");
  }
}

function applyAccount(response) {
  state.authenticated = Boolean(response?.authenticated && response?.user);
  state.user = state.authenticated ? response.user : null;
  renderTrigger();
  renderHomeAccountSummary();
}

function canPlay() {
  return Boolean(state.authenticated && state.user?.email_verified);
}

function renderTrigger() {
  if (!trigger) return;
  if (!state.authenticated || !state.user) {
    trigger.textContent = "Sign up / Log in";
    trigger.classList.remove("signed-in");
    return;
  }
  trigger.textContent = state.user.email_verified
    ? `${state.user.username} · ${state.user.total_score}/${state.user.total_possible}`
    : `${state.user.username} · Verify email`;
  trigger.classList.add("signed-in");
}

function renderHomeAccountSummary() {
  const container = document.querySelector("#account-summary");
  if (!container) return;
  container.replaceChildren();

  if (!state.authenticated || !state.user) {
    const copy = document.createElement("p");
    copy.textContent = "Sign up and verify your email to unlock quizzes, save high scores and build an overall total.";
    const action = document.createElement("button");
    action.type = "button";
    action.className = "button button-primary";
    action.textContent = "Sign up / Log in";
    action.addEventListener("click", () => openAccountModal("signup"));
    container.append(copy, action);
    return;
  }

  if (!state.user.email_verified) {
    const copy = document.createElement("p");
    copy.textContent = state.user.email
      ? `Verify ${state.user.email} to unlock quizzes.`
      : "Add and verify an email address to unlock quizzes.";
    const action = document.createElement("button");
    action.type = "button";
    action.className = "button button-primary";
    action.textContent = "Verify email";
    action.addEventListener("click", () => openAccountModal("account"));
    container.append(copy, action);
    return;
  }

  const user = state.user;
  container.append(
    statCard("Best points", `${user.total_score}/${user.total_possible}`),
    statCard("Quizzes completed", String(user.quizzes_completed)),
    statCard("Overall accuracy", `${user.percentage}%`),
  );
}

async function refreshOverallLeaderboard() {
  const container = document.querySelector("#overall-leaderboard");
  if (!container) return;
  container.replaceChildren(loadingLine("Loading overall scores…"));
  try {
    const response = await api("/api/leaderboard?limit=25");
    renderLeaderboardTable(container, response.leaderboard || [], "overall");
  } catch {
    container.replaceChildren(messageLine("The overall leaderboard is not available yet."));
  }
}

function initializeQuizLeaderboard() {
  const slug = new URLSearchParams(location.search).get("slug") || "";
  if (!/^[a-z0-9][a-z0-9-]{0,79}$/i.test(slug)) return;
  refreshQuizLeaderboard(slug);

  const results = document.querySelector("#quiz-results");
  if (!results) return;
  const observer = new MutationObserver(() => {
    if (!results.classList.contains("hidden")) {
      setTimeout(async () => {
        await loadAccount();
        await refreshQuizLeaderboard(slug);
        renderResultAccountNote(results);
      }, 100);
    }
  });
  observer.observe(results, { attributes: true, attributeFilter: ["class"] });
}

async function refreshQuizLeaderboard(slug) {
  const container = document.querySelector("#quiz-leaderboard-list");
  const mine = document.querySelector("#quiz-leaderboard-mine");
  if (!container) return;
  container.replaceChildren(loadingLine("Loading high scores…"));
  if (mine) mine.replaceChildren();

  try {
    const response = await api(`/api/quizzes/${encodeURIComponent(slug)}/leaderboard?limit=25`);
    renderLeaderboardTable(container, response.leaderboard || [], "quiz");
    if (mine && response.mine) {
      mine.textContent = `Your best: ${response.mine.score}/${response.mine.total} · Rank #${response.mine.rank} · ${response.mine.attempts} attempt${response.mine.attempts === 1 ? "" : "s"}`;
      mine.classList.remove("hidden");
    } else if (mine) {
      mine.classList.add("hidden");
    }
  } catch {
    container.replaceChildren(messageLine("No high scores to show yet."));
  }
}

function renderLeaderboardTable(container, rows, kind) {
  container.replaceChildren();
  if (!Array.isArray(rows) || rows.length === 0) {
    container.append(messageLine("No scores yet. Be the first on the board."));
    return;
  }

  const table = document.createElement("div");
  table.className = "leaderboard-table";
  const header = document.createElement("div");
  header.className = "leaderboard-row leaderboard-header";
  header.append(
    leaderboardCell("Rank", "rank"),
    leaderboardCell("Player", "player"),
    leaderboardCell(kind === "overall" ? "Best points" : "Best score", "score"),
    leaderboardCell(kind === "overall" ? "Quizzes" : "Accuracy", "extra"),
  );
  table.append(header);

  for (const row of rows) {
    const line = document.createElement("div");
    line.className = "leaderboard-row" + (row.current_user ? " current-user" : "");
    line.append(
      leaderboardCell(`#${row.rank}`, "rank"),
      leaderboardCell(row.username, "player"),
      leaderboardCell(kind === "overall" ? `${row.total_score}/${row.total_possible}` : `${row.score}/${row.total}`, "score"),
      leaderboardCell(kind === "overall" ? `${row.quizzes_completed} · ${row.percentage}%` : `${row.percentage}%`, "extra"),
    );
    table.append(line);
  }
  container.append(table);
}

function leaderboardCell(text, kind) {
  const cell = document.createElement("span");
  cell.className = `leaderboard-cell ${kind}`;
  cell.textContent = text;
  return cell;
}

function renderResultAccountNote(results) {
  let note = results.querySelector(".result-account-note");
  if (!note) {
    note = document.createElement("div");
    note.className = "result-account-note";
    const actions = results.querySelector(".result-actions");
    if (actions) results.insertBefore(note, actions);
    else results.append(note);
  }
  note.replaceChildren();

  if (canPlay()) {
    const strong = document.createElement("strong");
    strong.textContent = "Score saved.";
    const copy = document.createElement("span");
    copy.textContent = ` Your best scores now total ${state.user.total_score}/${state.user.total_possible} across ${state.user.quizzes_completed} quiz${state.user.quizzes_completed === 1 ? "" : "zes"}.`;
    note.append(strong, copy);
  }
}

async function refreshLeaderboards() {
  await loadAccount();
  if (page === "home") await refreshOverallLeaderboard();
  if (page === "quiz") {
    const slug = new URLSearchParams(location.search).get("slug") || "";
    if (/^[a-z0-9][a-z0-9-]{0,79}$/i.test(slug)) await refreshQuizLeaderboard(slug);
  }
}

function isQuizLink(href) {
  try {
    const url = new URL(href, location.href);
    if (url.origin !== location.origin) return false;
    return (url.pathname === "/quiz.html" || url.pathname === "/quiz") && Boolean(url.searchParams.get("slug"));
  } catch {
    return false;
  }
}

function loadingLine(text) {
  const paragraph = document.createElement("p");
  paragraph.className = "leaderboard-message";
  paragraph.textContent = text;
  return paragraph;
}

function messageLine(text) {
  const paragraph = document.createElement("p");
  paragraph.className = "leaderboard-message";
  paragraph.textContent = text;
  return paragraph;
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
  if (!response.ok) throw new Error(payload?.error || `Request failed (${response.status}).`);
  return payload || {};
}
