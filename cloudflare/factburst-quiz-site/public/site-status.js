fetch("/api/site/status", { credentials: "same-origin", cache: "no-store" })
  .then(response => response.ok ? response.json() : null)
  .then(status => {
    if (!status?.maintenance) return;
    if (status.is_admin) {
      showAdminBanner(status.message || "");
      return;
    }
    showMaintenanceScreen(status.message || "Factburst Quiz is currently undergoing maintenance. Please check back shortly.");
  })
  .catch(() => {});

function showAdminBanner(message) {
  if (document.querySelector("#factburst-maintenance-banner")) return;
  const banner = document.createElement("div");
  banner.id = "factburst-maintenance-banner";
  banner.className = "site-maintenance-admin-banner";
  banner.textContent = `MAINTENANCE MODE — You are viewing the live site as an administrator. ${message}`;
  document.body.prepend(banner);
  document.body.classList.add("admin-maintenance-active");
}

function showMaintenanceScreen(message) {
  if (document.querySelector("#factburst-maintenance-screen")) return;
  document.title = "Maintenance | Factburst Quiz";
  document.body.innerHTML = `
    <div id="factburst-maintenance-screen" class="factburst-maintenance-screen">
      <div class="factburst-maintenance-bar">Factburst Quiz is in maintenance mode</div>
      <main class="factburst-maintenance-card">
        <h1>We’ll be back shortly</h1>
        <p>${escapeHtml(message)}</p>
        <section>
          <strong>Administrator access</strong>
          <p>Admins can sign in below to continue using the site while maintenance is active.</p>
          <form id="factburst-maintenance-login">
            <label>Username<input id="factburst-maintenance-user" autocomplete="username" required></label>
            <label>Password<input id="factburst-maintenance-password" type="password" autocomplete="current-password" required></label>
            <button>Admin sign in</button>
            <p id="factburst-maintenance-status" class="factburst-maintenance-status"></p>
          </form>
        </section>
      </main>
    </div>`;

  const style = document.createElement("style");
  style.textContent = `
    html,body{margin:0;min-height:100%;background:#061447}.factburst-maintenance-screen{min-height:100vh;display:grid;place-items:center;padding:72px 16px 32px;background:linear-gradient(135deg,#061447,#28166c);font:16px system-ui,sans-serif;color:#eef5ff}.factburst-maintenance-bar{position:fixed;inset:0 0 auto;background:#ffd43b;color:#111;padding:10px 18px;text-align:center;font-weight:800;z-index:99999}.factburst-maintenance-card{width:min(620px,100%);box-sizing:border-box;padding:34px;border:1px solid #3556a6;border-radius:22px;background:#0b1c59;box-shadow:0 24px 70px #0006}.factburst-maintenance-card h1{margin:0 0 12px;font-size:34px}.factburst-maintenance-card p{color:#bdd1ff;line-height:1.6}.factburst-maintenance-card section{margin-top:28px;padding-top:24px;border-top:1px solid #ffffff1c}.factburst-maintenance-card label{display:grid;gap:6px;margin:10px 0;font-weight:700}.factburst-maintenance-card input{box-sizing:border-box;width:100%;padding:12px;border-radius:10px;border:1px solid #5270b7;background:#06123d;color:white}.factburst-maintenance-card button{margin-top:10px;padding:12px 18px;border:0;border-radius:10px;background:#38d5ff;color:#03102d;font-weight:800;cursor:pointer}.factburst-maintenance-status{min-height:22px;margin-top:12px;color:#ffb7c9!important}`;
  document.head.appendChild(style);

  document.querySelector("#factburst-maintenance-login")?.addEventListener("submit", async event => {
    event.preventDefault();
    const statusText = document.querySelector("#factburst-maintenance-status");
    if (statusText) statusText.textContent = "Signing in…";
    try {
      const response = await fetch("/api/account/login", {
        method: "POST",
        credentials: "same-origin",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          username: document.querySelector("#factburst-maintenance-user")?.value || "",
          password: document.querySelector("#factburst-maintenance-password")?.value || "",
        }),
      });
      const payload = await response.json();
      if (!response.ok) throw new Error(payload?.error || "Sign in failed.");
      const siteStatus = await fetch("/api/site/status", { credentials: "same-origin", cache: "no-store" }).then(item => item.json());
      if (siteStatus?.is_admin) {
        location.reload();
        return;
      }
      await fetch("/api/account/logout", { method: "POST", credentials: "same-origin" });
      throw new Error("This account is not an administrator.");
    } catch (error) {
      if (statusText) statusText.textContent = error?.message || "Sign in failed.";
    }
  });
}

function escapeHtml(value) {
  return String(value || "").replace(/[&<>"']/g, character => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  })[character]);
}
