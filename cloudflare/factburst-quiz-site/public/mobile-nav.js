(() => {
  const MOBILE_QUERY = "(max-width: 700px)";
  let initialized = false;

  function apply() {
    const nav = document.querySelector(".top-nav");
    if (!nav || initialized) return;
    const links = Array.from(nav.querySelectorAll(":scope > a"));
    if (!links.length) return;
    const header = nav.closest(".site-header");
    if (!header) return;
    initialized = true;

    const button = document.createElement("button");
    button.type = "button";
    button.className = "mobile-nav-toggle";
    button.setAttribute("aria-expanded", "false");
    button.setAttribute("aria-controls", "mobile-nav-menu");
    button.innerHTML = '<span class="mobile-nav-toggle-icon" aria-hidden="true"><span></span><span></span><span></span></span><span class="mobile-nav-toggle-label">Menu</span>';

    const panel = document.createElement("div");
    panel.id = "mobile-nav-menu";
    panel.className = "mobile-nav-menu";
    panel.hidden = true;

    const panelLinks = document.createElement("div");
    panelLinks.className = "mobile-nav-menu-links";
    for (const link of links) {
      const clone = link.cloneNode(true);
      clone.classList.add("mobile-nav-link");
      panelLinks.append(clone);
    }
    panel.append(panelLinks);

    const actions = document.createElement("div");
    actions.className = "mobile-nav-menu-actions";
    const notificationProxy = document.createElement("button");
    notificationProxy.type = "button";
    notificationProxy.className = "mobile-nav-notification";
    notificationProxy.textContent = "🔔  Notifications";
    notificationProxy.addEventListener("click", () => {
      const original = nav.querySelector("#notification-button");
      if (original) original.click();
    });
    actions.append(notificationProxy);
    panel.append(actions);

    const brand = header.querySelector(":scope > .brand");
    const account = header.querySelector(":scope > .account-header-area");
    const topRow = document.createElement("div");
    topRow.className = "mobile-header-top";
    if (brand) topRow.append(brand);
    const controls = document.createElement("div");
    controls.className = "mobile-header-controls";
    if (account) controls.append(account);
    controls.append(button);
    topRow.append(controls);

    header.insertBefore(topRow, nav);
    header.insertBefore(panel, nav);
    nav.classList.add("desktop-navigation-source");

    function closeMenu() {
      button.setAttribute("aria-expanded", "false");
      button.classList.remove("is-open");
      panel.hidden = true;
    }

    button.addEventListener("click", () => {
      const open = button.getAttribute("aria-expanded") === "true";
      button.setAttribute("aria-expanded", String(!open));
      button.classList.toggle("is-open", !open);
      panel.hidden = open;
    });
    panel.addEventListener("click", (event) => {
      if (event.target.closest("a")) closeMenu();
    });
    document.addEventListener("click", (event) => {
      if (!header.contains(event.target)) closeMenu();
    });
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape") closeMenu();
    });

    const style = document.createElement("style");
    style.textContent = `
      .mobile-header-top,.mobile-nav-menu{display:none}
      @media (max-width:700px){
        .site-header{display:block!important;padding:8px 12px 10px!important}
        .mobile-header-top{display:flex;align-items:center;justify-content:space-between;gap:12px;min-height:50px}
        .mobile-header-top>.brand{min-width:0;display:flex;align-items:center}
        .mobile-header-controls{display:flex;align-items:center;gap:7px;flex:0 0 auto}
        .mobile-header-controls>.account-header-area{margin:0!important}
        .mobile-header-controls .account-trigger{max-width:126px;min-height:40px;padding:7px 10px;font-size:12px}
        .mobile-nav-toggle{display:inline-flex;align-items:center;justify-content:center;gap:8px;min-width:72px;min-height:40px;padding:8px 11px;border:1px solid rgba(170,192,224,.18);border-radius:10px;background:rgba(255,255,255,.035);color:#f8fbff;font:inherit;font-size:12px;font-weight:750;cursor:pointer}
        .mobile-nav-toggle:hover{background:rgba(53,199,255,.08)}
        .mobile-nav-toggle-icon{display:flex;flex-direction:column;gap:3px;width:15px}
        .mobile-nav-toggle-icon span{display:block;width:15px;height:2px;border-radius:999px;background:currentColor;transition:transform 160ms ease,opacity 160ms ease}
        .mobile-nav-toggle.is-open .mobile-nav-toggle-icon span:nth-child(1){transform:translateY(5px) rotate(45deg)}
        .mobile-nav-toggle.is-open .mobile-nav-toggle-icon span:nth-child(2){opacity:0}
        .mobile-nav-toggle.is-open .mobile-nav-toggle-icon span:nth-child(3){transform:translateY(-5px) rotate(-45deg)}
        .mobile-nav-menu{margin-top:7px;padding:8px;border:1px solid rgba(170,192,224,.15);border-radius:14px;background:rgba(8,14,24,.98);box-shadow:0 18px 48px rgba(0,0,0,.34)}
        .mobile-nav-menu[hidden]{display:none!important}
        .mobile-nav-menu-links{display:grid;gap:3px}
        .mobile-nav-link{display:flex;align-items:center;min-height:44px;padding:10px 12px;border-radius:9px;color:var(--muted,#9ba9bd);text-decoration:none;font-size:14px;font-weight:700}
        .mobile-nav-link:hover,.mobile-nav-link:focus-visible{background:rgba(255,255,255,.045);color:#fff}
        .mobile-nav-link[aria-current="page"]{background:rgba(53,199,255,.09);color:#fff}
        .mobile-nav-menu-actions{margin-top:7px;padding-top:7px;border-top:1px solid rgba(170,192,224,.09)}
        .mobile-nav-notification{width:100%;min-height:42px;justify-content:flex-start;padding:9px 12px;border:0;border-radius:9px;background:transparent;color:var(--muted,#9ba9bd);font:inherit;font-size:14px;font-weight:700;text-align:left;cursor:pointer}
        .mobile-nav-notification:hover{background:rgba(255,255,255,.045);color:#fff}
        .desktop-navigation-source{display:none!important}
      }
      @media (max-width:380px){
        .mobile-header-controls .account-trigger{max-width:100px}
        .mobile-nav-toggle{min-width:64px}
        .mobile-nav-toggle-label{display:none}
      }
      @media (prefers-reduced-motion:reduce){.mobile-nav-toggle-icon span{transition:none}}
    `;
    document.head.append(style);
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", apply, { once: true });
  else apply();
})();
