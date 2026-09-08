(() => {
  "use strict";
  const KEY = "factburst_admin_session_key";
  const $ = s => document.querySelector(s);
  const esc = v => String(v ?? "").replace(/[&<>'"]/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[c]));
  function headers(){return {Authorization:`Bearer ${sessionStorage.getItem(KEY)||""}`,accept:"application/json"};}
  async function load(){const status=$("#analytics-status");try{status.textContent="Loading analytics…";const days=$("#analytics-days").value||30;const r=await fetch(`/api/admin/analytics?days=${encodeURIComponent(days)}`,{headers:headers(),credentials:"same-origin"});const data=await r.json().catch(()=>({}));if(!r.ok)throw new Error(data.error||`Request failed (${r.status}).`);render(data);status.textContent="Updated just now.";}catch(e){status.textContent=e.message;}}
  function render(d){
    $("#analytics-events").textContent=String(d.totals?.events||0);$("#analytics-starts").textContent=String(d.totals?.starts||0);$("#analytics-completions").textContent=String(d.totals?.completions||0);$("#analytics-shares").textContent=String(d.totals?.shares||0);
    const rate=d.totals?.starts?Math.round((d.totals.completions/d.totals.starts)*100):0;$("#analytics-rate").textContent=`${rate}%`;
    $("#analytics-events-list").innerHTML=(d.events||[]).map(x=>`<div class="admin-analytics-row"><span>${esc(x.event_name)}</span><strong>${Number(x.count||0)}</strong></div>`).join("")||'<p class="admin-empty-text">No events recorded.</p>';
    $("#analytics-quizzes-list").innerHTML=(d.quizzes||[]).map(x=>`<div class="admin-analytics-row"><span>${esc(x.quiz_slug)}</span><strong>${Number(x.count||0)}</strong></div>`).join("")||'<p class="admin-empty-text">No quiz activity recorded.</p>';
    $("#analytics-sources-list").innerHTML=(d.sources||[]).map(x=>`<div class="admin-analytics-row"><span>${esc(x.source)}</span><strong>${Number(x.count||0)}</strong></div>`).join("")||'<p class="admin-empty-text">No source data recorded.</p>';
    const max=Math.max(1,...(d.daily||[]).map(x=>Number(x.count||0)));$("#analytics-daily-list").innerHTML=(d.daily||[]).map(x=>{const n=Number(x.count||0);return `<div class="admin-analytics-day"><span>${esc(x.day)}</span><div class="admin-analytics-bar"><i style="width:${Math.round(n/max*100)}%"></i></div><strong>${n}</strong></div>`}).join("")||'<p class="admin-empty-text">No daily activity recorded.</p>';
  }
  function init(){const button=$("#analytics-refresh"),select=$("#analytics-days");if(!button||!select)return;button.addEventListener("click",load);select.addEventListener("change",load);window.addEventListener("factburst:admin-auth-ready",load);if(sessionStorage.getItem(KEY))load();}
  if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",init,{once:true});else init();
})();
