let factburstAdsStarted = false;

if (document.body.dataset.page === "quiz") {
  startAds();
}

function startAds() {
  if (factburstAdsStarted) return;
  factburstAdsStarted = true;
  initializeAds().catch(error => console.error("Factburst ads unavailable", error));
}

async function initializeAds() {
  const response = await fetch("/api/site/ads", { credentials: "same-origin", cache: "no-store" });
  if (!response.ok) return;
  const config = await response.json();
  if (!config?.enabled || !validClient(config.client)) return;

  const desktop = window.matchMedia("(min-width: 1180px)").matches;
  if (desktop) {
    initializeDesktopAds(config);
  } else {
    initializeMobileAd(config);
  }
}

function initializeDesktopAds(config) {
  const slots = [
    [document.querySelector("#quiz-ad-left"), config.left_slot],
    [document.querySelector("#quiz-ad-right"), config.right_slot],
  ].filter(([host, slot]) => host && validSlot(slot));

  for (const [host, slot] of slots) mountAd(host, slot, "quiz-side-ad");
}

function initializeMobileAd(config) {
  const host = document.querySelector("#quiz-ad-mobile");
  const slot = validSlot(config.left_slot) ? config.left_slot : config.right_slot;
  if (!host || !validSlot(slot)) return;
  mountAd(host, slot, "quiz-mobile-ad");
}

function mountAd(host, slot, className) {
  host.classList.remove("hidden");
  const label = document.createElement("span");
  label.className = "quiz-ad-label";
  label.textContent = "Advertisement";

  const ad = document.createElement("ins");
  ad.className = `adsbygoogle ${className}`;
  ad.style.display = "block";
  ad.dataset.adClient = currentClient();
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

function currentClient() {
  return document.querySelector('meta[name="google-adsense-account"]')?.content || "";
}

function validClient(value) {
  return /^ca-pub-\d{10,24}$/.test(String(value || ""));
}

function validSlot(value) {
  return /^\d{4,20}$/.test(String(value || ""));
}
