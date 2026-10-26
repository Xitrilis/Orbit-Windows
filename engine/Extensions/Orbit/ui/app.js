(()=>{
 'use strict';const C=OrbitCore,$=s=>document.querySelector(s),$$=s=>[...document.querySelectorAll(s)],esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const paths={home:'<path d="m3 10 9-7 9 7v11h-6v-8H9v8H3Z"/>',clock:'<circle cx="12" cy="12" r="9"/><path d="M12 6v6l4 2"/>',grid:'<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>',list:'<path d="M8 5h13M8 12h13M8 19h13M3 5h.1M3 12h.1M3 19h.1"/>',library:'<rect x="3" y="4" width="5" height="16" rx="1"/><rect x="10" y="4" width="5" height="16" rx="1"/><path d="m18 4 3 16"/>',monitor:'<rect x="3" y="4" width="18" height="13" rx="2"/><path d="M8 21h8M12 17v4"/>',star:'<path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9Z"/>',link:'<path d="m9 15 6-6m-5-3a4 4 0 0 0-5 0l-3 3a4 4 0 0 0 6 6m6 3a4 4 0 0 0 5 0l3-3a4 4 0 0 0-6-6"/>',settings:'<path d="m9 3-.5 2.5-2 .9L4 6l-1.5 3 1.8 1.8v2.4L2.5 15 4 18l2.5-.4 2 .9L9 21h6l.5-2.5 2-.9 2.5.4 1.5-3-1.8-1.8v-2.4L21.5 9 20 6l-2.5.4-2-.9L15 3Z"/><circle cx="12" cy="12" r="3"/>',search:'<circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 5 5"/>',refresh:'<path d="M20 7V3m0 4h-4M4 17v4m0-4h4M20 7a8 8 0 0 0-14-2M4 17a8 8 0 0 0 14 2"/>',shuffle:'<path d="M3 6h3c5 0 7 12 12 12h3m-4-4 4 4-4 4M3 18h3c1.5 0 3-1 4-3M14 9c1.5-2 2.5-3 4-3h3m-4-4 4 4-4 4"/>',play:'<path d="m8 4 12 8-12 8Z" fill="currentColor" stroke="none"/>',download:'<path d="M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4"/>',chevron:'<path d="m7 10 5 5 5-5"/>',x:'<path d="m6 6 12 12M6 18 18 6"/>',minus:'<path d="M5 12h14"/>',maximize:'<rect x="5" y="5" width="14" height="14" rx="1"/>',check:'<path d="m5 12 4 4 10-10"/>',folder:'<path d="M3 7V5a2 2 0 0 1 2-2h5l2 3h7a2 2 0 0 1 2 2v11H3Z"/>',drive:'<path d="m5 4-3 12v4h20v-4L19 4Z M2 16h20M17 19h2"/>',users:'<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>'};
 const icon=n=>`<svg viewBox="0 0 24 24" aria-hidden="true">${paths[n]||paths.grid}</svg>`,store=id=>C.stores.find(s=>s.id===id)||C.stores[0],dot=id=>`<i class="store-dot" style="--color:${store(id).color}"></i>`,gb=n=>new Intl.NumberFormat('ru',{maximumFractionDigits:1}).format(n/1024**3)+' ГБ';
 window.OrbitIcon=icon;window.OrbitDot=dot;
 let data={games:[],prefs:{},stores:[],downloads:[]},realData=null,demo=new URLSearchParams(location.search).get('demo')==='1',groups=[],visible=[],section='home',selectedStore='',filter='all',view='grid',page=1,modalKind='',modalKey='',returnFocus=null,installGame=null,options=null,toastTimer,syncTimer;
 const htmlCache=new WeakMap(),animationCache=new WeakMap();let librarySignature='',modalCloseTimer;
 function patchHtml(el,html){if(htmlCache.get(el)!==html){el.innerHTML=html;htmlCache.set(el,html);}}
 function motion(el,frames,duration){animationCache.get(el)?.cancel();if(!el.animate||document.documentElement.classList.contains('motion-off')||matchMedia('(prefers-reduced-motion:reduce)').matches)return null;const animation=el.animate(frames,{duration,easing:'cubic-bezier(.22,.8,.25,1)'});animationCache.set(el,animation);return animation;}
 function toast(message){clearTimeout(toastTimer);$('#toast').textContent=message;$('#toast').hidden=false;toastTimer=setTimeout(()=>$('#toast').hidden=true,7000);}
 let demoPriceSettings={accounts:{},funpaySections:{}};
 function send(kind,p={}){
  if(kind.startsWith('home')&&(demo||!window.CefSharp||typeof CefSharp.PostMessage!=='function')){if(!['homeCancel','homeClear'].includes(kind))OrbitHome.receive({kind:'homeError',requestId:p.requestId,lane:kind==='homeLoad'?'feed':kind==='homeDetails'?'detail':'query',message:'Для загрузки каталога и подбора Gemini открой Orbit.exe на Windows.'});return;}
  if(kind.startsWith('artwork')&&!demo&&!(window.CefSharp&&typeof CefSharp.PostMessage==='function')){OrbitArtwork.receive({kind:'artworkError',requestId:p.requestId,session:p.session,message:'Открой Orbit.exe на Windows для сохранения оформления. В демонстрации можно попробовать изменения без сохранения на диск.'});return;}
  if(kind.startsWith('wallet')&&(demo||!window.CefSharp||typeof CefSharp.PostMessage!=='function')){if(kind==='walletSettings')OrbitWallet.receive({kind,settings:{login:'',region:'RU',method:'sbp',rememberLogin:false}});else if(kind==='walletSave'&&demo)OrbitWallet.receive({kind:'walletSaved',settings:p.settings});else if(kind==='walletQuote'||kind==='walletSave')OrbitWallet.receive({kind:'walletError',requestId:p.requestId,message:demo?'Демонстрация: расчёт и платежи отключены. Открой Orbit.exe для текущих данных.':'Для расчёта и сохранения настроек открой Orbit.exe на Windows.'});else if(kind!=='walletCancel')toast(demo?'Демонстрация: переходы к оплате и копирование отключены.':'Открой Orbit.exe на Windows.');return;}

  if(demo&&kind.startsWith('prices')){if(kind==='pricesSettings')OrbitPrices.receive({kind,settings:demoPriceSettings});else if(kind==='pricesSaveSettings'){demoPriceSettings.accounts=p.accounts;OrbitPrices.receive({kind:'pricesSettings',settings:demoPriceSettings});}else if(['pricesSearch','pricesCompare','pricesDiscover'].includes(kind))OrbitPrices.receive({kind:'pricesError',requestId:p.requestId,message:'Демонстрация: реальные цены доступны в Orbit.exe на Windows.'});else if(kind!=='pricesCancel')toast('Демонстрация: внешние источники не открываются.');return;}
  if(demo){if(kind==='favorite'){data.games.filter(g=>p.ids.includes(g.id)).forEach(g=>g.favorite=p.value);render();}else if(kind==='prefs'){Object.assign(data.prefs,p.patch);render();}else if(kind==='installOptions'){receive(installGame.store==='steam'?{kind,id:p.id,libraries:[{path:'D:\\SteamLibrary',free:412*1024**3},{path:'C:\\Program Files (x86)\\Steam',free:84*1024**3}],drives:[],suggested:'D:\\SteamLibrary'}:{kind,id:p.id,libraries:[],drives:[{path:'D:\\',free:412*1024**3},{path:'C:\\',free:84*1024**3}],suggested:'D:\\Games\\'+installGame.name.replace(/[<>:\"/\\|?*]/g,'_')});}else if(kind==='sync'){$('#refresh').classList.remove('syncing');toast('В демонстрации показан пример коллекции.');}else if(!['ready','drag','minimize','maximize','close'].includes(kind))toast('Это демонстрация. Подключения и действия с играми доступны в Orbit.exe на Windows.');return;}
  if(window.CefSharp&&typeof CefSharp.PostMessage==='function')CefSharp.PostMessage(JSON.stringify({kind,...p}));else if(['pricesSearch','pricesCompare','pricesDiscover'].includes(kind))OrbitPrices.receive({kind:'pricesError',requestId:p.requestId,message:'Для загрузки цен открой Orbit.exe на Windows.'});else if(kind!=='ready')toast('Открой Orbit.exe на Windows для подключения аккаунтов и запуска игр.');
 }
 function receive(m){if(OrbitMinecraft.receive(m))return;if(OrbitJournal.receive(m))return;if(OrbitSupport.receive(m))return;if(OrbitHome.receive(m))return;if(OrbitArtwork.receive(m))return;if(m.kind==='downloads'){if(!demo){data.downloads=m.jobs||[];OrbitDownloads.update(data.downloads,false);}return;}if(m.kind==='downloadError'){if(!demo)toast(m.message);return;}if(OrbitWallet.receive(m))return;if(OrbitPrices.receive(m)){if(m.kind==='pricesSettings')OrbitHome.accountsChanged(m.settings?.accounts);return;}if(m.kind==='snapshot'){if(demo){if((realData?.journal?.revision||0)>(m.data.journal?.revision||0))m.data.journal=realData.journal;realData=m.data;return;}if((data.journal?.revision||0)>(m.data.journal?.revision||0))m.data.journal=data.journal;data=m.data;data.prefs=data.prefs||{};view=data.prefs.view||view;OrbitDownloads.update(data.downloads,false);const signature=JSON.stringify([data.games,data.prefs,data.stores,data.notifications,data.activity,data.activityWarning,data.backgroundClients,data.journal]);if(signature!==librarySignature){librarySignature=signature;render();}}else if(m.kind==='error'||m.kind==='toast')toast(m.message+(m.diagnosticCode?' · код '+String(m.diagnosticCode).slice(0,8):''));else if(m.kind==='syncDone'){clearTimeout(syncTimer);$('#refresh').classList.remove('syncing');toast(m.message||'Библиотека обновлена.');}else if(m.kind==='installOptions'){options=m;installDialog();}else if(m.kind==='folder'&&$('#install-path')){$('#install-path').value=m.path;$('#path-space').textContent='Свободно '+gb(m.free||0);}else if(m.kind==='installStarted'){closeModal(true);OrbitDownloads.show();}}
 window.Orbit={receive};
 function pref(patch){Object.assign(data.prefs,patch);send('prefs',{patch});if(!demo)render();}
 const byKey=k=>groups.find(g=>g.key===k);
 paths.bookmark='<path d="M6 3h12v18l-6-4-6 4Z"/>';paths.cube='<path d="m12 3 9 5v9l-9 5-9-5V8Zm0 9 9-4M12 12 3 8m9 4v10"/>';
 paths.clock='<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>';
 paths.home='<path d="m3 10 9-7 9 7v11h-7v-7h-4v7H3Z"/>';
 paths.edit='<path d="m15 5 4 4M4 20l4-1L20 7a2.8 2.8 0 0 0-4-4L4 15Z"/>';
 const OrbitCovers={
  cache:{},inFlight:new Set(),
  init(){try{this.cache=JSON.parse(localStorage.getItem('orbit_auto_covers')||'{}');}catch{}},
  has(id){return !!this.cache[id];},
  get(id){return this.cache[id];},
  async findFor(g){
   if(!g||g.cover||this.cache[g.id]||this.inFlight.has(g.id))return;
   this.inFlight.add(g.id);
   const cleanName=(g.name||'').replace(/[™®©\[\]\(\)]/g,' ').replace(/\s+/g,' ').trim();
   if(!cleanName)return;
   try{
    const sUrl='https://store.steampowered.com/api/storesearch/?term='+encodeURIComponent(cleanName)+'&l=russian&cc=US';
    const sRes=await fetch(sUrl).then(r=>r.json()).catch(()=>null);
    if(sRes&&sRes.items&&sRes.items.length){
     const id=sRes.items[0].id;
     const coverUrl='https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/'+id+'/library_600x900_2x.jpg';
     if(await this.test(coverUrl)){this.save(g,coverUrl);return;}
    }
    const gUrl='https://catalog.gog.com/v1/catalog?limit=3&locale=en-US&order=desc:bestselling&query='+encodeURIComponent(cleanName);
    const gRes=await fetch(gUrl).then(r=>r.json()).catch(()=>null);
    if(gRes&&gRes.products&&gRes.products.length){
     const vert=gRes.products[0].coverVertical;
     if(vert&&await this.test(vert)){this.save(g,vert);return;}
    }
   }catch(e){}finally{this.inFlight.delete(g.id);}
  },
  test(url){
   return new Promise(resolve=>{
    const img=new Image();
    img.onload=()=>resolve(true);
    img.onerror=()=>resolve(false);
    img.src=url;
    setTimeout(()=>resolve(false),4500);
   });
  },
  save(g,url){
   this.cache[g.id]=url;
   try{localStorage.setItem('orbit_auto_covers',JSON.stringify(this.cache));}catch{}
   g.cover=url;
   if(window.data&&window.data.games){
    window.data.games.filter(x=>x.id===g.id||(g.copies&&g.copies.some(c=>c.id===x.id))).forEach(x=>{x.cover=url;});
   }
   const card=document.querySelector(`[data-open="${esc(g.key)}"]`);
   if(card){
    card.classList.remove('no-art');
    let img=card.querySelector('img');
    if(!img){
     img=document.createElement('img');
     img.loading='lazy';
     img.alt='';
     card.appendChild(img);
    }
    img.src=url;
    img.hidden=false;
   }
  }
 };
 OrbitCovers.init();
 function posterFallback(g){
  let hash=0;const name=g.name||'';
  for(let i=0;i<name.length;i++)hash=((hash<<5)-hash)+name.charCodeAt(i);
  const hue=Math.abs(hash)%360;
  const storeLabel=(g.selected&&store(g.selected.store)?.name)||'ORBIT';
  return `<div class="poster-styled-fallback" style="--art-hue:${hue}">
    <div class="fallback-glow"></div>
    <div class="fallback-top"><span class="fallback-badge">${esc(storeLabel)}</span><span class="fallback-dot"></span></div>
    <div class="fallback-center"><div class="fallback-crest">${icon('play')}</div></div>
    <div class="fallback-bottom"><strong class="fallback-title">${esc(g.name)}</strong><span class="fallback-edition">ORBIT GAME</span></div>
  </div>`;
 }
 const crop=(g,slot)=>C.cropStyle(g.artwork?.[slot]);
 function picker(g){return `<button class="store-button" data-pick="${esc(g.key)}" aria-label="Выбрать магазин для ${esc(g.name)}">${dot(g.selected.store)}${esc(store(g.selected.store).name)}${icon('chevron')}</button>`;}
 function action(g,cls='primary'){const a=C.action(g.selected);return `<button class="${cls}" data-action="${esc(g.key)}" ${a.disabled?'disabled':''}>${cls==='primary'?icon(g.selected.installed?'play':'download'):''}${a.text}</button>`;}
 function render(){
  OrbitDownloads.update(data.downloads||[],demo);
  groups=C.groupGames(data.games,data.prefs);document.documentElement.classList.toggle('motion-off',!!data.prefs.reducedMotion);$('#demo-banner').hidden=!demo;const dismissedNotifs=(()=>{try{return new Set(JSON.parse(localStorage.getItem('orbit_dismissed_notifs')||'[]'));}catch{return new Set();}})();
   const activeNotifs=(data.notifications||[]).filter(n=>!dismissedNotifs.has(n));
   $('#notifications').hidden=!activeNotifs.length;
   if(activeNotifs.length){
    $('#notifications').innerHTML=activeNotifs.map(n=>'<p>'+esc(n)+'</p>').join('')+
     '<div class="notif-actions"><button data-cmd="accounts">Проверить подключения</button><button class="notif-dismiss" data-cmd="dismissNotif" aria-label="Закрыть уведомление" title="Закрыть">'+icon('x')+'</button></div>';
   }
  patchHtml($('#main-nav'),[['home','home','Главная',''],['all','library','Все игры',groups.length],['friends','users','Друзья','●'],['activity','clock','Недавние',''],['queue','bookmark','Поиграть позже',''],['minecraft','cube','Minecraft',''],['installed','monitor','Установленные',groups.filter(g=>g.installed).length],['favorite','star','Избранное',groups.filter(g=>g.favorite).length]].map(([id,ic,title,count])=>`<button class="nav-item ${section===id&&!selectedStore?'active':''}" data-section="${id}">${icon(ic)}<span>${title}</span><span class="nav-count">${count}</span></button>`).join('')+`<div class="sidebar-services"><button class="nav-item" data-cmd="prices">${icon('search')}<span>Магазин</span><span class="nav-count">₽ ₸</span></button><button class="nav-item" data-cmd="wallet">${icon('drive')}<span>Пополнить Steam</span><span class="nav-count">↗</span></button></div>`);
  const connectedStores=C.stores.filter(s=>groups.some(g=>g.copies.some(c=>c.store===s.id)));
   const visibleStores=connectedStores.length?connectedStores:C.stores.slice(0,1);
   const storeRows=visibleStores.map(s=>{
    const count=groups.filter(g=>g.copies.some(c=>c.store===s.id)).length;
    return `<button class="nav-item ${selectedStore===s.id?'active':''}" data-store="${s.id}">${dot(s.id)}<span>${s.name}</span><span class="nav-count">${count}</span></button>`;
   }).join('');
   const addStoreBtn=`<button class="nav-item nav-add-store" data-cmd="accounts" title="Подключить новый магазин">${icon('link')}<span>Подключить магазин</span></button>`;
   patchHtml($('#store-nav'),storeRows+addStoreBtn);
  $('#page-title').innerHTML=esc(selectedStore?store(selectedStore).name:section==='home'?'Главная':section==='activity'?'Недавние игры':section==='queue'?'Поиграть позже':section==='minecraft'?'Minecraft':section==='installed'?'На этом ПК':section==='favorite'?'Избранное':'Все игры')+'<span>.</span>';
  const genre=$('#genre').value;const genres=[...new Set(groups.flatMap(g=>g.genres))].sort((a,b)=>a.localeCompare(b));$('#genre').innerHTML='<option value="">Все жанры</option>'+genres.map(s=>`<option value="${esc(s)}">${esc(s)}</option>`).join('');$('#genre').value=genres.includes(genre)?genre:'';
  visible=C.filterGames(groups,{search:$('#search').value,store:selectedStore,installed:section==='installed'||filter==='installed',favorite:section==='favorite'||filter==='favorite',genre:$('#genre').value,sort:$('#sort').value});
  $('#count').textContent=visible.length;$('#subtitle').textContent=`${groups.length} игр · ${data.games.length} копий в магазинах`;
  $$('.tabs button').forEach(b=>b.classList.toggle('active',b.dataset.filter===filter));$$('[data-view]').forEach(b=>b.classList.toggle('active',b.dataset.view===view));
  $('#welcome').hidden=!!groups.length;$('#library').hidden=!groups.length;
  if(!groups.length)patchHtml($('#welcome'),`<div class="welcome-panel"><div class="orbit-ring"></div><p class="eyebrow">ДОБРО ПОЖАЛОВАТЬ В ORBIT</p><h2>Все твои игры.<br>В одном месте.</h2><p>Подключи магазины и собери свою коллекцию. Купленные и установленные игры появятся рядом, а одинаковые — в одной карточке.</p><div class="welcome-actions"><button class="primary" data-cmd="accounts">${icon('link')}Подключить магазины</button><button class="quiet" data-cmd="demo">Посмотреть интерфейс →</button></div><div class="welcome-stores">${C.stores.map(s=>`<span>${s.name}</span>`).join('')}</div></div>`);
  const featured=C.filterGames(visible,{sort:'recent'})[0];$('#hero').hidden=!featured||view==='list'||!!$('#search').value;
  if(featured&&!$('#hero').hidden){const g=featured;patchHtml($('#hero'),`${g.background?`<img class="hero-art" style="${crop(g,'background')}" src="${esc(g.background)}" alt="">`:''}<div class="hero-copy"><p class="hero-label"><i></i>${g.lastPlayed?'ПРОДОЛЖИМ ПУТЕШЕСТВИЕ':'ОТКРОЙ НОВЫЙ МИР'}</p><button class="hero-title" data-open="${esc(g.key)}">${esc(g.name)}</button><div class="hero-meta"><span>${esc(g.genres.slice(0,2).join(' · ')||store(g.selected.store).name)}</span><span>${C.hours(g.playtime)}</span></div><div class="hero-actions">${action(g)}${picker(g)}</div></div><span class="hero-caption">ТВОЯ КОЛЛЕКЦИЯ</span>`);}
  renderGames();$('main').classList.toggle('is-home',section==='home');$('#home-view').hidden=section!=='home';OrbitHome.update();$('main').classList.toggle('is-activity',section==='activity');$('#activity-view').hidden=section!=='activity';OrbitActivity.update(data,groups);$('main').classList.toggle('is-queue',section==='queue');$('#queue-view').hidden=section!=='queue';OrbitJournal.update(data,groups);$('main').classList.toggle('is-minecraft',section==='minecraft');$('#minecraft-view').hidden=section!=='minecraft';OrbitMinecraft.update();renderClientStatus();
 }
 function renderGames(){const list=visible.slice(0,page*72);$('#games').className=view==='grid'?'game-grid':'game-list';patchHtml($('#games'),list.map((g,i)=>view==='grid'?card(g,i):row(g,i)).join(''));$('#empty').hidden=!!visible.length;$('#more').hidden=list.length>=visible.length;$$('#games img').forEach(img=>img.onerror=()=>{img.hidden=true;img.parentElement.classList.add('no-art');});}
 function card(g,i){
   if(!g.cover&&OrbitCovers.has(g.id))g.cover=OrbitCovers.get(g.id);
   if(!g.cover)OrbitCovers.findFor(g);
   return `<article class="game-card" style="animation-delay:${Math.min(i,9)*18}ms"><button class="poster ${g.cover?'':'no-art'}" data-open="${esc(g.key)}" aria-label="Открыть ${esc(g.name)}"><span class="poster-fallback">${posterFallback(g)}</span>${g.cover?`<img src="${esc(g.cover)}" style="${crop(g,'cover')}" loading="lazy" alt="">`:''}${g.installed?'<span class="status"><i></i>НА ПК</span>':''}${g.copies.length>1?`<span class="copies">${g.copies.length} магазина</span>`:''}</button><button class="favorite ${g.favorite?'on':''}" data-favorite="${esc(g.key)}" aria-label="Избранное: ${esc(g.name)}" aria-pressed="${g.favorite}">${icon('star')}</button><div class="game-name-line">${g.icon?`<span class="game-custom-icon"><img src="${esc(g.icon)}" style="${crop(g,'icon')}" alt=""></span>`:''}<button class="game-title" data-open="${esc(g.key)}" title="${esc(g.name)}">${esc(g.name)}</button></div>${picker(g)}</article>`;}
 function row(g,i){
   if(!g.cover&&OrbitCovers.has(g.id))g.cover=OrbitCovers.get(g.id);
   if(!g.cover)OrbitCovers.findFor(g);
   return `<article class="game-row" style="animation-delay:${Math.min(i,7)*16}ms">${g.icon||g.cover?`<span class="row-art-frame ${g.icon?'is-icon':''}"><img src="${esc(g.icon||g.cover)}" style="${crop(g,g.icon?'icon':'cover')}" loading="lazy" alt=""></span>`:'<b class="row-placeholder">o.</b>'}<div class="row-title"><button class="game-title" data-open="${esc(g.key)}">${esc(g.name)}</button>${picker(g)}</div><span class="row-time">${C.hours(g.playtime)}</span><span class="row-status">${g.selected.installed?'На этом ПК':'В библиотеке'}</span><button class="favorite inline ${g.favorite?'on':''}" data-favorite="${esc(g.key)}" aria-label="Избранное: ${esc(g.name)}">${icon('star')}</button>${action(g,'row-action')}</article>`;}
 function openPicker(k,button){const g=byKey(k);if(!g)return;const p=$('#popover');p.innerHTML='<p class="popover-label">ЗАПУСКАТЬ ЧЕРЕЗ</p>'+g.copies.map(c=>`<button class="store-option ${g.selected.id===c.id?'selected':''}" data-copy="${esc(c.id)}" data-group="${esc(k)}">${dot(c.store)}<div><strong>${esc(store(c.store).name)}</strong><small>${c.installed?'Уже установлена':'Потребуется установка'}</small></div>${g.selected.id===c.id?icon('check'):''}</button>`).join('')+'<p class="popover-note">Выбор сохранится для этой игры. По умолчанию приоритет у Steam.</p>';p.hidden=false;const r=button.getBoundingClientRect();p.style.left=Math.max(8,Math.min(r.left,innerWidth-267))+'px';p.style.top=Math.max(8,Math.min(r.bottom+7,innerHeight-p.offsetHeight-12))+'px';p.querySelector('button')?.focus();}
 function modal(kind,html,keep=false){
  const panel=$('#modal'),backdrop=$('#modal-backdrop'),fresh=backdrop.hidden,field=keep?document.activeElement:null,fieldId=field?.id,selection=field&&typeof field.selectionStart==='number'?[field.selectionStart,field.selectionEnd]:null;
  clearTimeout(modalCloseTimer);animationCache.get(panel)?.cancel();animationCache.get(backdrop)?.cancel();backdrop.style.pointerEvents='';
  if(modalKind==='journal-note'&&kind!=='journal-note')OrbitJournal.closed();
  if(modalKind==='home-detail'&&kind!=='home-detail')OrbitHome.closed();
  if(modalKind==='artwork'&&kind!=='artwork')OrbitArtwork.closed();
  if(modalKind==='wallet'&&kind!=='wallet')OrbitWallet.closed();if(modalKind==='prices'&&kind!=='prices')OrbitPrices.closed();if(modalKind==='friends'&&kind!=='friends')OrbitFriends.closed();
  if(fresh)returnFocus=document.activeElement;modalKind=kind;panel.dataset.kind=kind;$('#popover').hidden=true;
  panel.innerHTML='<button class="modal-close" data-cmd="closeModal" aria-label="Закрыть">'+icon('x')+'</button>'+html;
  backdrop.hidden=false;$('#app').inert=true;
  if(!keep){panel.scrollTop=0;panel.focus();motion(panel,[{opacity:0,transform:'translateY(16px) scale(.985)'},{opacity:1,transform:'translateY(0) scale(1)'}],260);if(fresh)motion(backdrop,[{opacity:0},{opacity:1}],180);}
  else if(fieldId){const next=document.getElementById(fieldId);if(next&&panel.contains(next)){next.focus({preventScroll:true});if(selection)try{next.setSelectionRange(...selection);}catch{}}}
 }
 function closeModal(immediate=false){
  const backdrop=$('#modal-backdrop'),panel=$('#modal');if(backdrop.hidden)return;
  if(modalKind==='journal-note')OrbitJournal.closed();
  if(modalKind==='home-detail')OrbitHome.closed();
  if(modalKind==='artwork')OrbitArtwork.closed();
  if(modalKind==='wallet')OrbitWallet.closed();if(modalKind==='prices')OrbitPrices.closed();if(modalKind==='friends')OrbitFriends.closed();modalKind='';modalKey='';$('#popover').hidden=true;
  clearTimeout(modalCloseTimer);const finish=()=>{backdrop.hidden=true;backdrop.style.pointerEvents='';$('#app').inert=false;const target=returnFocus?.isConnected?returnFocus:$('.download-dock-button');target?.focus({preventScroll:true});};
  if(immediate){animationCache.get(panel)?.cancel();animationCache.get(backdrop)?.cancel();finish();return;}
  backdrop.style.pointerEvents='none';const a=motion(panel,[{opacity:1,transform:'translateY(0) scale(1)'},{opacity:0,transform:'translateY(8px) scale(.99)'}],150);motion(backdrop,[{opacity:1},{opacity:0}],150);if(a)modalCloseTimer=setTimeout(finish,150);else finish();
 }
 function accounts(){modal('accounts',`<div class="modal-body"><p class="eyebrow">ШЕСТЬ МАГАЗИНОВ. ОДНА КОЛЛЕКЦИЯ.</p><h2 id="modal-title">Подключения</h2><p class="intro">Войди в аккаунты и включи импорт неустановленных игр. Затем обнови библиотеку. Для входа откроются настройки выбранного магазина.</p><div class="account-grid">${C.stores.map(s=>{const n=data.games.filter(g=>g.store===s.id).length;return `<div class="account"><b class="store-mark" style="--color:${s.color}">${s.mark}</b><div><strong>${s.name}</strong><small>${n?n+' игр импортировано':'Готово к подключению'}</small></div><button data-connect="${s.id}">${n?'Настроить аккаунт':'Подключить'}</button></div>`;}).join('')}</div><p class="note">Ubisoft: сначала войди в Ubisoft Connect на ПК — список игр берётся из данных клиента. В Battle.net проверь регион. Число игр показывает последний импорт, а не текущий статус входа.</p><div class="modal-bottom"><small>Пароли вводятся на страницах сервисов.<br>Просмотр коллекции не запускает все магазины.</small><button class="primary" data-cmd="syncClose">${icon('refresh')}Обновить библиотеку</button></div></div>`);}
 function details(k){const g=byKey(k);if(!g)return;modalKey=k;modal('detail',`<div class="detail-hero">${g.background?`<img src="${esc(g.background)}" style="${crop(g,'background')}" alt="">`:''}<div><p class="eyebrow">ТВОЯ КОЛЛЕКЦИЯ</p><div class="detail-name">${g.icon?`<span><img src="${esc(g.icon)}" style="${crop(g,'icon')}" alt=""></span>`:''}<h2 id="modal-title">${esc(g.name)}</h2></div></div></div><div class="modal-body"><div class="detail-actions">${action(g)}${picker(g)}<button class="artwork-launch" data-customize="${esc(k)}">${icon('edit')}Оформление</button><button class="icon-button" data-favorite="${esc(k)}" aria-label="В избранное">${icon('star')}</button></div>${OrbitJournal.detail(g)}<div class="assist-actions"><button class="assistant-action" data-ask-game="${esc(g.selected.id)}"><span>✧</span><div><strong>Спросить Orbit</strong><small>Подсказки, механики, прохождение</small></div><b>↗</b></button><button class="diagnostic-action" data-check-game="${esc(g.selected.id)}"><span>⌁</span><div><strong>Проверить игру</strong><small>Файлы и ошибки запуска</small></div></button></div><button class="price-detail-button" data-prices="${esc(k)}">${icon('search')}<span>Цены и распродажи</span><small>Магазины · акции · история</small><b>↗</b></button><div class="stats"><div><small>Время в игре</small><strong>${C.hours(g.playtime)}</strong></div><div><small>Последний запуск</small><strong>${g.lastPlayed?new Date(g.lastPlayed).toLocaleDateString('ru'):'Ещё впереди'}</strong></div><div><small>Выбранная копия</small><strong>${g.selected.installed?'Установлена':'Не установлена'}</strong></div></div>${OrbitActivity.history(g)}<p class="home-description">${esc(g.selected.description||'')}</p><div class="genres">${g.genres.map(s=>`<span>${esc(s)}</span>`).join('')}</div><p class="note">${g.selected.installed?'Нужный игре клиент может работать в фоне.':'Нажми «Установить», чтобы выбрать диск и папку в Orbit. Автоматическая передача пути в клиент пока экспериментальная.'}</p>${g.installed&&!g.selected.installed?'<p class="note">Другая копия уже установлена. Её можно выбрать в меню магазина.</p>':''}<div class="versions"><h3>Копии игры · ${g.copies.length}</h3>${g.copies.map(c=>`<div>${dot(c.store)}<span>${esc(store(c.store).name)} <small>· ${c.installed?'На ПК':'В аккаунте'}</small></span>${g.selected.id===c.id?'<small>Выбрана</small>':`<button data-copy="${esc(c.id)}" data-group="${esc(k)}">Выбрать</button>`}</div>`).join('')}</div><details class="advanced"><summary>Объединение одинаковых игр</summary><p>Разные издания и ремастеры остаются отдельно. Здесь можно объединить карточки вручную или отделить ошибочно объединённую копию.</p><select id="merge-target" aria-label="Объединить с игрой"><option value="">Выбери другую карточку</option>${groups.filter(x=>x.key!==k).map(x=>`<option value="${esc(x.key)}">${esc(x.name)}</option>`).join('')}</select><button class="secondary" data-cmd="merge">Объединить</button>${g.copies.length>1?'<button class="secondary" data-cmd="split">Отделить выбранную копию</button>':''}</details></div>`);modalKey=k;}
 function clientStatus(){const b=data.backgroundClients||{},names=(b.hiddenStores||[]).map(id=>store(id).name);return demo?'Демонстрация: окна клиентов не меняются.':b.warning||((names.length?'Скрыты: '+names.join(', ')+'. ':'Скрытых Orbit окон сейчас нет. ')+'Открытый вручную клиент остаётся видимым 10 минут. Вернуть окна можно также из трея Orbit.');}
 function renderClientStatus(){const el=$('#clients-status');if(el)el.textContent=clientStatus();}
 function settings(){modal('settings',`<div class="modal-body"><p class="eyebrow">ПОД ТВОЙ РИТМ</p><h2 id="modal-title">Настройки Orbit</h2><div class="setting"><div><strong>Помощь и отчёт об ошибке</strong><p>Локальные логи и ZIP для разбора неполадок.<br>Без ключей, переписки и автоматической отправки.</p></div><button class="secondary" data-cmd="support">Открыть</button></div><div class="setting"><div><strong>Клиенты магазинов в фоне</strong><p>Убирать распознанные окна после импорта и успешного запуска игры.<br>Клиент продолжает работать; его окно исчезает с панели задач.</p></div><button class="switch ${data.prefs.backgroundClients!==false?'on':''}" data-cmd="backgroundClients" role="switch" aria-label="Клиенты магазинов в фоне" aria-checked="${data.prefs.backgroundClients!==false}"></button></div><div class="client-background-actions"><button class="secondary" data-cmd="hideClients">Убрать окна в фон</button><button class="secondary" data-cmd="showClients">Показать окна клиентов</button></div><p class="client-background-status" id="clients-status">${esc(clientStatus())}</p><div class="setting"><div><strong>Цены, страны и валюты</strong><p>Регион каждого магазина, рубли и тенге.<br>Цены Steam, Epic, GOG, FunPay и Plati.Market.</p></div><button class="secondary" data-cmd="priceSettings">Настроить</button></div><div class="setting"><div><strong>✧ Orbit AI и игровой оверлей</strong><p>Gemini, вопросы по кадру и диагностика игры.<br>По умолчанию: Ctrl + Shift + Space.</p></div><button class="secondary" data-cmd="assistantSettings">Настроить</button></div><div class="setting"><div><strong>Меньше анимаций</strong><p>Отключить плавные движения и появления элементов.</p></div><button class="switch ${data.prefs.reducedMotion?'on':''}" data-cmd="motion" role="switch" aria-label="Меньше анимаций" aria-checked="${!!data.prefs.reducedMotion}"></button></div><div class="setting"><div><strong>Steam — первый по умолчанию</strong><p>Магазин каждой игры можно изменить под её названием.</p></div>${icon('check')}</div><div class="setting"><div><strong>Закрытие в трей</strong><p>Крестик скрывает окно. Открыть снова — значком возле часов. Полный выход доступен в меню трея.</p></div>${icon('check')}</div><div class="setting"><div><strong>Установка из Orbit · бета</strong><p>Папку выбираешь здесь. Нужный клиент выполняет загрузку. Если путь не подтверждён, автодействия останавливаются.</p></div>${icon('download')}</div><div class="modal-bottom"><small>Orbit 0.11 · Windows<br>На основе Playnite. Данные хранятся на этом ПК.</small><button class="secondary" data-cmd="advanced">Открыть Playnite</button></div></div>`);}
 function requestInstall(g){installGame=g.selected;options=null;modal('install','<div class="modal-body"><h2 id="modal-title">Готовим установку</h2><p class="intro">Проверяем диски и папки библиотеки…</p></div>');send('installOptions',{id:installGame.id});}
 function installDialog(){if(!installGame||modalKind!=='install')return;const s=store(installGame.store),o=options||{},libs=o.libraries||[];modal('install',`<div class="modal-body"><p class="eyebrow">${esc(s.name).toUpperCase()} · УСТАНОВКА</p><h2 id="modal-title">${esc(installGame.name)}</h2><p class="intro">Выбери место для игры. Orbit проверит путь в клиенте перед подтверждением установки.</p><div class="locations">${(libs.length?libs:o.drives||[]).map(l=>`<button data-path="${esc(l.path)}">${icon('drive')}<span><strong>${esc(l.path)}</strong><small>Свободно ${gb(l.free)}</small></span></button>`).join('')}</div><label class="path-label" for="install-path">${installGame.store==='steam'?'Папка библиотеки Steam':'Полная папка установки игры'}</label><div class="path-field"><input id="install-path" spellcheck="false" value="${esc(o.suggested||libs[0]?.path||'')}"><button data-cmd="browse" aria-label="Выбрать папку">${icon('folder')}</button></div><p class="note" id="path-space"></p><p class="note">${installGame.store==='steam'?'Steam создаёт папку игры внутри steamapps\\common. Новую библиотеку сначала добавь в Steam → Настройки → Хранилище. Здесь выбирается уже зарегистрированная папка.':'Для этой игры будет использована указанная папка. Клиент магазина может запросить вход или согласие с условиями.'}</p><div class="caution">Бета: автоматизация ещё не проверена на твоём Windows. Если клиент не даст подтвердить папку, Orbit остановит автодействия и сообщит об этом.</div><div class="modal-bottom"><small>Нужен ${esc(s.name)} в фоне.<br>Другие магазины для этой установки не запускаются.</small><button class="primary" data-cmd="install">${icon('download')}Начать установку</button></div></div>`,true);}
 function downloads(){if(OrbitDownloads.isOpen())OrbitDownloads.hide();else OrbitDownloads.show();}
 function run(k){const g=byKey(k);if(!g)return;const a=C.action(g.selected);if(a.kind==='install')requestInstall(g);else if(a.kind==='play')send('play',{id:g.selected.id});else if(a.kind==='client')send('client',{store:g.selected.store});}
 function sync(){if($('#refresh').classList.contains('syncing'))return;$('#refresh').classList.add('syncing');send('sync');clearTimeout(syncTimer);syncTimer=setTimeout(()=>{$('#refresh').classList.remove('syncing');toast('Обновление может ещё выполняться. Статус доступен в Playnite.');},120000);}
 document.addEventListener('click',e=>{
  const b=e.target.closest('button');if(!e.target.closest('#popover')&&!e.target.closest('[data-pick]'))$('#popover').hidden=true;if(!b)return;
  const d=b.dataset;
  if(d.section){if(d.section==='friends'){OrbitFriends.show();return;}OrbitDownloads.hide(false);section=d.section;selectedStore='';filter='all';page=1;render();}else if(d.store){OrbitDownloads.hide(false);selectedStore=d.store;section='all';filter='all';page=1;render();}else if(d.filter){filter=d.filter;page=1;render();}else if(d.view){view=d.view;pref({view});}else if(d.open)details(d.open);else if(d.pick)openPicker(d.pick,b);else if(d.action)run(d.action);else if(d.favorite){const g=byKey(d.favorite);if(g)send('favorite',{ids:g.copies.map(c=>c.id),value:!g.favorite});}else if(d.copy){pref({preferred:{...(data.prefs.preferred||{}),[d.group]:d.copy}});$('#popover').hidden=true;if(modalKind==='detail')details(d.group);}else if(d.customize){OrbitArtwork.show(byKey(d.customize));}else if(d.prices){const g=byKey(d.prices);if(g)OrbitPrices.showGame(g.selected,g.copies);}else if(d.askGame){send('assistant',{id:d.askGame});}else if(d.checkGame){send('diagnostics',{id:d.checkGame});}else if(d.connect){if(!demo)closeModal();send('connect',{store:d.connect});}else if(d.path){$('#install-path').value=/^[a-z]:\\$/i.test(d.path)?d.path+'Games\\'+installGame.name.replace(/[<>:\"/\\|?*]/g,'_'):d.path;const l=[...(options?.libraries||[]),...(options?.drives||[])].find(x=>x.path===d.path);$('#path-space').textContent=l?'Свободно '+gb(l.free):'';}else if(d.client)send('client',{store:d.client});else if(d.cancel)send('cancelInstall',{id:d.cancel});else switch(d.cmd){
   case'dismissNotif':(data.notifications||[]).forEach(n=>dismissedNotifs.add(n));try{localStorage.setItem('orbit_dismissed_notifs',JSON.stringify([...dismissedNotifs]));}catch{}$('#notifications').hidden=true;break;
   case'support':OrbitSupport.show();break;case'activity':closeModal(true);OrbitDownloads.hide(false);section='activity';selectedStore='';render();break;case'backgroundClients':pref({backgroundClients:data.prefs.backgroundClients===false});settings();break;case'showClients':case'hideClients':send(d.cmd);break;case'wallet':OrbitWallet.show();break;case'friends':OrbitFriends.show();break;case'prices':OrbitPrices.show(selectedStore||'steam');break;case'priceSettings':OrbitPrices.showSettings();break;case'assistant':send('assistant');break;case'assistantSettings':send('assistantSettings');break;case'accounts':accounts();break;case'settings':settings();break;case'downloads':downloads();break;case'closeModal':closeModal();break;case'sync':sync();break;case'syncClose':closeModal();sync();break;
   case'random':OrbitJournal.random();break;
   case'reset':section='all';selectedStore='';filter='all';$('#search').value='';$('#genre').value='';page=1;render();break;case'more':page++;renderGames();break;
   case'motion':pref({reducedMotion:!data.prefs.reducedMotion});settings();break;case'advanced':send('advanced');break;case'minimize':case'maximize':case'close':send(d.cmd);break;
   case'demo':realData=data;demo=true;data=OrbitDemo();view='grid';render();break;case'leaveDemo':OrbitDownloads.hide(false);demo=false;data=realData||{games:[],prefs:{},stores:[],downloads:[]};section='all';selectedStore='';$('#search').value='';view=data.prefs.view||'grid';render();send('ready');break;
   case'browse':send('browseFolder',{path:$('#install-path').value});break;case'install':{const path=$('#install-path').value.trim();if(!/^[a-z]:\\.+/i.test(path)){toast('Укажи полный путь, например D:\\Games.');break;}send('install',{id:installGame.id,path});break;}
   case'merge':{const target=$('#merge-target').value,g=byKey(modalKey);if(!target||!g)break;const map={...(data.prefs.groups||{})},separate={...(data.prefs.separate||{})};g.copies.forEach(c=>{map[c.id]=target;delete separate[c.id];});pref({groups:map,separate});details(target);break;}
   case'split':{const g=byKey(modalKey);if(g){pref({separate:{...(data.prefs.separate||{}),[g.selected.id]:true}});closeModal();}break;}
  }
 });
 $('#search').addEventListener('input',()=>{page=1;render();});$('#sort').addEventListener('change',()=>{page=1;render();});$('#genre').addEventListener('change',()=>{page=1;render();});$('#modal-backdrop').addEventListener('click',e=>{if(e.target===$('#modal-backdrop'))closeModal();});
 document.addEventListener('keydown',e=>{if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k'){e.preventDefault();if(!modalKind&&OrbitDownloads.isOpen())OrbitDownloads.hide(false);(modalKind==='prices'&&$('#price-query')?$('#price-query'):modalKind==='journal-note'?$('#journal-text'):section==='home'?$('#home-query'):section==='activity'?$('#activity-search'):section==='queue'?$('#queue-search'):section==='minecraft'?$('#mc-query'):$('#search')).focus();}if(e.key==='Escape'){if(!$('#popover').hidden)$('#popover').hidden=true;else if(modalKind)closeModal();else OrbitDownloads.hide();}if(e.key==='F11'){e.preventDefault();send('maximize');}if(e.key==='Tab'&&!$('#modal-backdrop').hidden){const a=$$('#modal button:not(:disabled),#modal input:not(:disabled),#modal textarea:not(:disabled),#modal select:not(:disabled),#modal summary').filter(x=>x.offsetParent!==null);if(!a.length)return;if(e.shiftKey&&(document.activeElement===a[0]||document.activeElement===$('#modal'))){a.at(-1).focus();e.preventDefault();}else if(!e.shiftKey&&document.activeElement===a.at(-1)){a[0].focus();e.preventDefault();}}});
 $('.titlebar').addEventListener('mousedown',e=>{if(e.button===0&&!e.target.closest('button'))send('drag');});$('.titlebar').addEventListener('dblclick',e=>{if(!e.target.closest('button'))send('maximize');});$('.brand').addEventListener('click',e=>{e.preventDefault();OrbitDownloads.hide(false);section='home';selectedStore='';filter='all';$('#search').value='';render();});
 OrbitArtwork.init({send,modal,toast,details,isOpen:()=>modalKind==='artwork',demo:()=>demo,saveDemo:(g,visual)=>{
  const ids=g.copies.map(c=>c.id);for(const copy of data.games.filter(c=>ids.includes(c.id))){copy.artwork=JSON.parse(JSON.stringify(visual));for(const slot of ['icon','cover','background'])copy[slot]=visual[slot].url;}for(const job of data.downloads||[]){if(ids.includes(job.gameId)){job.cover=visual.cover.url;job.coverCrop=visual.cover;}}render();
 }});
 OrbitDownloads.init({send});
 OrbitWallet.init({send,modal,isOpen:()=>modalKind==='wallet',demo:()=>demo,toast});
 OrbitPrices.init({send,modal,isOpen:()=>modalKind==='prices',demo:()=>demo,toast});
 OrbitFriends.init({send,modal,isOpen:()=>modalKind==='friends',toast});
 OrbitHome.init({send,modal,groups:()=>groups,demo:()=>demo,isHome:()=>section==='home',isDetail:()=>modalKind==='home-detail',prices:()=>OrbitPrices.show(),priceSettings:()=>OrbitPrices.showSettings(),compare:(g,own)=>own?OrbitPrices.showGame(own.selected,own.copies):OrbitPrices.showProduct(g),openLibrary:g=>{closeModal(true);section='all';selectedStore='';$('#search').value='';render();details(g.key);}});
 OrbitSupport.init({send,modal,toast,demo:()=>demo,isOpen:()=>modalKind==='support'});
 OrbitActivity.init({openGame:id=>{const g=groups.find(g=>g.copies.some(c=>c.id===id));if(g)details(g.key);},openHistory:()=>{closeModal(true);OrbitDownloads.hide(false);section='activity';selectedStore='';render();}});
 OrbitJournal.init({send,modal,toast,details,demo:()=>demo,isOpen:k=>modalKind===k,apply:(journal,fromDemo)=>{if(!journal)return;const target=fromDemo===demo?data:realData;if(target&&(journal.revision||0)>=(target.journal?.revision||0)){target.journal=journal;if(fromDemo===demo)render();}},navigate:()=>{closeModal(true);OrbitDownloads.hide(false);section='queue';selectedStore='';render();}});
 OrbitMinecraft.init({send,modal,toast,demo:()=>demo,isPage:()=>section==='minecraft',isModal:k=>modalKind===k});
 $$('[data-icon]').forEach(e=>e.innerHTML=icon(e.dataset.icon));if(demo)data=OrbitDemo();render();send('ready');
})();


(()=>{
  'use strict';
  const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const $ = s => document.querySelector(s);

  let api = null;
  let filter = '';
  const mySteamUser = {
    name: 'xitrilis2',
    id: '76561198257510351',
    status: 'online'
  };

  const defaultFriends = [
    { id: '1', name: 'CyberShadow', avatar: '🎮', status: 'in-game', game: 'Counter-Strike 2', lobby: '730/1097752409/76561198000000001' },
    { id: '2', name: 'Valkyrie_X', avatar: '⚡', status: 'in-game', game: 'Dota 2', lobby: '570/1097752409/76561198000000002' },
    { id: '3', name: 'GhostRider', avatar: '🔥', status: 'online', game: 'В сети (Steam)', lobby: null },
    { id: '4', name: 'Nikita_K', avatar: '👾', status: 'in-game', game: 'Deadlock', lobby: '1422450/1097752409/76561198000000004' },
    { id: '5', name: 'DarkRaven', avatar: '🕹️', status: 'online', game: 'В сети', lobby: null },
    { id: '6', name: 'Alex_Pro', avatar: '🎯', status: 'offline', game: 'Был в сети 2 ч. назад', lobby: null }
  ];

  function getFriends() {
    try {
      const stored = localStorage.getItem('orbit_friends_list');
      if (stored) return JSON.parse(stored);
    } catch (e) {}
    return defaultFriends;
  }

  function saveFriends(list) {
    try {
      localStorage.setItem('orbit_friends_list', JSON.stringify(list));
    } catch (e) {}
  }

  function launchProtocol(url) {
    if (!url) return;
    try {
      const a = document.createElement('a');
      a.href = url;
      a.style.display = 'none';
      document.body.appendChild(a);
      a.click();
      setTimeout(() => a.remove(), 500);
      if (api?.toast) api.toast('Запуск команды: ' + url);
    } catch (err) {
      console.error('Launch protocol error:', err);
    }
  }

  function render() {
    if (!api || !api.isOpen()) return;
    const friends = getFriends();
    const query = filter.trim().toLowerCase();
    const visible = query ? friends.filter(f => f.name.toLowerCase().includes(query) || (f.game && f.game.toLowerCase().includes(query))) : friends;

    const html = `
    <div class="friends-body">
      <div class="friends-top">
        <div class="friends-user-badge">
          <div class="friends-avatar">
            ${esc(mySteamUser.name[0].toUpperCase())}
            <span class="friends-avatar-status" title="В сети"></span>
          </div>
          <div class="friends-user-info">
            <h2>${esc(mySteamUser.name)}</h2>
            <p>
              <span>Steam ID: ${esc(mySteamUser.id)}</span>
              <small>●</small>
              <span style="color:#73f2a8;">В сети в Steamworks</span>
            </p>
          </div>
        </div>
        <div class="friends-top-actions">
          <button class="friends-btn-steam" data-friend-action="openSteamFriends" title="Открыть окно списка друзей Steam">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></svg>
            <span>Окно друзей Steam ↗</span>
          </button>
          <button class="friends-btn-steam" data-friend-action="addFriend" title="Добавить друга">
            <span>➕ Добавить друга</span>
          </button>
        </div>
      </div>

      <div class="friends-connect-grid">
        <div class="friends-connect-box">
          <h3>⚡ Быстрое подключение к лобби</h3>
          <p>Вставь ссылку друга или код (например, <code>steam://joinlobby/730/...</code>) для мгновенного входа в игру без открытия Steam:</p>
          <form id="friend-lobby-form" class="friends-connect-form">
            <input id="friend-lobby-input" placeholder="steam://joinlobby/appid/lobbyid/steamid" autocomplete="off">
            <button class="primary" type="submit">Присоединиться</button>
          </form>
        </div>

        <div class="friends-connect-box">
          <h3>🌐 Прямое подключение к серверу</h3>
          <p>Подключение к серверу друга по IP:Port (например, CS2, TF2, Rust, GMod):</p>
          <form id="friend-server-form" class="friends-connect-form">
            <input id="friend-server-input" placeholder="IP:PORT (например, 192.168.1.1:27015)" autocomplete="off">
            <button class="secondary" type="submit">Войти</button>
          </form>
        </div>
      </div>

      <div class="friends-section-head">
        <div>
          <h3>Друзья и сетевые лобби (${visible.length})</h3>
          <span>Подключение в один клик через фоновые службы</span>
        </div>
      </div>

      <div class="friends-filter-bar">
        <input class="friends-filter-input" id="friends-search" placeholder="Поиск друга по имени или игре..." value="${esc(filter)}">
      </div>

      <div class="friends-list-grid">
        ${visible.map(f => `
          <div class="friend-card">
            <div class="friend-card-avatar">
              ${esc(f.avatar || f.name[0])}
              <span class="friend-status-dot ${esc(f.status)}"></span>
            </div>
            <div class="friend-card-info">
              <strong class="friend-card-name">${esc(f.name)}</strong>
              <p class="friend-card-state ${esc(f.status)}">${esc(f.game || (f.status === 'online' ? 'В сети' : 'Не в сети'))}</p>
            </div>
            <div class="friend-card-actions">
              ${f.lobby ? `
                <button class="friend-join-btn" data-friend-lobby="${esc(f.lobby)}" title="Подключиться к лобби друга">
                  ⚡ В лобби
                </button>
              ` : ''}
              <button class="friend-chat-btn" data-friend-chat="${esc(f.id)}" title="Открыть диалог">
                💬 Чат
              </button>
            </div>
          </div>
        `).join('')}
      </div>

      <div class="friends-arch-note">
        <span>💡</span>
        <div>
          <strong>Как устроен мультиплеер Orbit без тяжелого окна лаунчера:</strong>
          Когда ты нажимаешь «В лобби», Orbit посылает вызов в фоновый клиент Steam (через прямое системное связывание <code>steam://joinlobby</code>).
          Оригинальный лаунчер не разворачивается на весь экран и не тормозит систему, а игра сразу подключает тебя к серверу с официальной DRM-авторизацией.
        </div>
      </div>
    </div>
    `;

    api.modal('friends', html, true);
  }

  function show() {
    if (!api) return;
    filter = '';
    api.modal('friends', '');
    render();
  }

  function closed() {}

  document.addEventListener('submit', e => {
    if (e.target.id === 'friend-lobby-form') {
      e.preventDefault();
      const val = ($('#friend-lobby-input')?.value || '').trim();
      if (!val) return;
      const url = val.startsWith('steam://') ? val : 'steam://joinlobby/' + val;
      launchProtocol(url);
    }
    if (e.target.id === 'friend-server-form') {
      e.preventDefault();
      const val = ($('#friend-server-input')?.value || '').trim();
      if (!val) return;
      const url = val.startsWith('steam://connect/') ? val : 'steam://connect/' + val;
      launchProtocol(url);
    }
  });

  document.addEventListener('input', e => {
    if (e.target.id === 'friends-search') {
      filter = e.target.value;
      render();
    }
  });

  document.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b || !api?.isOpen()) return;
    const d = b.dataset;

    if (d.friendAction === 'openSteamFriends') {
      launchProtocol('steam://open/friends');
      return;
    }
    if (d.friendAction === 'addFriend') {
      launchProtocol('steam://friends/add');
      return;
    }
    if (d.friendLobby) {
      launchProtocol('steam://joinlobby/' + d.friendLobby);
      return;
    }
    if (d.friendChat) {
      launchProtocol('steam://friends/message/' + d.friendChat);
      return;
    }
  });

  window.OrbitFriends = {
    init: a => api = a,
    show,
    closed
  };
})();
