(() => {
  "use strict";
  const KEY="factburst_admin_session_key";
  const NAV=[["Dashboard","/admin"],["Quizzes","/admin/quizzes"],["Social","/admin/social"],["Analytics","/admin/analytics"],["Settings","/admin/settings"],["Users","/admin/users"]];
  const path=location.pathname.replace(/\/$/,"")||"/admin";
  function shell(){const app=document.querySelector("#admin-app");if(!app)return;let nav=app.querySelector(".admin-sidebar");if(!nav){nav=document.createElement("aside");nav.className="admin-sidebar";nav.setAttribute("aria-label","Admin navigation");nav.innerHTML=`<div class="admin-sidebar-label">Manage</div>${NAV.map(([label,href])=>`<a href="${href}" class="${href===path?"active":""}">${label}</a>`).join("")}`;app.insertBefore(nav,app.firstElementChild);}}
  function addSignOut(){const header=document.querySelector(".admin-header-actions");if(!header||header.querySelector("[data-admin-signout]"))return;const b=document.createElement("button");b.className="button button-ghost";b.type="button";b.dataset.adminSignout="1";b.textContent="Sign out";header.appendChild(b);b.onclick=async()=>{try{await fetch("/api/admin/auth/logout",{method:"POST",credentials:"same-origin"});}catch{}sessionStorage.removeItem(KEY);location.href="/admin";};}
  async function boot(){if(document.querySelector("#login-form")){shell();return;}try{const r=await fetch("/api/admin/auth/session",{credentials:"same-origin",cache:"no-store"});if(r.ok){sessionStorage.setItem(KEY,"session");shell();addSignOut();return;}}catch{}if(path!=="/admin"){location.href="/admin";return;}shell();}
  if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot,{once:true});else boot();
})();
