initializeProfileLink().catch(() => {});

async function initializeProfileLink() {
  const response = await fetch("/api/account", { credentials: "same-origin" });
  if (!response.ok) return;
  const payload = await response.json();
  if (!payload?.authenticated || !payload?.user?.email_verified) return;

  const header = document.querySelector(".site-header");
  if (!header) return;
  header.querySelector(".account-profile-link")?.remove();

  const welcome = document.createElement("span");
  welcome.className = "account-welcome";
  welcome.append(document.createTextNode("Welcome "));
  const link = document.createElement("a");
  link.href = "/profile.html";
  link.textContent = payload.user.username || "Player";
  link.setAttribute("aria-label", `Open ${payload.user.username || "your"} Factburst profile`);
  welcome.append(link);

  const trigger = header.querySelector(".account-trigger");
  if (trigger) trigger.replaceWith(welcome);
  else header.append(welcome);
}
