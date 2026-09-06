(() => {
  "use strict";
  const SESSION_OK = "/api/admin/auth/session";
  const LOGIN = "/api/admin/auth/login";
  const SETUP = "/api/admin/auth/setup";
  const LOGOUT = "/api/admin/auth/logout";
  let authPassed = false;
  let busy = false;

  function status(message, type = "") {
    const el = document.querySelector("#login-status");
    if (!el) return;
    el.textContent = message;
    el.className = `admin-status ${type}`.trim();
  }

  function submitToLegacyAdmin() {
    const key = document.querySelector("#admin-key");
    const form = document.querySelector("#login-form");
    if (!key || !form) return;
    key.value = "session";
    authPassed = true;
    form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
  }

  async function hasSession() {
    try {
      const response = await fetch(SESSION_OK, { credentials: "same-origin", cache: "no-store" });
      return response.ok;
    } catch { return false; }
  }

  async function login(event) {
    if (authPassed || busy) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const code = document.querySelector("#admin-key")?.value.trim() || "";
    if (!/^\d{6}$/.test(code)) {
      status("Enter the 6-digit code from Google Authenticator.", "error");
      return;
    }
    busy = true;
    status("Verifying authenticator code…");
    try {
      const response = await fetch(LOGIN, {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify({ code, trust_device: true }),
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(data?.error || "Authenticator code was not accepted.");
      status("Signed in. This device is trusted for 7 days.", "success");
      submitToLegacyAdmin();
    } catch (error) {
      status(error.message, "error");
    } finally { busy = false; }
  }

  function addSetupUi() {
    const form = document.querySelector("#login-form");
    if (!form || document.querySelector("#authenticator-setup")) return;
    const setup = document.createElement("details");
    setup.id = "authenticator-setup";
    setup.className = "admin-security-note";
    setup.innerHTML = `
      <summary>Set up Google Authenticator</summary>
      <p>Use the website publishing key once to create your authenticator secret.</p>
      <label for="setup-publishing-key">Website publishing key</label>
      <input id="setup-publishing-key" type="password" autocomplete="off" autocapitalize="none" spellcheck="false">
      <button id="setup-authenticator" class="button button-secondary" type="button">Generate authenticator secret</button>
      <div id="authenticator-result" class="hidden"></div>`;
    form.after(setup);

    setup.querySelector("#setup-authenticator").addEventListener("click", async () => {
      const key = setup.querySelector("#setup-publishing-key").value.trim();
      if (!key) return status("Enter the publishing key to set up the authenticator.", "error");
      const button = setup.querySelector("#setup-authenticator");
      button.disabled = true;
      try {
        const response = await fetch(SETUP, {
          method: "POST",
          credentials: "same-origin",
          headers: { Authorization: `Bearer ${key}`, Accept: "application/json" },
        });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data?.error || "Could not create the authenticator setup.");
        const result = setup.querySelector("#authenticator-result");
        result.classList.remove("hidden");
        result.textContent = `Secret: ${data.secret || ""}. Add this secret to Google Authenticator using Enter a setup key, then use the generated 6-digit code here.`;
        status("Authenticator secret created. Add it to Google Authenticator now.", "success");
      } catch (error) { status(error.message, "error"); }
      finally { button.disabled = false; }
    });
  }

  function init() {
    const form = document.querySelector("#login-form");
    const key = document.querySelector("#admin-key");
    const label = document.querySelector("label[for='admin-key']");
    if (!form || !key) return;
    if (label) label.textContent = "Google Authenticator code";
    key.type = "text";
    key.inputMode = "numeric";
    key.maxLength = 6;
    key.pattern = "[0-9]{6}";
    key.autocomplete = "one-time-code";
    key.placeholder = "123456";
    form.addEventListener("submit", login, true);
    addSetupUi();
    document.querySelector("#sign-out")?.addEventListener("click", () => {
      fetch(LOGOUT, { method: "POST", credentials: "same-origin", keepalive: true }).catch(() => {});
    }, true);
    hasSession().then(ok => { if (ok) submitToLegacyAdmin(); });
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init, { once: true });
  else init();
})();
