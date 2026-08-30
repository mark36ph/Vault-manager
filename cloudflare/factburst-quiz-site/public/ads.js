let factburstAdsStarted = false;

if (document.body.dataset.page === "quiz") {
  if (hasAdvertisingConsent()) {
    startAds();
  }
  window.addEventListener("factburst:cookie-consent", event => {
    if (event.detail?.choice === "accepted") startAds();
  });
}

function hasAdvertisingConsent() {
  try {
    if (window.factburstCookieConsent?.acceptsOptional) {
      return window.factburstCookieConsent.acceptsOptional();
    }
    return localStorage.getItem("factburst-cookie-consent-v1") === "accepted";
  } catch {
    return false;
  }
}

function startAds() {
  if (factburstAdsStarted) return;
  factburstAdsStarted = true;
  initializeSideAds().catch(error => console.error("Factburst side ads unavailable", error));
}

async function initializeSideAds() {
  if (!window.matchMedia("(min-width: 1180px)").matches) return;
  const response = await fetch("/api/site/ads", { credentials: "same-origin", cache: "no-store" });
  if (!response.ok) return;
  const config = await response.json();
  if (!config?.enabled || !validClient(config.client)) return;

  const slots = [
    [document.querySelector("#quiz-ad-left"), config.left_slot],
    [document.querySelector("#quiz-ad-right"), config.right_slot],
  ].filter(([host, slot]) => host && validSlot(slot));
  if (slots.length === 0) return;

  const script = document.createElement("script");
  script.async = true;
  script.crossOrigin = "anonymous";
  script.src = `https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=${encodeURIComponent(config.client)}`;
  document.head.append(script);

  for (const [host, slot] of slots) {
    host.classList.remove("hidden");
    const label = document.createElement("span");
    label.className = "quiz-ad-label";
    label.textContent = "Advertisement";
    const ad = document.createElement("ins");
    ad.className = "adsbygoogle quiz-side-ad";
    ad.style.display = "block";
    ad.dataset.adClient = config.client;
    ad.dataset.adSlot = slot;
    ad.dataset.adFormat = "auto";
    ad.dataset.fullWidthResponsive = "true";
    host.replaceChildren(label, ad);
    try {
      (window.adsbygoogle = window.adsbygoogle || []).push({});
    } catch (error) {
      console.error("Could not initialize AdSense slot", error);
    }
  }
}

function validClient(value) {
  return /^ca-pub-\d{10,24}$/.test(String(value || ""));
}

function validSlot(value) {
  return /^\d{4,20}$/.test(String(value || ""));
}
