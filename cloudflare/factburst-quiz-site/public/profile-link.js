initializeProfileLink().catch(() => {});

async function initializeProfileLink() {
  const response = await fetch("/api/account", { credentials: "same-origin" });
  if (!response.ok) return;
  const payload = await response.json();
  if (!payload?.authenticated || !payload?.user?.email_verified) return;

  const header = document.querySelector(".site-header");
  if (!header || header.querySelector(".account-profile-link")) return;
  const link = document.createElement("a");
  link.className = "account-profile-link";
  link.href = "/profile.html";
  link.textContent = "Profile";
  const trigger = header.querySelector(".account-trigger");
  if (trigger) header.insertBefore(link, trigger);
  else header.append(link);
}
