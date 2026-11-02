(()=>{
 'use strict';
 // Themes only change CSS variables (see tokens.css). The choice is stored locally, per user.
 const KEY='orbit_theme',THEMES=['aurora','ocean','sunset','graphite'];
 const read=()=>{try{const v=localStorage.getItem(KEY);return THEMES.includes(v)?v:'aurora';}catch{return 'aurora';}};
 let current=read();
 const mark=()=>document.querySelectorAll('[data-theme-set]').forEach(b=>{
  const on=b.dataset.themeSet===current;
  b.classList.toggle('active',on);
  b.setAttribute('aria-pressed',String(on));
 });
 const apply=t=>{current=THEMES.includes(t)?t:'aurora';document.documentElement.dataset.theme=current;mark();};
 apply(current);

 // Soft entrance for the content area whenever the user switches section or store.
 let timer=0;
 const enter=()=>{
  const m=document.querySelector('main');
  if(!m)return;
  m.classList.remove('view-enter');
  void m.offsetWidth;
  m.classList.add('view-enter');
  clearTimeout(timer);
  timer=setTimeout(()=>m.classList.remove('view-enter'),800);
 };

 document.addEventListener('click',e=>{
  const swatch=e.target.closest&&e.target.closest('[data-theme-set]');
  if(swatch){
   try{localStorage.setItem(KEY,swatch.dataset.themeSet);}catch{}
   apply(swatch.dataset.themeSet);
   return;
  }
  if(e.target.closest&&e.target.closest('[data-section],[data-store]'))enter();
 },true);

 // The settings dialog is rendered on demand, so mark the active swatch after each render.
 const watch=()=>{
  const modal=document.getElementById('modal');
  if(modal)new MutationObserver(mark).observe(modal,{childList:true,subtree:true});
 };
 if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',watch);else watch();
})();
