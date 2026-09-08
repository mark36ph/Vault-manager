(() => {
  "use strict";

  // The old social performance page is no longer the entry point. The admin
  // page owns the Social Hub now, while this loader keeps the integration
  // compatible with the existing admin shell.
  function loadSocialHub() {
    if (document.querySelector("script[data-social-hub]") || document.querySelector("#admin-section-social-hub")) return;
    const script = document.createElement("script");
    script.src = "/admin-social-hub.js?v=2";
    script.dataset.socialHub = "1";
    document.head.appendChild(script);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", loadSocialHub, { once: true });
  } else {
    loadSocialHub();
  }
})();
