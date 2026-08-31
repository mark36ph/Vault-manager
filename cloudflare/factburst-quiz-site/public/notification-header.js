(() => {
  const CONSENT_KEY = "factburst-cookie-consent-v1";
  const CONSENT_ACCEPTED = "accepted";
  const CONSENT_ESSENTIAL = "essential";

  let memoryConsent = "";

  function prepareNavigation() {
    const nav = document.querySelector(".top-nav");
    if (!nav) return false;

    const currentPath = location.pathname.replace(/\/+$/, "") || "/";
    const activePath = currentPath === "/quiz.html" || currentPath.startsWith("/quiz/") || currentPath.startsWith("/quizzes/")
      ? "/quizzes"
      : currentPath === "/quizzes.html"
        ? "/quizzes"
        : currentPath;

    for (const link of nav.querySelectorAll("a[href]")) {
      link.removeAttribute("aria-current");
      let linkPath = new URL(link.href, location.origin).pathname.replace(/\/+$/, "") || "/";
      if (linkPath === "/quizzes.html") linkPath = "/quizzes";
      if (linkPath === activePath) link.setAttribute("aria-current", "page");
    }

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

  function readConsent() {
    try {
      return localStorage.getItem(CONSENT_KEY) || memoryConsent;
    } catch {
      return memoryConsent;
    }
  }

  function writeConsent(choice) {
    memoryConsent = choice;
    try {
      localStorage.setItem(CONSENT_KEY, choice);
    } catch {}
  }

  function setConsent(choice) {
    if (![CONSENT_ACCEPTED, CONSENT_ESSENTIAL].includes(choice)) return;
    const previous = readConsent();
    writeConsent(choice);
    document.querySelector("#factburst-cookie-banner")?.classList.add("hidden");
    window.dispatchEvent(new CustomEvent("factburst:cookie-consent", { detail: { choice } }));

    if (previous === CONSENT_ACCEPTED && choice === CONSENT_ESSENTIAL) {
      location.reload();
    }
  }

  function footerLink(label, href) {
    const link = document.createElement("a");
    link.href = href;
    link.textContent = label;
    return link;
  }

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
    copy.append(
      document.createTextNode("Factburst uses essential storage for core site preferences and account features. With your permission, optional advertising services may use cookies or similar identifiers. Read the "),
      footerLink("privacy notice", "/privacy.html"),
      document.createTextNode(" for more information."),
    );
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

  function showCookieBanner(force = false) {
    const banner = document.querySelector("#factburst-cookie-banner");
    if (!banner) return;
    if (force || !readConsent()) banner.classList.remove("hidden");
  }

  function initializeSharedShell() {
    prepareNavigation();
    bindStaticFooter();
    buildCookieBanner();
    showCookieBanner();

    window.factburstCookieConsent = {
      get: readConsent,
      open: () => showCookieBanner(true),
      acceptsOptional: () => readConsent() === CONSENT_ACCEPTED,
    };

    if (placeNotificationUi()) return;

    const observer = new MutationObserver(() => {
      if (!placeNotificationUi()) return;
      observer.disconnect();
    });
    observer.observe(document.body, { childList: true, subtree: true });
    window.setTimeout(() => observer.disconnect(), 10000);
  }

  if (document.querySelector(".site-header")) {
    initializeSharedShell();
  } else if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeSharedShell, { once: true });
  } else {
    initializeSharedShell();
  }
})();
