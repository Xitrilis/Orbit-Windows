(()=>{
 'use strict';
 const $=s=>document.querySelector(s),$$=s=>[...document.querySelectorAll(s)];
 const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const money=(v,c)=>v==null?'—':new Intl.NumberFormat('ru-RU',{style:'currency',currency:c||'RUB',currencyDisplay:'narrowSymbol',minimumFractionDigits:0,maximumFractionDigits:2}).format(v);
 const safeImage=s=>typeof s==='string'&&(/^(https:|file:|data:image\/|assets\/)/i.test(s))?s:'';

 const ALIASES={
  'кибер':'cyber','киберпанк':'cyberpunk','ведьмак':'witcher','ведьма':'witcher','гта':'gta','батман':'batman','бэтмен':'batman','дота':'dota','кс':'counter-strike','контра':'counter-strike','ассасин':'assassin','сталкер':'stalker','борда':'borderlands','скайрим':'skyrim','фоллаут':'fallout','халфа':'half-life','майнкрафт':'minecraft','метро':'metro','резидент':'resident evil','фифа':'fifa'
 };

 function translit(str){
  const ru='абвгдеёжзийклмнопрстуфхцчшщъыьэюя';
  const en=['a','b','v','g','d','e','e','zh','z','i','y','k','l','m','n','o','p','r','s','t','u','f','h','ts','ch','sh','sch','','y','','e','yu','ya'];
  let res='';
  for(const ch of String(str||'').toLowerCase()){
   const idx=ru.indexOf(ch);
   res+=idx>=0?en[idx]:ch;
  }
  return res;
 }

 let api=null,qsTimer=null,qsSeq=0,activeIdx=-1,steamResults=[],isOpenState=false,currentQuery='',returnFocus=null;

 function matchesQuery(name,q){
  if(!name||!q)return false;
  const n=name.toLowerCase(),ql=q.toLowerCase().trim();
  if(!ql)return false;
  if(n.includes(ql))return true;
  const alias=ALIASES[ql];
  if(alias&&n.includes(alias))return true;
  for(const [k,v] of Object.entries(ALIASES)){
   if(ql.includes(k)&&n.includes(v))return true;
  }
  const tr=translit(ql);
  if(tr.length>=3&&n.includes(tr))return true;
  return false;
 }

 function init(a){
  api=a;
  ensureDom();
  bindEvents();
 }

 function ensureDom(){
  if($('#quick-search-backdrop'))return;
  const el=document.createElement('div');
  el.id='quick-search-backdrop';
  el.className='quick-search-backdrop';
  el.hidden=true;
  el.innerHTML=`
   <div class="quick-search-box" role="dialog" aria-modal="true" aria-label="Быстрый поиск игр">
    <div class="quick-search-header">
     <span class="quick-search-lens"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 5 5"/></svg></span>
     <input id="quick-search-input" class="quick-search-input" type="text" placeholder="Быстрый поиск игр в библиотеке и Steam... (Ctrl+F)" autocomplete="off" spellcheck="false">
     <button type="button" class="quick-search-esc" id="quick-search-close-btn" title="Закрыть">Esc</button>
    </div>
    <div id="quick-search-results" class="quick-search-results"></div>
    <div class="quick-search-footer">
     <span><kbd>↑</kbd><kbd>↓</kbd> Навигация</span>
     <span><kbd>Enter</kbd> Выбрать</span>
     <span><kbd>Ctrl+F</kbd> / <kbd>Esc</kbd> Закрыть</span>
    </div>
   </div>`;
  document.body.appendChild(el);
 }

 function bindEvents(){
  const backdrop=$('#quick-search-backdrop');
  if(!backdrop)return;
  backdrop.addEventListener('click',e=>{
   if(e.target===backdrop||e.target.closest('#quick-search-close-btn')){
    close();
    return;
   }
   const btn=e.target.closest('button[data-qs-action]');
   if(btn){
    const act=btn.dataset.qsAction;
    close();
    if(act==='play'&&btn.dataset.qsId)api?.play?.(btn.dataset.qsId);
    else if(act==='open'&&btn.dataset.qsKey)api?.openKey?.(btn.dataset.qsKey);
    return;
   }
   const item=e.target.closest('.quick-search-item');
   if(item)activateItem(item);
  });

  const input=$('#quick-search-input');
  if(input){
   input.addEventListener('input',()=>handleInput(input.value.trim()));
   input.addEventListener('keydown',handleKeydown);
  }
 }

 function open(){
  ensureDom();
  const backdrop=$('#quick-search-backdrop');
  if(!backdrop)return;
  returnFocus=document.activeElement;
  backdrop.hidden=false;
  isOpenState=true;
  activeIdx=0;
  currentQuery='';
  steamResults=[];
  qsSeq++;
  clearTimeout(qsTimer);
  const input=$('#quick-search-input');
  if(input){
   input.value='';
   input.focus();
  }
  renderRecentOrEmpty();
 }

 function close(){
  const backdrop=$('#quick-search-backdrop');
  if(!backdrop)return;
  backdrop.hidden=true;
  isOpenState=false;
  activeIdx=-1;
  currentQuery='';
  steamResults=[];
  qsSeq++;
  clearTimeout(qsTimer);
  if(returnFocus&&returnFocus.isConnected&&typeof returnFocus.focus==='function'){
   returnFocus.focus({preventScroll:true});
  }
  returnFocus=null;
 }

 function toggle(){
  if(isOpenState)close();else open();
 }

 function isOpen(){
  return isOpenState;
 }

 function renderRecentOrEmpty(){
  const results=$('#quick-search-results');
  if(!results)return;
  const groups=api?.groups?.()||[];
  const recent=groups.filter(g=>g.lastPlayed>0||g.playtime>0).sort((a,b)=>(b.lastPlayed||0)-(a.lastPlayed||0)).slice(0,5);
  const pool=recent.length?recent:groups.slice(0,5);
  results.innerHTML=`
   <div class="quick-search-section-title"><span>Недавно играли · Быстрый запуск</span><small>Ctrl+F в любом месте</small></div>
   ${pool.map((g,i)=>renderLibraryItem(g,i)).join('')}
   <div class="quick-search-section-title" style="margin-top:8px"><span>Быстрые разделы</span></div>
   <div style="display:flex;gap:8px;padding:4px 10px 8px;flex-wrap:wrap">
    <button type="button" class="quick-search-btn" data-qs-nav="prices">Магазин и скидки ↗</button>
    <button type="button" class="quick-search-btn" data-qs-nav="all">Вся библиотека ↗</button>
    <button type="button" class="quick-search-btn" data-qs-nav="minecraft">Minecraft ↗</button>
   </div>`;
  activeIdx=0;
  highlightActive();
 }

 function renderLibraryItem(g,idx){
  const art=safeImage(g.icon||g.cover||g.background);
  const copyId=g.selected?.id||g.copies?.[0]?.id||g.id;
  const isInstalled=Boolean(g.installed&&copyId);
  return `
   <div class="quick-search-item ${idx===activeIdx?'active':''}"
        data-qs-idx="${idx}"
        data-qs-kind="library"
        data-qs-installed="${isInstalled}"
        data-qs-id="${esc(copyId)}"
        data-qs-key="${esc(g.key)}">
    <div class="quick-search-art">${art?`<img src="${esc(art)}" alt="">`:`<span>${esc((g.name||'G')[0])}</span>`}</div>
    <div class="quick-search-meta">
     <div class="quick-search-name">${esc(g.name)}</div>
     <div class="quick-search-sub">
      <span>${esc(g.selected?.store?g.selected.store.toUpperCase():'PC')}</span>
      <span>·</span>
      <span>${isInstalled?'✓ На этом ПК':'В библиотеке'}</span>
      ${g.playtime?`<span>·</span><span>${Math.round(g.playtime/3600)} ч.</span>`:''}
     </div>
    </div>
    <div class="quick-search-side">
     ${isInstalled?`<button type="button" class="quick-search-btn" data-qs-action="play" data-qs-id="${esc(copyId)}">Играть ↗</button>`:`<button type="button" class="quick-search-btn" data-qs-action="open" data-qs-key="${esc(g.key)}">В библиотеку ↗</button>`}
    </div>
   </div>`;
 }

 function renderSteamItem(p,idx){
  const o=p.preview;
  let priceText='В магазине ↗',disc=0;
  if(o){
   if(o.free)priceText='Бесплатно';
   else{
    priceText=o.rub!=null?(o.currency!=='RUB'?'≈ ':'')+money(o.rub,'RUB'):money(o.amount,o.currency);
    disc=o.originalAmount>o.amount&&o.amount!=null?Math.round((1-o.amount/o.originalAmount)*100):0;
   }
  }
  return `
   <div class="quick-search-item ${idx===activeIdx?'active':''}"
        data-qs-idx="${idx}"
        data-qs-kind="steam"
        data-qs-product="${esc(p.token||p.id)}">
    <div class="quick-search-art">${p.image?`<img src="${esc(p.image)}" alt="">`:`<span>S</span>`}</div>
    <div class="quick-search-meta">
     <div class="quick-search-name">${esc(p.name)}</div>
     <div class="quick-search-sub">
      <span>${esc(p.productType||'Steam · Каталог')}</span>
     </div>
    </div>
    <div class="quick-search-side">
     <div class="quick-search-price-badge">
      ${disc>0?`<span class="quick-search-disc">−${disc}%</span>`:''}
      <span>${esc(priceText)}</span>
     </div>
     <button type="button" class="quick-search-btn" data-qs-action="product" data-qs-product="${esc(p.token||p.id)}">Цены ↗</button>
    </div>
   </div>`;
 }

 function handleInput(q){
  activeIdx=0;
  currentQuery=q;
  if(!q){
   steamResults=[];
   renderRecentOrEmpty();
   return;
  }
  const groups=api?.groups?.()||[];
  const libMatches=groups.filter(g=>matchesQuery(g.name,q)).slice(0,6);
  renderResults(q,libMatches,steamResults);

  clearTimeout(qsTimer);
  if(q.length>=2){
   const reqSeq=++qsSeq;
   const reqQ=q;
   qsTimer=setTimeout(()=>{
    if(!isOpenState||currentQuery!==reqQ)return;
    api?.send?.('pricesSearch',{store:'steam',query:reqQ,requestId:'qs-'+reqSeq,force:false});
   },240);
  }
 }

 function renderResults(q,libMatches,steamItems){
  const results=$('#quick-search-results');
  if(!results)return;
  let idx=0;
  let html='';
  if(libMatches.length){
   html+=`<div class="quick-search-section-title"><span>Твоя коллекция · ${libMatches.length}</span></div>`;
   html+=libMatches.map(g=>renderLibraryItem(g,idx++)).join('');
  }
  if(steamItems.length){
   html+=`<div class="quick-search-section-title" style="margin-top:6px"><span>Каталог Steam и цены · ${steamItems.length}</span></div>`;
   html+=steamItems.slice(0,6).map(p=>renderSteamItem(p,idx++)).join('');
  }
  if(!libMatches.length&&!steamItems.length){
   html=`<div class="quick-search-empty"><p>Ищем «${esc(q)}» по библиотеке и витрине Steam…</p></div>`;
  }
  results.innerHTML=html;
  highlightActive();
 }

 function activateItem(item){
  if(!item)return;
  const d=item.dataset;
  close();
  if(d.qsKind==='library'){
   if(d.qsInstalled==='true'&&d.qsId){
    api?.play?.(d.qsId);
   }else if(d.qsKey){
    api?.openKey?.(d.qsKey);
   }
  }else if(d.qsKind==='steam'&&d.qsProduct){
   const p=steamResults.find(x=>(x.token||x.id)===d.qsProduct);
   if(p&&api?.openProduct)api.openProduct(p);
   else if(api?.openPrices)api.openPrices();
  }
 }

 function handleKeydown(e){
  const items=$$('.quick-search-item');
  if(e.key==='ArrowDown'){
   e.preventDefault();
   if(!items.length)return;
   activeIdx=(activeIdx+1)%items.length;
   highlightActive();
  }else if(e.key==='ArrowUp'){
   e.preventDefault();
   if(!items.length)return;
   activeIdx=(activeIdx-1+items.length)%items.length;
   highlightActive();
  }else if(e.key==='Enter'){
   e.preventDefault();
   if(activeIdx>=0&&items[activeIdx]){
    activateItem(items[activeIdx]);
   }else if(items.length>0){
    activateItem(items[0]);
   }
  }else if(e.key==='Escape'){
   e.preventDefault();
   close();
  }
 }

 function highlightActive(){
  const items=$$('.quick-search-item');
  items.forEach((item,i)=>{
   const isActive=i===activeIdx;
   item.classList.toggle('active',isActive);
   if(isActive)item.scrollIntoView({block:'nearest'});
  });
 }

 function receive(m){
  if(!isOpenState)return false;
  if(m.kind==='pricesSearchResult'&&typeof m.requestId==='string'&&m.requestId.startsWith('qs-')){
   const input=$('#quick-search-input');
   const liveQuery=input?input.value.trim():'';
   if(m.requestId==='qs-'+qsSeq&&liveQuery===currentQuery){
    steamResults=m.data?.products||[];
    const groups=api?.groups?.()||[];
    const libMatches=currentQuery?groups.filter(g=>matchesQuery(g.name,currentQuery)).slice(0,6):[];
    renderResults(currentQuery,libMatches,steamResults);
   }
   return true;
  }
  return false;
 }

 document.addEventListener('click',e=>{
  const b=e.target.closest('[data-qs-nav]');
  if(!b)return;
  const nav=b.dataset.qsNav;
  close();
  if(nav==='prices')api?.openPrices?.();
  else if(nav==='all')api?.openSection?.('all');
  else if(nav==='minecraft')api?.openSection?.('minecraft');
 });

 window.OrbitQuickSearch={init,open,close,toggle,isOpen,receive};
})();
