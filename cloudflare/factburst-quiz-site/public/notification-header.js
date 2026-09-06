(() => {
  const CONSENT_KEY = "factburst-cookie-consent-v1";
  const CONSENT_ACCEPTED = "accepted";
  const CONSENT_ESSENTIAL = "essential";
  const CLEAN_PAGE_PATHS = new Map([
    ["/index.html", "/"],
    ["/quizzes.html", "/quizzes"],
    ["/profile.html", "/profile"],
    ["/leaderboard.html", "/leaderboard"],
    ["/terms.html", "/terms"],
    ["/privacy.html", "/privacy"],
  ]);
  const MAIN_NAV_ITEMS = [
    ["Home", "/"],
    ["Quizzes", "/quizzes"],
    ["About", "/about.html"],
    ["FAQ", "/faq.html"],
    ["Contact", "/contact.html"],
    ["Leaderboard", "/leaderboard.html"],
    ["Profile", "/profile.html"],
  ];
  let memoryConsent = "";
  function cleanPublicPath(pathname) { return CLEAN_PAGE_PATHS.get(pathname) || pathname; }
  function cleanLegacyLinks(root = document) {
    for (const link of root.querySelectorAll?.("a[href]") || []) {
      let target;
      try { target = new URL(link.href, location.origin); } catch { continue; }
      if (target.origin !== location.origin) continue;
      const cleanPath = CLEAN_PAGE_PATHS.get(target.pathname);
      if (!cleanPath) continue;
      link.href = `${cleanPath}${target.search}${target.hash}`;
    }
  }
  function cleanLegacyLinkOnClick(event) {
    const link = event.target?.closest?.("a[href]");
    if (!link) return;
    let target;
    try { target = new URL(link.href, location.origin); } catch { return; }
    if (target.origin !== location.origin) return;
    const cleanPath = CLEAN_PAGE_PATHS.get(target.pathname);
    if (!cleanPath) return;
    link.href = `${cleanPath}${target.search}${target.hash}`;
  }
  function renderMainNavigation(nav) {
    if (!nav) return;
    const currentPath = location.pathname.replace(/\/+$/, "") || "/";
    const activePath = currentPath === "/quiz.html" || currentPath.startsWith("/quiz/") || currentPath.startsWith("/quizzes/") ? "/quizzes" : cleanPublicPath(currentPath);
    const fragment = document.createDocumentFragment();
    for (const [label, href] of MAIN_NAV_ITEMS) {
      const link = document.createElement("a");
      link.href = href;
      link.textContent = label;
      if (cleanPublicPath(href) === activePath) link.setAttribute("aria-current", "page");
      fragment.append(link);
    }
    const slot = document.createElement("span");
    slot.className = "notification-slot";
    slot.setAttribute("aria-hidden", "true");
    fragment.append(slot);
    nav.replaceChildren(fragment);
  }
  function prepareNavigation() {
    const cleanLocation = CLEAN_PAGE_PATHS.get(location.pathname);
    if (cleanLocation) history.replaceState(history.state, "", `${cleanLocation}${location.search}${location.hash}`);
    cleanLegacyLinks();
    const nav = document.querySelector(".top-nav");
    if (!nav) return false;
    renderMainNavigation(nav);
    const profilePlay = document.querySelector('.profile-actions a[href="/#browse"]');
    if (profilePlay) profilePlay.href = "/quizzes";
    const resultPlay = document.querySelector('.result-actions a[href="/"]');
    if (resultPlay) resultPlay.href = "/quizzes";
    const quizErrorBack = document.querySelector('#quiz-error a[href="/"]');
    if (quizErrorBack) quizErrorBack.href = "/quizzes";
    return true;
  }
  function placeNotificationUi() {
    const nav = document.querySelector(".top-nav");
    const slot = nav?.querySelector(".notification-slot");
    const button = document.querySelector("#notification-button");
    const panel = document.querySelector("#notification-panel");
    if (!nav || !slot || !button) return false;
    button.classList.add("notification-button-header");
    if (!button.querySelector(".notification-button-label")) {
      const label = document.createElement("span");
      label.className = "notification-button-label";
      label.textContent = "Notifications";
      button.replaceChildren(document.createTextNode("🔔 "), label);
    }
    slot.removeAttribute("aria-hidden");
    if (button.parentElement !== slot) slot.replaceChildren(button);
    if (panel) panel.classList.add("notification-panel-header");
    return true;
  }
  async function addQuizContentPreview() {
    if (!document.querySelector("#quiz-player")) return;
    if (document.querySelector("#quiz-question-preview")) return;
    const match = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})$/i);
    if (!match) return;
    try {
      const response = await fetch(`/api/quizzes/${encodeURIComponent(match[1].toLowerCase())}`, { headers: { accept: "application/json" } });
      if (!response.ok) return;
      const payload = await response.json();
      const questions = Array.isArray(payload?.questions) ? payload.questions : [];
      if (!questions.length) return;
      const section = document.createElement("section");
      section.id = "quiz-question-preview";
      section.className = "quiz-leaderboard-section quiz-question-preview";
      section.setAttribute("aria-labelledby", "quiz-question-preview-title");
      const heading = document.createElement("div");
      heading.className = "section-heading";
      heading.innerHTML = '<div><p class="eyebrow">Quiz preview</p><h2 id="quiz-question-preview-title">Questions you will face</h2></div>';
      section.append(heading);
      const intro = document.createElement("p");
      intro.className = "profile-help";
      intro.textContent = `This challenge contains ${questions.length} questions. Here are a few examples of the topics and question styles in the quiz — the answers are kept hidden until you play.`;
      section.append(intro);
      const previewGrid = document.createElement("div");
      previewGrid.className = "quiz-grid";
      for (const [index, item] of questions.slice(0, 3).entries()) {
        const card = document.createElement("article");
        card.className = "quiz-card";
        const number = document.createElement("span");
        number.className = "category-pill";
        number.textContent = `Question ${index + 1}`;
        const question = document.createElement("h3");
        question.textContent = String(item?.question || "").trim();
        card.append(number, question);
        previewGrid.append(card);
      }
      section.append(previewGrid);
      const results = document.querySelector("#quiz-results");
      const anchor = document.querySelector("#quiz-high-scores") || results;
      if (anchor?.parentNode) anchor.parentNode.insertBefore(section, anchor);
    } catch (error) { console.warn("Could not load quiz content preview", error); }
  }
  function readConsent() { try { return localStorage.getItem(CONSENT_KEY) || memoryConsent; } catch { return memoryConsent; } }
  function writeConsent(choice) { memoryConsent = choice; try { localStorage.setItem(CONSENT_KEY, choice); } catch {} }
  function setConsent(choice) {
    if (![CONSENT_ACCEPTED, CONSENT_ESSENTIAL].includes(choice)) return;
    const previous = readConsent();
    writeConsent(choice);
    document.querySelector("#factburst-cookie-banner")?.classList.add("hidden");
    window.dispatchEvent(new CustomEvent("factburst:cookie-consent", { detail: { choice } }));
    if (previous === CONSENT_ACCEPTED && choice === CONSENT_ESSENTIAL) location.reload();
  }
  function footerLink(label, href) { const link = document.createElement("a"); link.href = href; link.textContent = label; return link; }
  function bindStaticFooter() {
    const year = document.querySelector("[data-footer-year]");
    if (year) year.textContent = String(new Date().getFullYear());
    for (const button of document.querySelectorAll("[data-cookie-settings]")) {
      if (button.dataset.cookieBound === "1") continue;
      button.dataset.cookieBound = "1";
      button.addEventListener("click", () => showCookieBanner(true));
    }
  }
  function buildCookieBanner() {
    if (document.querySelector("#factburst-cookie-banner")) return;
    const banner = document.createElement("section");
    banner.id = "factburst-cookie-banner";
    banner.className = "cookie-consent hidden";
    banner.setAttribute("role", "dialog");
    banner.setAttribute("aria-modal", "false");
    banner.setAttribute("aria-labelledby", "cookie-consent-title");
    const content = document.createElement("div");
    content.className = "cookie-consent-copy";
    const eyebrow = document.createElement("span");
    eyebrow.className = "cookie-consent-eyebrow";
    eyebrow.textContent = "Your privacy choices";
    const title = document.createElement("h2");
    title.id = "cookie-consent-title";
    title.textContent = "Cookies and optional advertising";
    const copy = document.createElement("p");
    copy.append(document.createTextNode("Factburst uses essential storage for core site preferences and account features. With your permission, optional advertising services may use cookies or similar identifiers. Read the "), footerLink("privacy notice", "/privacy"), document.createTextNode(" for more information."));
    content.append(eyebrow, title, copy);
    const actions = document.createElement("div");
    actions.className = "cookie-consent-actions";
    const essential = document.createElement("button");
    essential.type = "button";
    essential.className = "button button-secondary";
    essential.textContent = "Essential only";
    essential.addEventListener("click", () => setConsent(CONSENT_ESSENTIAL));
    const accept = document.createElement("button");
    accept.type = "button";
    accept.className = "button button-primary";
    accept.textContent = "Accept cookies";
    accept.addEventListener("click", () => setConsent(CONSENT_ACCEPTED));
    actions.append(essential, accept);
    banner.append(content, actions);
    document.body.append(banner);
  }
  function showCookieBanner(force = false) { const banner = document.querySelector("#factburst-cookie-banner"); if (!banner) return; if (force || !readConsent()) banner.classList.remove("hidden"); }
  function initializeSharedShell() {
    prepareNavigation();
    document.addEventListener("click", cleanLegacyLinkOnClick, true);
    bindStaticFooter();
    buildCookieBanner();
    showCookieBanner();
    addQuizContentPreview();
    window.factburstCookieConsent = { get: readConsent, open: () => showCookieBanner(true), acceptsOptional: () => readConsent() === CONSENT_ACCEPTED };
    if (placeNotificationUi()) return;
    const observer = new MutationObserver(() => {
      cleanLegacyLinks();
      if (!placeNotificationUi()) return;
      observer.disconnect();
    });
    observer.observe(document.body, { childList: true, subtree: true });
    window.setTimeout(() => observer.disconnect(), 10000);
  }
  if (document.querySelector(".site-header")) initializeSharedShell();
  else if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initializeSharedShell, { once: true });
  else initializeSharedShell();
  const mobileNavScript = document.createElement("script");
  mobileNavScript.src = "/mobile-nav.js?v=1";
  mobileNavScript.defer = true;
  document.head.append(mobileNavScript);
})();
