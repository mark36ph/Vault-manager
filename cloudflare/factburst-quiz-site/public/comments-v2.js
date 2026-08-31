const quizSlug = currentQuizSlug();
const commentsList = document.querySelector("#quiz-comments-list");
const commentsComposer = document.querySelector("#quiz-comment-composer");
const commentsStatus = document.querySelector("#quiz-comment-status");
let viewer = { authenticated: false, user: null };

if (document.body.dataset.page === "quiz" && quizSlug) {
  startComments().catch(error => showStatus(error.message || "Comments are unavailable.", "error"));
}

async function startComments() {
  viewer = await getAccount();
  drawComposer();
  await loadComments();
}

async function loadComments() {
  commentsList?.replaceChildren(note("Loading comments…"));
  const data = await api(`/api/quizzes/${encodeURIComponent(quizSlug)}/comments?limit=200`);
  const items = Array.isArray(data.comments) ? data.comments : [];
  commentsList?.replaceChildren();
  if (!items.length) {
    commentsList?.append(note("No comments yet. Be the first to start the conversation."));
    return;
  }
  const roots = items.filter(item => !item.parent_id);
  for (const root of roots) {
    const thread = document.createElement("div");
    thread.className = "comment-thread";
    thread.append(drawComment(root));
    const replies = items.filter(item => Number(item.parent_id) === Number(root.id));
    if (replies.length) {
      const replyList = document.createElement("div");
      replyList.className = "comment-replies";
      replies.forEach(reply => replyList.append(drawComment(reply)));
      thread.append(replyList);
    }
    commentsList?.append(thread);
  }
}

function drawComposer() {
  commentsComposer?.replaceChildren();
  const user = viewer?.authenticated ? viewer.user : null;
  if (!user?.email_verified) {
    const p = note(user ? "Verify your email before posting comments." : "Log in with a verified Factburst account to join the conversation.");
    p.className = "comment-login-note";
    commentsComposer?.append(p);
    return;
  }
  commentsComposer?.append(makeForm(null, `Comment as ${user.username}`, "Post comment"));
}

function makeForm(parentId, labelText, submitText) {
  const form = document.createElement("form");
  form.className = parentId ? "comment-form comment-reply-form" : "comment-form";
  const label = document.createElement("label");
  label.textContent = labelText;
  const area = document.createElement("textarea");
  area.required = true; area.minLength = 2; area.maxLength = 600; area.rows = parentId ? 2 : 4;
  area.placeholder = parentId ? "Write a reply…" : "Share your thoughts about this quiz…";
  label.append(area);
  const footer = document.createElement("div"); footer.className = "comment-form-actions";
  const counter = document.createElement("span"); counter.textContent = "0 / 600";
  const send = document.createElement("button"); send.type = "submit"; send.className = "button button-primary"; send.textContent = submitText;
  area.addEventListener("input", () => { counter.textContent = `${area.value.length} / 600`; });
  footer.append(counter, send); form.append(label, footer);
  form.addEventListener("submit", async event => {
    event.preventDefault(); send.disabled = true; send.textContent = "Posting…";
    try {
      const url = parentId ? `/api/quizzes/${encodeURIComponent(quizSlug)}/comments/${parentId}/reply` : `/api/quizzes/${encodeURIComponent(quizSlug)}/comments`;
      await api(url, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ comment: area.value }) });
      showStatus(parentId ? "Reply posted." : "Comment posted.", "success"); await loadComments();
    } catch (error) { showStatus(error.message, "error"); }
    finally { send.disabled = false; send.textContent = submitText; }
  });
  return form;
}

function drawComment(item) {
  const card = document.createElement("article");
  card.className = `comment-card comment-status-${item.status || "active"}`;
  const meta = document.createElement("div"); meta.className = "comment-meta";
  const author = document.createElement("strong"); author.textContent = item.username || "Player";
  const time = document.createElement("span"); time.textContent = `${formatDate(item.created_at)}${item.edited_at ? " • edited" : ""}`;
  meta.append(author, time);
  const body = document.createElement("p"); body.textContent = item.body || "";
  const actions = document.createElement("div"); actions.className = "comment-actions";

  const like = action(`${item.liked ? "♥" : "♡"} ${item.likes || 0}`, async () => {
    const result = await api(`/api/quizzes/${encodeURIComponent(quizSlug)}/comments/${item.id}/like`, { method: "POST" });
    like.textContent = `${result.liked ? "♥" : "♡"} ${result.likes || 0}`;
    like.classList.toggle("active", Boolean(result.liked));
  });
  like.classList.toggle("active", Boolean(item.liked)); actions.append(like);

  if (item.status === "active") {
    actions.append(action("Reply", () => addReplyForm(card, item)));
    if (item.can_edit) actions.append(action("Edit", () => editItem(item)));
    if (item.can_edit || item.can_moderate) actions.append(action("Delete", () => removeItem(item)));
    actions.append(action("Report", () => reportItem(item)));
  }
  if (item.can_moderate) {
    actions.append(action(item.status === "hidden" ? "Restore" : "Hide", () => moderateItem(item, item.status === "hidden" ? "restore" : "hide"), "moderator"));
  }
  card.append(meta, body, actions);
  return card;
}

function addReplyForm(card, item) {
  card.querySelector(".comment-reply-form")?.remove();
  if (!viewer?.user?.email_verified) { showStatus("Log in with a verified account to reply.", "error"); return; }
  card.append(makeForm(item.id, `Reply to ${item.username}`, "Post reply"));
}

async function editItem(item) {
  const text = prompt("Edit your comment", item.body || "");
  if (text === null) return;
  await api(`/api/quizzes/${encodeURIComponent(quizSlug)}/comments/${item.id}`, { method: "PATCH", headers: { "content-type": "application/json" }, body: JSON.stringify({ comment: text }) });
  showStatus("Comment updated.", "success"); await loadComments();
}

async function removeItem(item) {
  if (!confirm("Remove this comment?")) return;
  await api(`/api/quizzes/${encodeURIComponent(quizSlug)}/comments/${item.id}`, { method: "DELETE" });
  showStatus("Comment removed.", "success"); await loadComments();
}

async function reportItem(item) {
  const reason = prompt("Report reason: spam, off_topic, or other", "spam");
  if (!reason) return;
  const detail = prompt("Optional details", "") || "";
  await api(`/api/quizzes/${encodeURIComponent(quizSlug)}/comments/${item.id}/report`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ reason: reason.trim().toLowerCase(), detail }) });
  showStatus("Comment reported for review.", "success");
}

async function moderateItem(item, mode) {
  await api(`/api/quizzes/${encodeURIComponent(quizSlug)}/comments/${item.id}/moderate`, { method: "PATCH", headers: { "content-type": "application/json" }, body: JSON.stringify({ action: mode }) });
  showStatus(mode === "hide" ? "Comment hidden." : "Comment restored.", "success"); await loadComments();
}

function action(label, handler, extra = "") {
  const button = document.createElement("button"); button.type = "button"; button.className = `comment-action ${extra}`.trim(); button.textContent = label;
  button.addEventListener("click", async () => { button.disabled = true; try { await handler(); } catch (error) { showStatus(error.message || "Action failed.", "error"); } finally { button.disabled = false; } });
  return button;
}

function currentQuizSlug() {
  const querySlug = new URLSearchParams(location.search).get("slug") || "";
  if (/^[a-z0-9][a-z0-9-]{0,79}$/i.test(querySlug)) return querySlug.toLowerCase();
  const match = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})\/?$/i);
  return match ? match[1].toLowerCase() : "";
}

async function getAccount() { try { return await api("/api/account"); } catch { return { authenticated: false, user: null }; } }
function showStatus(text, kind) { if (!commentsStatus) return; commentsStatus.textContent = text; commentsStatus.dataset.kind = kind || ""; }
function note(text) { const p = document.createElement("p"); p.className = "comments-message"; p.textContent = text; return p; }
function formatDate(value) { const d = new Date(value || ""); return Number.isNaN(d.getTime()) ? "" : new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(d); }
async function api(url, options = {}) { const response = await fetch(url, { credentials: "same-origin", ...options }); let payload = null; try { payload = await response.json(); } catch {} if (!response.ok) throw new Error(payload?.error || `Request failed (${response.status}).`); return payload || {}; }
