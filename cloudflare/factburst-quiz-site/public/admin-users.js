(() => {
  "use strict";
  const $ = s => document.querySelector(s);
  const list = $("#admin-users-list");
  const search = $("#admin-users-search");
  const status = $("#admin-users-status");
  if (!list || !search) return;
  let users = [];
  let selectedId = 0;

  const escapeHtml = value => String(value ?? "").replace(/[&<>\"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'\"':"&quot;", "'":"&#39;"}[c]));
  const setStatus = (message, type = "") => { if (status) { status.textContent = message; status.className = `admin-status ${type}`; } };

  async function api(path, options = {}) {
    const response = await fetch(path, { credentials: "same-origin", cache: "no-store", ...options });
    let data = {};
    try { data = await response.json(); } catch {}
    if (!response.ok) throw new Error(data.error || `Request failed (${response.status})`);
    return data;
  }

  function render() {
    const term = search.value.trim().toLowerCase();
    const visible = users.filter(u => !term || `${u.username} ${u.email} ${u.referral_code} ${u.referred_by || ""}`.toLowerCase().includes(term));
    list.innerHTML = visible.length ? visible.map(user => `
      <article class="admin-user-row" data-user-id="${user.id}">
        <div>
          <div class="admin-user-name">${escapeHtml(user.username)}</div>
          <div class="admin-user-email">${escapeHtml(user.email)}</div>
          <div class="admin-user-meta">
            <span class="admin-user-badge ${user.email_verified ? "verified" : "unverified"}">${user.email_verified ? "Verified" : "Unverified"}</span>
            <span class="admin-user-badge">${escapeHtml(user.referral_code || "No code")}</span>
            <span class="admin-user-badge">${Number(user.friends_count || 0)} friends</span>
            ${user.referred_by ? `<span class="admin-user-badge">Referred by ${escapeHtml(user.referred_by)}</span>` : ""}
          </div>
        </div>
        <div class="admin-user-actions"><button class="button button-secondary" data-action="view" data-id="${user.id}" type="button">Manage</button></div>
      </article>`).join("") : `<div class="admin-empty"><h3>No users found</h3><p>Try a different username, email or referral code.</p></div>`;
  }

  async function loadUsers() {
    setStatus("Loading users…");
    try { const data = await api("/api/admin/users?limit=100"); users = data.users || []; render(); setStatus(`${users.length} users loaded.`, "success"); }
    catch (error) { setStatus(error.message, "error"); }
  }

  async function manageUser(id) {
    selectedId = Number(id);
    setStatus("Loading user…");
    try {
      const data = await api(`/api/admin/users/${selectedId}`);
      const existing = list.querySelector(`[data-user-id="${selectedId}"]`);
      if (!existing) return;
      const user = data.user;
      existing.insertAdjacentHTML("beforeend", `
        <div class="admin-user-detail">
          <div class="admin-user-grid">
            <div class="admin-user-card">
              <h3>Account</h3>
              <form class="admin-user-form" data-form="edit">
                <label>Username<input name="username" value="${escapeHtml(user.username)}" maxlength="24" required></label>
                <label>Email<input name="email" type="email" value="${escapeHtml(user.email)}" maxlength="254" required></label>
                <label>New password<input name="password" type="password" minlength="10" maxlength="128" autocomplete="new-password" placeholder="Leave blank to keep current password"></label>
                <div class="admin-user-actions"><button class="button button-primary" type="submit">Save changes</button>${user.email_verified ? "" : "<button class=\"button button-secondary\" data-action=\"verify\" type=\"button\">Verify email</button>"}</div>
                <div class="admin-user-note">Passwords are securely hashed on the server; the current password is never displayed.</div>
              </form>
            </div>
            <div class="admin-user-card">
              <h3>Referral</h3>
              <p><strong>Code:</strong> ${escapeHtml(user.referral_code || "—")}</p>
              <p><strong>Referred by:</strong> ${escapeHtml(user.referred_by || "Nobody")}</p>
              <p><strong>Joined:</strong> ${escapeHtml(user.created_at ? new Date(user.created_at).toLocaleString() : "—")}</p>
              <p><strong>Last login:</strong> ${escapeHtml(user.last_login_at ? new Date(user.last_login_at).toLocaleString() : "—")}</p>
            </div>
          </div>
          <div class="admin-user-grid">
            <div class="admin-user-card"><h3>Friends (${(data.friends || []).length})</h3>${(data.friends || []).length ? `<ul class="admin-user-list">${data.friends.map(f => `<li><span>${escapeHtml(f.friend_username)}<br><small>${escapeHtml(f.friend_email)}</small></span><button class="button button-ghost danger-button" data-action="unlink" data-friendship="${f.id}" type="button">Unlink</button></li>`).join("")}</ul>` : `<p class="admin-user-empty">No friends linked.</p>`}</div>
            <div class="admin-user-card"><h3>Referred users (${(data.referred_users || []).length})</h3>${(data.referred_users || []).length ? `<ul class="admin-user-list">${data.referred_users.map(r => `<li><span>${escapeHtml(r.username)}<br><small>${escapeHtml(r.email)}</small></span><span class="admin-user-badge ${r.email_verified_at ? "verified" : "unverified"}">${r.email_verified_at ? "Verified" : "Unverified"}</span></li>`).join("")}</ul>` : `<p class="admin-user-empty">Nobody has used this referral code yet.</p>`}</div>
          </div>
        </div>`);
      setStatus("");
    } catch (error) { setStatus(error.message, "error"); }
  }

  list.addEventListener("click", async event => {
    const button = event.target.closest("button[data-action]");
    if (!button) return;
    const row = button.closest("[data-user-id]");
    const id = Number(button.dataset.id || row?.dataset.userId || selectedId);
    try {
      if (button.dataset.action === "view") return manageUser(id);
      if (button.dataset.action === "verify") { await api(`/api/admin/users/${id}/verify`, { method: "POST" }); setStatus("Email verified.", "success"); await loadUsers(); return manageUser(id); }
      if (button.dataset.action === "unlink") { if (!confirm("Unlink this friendship?")) return; await api(`/api/admin/users/${id}/friendship`, { method: "DELETE", headers: { "content-type": "application/json" }, body: JSON.stringify({ friendship_id: Number(button.dataset.friendship) }) }); setStatus("Friendship unlinked.", "success"); return manageUser(id); }
    } catch (error) { setStatus(error.message, "error"); }
  });

  list.addEventListener("submit", async event => {
    const form = event.target.closest('[data-form="edit"]');
    if (!form) return;
    event.preventDefault();
    const body = Object.fromEntries(new FormData(form).entries());
    try { await api(`/api/admin/users/${selectedId}`, { method: "PATCH", headers: { "content-type": "application/json" }, body: JSON.stringify(body) }); setStatus("User changes saved.", "success"); await loadUsers(); return manageUser(selectedId); }
    catch (error) { setStatus(error.message, "error"); }
  });

  search.addEventListener("input", render);
  $("#refresh-users")?.addEventListener("click", loadUsers);
  loadUsers();
})();