const SITE_ORIGIN = "https://factburstquiz.com";

const CLEAN_PAGE_ASSETS = new Map([
  ["/profile", "/profile.html"],
  ["/leaderboard", "/leaderboard.html"],
  ["/terms", "/terms.html"],
  ["/privacy", "/privacy.html"],
  ["/admin", "/admin.html"],
]);

const LEGACY_PAGE_PATHS = new Map([
  ["/index.html", "/"],
  ["/profile.html", "/profile"],
  ["/leaderboard.html", "/leaderboard"],
  ["/terms.html", "/terms"],
  ["/privacy.html", "/privacy"],
  ["/admin.html", "/admin"],
]);

export function seoAssetPath(pathname) {
  const path = String(pathname || "/");
  if (path === "/") return "/index.html";
  return CLEAN_PAGE_ASSETS.get(path) || path;
}

export function cleanRedirectLocation(url) {
  const targetPath = LEGACY_PAGE_PATHS.get(url?.pathname || "");
  if (!targetPath) return "";
  const target = new URL(targetPath, SITE_ORIGIN);
  target.search = url.search || "";
  return target.toString();
}

export function rewritePublicPaths(value) {
  let output = String(value ?? "");
  for (const [legacyPath, cleanPath] of LEGACY_PAGE_PATHS) {
    output = output.split(`${SITE_ORIGIN}${legacyPath}`).join(`${SITE_ORIGIN}${cleanPath}`);
    output = output.split(legacyPath).join(cleanPath);
  }
  return output;
}
