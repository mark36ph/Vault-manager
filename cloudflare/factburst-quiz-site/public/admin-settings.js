(() => {
  "use strict";
  const $ = s => document.querySelector(s);
  const toggle = $("#maintenance-toggle"), message = $("#maintenance-message"), save = $("#save-website-settings"), status = $("#website-settings-status");
  if (!toggle || !message || !save) return;
  const setStatus=(text,type="")=>{status.textContent=text;status.className=`admin-status ${type}`;};
  async function api(options={}){const r=await fetch("/api/admin/users/site-settings",{credentials:"same-origin",cache:"no-store",...options});let d={};try{d=await r.json();}catch{}if(!r.ok)throw new Error(d.error||`Request failed (${r.status})`);return d;}
  async function load(){try{const d=await api();toggle.checked=!!d.maintenance_enabled;message.value=d.maintenance_message||"";}catch(e){setStatus(e.message,"error");}}
  save.addEventListener("click",async()=>{save.disabled=true;setStatus("Saving…");try{const d=await api({method:"PATCH",headers:{"content-type":"application/json"},body:JSON.stringify({maintenance_enabled:toggle.checked,maintenance_message:message.value})});toggle.checked=!!d.maintenance_enabled;message.value=d.maintenance_message||"";setStatus(toggle.checked?"Maintenance mode enabled.":"Website is live.","success");}catch(e){setStatus(e.message,"error");}finally{save.disabled=false;}});
  load();
})();
