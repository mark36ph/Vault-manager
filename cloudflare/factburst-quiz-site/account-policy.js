const RESERVED_EXACT = new Set([
  "admin",
  "administrator",
  "mod",
  "moderator",
  "staff",
  "support",
  "owner",
  "official",
  "system",
  "root",
  "factburst",
  "factburstquiz",
]);

const RESERVED_PREFIXES = [
  "admin",
  "administrator",
  "moderator",
  "staff",
  "support",
  "factburst",
  "factburstquiz",
];

export function reservedUsernameReason(value) {
  const username = String(value || "").trim().toLowerCase();
  if (!username) return "";

  const tokens = username.split(/[^a-z0-9]+/).filter(Boolean);
  const compact = username.replace(/[^a-z0-9]/g, "");
  if (!compact) return "";

  if (compact.includes("factburst")) {
    return "That username is reserved for Factburst.";
  }

  if (RESERVED_EXACT.has(compact) || [...RESERVED_EXACT].some(term => new RegExp(`^${term}\\d+$`).test(compact))) {
    return "That username is reserved for site administration or support.";
  }

  if (tokens.some(token => RESERVED_EXACT.has(token) || [...RESERVED_EXACT].some(term => new RegExp(`^${term}\\d+$`).test(token)))) {
    return "That username is reserved for site administration or support.";
  }

  if (RESERVED_PREFIXES.some(prefix => compact.startsWith(prefix))) {
    return "That username is too similar to a reserved site role or Factburst identity.";
  }

  return "";
}

export function isReservedUsername(value) {
  return reservedUsernameReason(value).length > 0;
}
