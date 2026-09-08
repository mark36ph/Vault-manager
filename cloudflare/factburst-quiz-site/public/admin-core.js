(() => {
  "use strict";
  const KEY="factburst_admin_session_key";
  const NAV=[
    ["Dashboard","/admin"],["Quizzes","/admin/quizzes"],["Social","/admin/social"],["Analytics","/admin/analytics"],["Settings","/admin/settings"],["Users","/admin/users"]
  ];
  const path=location.pathname.replace(/\/$/,"")||"/admin";
  function boot(){
    const app=document.querySelector("#admin-app"); if(!app)return;
    const content=document.querySelector("#admin-content")||app;
    const header=document.querySelector(".admin-header");
    if(header&&!header.querySelector("[data-admin-signout]")){
      const b=document.createElement("button"); b.className="button button-ghost";b.type="button";b.dataset.adminSignout="1";b.textContent="Sign out";header.querySelector(".admin-header-actions")?.appendChild(b);
      b.onclick=async()=>{try{await fetch("/api/admin/auth/logout",{method:"POST",credentials:"same-origin"});}catch{} sessionStorage.removeItem(KEY);location.href="/admin";};
    }
    let nav=app.querySelector(".admin-sidebar");
    if(!nav){nav=document.createElement("aside");nav.className="admin-sidebar";nav.setAttribute("aria-label","Admin navigation");nav.innerHTML=`<div class="admin-sidebar-label">Manage</div>${NAV.map(([label,href])=>`<a href="${href}" class="${href===path?"active":""}">${label}</a>`).join("")}`;app.insertBefore(nav,content);}
    if(!sessionStorage.getItem(KEY)){
      fetch("/api/admin/auth/session",{credentials:"same-origin",cache:"no-store"}).then(r=>{if(r.ok)sessionStorage.setItem(KEY,"session");else if(path!=="/admin")location.href="/admin";}).catch(()=>{});
    }
  }
  if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot,{once:true});else boot();
})();
