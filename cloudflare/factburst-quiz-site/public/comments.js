const slug = new URLSearchParams(location.search).get("slug") || "";
const list = document.querySelector("#quiz-comments-list");
const composer = document.querySelector("#quiz-comment-composer");
const status = document.querySelector("#quiz-comment-status");

if (document.body.dataset.page === "quiz" && /^[a-z0-9][a-z0-9-]{0,79}$/i.test(slug)) {
  initializeComments().catch(error => setStatus(error.message || "Comments are unavailable.", "error"));
}

async function initializeComments() {
  await refreshComments();
  const account = await fetchAccount();
  renderComposer(account);
}

async function refreshComments() {
  if (!list) return;
  list.replaceChildren(message("Loading comments…"));
  try {
    const response = await api(`/api/quizzes/${encodeURIComponent(slug)}/comments?limit=100`);
    const comments = Array.isArray(response.comments) ? response.comments : [];
    list.replaceChildren();
    if (comments.length === 0) {
      list.append(message("No comments yet. Be the first to start the conversation."));
      return;
    }
    for (const comment of comments) list.append(commentCard(comment));
  } catch (error) {
    list.replaceChildren(message(error.message || "Comments are unavailable."));
  }
}

async function fetchAccount() {
  try {
    return await api("/api/account");
  } catch {
    return { authenticated: false, user: null };
  }
}

function renderComposer(account) {
  if (!composer) return;
  composer.replaceChildren();
  const user = account?.authenticated ? account.user : null;
  if (!user?.email_verified) {
    const note = document.createElement("p");
    note.className = "comment-login-note";
    note.textContent = user
      ? "Verify your email before posting comments. You can still read every comment."
      : "Log in with a verified Factburst account to post a comment. Everyone can read comments.";
    composer.append(note);
    return;
  }

  const form = document.createElement("form");
  form.className = "comment-form";
  const label = document.createElement("label");
  label.textContent = `Comment as ${user.username}`;
  const textarea = document.createElement("textarea");
  textarea.required = true;
  textarea.minLength = 2;
  textarea.maxLength = 600;
  textarea.rows = 4;
  textarea.placeholder = "Share your thoughts about this quiz…";
  label.append(textarea);
  const actions = document.createElement("div");
  actions.className = "comment-form-actions";
  const counter = document.createElement("span");
  counter.textContent = "0 / 600";
  const submit = document.createElement("button");
  submit.type = "submit";
  submit.className = "button button-primary";
  submit.textContent = "Post comment";
  actions.append(counter, submit);
  textarea.addEventListener("input", () => { counter.textContent = `${textarea.value.length} / 600`; });
  form.append(label, actions);
  form.addEventListener("submit", async event => {
    event.preventDefault();
    submit.disabled = true;
    submit.textContent = "Posting…";
    setStatus("", "");
    try {
      await api(`/api/quizzes/${encodeURIComponent(slug)}/comments`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ comment: textarea.value }),
      });
      textarea.value = "";
      counter.textContent = "0 / 600";
      setStatus("Comment posted.", "success");
      await refreshComments();
    } catch (error) {
      setStatus(error.message || "Could not post comment.", "error");
    } finally {
      submit.disabled = false;
      submit.textContent = "Post comment";
    }
  });
  composer.append(form);
}

function commentCard(comment) {
  const article = document.createElement("article");
  article.className = "comment-card";
  const header = document.createElement("div");
  header.className = "comment-meta";
  const name = document.createElement("strong");
  name.textContent = comment.username || "Player";
  const time = document.createElement("span");
  time.textContent = formatDate(comment.created_at);
  header.append(name, time);
  const body = document.createElement("p");
  body.textContent = comment.body || "";
  article.append(header, body);
  return article;
}

function formatDate(value) {
  const date = new Date(value || "");
  if (Number.isNaN(date.getTime())) return "";
  return new Intl.DateTimeFormat(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function message(text) {
  const p = document.createElement("p");
  p.className = "comments-message";
  p.textContent = text;
  return p;
}

function setStatus(text, kind) {
  if (!status) return;
  status.textContent = text;
  status.dataset.kind = kind || "";
}

async function api(url, options = {}) {
  const response = await fetch(url, { credentials: "same-origin", ...options });
  let payload = null;
  try { payload = await response.json(); } catch { payload = null; }
  if (!response.ok) throw new Error(payload?.error || `Request failed (${response.status}).`);
  return payload || {};
}
