(() => {
  const NAV_ITEMS = [
    ["Home", "/"],
    ["Quizzes", "/quizzes.html"],
    ["Leaderboard", "/leaderboard.html"],
    ["Profile", "/profile.html"],
  ];

  function installNavStyles() {
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

  function initializeNotificationHeader() {
    installNavStyles();
    normalizeNavigation();
    if (placeNotificationUi()) return;

    const observer = new MutationObserver(() => {
      if (!placeNotificationUi()) return;
      observer.disconnect();
    });
    observer.observe(document.body, { childList: true, subtree: true });
    window.setTimeout(() => observer.disconnect(), 10000);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeNotificationHeader, { once: true });
  } else {
    initializeNotificationHeader();
  }
})();
