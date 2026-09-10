(() => {
  "use strict";
  const KEY = "factburst_admin_session_key";
  const $ = s => document.querySelector(s);
  const esc = v => String(v ?? "").replace(/[&<>'"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[c]));
  const number = v => Number(v || 0).toLocaleString();

  function headers(){
    return { Authorization:`Bearer ${sessionStorage.getItem(KEY)||""}`, accept:"application/json" };
  }

  async function load(){
    const status = $("#analytics-status");
    try {
      status.textContent = "Loading analytics…";
      const days = $("#analytics-days").value || 30;
      const r = await fetch(`/api/admin/analytics?days=${encodeURIComponent(days)}`, { headers:headers(), credentials:"same-origin", cache:"no-store" });
      const data = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(data.error || `Request failed (${r.status}).`);
      render(data);
      status.textContent = "Updated just now.";
    } catch (e) {
      status.textContent = e.message;
    }
  }

  function rows(items, labelKey, limit = 10){
    return (items || []).slice(0, limit).map(x =>
      `<div class="analytics-row"><span title="${esc(x[labelKey])}">${esc(x[labelKey])}</span><strong>${number(x.count)}</strong></div>`
    ).join("") || '<p class="analytics-empty">No data recorded for this period.</p>';
  }

  function render(d){
    const totals = d.totals || {};
    $("#analytics-events").textContent = number(totals.events);
    $("#analytics-starts").textContent = number(totals.starts);
    $("#analytics-completions").textContent = number(totals.completions);
    $("#analytics-shares").textContent = number(totals.shares);
    $("#analytics-youtube").textContent = number(totals.youtube_clicks);
    const rate = totals.starts ? Math.round((Number(totals.completions || 0) / Number(totals.starts)) * 100) : 0;
    $("#analytics-rate").textContent = `${rate}%`;

    const quizzes = [...(d.quizzes || [])].sort((a,b) => Number(b.count||0) - Number(a.count||0));
    const sources = [...(d.sources || [])].sort((a,b) => Number(b.count||0) - Number(a.count||0));
    const topQuiz = quizzes[0];
    const topSource = sources[0];
    $("#analytics-top-quiz").textContent = topQuiz?.quiz_slug || "—";
    $("#analytics-top-quiz-count").textContent = topQuiz ? `${number(topQuiz.count)} tracked events` : "No quiz activity yet";
    $("#analytics-top-source").textContent = topSource?.source || "—";
    $("#analytics-top-source-count").textContent = topSource ? `${number(topSource.count)} tracked events` : "No source data yet";
    $("#analytics-period").textContent = `${d.from || "—"} → ${d.to || "—"}`;

    $("#analytics-events-list").innerHTML = rows(d.events, "event_name", 12);
    $("#analytics-quizzes-list").innerHTML = rows(quizzes, "quiz_slug", 12);
    $("#analytics-sources-list").innerHTML = rows(sources, "source", 12);

    const daily = d.daily || [];
    const max = Math.max(1, ...daily.map(x => Number(x.count || 0)));
    $("#analytics-daily-list").innerHTML = daily.map(x => {
      const n = Number(x.count || 0);
      const pct = Math.round((n / max) * 100);
      return `<div class="analytics-day"><span class="analytics-day-label">${esc(x.day)}</span><div class="analytics-bar" aria-label="${n} events"><i style="width:${pct}%"></i></div><strong>${number(n)}</strong></div>`;
    }).join("") || '<p class="analytics-empty">No daily activity recorded.</p>';
  }

  function init(){
    const button = $("#analytics-refresh"), select = $("#analytics-days");
    if (!button || !select) return;
    button.addEventListener("click", load);
    select.addEventListener("change", load);
    document.addEventListener("factburst-admin-session-ready", load);
    window.addEventListener("factburst:admin-auth-ready", load);
    if (sessionStorage.getItem(KEY)) load();
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init, {once:true});
  else init();
})();
