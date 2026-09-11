const dailyChallengeSection = document.querySelector("#daily-challenge");
const dailyChallengeCard = document.querySelector("#daily-challenge-card");
const dailyChallengeText = document.querySelector("#daily-challenge-text");
const dailyChallengeCta = document.querySelector("#daily-challenge-cta");
const dailyChallengeStreak = document.querySelector("#daily-challenge-streak");
const dailyChallengeTimer = document.querySelector("#daily-challenge-timer");
const dailyChallengeGoal = document.querySelector("#daily-challenge-goal");
const latestCard = document.querySelector("#latest-card");
const latestCta = document.querySelector("#latest-cta");
const moreGrid = document.querySelector("#home-more-grid");
const moreEmpty = document.querySelector("#home-more-empty");
let dailyTimerId = null;

initializeLanding().catch(error => {
  console.error(error);
  if (dailyChallengeSection) dailyChallengeSection.classList.add("hidden");
  if (latestCard) latestCard.replaceChildren(messageBlock("Website ready", "The quiz feed is not available yet."));
  if (moreGrid) moreGrid.replaceChildren();
  if (moreEmpty) moreEmpty.classList.remove("hidden");
});

async function initializeLanding() {
  const [dailyResponse, latestResponse, listResponse] = await Promise.all([
    fetchJson("/api/engagement/daily"),
    fetchJson("/api/quizzes/latest"),
    fetchJson("/api/quizzes?limit=12"),
  ]);

  renderDailyChallenge(dailyResponse);

  const now = Date.now();
  const quizzes = Array.isArray(listResponse.quizzes) ? listResponse.quizzes : [];
  const published = quizzes.filter(quiz => !isUpcomingQuiz(quiz, now));
  const latest = latestResponse.quiz || published[0] || null;

  renderLatest(latestCard, latest);
  if (latest && latestCta) {
    latestCta.href = quizUrl(latest.slug);
    latestCta.textContent = "Play the latest quiz";
  } else if (latestCta) {
    latestCta.href = "/quizzes";
    latestCta.textContent = "Browse quizzes";
  }

  const more = published
    .filter(quiz => !latest || quiz.slug !== latest.slug)
    .slice(0, 6);

  if (moreGrid) moreGrid.replaceChildren(...more.map(createQuizCard));
  if (moreEmpty) moreEmpty.classList.toggle("hidden", more.length !== 0);
}

function renderDailyChallenge(data) {
  if (!dailyChallengeSection || !dailyChallengeCard) return;
  const daily = data?.daily;
  if (!daily?.slug) {
    dailyChallengeSection.classList.add("hidden");
    return;
  }

  dailyChallengeCard.classList.remove("loading-card");
  const completed = Boolean(daily.completed || daily.score !== null && daily.score !== undefined);
  const total = Number(daily.total || daily.question_count || 10);
  const streak = Number(data?.streak?.current || 0);
  if (dailyChallengeText) {
    dailyChallengeText.textContent = completed
      ? `You completed today's ${total}-question challenge with ${Number(daily.score || 0)}/${total}. Come back tomorrow for a new challenge.`
      : `${daily.title || "Today's quiz"} · ${total} questions. Everyone gets the same daily challenge.`;
  }

  if (dailyChallengeCta) {
    dailyChallengeCta.href = quizUrl(daily.slug, true);
    dailyChallengeCta.textContent = completed ? "Play again" : "Play today's challenge";
  }

  if (dailyChallengeStreak) {
    if (streak > 0) {
      dailyChallengeStreak.textContent = `🔥 ${streak}-day streak`;
      dailyChallengeStreak.classList.remove("hidden");
    } else {
      dailyChallengeStreak.textContent = "🔥 Start your streak";
      dailyChallengeStreak.classList.remove("hidden");
    }
  }

  if (dailyChallengeGoal) {
    dailyChallengeGoal.textContent = streak >= 7
      ? "🏆 On Fire unlocked — keep the streak alive!"
      : streak > 0
        ? `🔥 ${7 - streak} more day${7 - streak === 1 ? "" : "s"} to unlock On Fire`
        : "Complete today's challenge to start a streak";
  }

  startDailyCountdown();
  dailyChallengeCard.classList.toggle("daily-complete", completed);
}

function startDailyCountdown() {
  if (!dailyChallengeTimer) return;
  if (dailyTimerId) clearInterval(dailyTimerId);
  const update = () => {
    const now = new Date();
    const next = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1));
    const remaining = Math.max(0, next.getTime() - now.getTime());
    const hours = Math.floor(remaining / 3600000);
    const minutes = Math.floor((remaining % 3600000) / 60000);
    const seconds = Math.floor((remaining % 60000) / 1000);
    dailyChallengeTimer.textContent = `New challenge in ${hours}h ${String(minutes).padStart(2, "0")}m ${String(seconds).padStart(2, "0")}s`;
  };
  update();
  dailyTimerId = setInterval(update, 1000);
}

function renderLatest(container, quiz) {
  if (!container) return;
  container.classList.remove("loading-card");
  if (!quiz) {
    container.replaceChildren(messageBlock(
      "More quizzes are on the way",
      "Browse the quiz library to see everything currently available.",
    ));
    return;
  }

  const copy = document.createElement("div");
  const category = document.createElement("span");
  category.className = "category-pill";
  category.textContent = quiz.category || "Quiz";
  const heading = document.createElement("h3");
  heading.textContent = quiz.title;
  const description = document.createElement("p");
  description.textContent = quiz.description || `${quiz.question_count || 10} questions. See how many you can get right.`;
  copy.append(category, heading, description);

  const action = document.createElement("a");
  action.className = "button button-primary";
  action.href = quizUrl(quiz.slug);
  action.textContent = "Play quiz";

  container.replaceChildren(copy, action);
}

function createQuizCard(quiz) {
  const card = document.createElement("a");
  card.className = "quiz-card";
  card.href = quizUrl(quiz.slug);

  const category = document.createElement("span");
  category.className = "category-pill";
  category.textContent = quiz.category || "Quiz";

  const heading = document.createElement("h3");
  heading.textContent = quiz.title;

  const copy = document.createElement("p");
  copy.textContent = quiz.description || "Test yourself and see what score you can get.";

  const footer = document.createElement("div");
  footer.className = "quiz-card-footer";
  const questions = document.createElement("span");
  questions.textContent = `${quiz.question_count || 0} questions`;
  const play = document.createElement("span");
  play.textContent = "Play →";
  footer.append(questions, play);

  card.append(category, heading, copy, footer);
  return card;
}

function isUpcomingQuiz(quiz, now = Date.now()) {
  if (!quiz?.publish_at) return false;
  const release = Date.parse(quiz.publish_at);
  return Number.isFinite(release) && release > now;
}

function quizUrl(slug, daily = false) {
  const value = String(slug || "").toLowerCase();
  if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(value)) return "/quizzes";
  return daily ? `/quiz/${encodeURIComponent(value)}?daily=1` : `/quiz/${encodeURIComponent(value)}`;
}

function messageBlock(title, copy) {
  const wrapper = document.createElement("div");
  const heading = document.createElement("h3");
  heading.textContent = title;
  const paragraph = document.createElement("p");
  paragraph.textContent = copy;
  wrapper.append(heading, paragraph);
  return wrapper;
}

async function fetchJson(url) {
  const response = await fetch(url, { credentials: "same-origin" });
  let payload = null;
  try { payload = await response.json(); } catch { payload = null; }
  if (!response.ok) throw new Error(payload?.error || `Request failed (${response.status}).`);
  return payload || {};
}
