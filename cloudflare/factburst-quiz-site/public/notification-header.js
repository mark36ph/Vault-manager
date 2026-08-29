(() => {
  function placeNotificationUi() {
    const nav = document.querySelector(".top-nav");
    const button = document.querySelector("#notification-button");
    const panel = document.querySelector("#notification-panel");
    if (!nav || !button) return false;

    button.classList.add("notification-button-header");
    button.textContent = "🔔 Notifications";
    if (button.parentElement !== nav) nav.append(button);
    if (panel) panel.classList.add("notification-panel-header");
    return true;
  }

  function initializeNotificationHeader() {
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
