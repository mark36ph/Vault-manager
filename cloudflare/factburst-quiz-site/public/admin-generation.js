(() => {
  "use strict";

  const layoutCss = `
    #admin-app{display:grid;grid-template-columns:220px minmax(0,1fr);column-gap:24px;align-items:start}
    #admin-app>.admin-page-heading,#admin-app>#app-status{grid-column:1 / -1}
    #admin-app>.admin-section-nav{grid-column:1;grid-row:3 / span 20;position:sticky;top:18px;display:flex;flex-direction:column;gap:4px;padding:10px;border:1px solid rgba(255,255,255,.1);border-radius:18px;background:rgba(10,16,27,.82);box-shadow:0 18px 50px rgba(0,0,0,.16);backdrop-filter:blur(16px);z-index:4}
    #admin-app>.admin-section-nav:before{content:"Manage";display:block;padding:7px 10px 8px;color:#9ca8ba;font-size:11px;font-weight:800;letter-spacing:.09em;text-transform:uppercase}
    #admin-app>.admin-section-nav a{display:flex;align-items:center;min-height:42px;padding:10px 12px;border-radius:11px;color:#aeb9ca;text-decoration:none;font-weight:700;font-size:13px;transition:background .16s ease,color .16s ease,transform .16s ease}
    #admin-app>.admin-section-nav a:hover{background:rgba(255,255,255,.06);color:#fff;transform:translateX(1px)}
    #admin-app>.admin-section-nav a.active{background:linear-gradient(135deg,rgba(110,145,255,.2),rgba(100,220,255,.1));color:#fff;box-shadow:inset 0 0 0 1px rgba(130,165,255,.18)}
    #admin-app>[data-admin-section-panel],#admin-app>#editor-panel,#admin-app>#admin-section-social-hub{grid-column:2;min-width:0}
    #admin-app>.admin-page-heading{margin-top:18px}
    #admin-app>.admin-page-heading p{margin-bottom:0}
    @media(max-width:860px){#admin-app{grid-template-columns:180px minmax(0,1fr);column-gap:16px}#admin-app>.admin-section-nav{top:12px}}
    @media(max-width:720px){#admin-app{display:block}#admin-app>.admin-section-nav{position:sticky;top:0;z-index:10;margin:0 0 16px;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:4px;padding:7px;border-radius:15px}#admin-app>.admin-section-nav:before{grid-column:1/-1;padding:4px 8px 3px}#admin-app>.admin-section-nav a{justify-content:center;min-height:38px;padding:8px 6px;font-size:12px}#admin-app>.admin-section-nav a:hover{transform:none}}
    @media(max-width:430px){#admin-app>.admin-section-nav{grid-template-columns:repeat(2,minmax(0,1fr))}
  `;

  function injectLayout() {
    if (document.getElementById("factburst-admin-layout-style")) return;
    const style = document.createElement("style");
    style.id = "factburst-admin-layout-style";
    style.textContent = layoutCss;
    document.head.appendChild(style);
  }

  function loadSocialHub() {
    if (document.querySelector("script[data-social-hub]") || document.querySelector("#admin-section-social-hub")) return;
    const script = document.createElement("script");
    script.src = "/admin-social.js?v=3";
    script.dataset.socialHub = "1";
    document.head.appendChild(script);
  }

  function removeWebsiteGenerationUi() {
    document.querySelectorAll('[data-admin-section="generation"]').forEach(el => el.remove());
    document.querySelectorAll('[data-admin-section-panel="generation"]').forEach(el => el.remove());
    const heading = document.querySelector("#admin-app .admin-page-heading p:not(.eyebrow)");
    if (heading && /generation/i.test(heading.textContent)) {
      heading.textContent = "Manage quizzes, publishing, social and growth analytics.";
    }
    if (location.hash === "#generation") {
      history.replaceState(null, "", "#dashboard");
      document.querySelector('[data-admin-section="dashboard"]')?.click();
    }
  }

  function init() {
    injectLayout();
    removeWebsiteGenerationUi();
    loadSocialHub();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init, { once: true });
  } else {
    init();
  }
})();
