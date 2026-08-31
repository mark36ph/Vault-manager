const page = document.body.dataset.page || "";
const state = { authenticated: false, user: null, mode: "login" };

const headerArea = buildHeaderArea();
const modal = buildModal();
document.body.append(modal.backdrop);

initialize().catch(error => console.error("Could not initialize Factburst account", error));

async function initialize() {
  await loadAccount();
  await handleVerificationLink();
  if (page === "home") await refreshOverallLeaderboard();
  if (page === "quiz") initializeQuizLeaderboard();
}

function buildHeaderArea() {
  const header = document.querySelector(".site-header");
  if (!header) return null;
  const area = document.createElement("div");
  area.className = "account-header-area";
  header.append(area);
  return area;
}

function renderHeader() {
  if (!headerArea) return;
  headerArea.replaceChildren();
  if (!state.authenticated || !state.user) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "account-trigger";
    button.textContent = "Sign up / Log in";
    button.addEventListener("click", () => openModal("login"));
    headerArea.append(button);
    return;
  }

  if (!state.user.email_verified) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "account-trigger signed-in";
    button.textContent = `${state.user.username} · Verify email`;
    button.addEventListener("click", () => openModal("account"));
    headerArea.append(button);
    return;
  }

  const welcome = document.createElement("span");
  welcome.className = "account-welcome";
  welcome.append(document.createTextNode("Welcome "));
  const link = document.createElement("a");
  link.href = "/profile.html";
  link.textContent = state.user.username || "Player";
  welcome.append(link);
  headerArea.append(welcome);
}

function buildModal() {
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
  close.addEventListener("click", closeModal);
  const content = document.createElement("div");
  content.id = "account-dialog-content";
  dialog.append(close, content);
  backdrop.append(dialog);
  backdrop.addEventListener("click", event => { if (event.target === backdrop) closeModal(); });
  document.addEventListener("keydown", event => { if (event.key === "Escape" && !backdrop.classList.contains("hidden")) closeModal(); });
  return { backdrop, content };
}

function openModal(mode = "login", message = "", kind = "") {
  state.mode = mode;
  renderModal(message, kind);
  modal.backdrop.classList.remove("hidden");
  document.body.classList.add("modal-open");
  setTimeout(() => modal.content.querySelector("input, button, a")?.focus(), 0);
}

function closeModal() {
  modal.backdrop.classList.add("hidden");
  document.body.classList.remove("modal-open");
}

function renderModal(message = "", kind = "") {
  modal.content.replaceChildren();
  if (state.authenticated && state.user) {
    renderSignedIn(message, kind);
    return;
  }
  renderAuthForm(message, kind);
}

function renderAuthForm(message = "", kind = "") {
  const eyebrow = el("p", "eyebrow", "Factburst account");
  const title = el("h2", "", state.mode === "signup" ? "Create your account" : "Welcome back");
  title.id = "account-dialog-title";
  const copy = el(
    "p",
    "account-copy",
    state.mode === "signup"
      ? "Create an account and verify your email to save scores, appear on leaderboards, add friends and send challenges. You can always play as a guest without saving anything."
      : "Log in to save your quiz scores and social progress. You can also close this window and keep playing as a guest.",
  );
  const tabs = el("div", "account-tabs");
  tabs.append(tab("Log in", "login"), tab("Sign up", "signup"));
  const form = document.createElement("form");
  form.className = "account-form";
  form.autocomplete = "on";

  const username = inputField("Username", "text", "username", "Your public username");
  username.input.required = true;
  username.input.minLength = 3;
  username.input.maxLength = 24;
  username.input.autocomplete = "username";

  let email = null;
  if (state.mode === "signup") {
    email = inputField("Email", "email", "email", "Used only for account verification");
    email.input.required = true;
    email.input.maxLength = 254;
    email.input.autocomplete = "email";
  }

  const password = inputField("Password", "password", "password", state.mode === "signup" ? "At least 10 characters" : "Your password");
  password.input.required = true;
  password.input.minLength = 10;
  password.input.maxLength = 128;
  password.input.autocomplete = state.mode === "signup" ? "new-password" : "current-password";

  const honeypot = document.createElement("input");
  honeypot.name = "website";
  honeypot.type = "text";
  honeypot.tabIndex = -1;
  honeypot.autocomplete = "off";
  honeypot.className = "account-honeypot";
  honeypot.setAttribute("aria-hidden", "true");

  const status = statusLine(message, kind);
  const submit = el("button", "button button-primary account-submit", state.mode === "signup" ? "Create account" : "Log in");
  submit.type = "submit";
  const guest = el("button", "button button-secondary account-submit", "Continue as guest");
  guest.type = "button";
  guest.addEventListener("click", closeModal);
  const privacy = el("p", "account-fine-print", "Your username is public. Your email stays private and is used for verification/account access.");

  form.append(username.label);
  if (email) form.append(email.label);
  form.append(password.label, honeypot, status, submit, guest);
  form.addEventListener("submit", async event => {
    event.preventDefault();
    submit.disabled = true;
    submit.textContent = state.mode === "signup" ? "Creating…" : "Logging in…";
    status.textContent = "";
    try {
      const response = await api(`/api/account/${state.mode}`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          username: username.input.value,
          email: email?.input.value || "",
          password: password.input.value,
          website: honeypot.value,
        }),
      });
      applyAccount(response);
      const defaultMessage = state.mode === "signup"
        ? "Account created. Check your email to verify it."
        : (state.user?.email_verified ? "Logged in. Future quiz results will be saved." : "Logged in. Verify your email to save scores.");
      renderModal(response.message || defaultMessage, response.verification_sent === false ? "error" : "success");
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

function renderSignedIn(message = "", kind = "") {
  const user = state.user;
  const eyebrow = el("p", "eyebrow", "Your Factburst account");
  const title = el("h2", "", user.username || "Player");
  title.id = "account-dialog-title";
  modal.content.append(eyebrow, title);

  if (!user.email_verified) {
    const warning = statusLine(user.email
      ? `Verify ${user.email} to save scores, comment, add friends and send challenges. You can still play quizzes as a guest.`
      : "Add and verify an email to save scores and use account features. You can still play quizzes as a guest.", "error");
    modal.content.append(warning);
    if (message) modal.content.append(statusLine(message, kind));
    renderVerificationActions(user);
    renderLogout();
    return;
  }

  modal.content.append(statusLine(message || `✓ Email verified: ${user.email}`, kind || "success"));
  const actions = el("div", "result-actions");
  const profile = el("a", "button button-primary", "Open profile");
  profile.href = "/profile.html";
  actions.append(profile);
  modal.content.append(actions);
}

function renderVerificationActions(user) {
  if (!user.email) {
    const form = document.createElement("form");
    form.className = "account-form";
    const email = inputField("Email", "email", "email", "Your email address");
    email.input.required = true;
    const status = statusLine();
    const submit = el("button", "button button-primary", "Send verification email");
    submit.type = "submit";
    form.append(email.label, status, submit);
    form.addEventListener("submit", async event => {
      event.preventDefault();
      submit.disabled = true;
      try {
        const response = await api("/api/account/email", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ email: email.input.value }),
        });
        applyAccount(response);
        renderModal(response.message || "Verification email sent.", response.verification_sent ? "success" : "error");
      } catch (error) {
        status.textContent = error.message || "Could not send verification email.";
        status.dataset.kind = "error";
        submit.disabled = false;
      }
    });
    modal.content.append(form);
    return;
  }

  const actions = el("div", "result-actions");
  const resend = el("button", "button button-primary", "Resend verification email");
  resend.type = "button";
  resend.addEventListener("click", async () => {
    resend.disabled = true;
    try {
      const response = await api("/api/account/resend-verification", { method: "POST" });
      applyAccount(response);
      renderModal(response.message || "Verification email sent.", response.verification_sent ? "success" : "error");
    } catch (error) {
      renderModal(error.message || "Could not send verification email.", "error");
    }
  });
  const change = el("button", "button button-secondary", "Use a different email");
  change.type = "button";
  change.addEventListener("click", () => renderEmailChange());
  actions.append(resend, change);
  modal.content.append(actions);
}

function renderEmailChange() {
  modal.content.replaceChildren();
  modal.content.append(el("p", "eyebrow", "Email verification"));
  const title = el("h2", "", "Use a different email");
  title.id = "account-dialog-title";
  modal.content.append(title);
  const form = document.createElement("form");
  form.className = "account-form";
  const field = inputField("New email", "email", "email", "Your new email address");
  field.input.required = true;
  const status = statusLine();
  const submit = el("button", "button button-primary", "Update and send verification");
  submit.type = "submit";
  const back = el("button", "button button-secondary", "Back");
  back.type = "button";
  back.addEventListener("click", () => renderModal());
  form.append(field.label, status, submit, back);
  form.addEventListener("submit", async event => {
    event.preventDefault();
    submit.disabled = true;
    try {
      const response = await api("/api/account/email", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ email: field.input.value }),
      });
      applyAccount(response);
      renderModal(response.message || "Verification email sent.", response.verification_sent ? "success" : "error");
    } catch (error) {
      status.textContent = error.message || "Could not update email.";
      status.dataset.kind = "error";
      submit.disabled = false;
    }
  });
  modal.content.append(form);
}

function renderLogout() {
  const logout = el("button", "button button-secondary", "Log out");
  logout.type = "button";
  logout.addEventListener("click", async () => {
    logout.disabled = true;
    try {
      await api("/api/account/logout", { method: "POST" });
      applyAccount({ authenticated: false, user: null });
      closeModal();
      await refreshLeaderboards();
    } catch {
      logout.disabled = false;
    }
  });
  modal.content.append(logout);
}

function renderHomeSummary() {
  const container = document.querySelector("#account-summary");
  if (!container) return;
  container.replaceChildren();
  if (!state.authenticated || !state.user) {
    const copy = el("p", "", "Play any quiz as a guest — guest scores are not saved anywhere. Sign up and verify your email if you want high scores, history, friends, comments and challenges.");
    const action = el("button", "button button-primary", "Sign up / Log in");
    action.type = "button";
    action.addEventListener("click", () => openModal("signup"));
    container.append(copy, action);
    return;
  }
  if (!state.user.email_verified) {
    const copy = el("p", "", `You can keep playing as a guest, but results will not be saved until ${state.user.email || "your email"} is verified.`);
    const action = el("button", "button button-primary", "Verify email");
    action.type = "button";
    action.addEventListener("click", () => openModal("account"));
    container.append(copy, action);
    return;
  }
  const user = state.user;
  container.append(
    statCard("Best points", `${user.total_score}/${user.total_possible}`),
    statCard("Quizzes completed", String(user.quizzes_completed)),
    statCard("Overall accuracy", `${user.percentage}%`),
  );
  const profile = el("a", "button button-secondary account-summary-profile", "Open full profile");
  profile.href = "/profile.html";
  container.append(profile);
}

function initializeQuizLeaderboard() {
  const slug = currentSlug();
  if (!slug) return;
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

function renderResultAccountNote(results) {
  let note = results.querySelector(".result-account-note");
  if (!note) {
    note = el("div", "result-account-note");
    const actions = results.querySelector(".result-actions");
    if (actions) results.insertBefore(note, actions);
    else results.append(note);
  }
  note.replaceChildren();
  if (canSaveScores()) {
    const strong = el("strong", "", "Score saved.");
    const copy = el("span", "", ` Your best scores now total ${state.user.total_score}/${state.user.total_possible} across ${state.user.quizzes_completed} quiz${state.user.quizzes_completed === 1 ? "" : "zes"}.`);
    note.append(strong, copy);
    return;
  }
  const strong = el("strong", "", "Guest result — not saved.");
  const copy = el("span", "", " This score was calculated for you but was not stored in your history or on a leaderboard.");
  const action = el("button", "account-inline-action", state.authenticated ? "Verify email to save future scores" : "Sign up to save future scores");
  action.type = "button";
  action.addEventListener("click", () => openModal(state.authenticated ? "account" : "signup"));
  note.append(strong, copy, action);
}

async function refreshOverallLeaderboard() {
  const container = document.querySelector("#overall-leaderboard");
  if (!container) return;
  container.replaceChildren(messageLine("Loading overall scores…"));
  try {
    const response = await api("/api/leaderboard?limit=25");
    renderLeaderboard(container, response.leaderboard || [], "overall");
  } catch {
    container.replaceChildren(messageLine("The overall leaderboard is not available yet."));
  }
}

async function refreshQuizLeaderboard(slug) {
  const container = document.querySelector("#quiz-leaderboard-list");
  const mine = document.querySelector("#quiz-leaderboard-mine");
  if (!container) return;
  container.replaceChildren(messageLine("Loading high scores…"));
  if (mine) mine.replaceChildren();
  try {
    const response = await api(`/api/quizzes/${encodeURIComponent(slug)}/leaderboard?limit=25`);
    renderLeaderboard(container, response.leaderboard || [], "quiz");
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

function renderLeaderboard(container, rows, kind) {
  container.replaceChildren();
  if (!Array.isArray(rows) || rows.length === 0) {
    container.append(messageLine("No scores yet. Be the first on the board."));
    return;
  }
  const table = el("div", "leaderboard-table");
  const header = el("div", "leaderboard-row leaderboard-header");
  header.append(cell("Rank", "rank"), cell("Player", "player"), cell(kind === "overall" ? "Best points" : "Best score", "score"), cell(kind === "overall" ? "Quizzes" : "Accuracy", "extra"));
  table.append(header);
  for (const row of rows) {
    const line = el("div", `leaderboard-row${row.current_user ? " current-user" : ""}`);
    line.append(
      cell(`#${row.rank}`, "rank"),
      cell(row.username, "player"),
      cell(kind === "overall" ? `${row.total_score}/${row.total_possible}` : `${row.score}/${row.total}`, "score"),
      cell(kind === "overall" ? `${row.quizzes_completed} · ${row.percentage}%` : `${row.percentage}%`, "extra"),
    );
    table.append(line);
  }
  container.append(table);
}

async function handleVerificationLink() {
  const params = new URLSearchParams(location.search);
  const token = params.get("verify_email") || "";
  if (!token) return;
  try {
    const response = await api(`/api/account/verify?token=${encodeURIComponent(token)}`);
    params.delete("verify_email");
    history.replaceState({}, "", `${location.pathname}${params.toString() ? `?${params}` : ""}${location.hash}`);
    await loadAccount();
    openModal("account", response.message || "Email verified.", "success");
  } catch (error) {
    params.delete("verify_email");
    history.replaceState({}, "", `${location.pathname}${params.toString() ? `?${params}` : ""}${location.hash}`);
    openModal(state.authenticated ? "account" : "login", error.message || "Could not verify that email.", "error");
  }
}

async function loadAccount() {
  try {
    applyAccount(await api("/api/account"));
  } catch (error) {
    applyAccount({ authenticated: false, user: null });
    if (error.message) console.warn(error.message);
  }
}

function applyAccount(response) {
  state.authenticated = Boolean(response?.authenticated && response?.user);
  state.user = state.authenticated ? response.user : null;
  renderHeader();
  renderHomeSummary();
}

function canSaveScores() {
  return Boolean(state.authenticated && state.user?.email_verified);
}

async function refreshLeaderboards() {
  await loadAccount();
  if (page === "home") await refreshOverallLeaderboard();
  if (page === "quiz") {
    const slug = currentSlug();
    if (slug) await refreshQuizLeaderboard(slug);
  }
}

function currentSlug() {
  const querySlug = new URLSearchParams(location.search).get("slug") || "";
  if (/^[a-z0-9][a-z0-9-]{0,79}$/i.test(querySlug)) return querySlug.toLowerCase();
  const match = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})\/?$/i);
  return match ? match[1].toLowerCase() : "";
}

function tab(label, mode) {
  const button = el("button", `account-tab${state.mode === mode ? " active" : ""}`, label);
  button.type = "button";
  button.addEventListener("click", () => { state.mode = mode; renderModal(); });
  return button;
}

function inputField(labelText, type, name, placeholder) {
  const label = document.createElement("label");
  label.textContent = labelText;
  const input = document.createElement("input");
  input.type = type;
  input.name = name;
  input.placeholder = placeholder;
  label.append(input);
  return { label, input };
}

function statusLine(message = "", kind = "") {
  const status = el("p", "account-status", message);
  if (kind) status.dataset.kind = kind;
  return status;
}

function statCard(label, value) {
  const card = el("div", "account-stat");
  card.append(el("strong", "", value), el("span", "", label));
  return card;
}

function cell(text, kind) {
  return el("span", `leaderboard-cell ${kind}`, text);
}

function messageLine(text) {
  return el("p", "leaderboard-message", text);
}

function el(tag, className = "", text = "") {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== "") node.textContent = text;
  return node;
}

async function api(url, options = {}) {
  const response = await fetch(url, { credentials: "same-origin", ...options });
  let payload = null;
  try { payload = await response.json(); } catch { payload = null; }
  if (!response.ok) throw new Error(payload?.error || `Request failed (${response.status}).`);
  return payload || {};
}
