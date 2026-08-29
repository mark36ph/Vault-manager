const slug = new URLSearchParams(location.search).get("slug") || "";
const challengeToken = new URLSearchParams(location.search).get("challenge") || "";
let incomingChallenge = null;
let resultObserver = null;

if (document.body.dataset.page === "quiz" && /^[a-z0-9][a-z0-9-]{0,79}$/i.test(slug)) {
  initializeChallenges().catch(error => console.error("Challenge UI failed", error));
}

async function initializeChallenges() {
  if (challengeToken && /^[A-Za-z0-9_-]{20,120}$/.test(challengeToken)) {
    try {
      const response = await api(`/api/challenges/${encodeURIComponent(challengeToken)}`);
      incomingChallenge = response.challenge || null;
      if (incomingChallenge && String(incomingChallenge.quiz_slug || "").toLowerCase() === slug.toLowerCase()) {
        showChallengeBanner(incomingChallenge);
      }
    } catch (error) {
      showChallengeNotice(error.message || "This challenge is no longer available.", "error");
    }
  }

  const results = document.querySelector("#quiz-results");
  if (!results) return;
  resultObserver = new MutationObserver(() => {
    if (!results.classList.contains("hidden")) enhanceResults(results);
  });
  resultObserver.observe(results, { attributes: true, attributeFilter: ["class"] });
  if (!results.classList.contains("hidden")) enhanceResults(results);
}

function enhanceResults(results) {
  if (results.dataset.challengeEnhanced === "true") return;
  results.dataset.challengeEnhanced = "true";

  const actions = results.querySelector(".result-actions");
  if (actions) {
    const challenge = document.createElement("button");
    challenge.type = "button";
    challenge.className = "button button-primary";
    challenge.textContent = "Challenge a friend";
    challenge.addEventListener("click", openChallengePicker);
    actions.insertBefore(challenge, actions.firstChild);
  }

  if (incomingChallenge) {
    const scoreText = String(document.querySelector("#result-score")?.textContent || "");
    const score = Number.parseInt(scoreText.split("/")[0], 10);
    if (Number.isFinite(score)) {
      const target = Number(incomingChallenge.score || 0);
      const outcome = score > target
        ? `You beat ${incomingChallenge.challenger}'s challenge!`
        : score === target
          ? `You tied ${incomingChallenge.challenger}'s challenge.`
          : `${incomingChallenge.challenger} stays ahead — try again to beat ${target}/${incomingChallenge.total}.`;
      showChallengeNotice(outcome, score >= target ? "success" : "");
    }
  }
}

function showChallengeBanner(challenge) {
  const loading = document.querySelector("#quiz-loading");
  const main = document.querySelector(".quiz-shell");
  if (!main || document.querySelector("#challenge-banner")) return;
  const banner = document.createElement("section");
  banner.id = "challenge-banner";
  banner.className = "challenge-banner";
  banner.innerHTML = `
    <span class="challenge-kicker">Friend challenge</span>
    <strong></strong>
    <span></span>`;
  banner.querySelector("strong").textContent = `${challenge.challenger} challenged you to beat ${challenge.score}/${challenge.total}`;
  banner.querySelector("span:last-child").textContent = challenge.quiz_title || "Factburst Quiz";
  main.insertBefore(banner, loading || main.firstChild);
}

function showChallengeNotice(message, kind = "") {
  let notice = document.querySelector("#challenge-notice");
  if (!notice) {
    notice = document.createElement("div");
    notice.id = "challenge-notice";
    notice.className = "challenge-notice";
    const results = document.querySelector("#quiz-results");
    if (results) results.insertBefore(notice, results.querySelector(".result-actions") || null);
    else document.querySelector(".quiz-shell")?.prepend(notice);
  }
  notice.textContent = message;
  notice.dataset.kind = kind;
}

async function openChallengePicker() {
  closeChallengePicker();
  const overlay = document.createElement("div");
  overlay.id = "challenge-picker-overlay";
  overlay.className = "challenge-picker-overlay";
  overlay.innerHTML = `
    <section class="challenge-picker" role="dialog" aria-modal="true" aria-labelledby="challenge-picker-title">
      <button type="button" class="challenge-picker-close" aria-label="Close">×</button>
      <p class="eyebrow">Challenge a friend</p>
      <h2 id="challenge-picker-title">Who can beat your score?</h2>
      <p class="challenge-picker-copy">Choose a Factburst friend, or make a link you can send to anyone.</p>
      <div id="challenge-friend-list" class="challenge-friend-list"><p>Loading friends…</p></div>
      <button type="button" id="challenge-share-anyone" class="button button-secondary">Share challenge link</button>
      <p id="challenge-picker-status" class="challenge-picker-status" aria-live="polite"></p>
    </section>`;
  document.body.append(overlay);
  document.body.classList.add("modal-open");
  overlay.querySelector(".challenge-picker-close")?.addEventListener("click", closeChallengePicker);
  overlay.addEventListener("click", event => {
    if (event.target === overlay) closeChallengePicker();
  });
  overlay.querySelector("#challenge-share-anyone")?.addEventListener("click", () => createAndShareChallenge(null));

  try {
    const response = await api("/api/friends");
    renderChallengeFriends(Array.isArray(response.friends) ? response.friends : []);
  } catch (error) {
    const host = overlay.querySelector("#challenge-friend-list");
    if (host) host.textContent = error.message || "Could not load friends.";
  }
}

function renderChallengeFriends(friends) {
  const host = document.querySelector("#challenge-friend-list");
  if (!host) return;
  host.replaceChildren();
  if (friends.length === 0) {
    const empty = document.createElement("p");
    empty.className = "challenge-empty";
    empty.textContent = "You do not have any Factburst friends yet. Use the shareable link below or add friends from your profile.";
    host.append(empty);
    return;
  }

  for (const friend of friends) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "challenge-friend";
    const name = document.createElement("strong");
    name.textContent = friend.username || "Player";
    const stats = document.createElement("span");
    stats.textContent = `${friend.quizzes_completed || 0} quizzes • ${friend.percentage || 0}% accuracy`;
    button.append(name, stats);
    button.addEventListener("click", () => createAndShareChallenge(friend));
    host.append(button);
  }
}

async function createAndShareChallenge(friend) {
  const status = document.querySelector("#challenge-picker-status");
  if (status) status.textContent = "Creating challenge…";
  try {
    const response = await api("/api/challenges", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ slug, friend_user_id: friend?.user_id ?? null }),
    });
    const challenge = response.challenge;
    const recipient = friend ? ` ${friend.username}` : "";
    const text = `${challenge.challenger} challenges${recipient} to beat ${challenge.score}/${challenge.total} on “${challenge.quiz_title}” at Factburst Quiz.`;
    if (navigator.share) {
      await navigator.share({ title: "Factburst Quiz challenge", text, url: challenge.url });
      if (status) status.textContent = "Challenge ready to send.";
    } else {
      await navigator.clipboard.writeText(`${text} ${challenge.url}`);
      if (status) status.textContent = "Challenge copied to your clipboard.";
    }
  } catch (error) {
    if (error?.name === "AbortError") return;
    if (status) status.textContent = error.message || "Could not create challenge.";
  }
}

function closeChallengePicker() {
  document.querySelector("#challenge-picker-overlay")?.remove();
  document.body.classList.remove("modal-open");
}

async function api(url, options = {}) {
  const response = await fetch(url, { credentials: "same-origin", ...options });
  let payload = null;
  try {
    payload = await response.json();
  } catch {
    payload = null;
  }
  if (!response.ok) throw new Error(payload?.error || `Request failed (${response.status}).`);
  return payload || {};
}
