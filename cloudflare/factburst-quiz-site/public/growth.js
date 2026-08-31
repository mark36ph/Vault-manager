(() => {
  const SITE_ORIGIN = "https://factburstquiz.com";
  const EVENT_ENDPOINT = "/api/analytics/event";
  const tracked = new Set();

  function analyticsAllowed() {
    return navigator.globalPrivacyControl !== true &&
      navigator.doNotTrack !== "1" &&
      window.doNotTrack !== "1";
  }

  function currentSlug() {
    const slug = new URLSearchParams(location.search).get("slug") || "";
    return /^[a-z0-9][a-z0-9-]{0,79}$/i.test(slug) ? slug.toLowerCase() : "";
  }

  function sourceName() {
    if (!document.referrer) return "direct";
    try {
      const ref = new URL(document.referrer);
      if (ref.origin !== location.origin && ref.origin !== SITE_ORIGIN) return "external";
      if (ref.pathname === "/" || ref.pathname === "/index.html") return "home";
      if (ref.pathname === "/quizzes.html") return "quizzes";
      if (ref.pathname === "/leaderboard.html") return "leaderboard";
      if (ref.pathname === "/profile.html") return "profile";
      if (ref.pathname === "/quiz.html") return "quiz";
      return "internal";
    } catch {
      return "external";
    }
  }

  function track(event, detail = {}) {
    if (!analyticsAllowed()) return;
    const payload = {
      event,
      quiz_slug: detail.quiz_slug || currentSlug(),
      source: detail.source || sourceName(),
    };
    fetch(EVENT_ENDPOINT, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(payload),
      keepalive: true,
      credentials: "same-origin",
    }).catch(() => {});
  }

  function trackOnce(key, event, detail = {}) {
    if (tracked.has(key)) return;
    tracked.add(key);
    track(event, detail);
  }

  function canonicalQuizUrl() {
    const slug = currentSlug();
    return slug ? `${SITE_ORIGIN}/quiz.html?slug=${encodeURIComponent(slug)}` : `${SITE_ORIGIN}/quizzes.html`;
  }

  function resultShareDetails() {
    const score = document.querySelector("#result-score")?.textContent?.trim() || "";
    const title = document.querySelector("#quiz-title")?.textContent?.trim() || "Factburst Quiz";
    const url = canonicalQuizUrl();
    const text = score
      ? `I scored ${score} on “${title}” at Factburst Quiz. Can you beat me?`
      : `Try “${title}” on Factburst Quiz. Can you beat my score?`;
    return { score, title, url, text };
  }

  async function shareResult(button) {
    const details = resultShareDetails();
    const original = button.textContent;
    button.disabled = true;
    try {
      if (navigator.share) {
        await navigator.share({
          title: `${details.score ? `${details.score} on ` : ""}${details.title} | Factburst Quiz`,
          text: details.text,
          url: details.url,
        });
        track("score_shared");
      } else {
        await navigator.clipboard.writeText(`${details.text} ${details.url}`);
        button.textContent = "Copied!";
        track("score_shared");
        window.setTimeout(() => { button.textContent = original; }, 1800);
      }
    } catch (error) {
      if (error?.name !== "AbortError") {
        try {
          await navigator.clipboard.writeText(`${details.text} ${details.url}`);
          button.textContent = "Copied!";
          track("score_shared");
          window.setTimeout(() => { button.textContent = original; }, 1800);
        } catch {}
      }
    } finally {
      button.disabled = false;
    }
  }

  async function copyQuizLink(button) {
    const url = canonicalQuizUrl();
    const original = button.textContent;
    try {
      await navigator.clipboard.writeText(url);
      button.textContent = "Link copied!";
      window.setTimeout(() => { button.textContent = original; }, 1600);
    } catch {}
  }

  function ensureResultActions() {
    const results = document.querySelector("#quiz-results");
    if (!results || results.classList.contains("hidden")) return;
    const share = results.querySelector("#share-score");
    if (share) share.textContent = "Share my score";

    const actions = results.querySelector(".result-actions");
    if (!actions || actions.querySelector("#copy-quiz-link")) return;
    const copy = document.createElement("button");
    copy.id = "copy-quiz-link";
    copy.type = "button";
    copy.className = "button button-secondary";
    copy.textContent = "Copy quiz link";
    const playAnother = actions.querySelector('a[href="/quizzes.html"]');
    if (playAnother) actions.insertBefore(copy, playAnother);
    else actions.append(copy);
  }

  function observeQuizState() {
    const player = document.querySelector("#quiz-player");
    const results = document.querySelector("#quiz-results");
    if (!player && !results) return;

    const check = () => {
      if (player && !player.classList.contains("hidden")) {
        trackOnce("quiz-opened", "quiz_opened");
      }
      if (results && !results.classList.contains("hidden")) {
        trackOnce("quiz-completed", "quiz_completed");
        ensureResultActions();
      }
    };

    check();
    const observer = new MutationObserver(check);
    if (player) observer.observe(player, { attributes: true, attributeFilter: ["class"] });
    if (results) observer.observe(results, { attributes: true, attributeFilter: ["class"], childList: true, subtree: true });
  }

  function trackPageView() {
    const layout = document.body.dataset.layout || "";
    const page = document.body.dataset.page || "";
    if (layout === "landing") trackOnce("view", "home_view", { quiz_slug: "" });
    else if (layout === "directory") trackOnce("view", "quiz_directory_view", { quiz_slug: "" });
    else if (layout === "leaderboard") trackOnce("view", "leaderboard_view", { quiz_slug: "" });
    else if (page === "quiz") observeQuizState();
  }

  document.addEventListener("click", event => {
    const quizLink = event.target.closest?.('a[href*="quiz.html?slug="]');
    if (quizLink) {
      try {
        const target = new URL(quizLink.href, location.href);
        const slug = target.searchParams.get("slug") || "";
        if (/^[a-z0-9][a-z0-9-]{0,79}$/i.test(slug)) {
          track("quiz_link_clicked", { quiz_slug: slug.toLowerCase() });
        }
      } catch {}
    }

    const answer = event.target.closest?.(".answer-button");
    if (answer) trackOnce("quiz-started", "quiz_started");

    const youtube = event.target.closest?.("#watch-video");
    if (youtube && !youtube.classList.contains("hidden")) track("youtube_clicked");

    const copy = event.target.closest?.("#copy-quiz-link");
    if (copy) {
      event.preventDefault();
      copyQuizLink(copy);
    }
  });

  document.addEventListener("click", event => {
    const share = event.target.closest?.("#share-score");
    if (!share) return;
    const results = document.querySelector("#quiz-results");
    if (!results || results.classList.contains("hidden")) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    shareResult(share);
  }, true);

  window.factburstTrack = track;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", trackPageView, { once: true });
  } else {
    trackPageView();
  }
})();
