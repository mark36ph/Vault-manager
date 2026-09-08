(() => {
  const $ = (id) => document.getElementById(id);
  const number = (value) => Number(value || 0).toLocaleString();
  const esc = (value) => String(value ?? "").replace(/[&<>\"']/g, ch => ({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[ch]));

  async function load() {
    const status = $("social-status");
    if (!status) return;
    status.textContent = "Loading social stats…";
    try {
      const platform = $("social-platform-filter")?.value || "";
      const query = platform ? `?platform=${encodeURIComponent(platform)}&limit=500` : "?limit=500";
      const response = await fetch(`/api/social/stats${query}`, { credentials: "same-origin", headers: { accept: "application/json" } });
      const payload = await response.json();
      if (!response.ok) throw new Error(payload?.error || "Social stats could not be loaded.");
      render(payload?.stats || []);
      status.textContent = `Updated ${new Date().toLocaleString()}.`;
    } catch (error) {
      status.textContent = error?.message || "Social stats could not be loaded.";
    }
  }

  function render(stats) {
    const totals = { youtube: 0, facebook: 0, instagram: 0, engagement: 0 };
    for (const item of stats) {
      const platform = String(item.platform || "");
      totals[platform] = (totals[platform] || 0) + Number(item.views || 0);
      totals.engagement += Number(item.likes || 0) + Number(item.reactions || 0);
    }
    const yt = $("social-youtube-views"); const fb = $("social-facebook-views"); const ig = $("social-instagram-views"); const engagement = $("social-engagement");
    if (yt) yt.textContent = number(totals.youtube); if (fb) fb.textContent = number(totals.facebook); if (ig) ig.textContent = number(totals.instagram); if (engagement) engagement.textContent = number(totals.engagement);
    const list = $("social-list"), empty = $("social-empty");
    if (!list || !empty) return;
    list.innerHTML = ""; empty.classList.toggle("hidden", stats.length !== 0);
    for (const item of stats) {
      const card = document.createElement("article"); card.className = "social-stat-card";
      const platform = String(item.platform || ""); const label = platform === "youtube" ? "YouTube" : platform === "facebook" ? "Facebook" : "Instagram";
      const metrics = platform === "facebook" ? `Views ${number(item.views)} · Reactions ${number(item.reactions)} · Comments ${number(item.comments)} · Shares ${number(item.shares)}` : `Views ${number(item.views)} · Likes ${number(item.likes)} · Comments ${number(item.comments)}`;
      const date = item.captured_at ? new Date(item.captured_at).toLocaleString() : "—"; const link = item.url ? `<a href="${esc(item.url)}" target="_blank" rel="noopener">Open post</a>` : "";
      card.innerHTML = `<div class="social-stat-head"><div><span class="social-platform social-${esc(platform)}">${label}</span><h3>${esc(item.quiz_slug)}</h3></div><strong>${esc(item.status || "unknown")}</strong></div><p>${metrics}</p><small>Last reported: ${esc(date)} ${link ? "· " + link : ""}</small>${item.error_message ? `<div class="social-error">${esc(item.error_message)}</div>` : ""}`;
      list.append(card);
    }
  }

  $("refresh-social")?.addEventListener("click", load);
  $("social-platform-filter")?.addEventListener("change", load);
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", load, { once: true }); else load();

  if (!document.querySelector("script[data-social-hub]") && !document.querySelector("#admin-section-social-hub")) {
    const script = document.createElement("script"); script.src = "/admin-social-hub.js?v=1"; script.dataset.socialHub = "1"; document.head.appendChild(script);
  }
})();
