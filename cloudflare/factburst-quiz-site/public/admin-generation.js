(() => {
  "use strict";

  // Quiz generation now belongs to the desktop app. Keep the old file as a
  // compatibility shim so cached admin pages do not expose the retired web UI.
  const retireWebGeneration = () => {
    document.querySelector('[data-admin-section="generation"]')?.remove();
    document.querySelector('#admin-section-generation')?.remove();

    const socialScript = document.createElement("script");
    socialScript.src = "/admin-social.js?v=3";
    socialScript.dataset.socialHubLoader = "1";
    document.head.appendChild(socialScript);

    if (window.location.hash === "#generation") {
      history.replaceState(null, "", "#dashboard");
    }
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", retireWebGeneration, { once: true });
  } else {
    retireWebGeneration();
  }
})();
