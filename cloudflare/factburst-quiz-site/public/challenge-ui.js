const slug = new URLSearchParams(location.search).get("slug") || "";
const challengeToken = new URLSearchParams(location.search).get("challenge") || "";
let incomingChallenge = null;
let resultObserver = null;

if (document.body.dataset.page === "quiz" && /^[a-z0-9][a-z0-9-]{0,79}$/i.test(slug)) {
  document.addEventListener("click", interceptLegacyRematch, true);
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
      <p class="challenge-picker-copy">Choose a Factburst friend. We’ll put the challenge in their notifications and email them when email delivery is available.</p>
      <div id="challenge-friend-list" class="challenge-friend-list"><p>Loading friends…</p></div>
      <p id="challenge-picker-status" class="challenge-picker-status" aria-live="polite"></p>
    </section>`;
  document.body.append(overlay);
  document.body.classList.add("modal-open");
  overlay.querySelector(".challenge-picker-close")?.addEventListener("click", closeChallengePicker);
  overlay.addEventListener("click", event => {
    if (event.target === overlay) closeChallengePicker();
  });

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
    const empty = document.createElement("div");
    empty.className = "challenge-empty";
    const copy = document.createElement("p");
    copy.textContent = "You do not have any Factburst friends yet. Add someone from your profile, then come back and challenge them to this score.";
    const profile = document.createElement("a");
    profile.className = "button button-secondary challenge-profile-link";
    profile.href = "/profile.html";
    profile.textContent = "Add friends in Profile";
    empty.append(copy, profile);
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
    button.addEventListener("click", () => sendFriendChallenge(friend, button));
    host.append(button);
  }
}

async function sendFriendChallenge(friend, button) {
  const status = document.querySelector("#challenge-picker-status");
  if (status) status.textContent = `Sending challenge to ${friend?.username || "friend"}…`;
  const buttons = Array.from(document.querySelectorAll(".challenge-friend"));
  buttons.forEach(item => { item.disabled = true; });
  try {
    const response = await api("/api/challenges", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ slug, friend_user_id: friend?.user_id }),
    });
    const challenge = response.challenge || {};
    if (button) button.dataset.sent = "true";
    if (status) {
      status.textContent = challenge.email_sent
        ? `Challenge sent to ${friend.username}. It’s in their Factburst notifications and an email alert was sent.`
        : `Challenge sent to ${friend.username}. It’s waiting in their Factburst notifications.`;
    }
  } catch (error) {
    buttons.forEach(item => { item.disabled = false; });
    if (status) status.textContent = error.message || "Could not send challenge.";
  }
}

async function interceptLegacyRematch(event) {
  if (!challengeToken || !event.target?.closest) return;
  const button = event.target.closest("button");
  if (!button || String(button.textContent || "").trim() !== "Rematch") return;

  event.preventDefault();
  event.stopImmediatePropagation();
  button.disabled = true;
  button.textContent = "Sending rematch…";

  try {
    const result = await api(`/api/engagement/challenge-result?token=${encodeURIComponent(challengeToken)}`);
    const account = await api("/api/account");
    const myId = Number(account?.user?.id || 0);
    const challengerId = Number(result?.challenger?.user_id || 0);
    const challengedId = Number(result?.challenged?.user_id || 0);
    const friendId = myId === challengerId ? challengedId : challengerId;
    if (!friendId || friendId === myId) throw new Error("Could not find the friend for this rematch.");

    const response = await api("/api/challenges", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ slug, friend_user_id: friendId }),
    });
    const challenge = response.challenge || {};
    button.textContent = "Rematch sent";
    showChallengeNotice(
      challenge.email_sent
        ? `Rematch sent to ${challenge.challenged_username || "your friend"}. They have a Factburst notification and an email alert.`
        : `Rematch sent to ${challenge.challenged_username || "your friend"}. It’s waiting in their Factburst notifications.`,
      "success",
    );
  } catch (error) {
    button.disabled = false;
    button.textContent = "Rematch";
    showChallengeNotice(error.message || "Could not send rematch.", "error");
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
