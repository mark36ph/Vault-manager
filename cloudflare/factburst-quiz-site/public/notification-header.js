(() => {
  const NAV_ITEMS = [
    ["Home", "/"],
    ["Quizzes", "/quizzes.html"],
    ["Leaderboard", "/leaderboard.html"],
    ["Profile", "/profile.html"],
  ];
  const CONSENT_KEY = "factburst-cookie-consent-v1";
  const CONSENT_ACCEPTED = "accepted";
  const CONSENT_ESSENTIAL = "essential";

  let memoryConsent = "";

  function installSharedStyles() {
    if (!document.querySelector('link[href="/legal-shell.css"]')) {
      const link = document.createElement("link");
      link.rel = "stylesheet";
      link.href = "/legal-shell.css";
      document.head.append(link);
    }

    if (document.querySelector("#factburst-page-nav-styles")) return;
    const style = document.createElement("style");
    style.id = "factburst-page-nav-styles";
    style.textContent = `
      .top-nav a[aria-current="page"] {
        color: #fff;
        background: rgba(53, 199, 255, 0.10);
        border-color: rgba(53, 199, 255, 0.18);
      }
      @media (max-width: 420px) {
        .site-header { flex-wrap: wrap !important; }
        .top-nav {
          order: 3 !important;
          width: 100% !important;
          flex: 1 0 100% !important;
          justify-content: flex-start !important;
          overflow-x: auto !important;
        }
        .top-nav a {
          display: inline-flex !important;
          flex: 0 0 auto;
          font-size: 12px !important;
          padding: 6px 8px !important;
        }
      }
    `;
    document.head.append(style);
  }

  function normalizeNavigation() {
    const nav = document.querySelector(".top-nav");
    if (!nav) return false;

    const currentPath = location.pathname.replace(/\/+$/, "") || "/";
    const notificationButton = nav.querySelector("#notification-button");

    nav.replaceChildren();
    for (const [label, href] of NAV_ITEMS) {
      const link = document.createElement("a");
      link.href = href;
      link.textContent = label;
      if (currentPath === href) link.setAttribute("aria-current", "page");
      nav.append(link);
    }
    if (notificationButton) nav.append(notificationButton);

    const profilePlay = document.querySelector('.profile-actions a[href="/#browse"]');
    if (profilePlay) profilePlay.href = "/quizzes.html";
    const resultPlay = document.querySelector('.result-actions a[href="/"]');
    if (resultPlay) resultPlay.href = "/quizzes.html";
    const quizErrorBack = document.querySelector('#quiz-error a[href="/"]');
    if (quizErrorBack) quizErrorBack.href = "/quizzes.html";

    return true;
  }

  function placeNotificationUi() {
    const nav = document.querySelector(".top-nav");
    const button = document.querySelector("#notification-button");
    const panel = document.querySelector("#notification-panel");
    if (!nav || !button) return false;

    button.classList.add("notification-button-header");
    if (!button.querySelector(".notification-button-label")) {
      const label = document.createElement("span");
      label.className = "notification-button-label";
      label.textContent = "Notifications";
      button.replaceChildren(document.createTextNode("🔔 "), label);
    }
    if (button.parentElement !== nav) nav.append(button);
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

  function buildFooter() {
    if (document.querySelector("#factburst-professional-footer")) return;

    const footer = document.createElement("footer");
    footer.id = "factburst-professional-footer";
    footer.className = "site-footer professional-footer";

    const inner = document.createElement("div");
    inner.className = "shell professional-footer-grid";

    const identity = document.createElement("div");
    identity.className = "professional-footer-brand";
    const brand = document.createElement("a");
    brand.href = "/";
    brand.className = "professional-footer-brand-link";
    brand.textContent = "Factburst Quiz";
    const strapline = document.createElement("p");
    strapline.textContent = "Fast questions. Factual answers.";
    identity.append(brand, strapline);

    const explore = document.createElement("nav");
    explore.className = "professional-footer-links";
    explore.setAttribute("aria-label", "Explore Factburst");
    const exploreTitle = document.createElement("strong");
    exploreTitle.textContent = "Explore";
    explore.append(
      exploreTitle,
      footerLink("Home", "/"),
      footerLink("Quizzes", "/quizzes.html"),
      footerLink("Leaderboard", "/leaderboard.html"),
      footerLink("Profile", "/profile.html"),
    );

    const legal = document.createElement("nav");
    legal.className = "professional-footer-links";
    legal.setAttribute("aria-label", "Legal and privacy");
    const legalTitle = document.createElement("strong");
    legalTitle.textContent = "Legal";
    const cookieSettings = document.createElement("button");
    cookieSettings.type = "button";
    cookieSettings.className = "footer-link-button";
    cookieSettings.textContent = "Cookie settings";
    cookieSettings.addEventListener("click", () => showCookieBanner(true));
    legal.append(
      legalTitle,
      footerLink("Terms of use", "/terms.html"),
      footerLink("Privacy notice", "/privacy.html"),
      cookieSettings,
    );

    inner.append(identity, explore, legal);

    const bottom = document.createElement("div");
    bottom.className = "shell professional-footer-bottom";
    const copyright = document.createElement("span");
    copyright.textContent = `© ${new Date().getFullYear()} Factburst Quiz`;
    const note = document.createElement("span");
    note.textContent = "Quiz content is provided for entertainment and general information.";
    bottom.append(copyright, note);

    footer.append(inner, bottom);

    const existing = document.querySelector(".site-footer");
    if (existing) existing.replaceWith(footer);
    else document.body.append(footer);
  }

  function footerLink(label, href) {
    const link = document.createElement("a");
    link.href = href;
    link.textContent = label;
    return link;
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
    installSharedStyles();
    normalizeNavigation();
    buildFooter();
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

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeSharedShell, { once: true });
  } else {
    initializeSharedShell();
  }
})();
