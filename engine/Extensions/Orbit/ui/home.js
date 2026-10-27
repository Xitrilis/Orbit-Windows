(()=>{
 'use strict';
 const $=s=>document.querySelector(s),esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const safeImage=s=>typeof s==='string'&&(/^(https:|file:|data:image\/|assets\/)/i.test(s))?s:'';
 const image=(s,cls='')=>safeImage(s)?`<img src="${esc(s)}" class="${cls}" alt="" loading="lazy">`:'';
 const stores=Object.fromEntries(OrbitCore.stores.map(s=>[s.id,s.name]));
 const features={'Single-player':'Для одного','Co-op':'Кооператив','Online Co-op':'Кооператив по сети','Shared/Split Screen Co-op':'Вдвоём на одном ПК'};
 const number=n=>new Intl.NumberFormat('ru-RU',{maximumFractionDigits:0}).format(n);
 const money=g=>{try{return g.price==null?'':new Intl.NumberFormat('ru-RU',{style:'currency',currency:g.currency,maximumFractionDigits:2}).format(g.price);}catch{return '';}};
 let api,feed=null,feedBusy=false,feedError='',loaded=false,wasDemo=null,seq=0,expected={feed:'',query:'',detail:''},tab='mood',busy=false,reply='',error='',results=[],chat=[],active=null,detailBusy=false,detailError='',lastQuery='',aiReady=false,regionSignature='';
 const cards=new Map(),cache=new WeakMap();
 function patch(el,html){if(el&&cache.get(el)!==html){el.innerHTML=html;cache.set(el,html);}}
 function request(lane,kind,data={}){const id='home-'+(++seq);expected[lane]=id;api.send(kind,{...data,requestId:id});return id;}
 function mine(g){return api.groups().find(x=>x.copies.some(c=>c.id===(g.libraryId||g.id)||(c.store==='steam'&&c.appId&&c.appId===g.appId)))||api.groups().find(x=>OrbitCore.titleKey(x.name)===OrbitCore.titleKey(g.name));}
 function local(g){const c=g.selected;return {id:c.id,libraryId:c.id,name:g.name,appId:c.appId,store:c.store,image:g.background,cover:g.cover,description:c.description,features:c.features||[],genres:g.genres,owned:true,installed:g.installed};}
 function remember(g){cards.set(g.id,g);return g;}
 function card(g){remember(g);const own=mine(g),price=money(g),discount=g.originalPrice>g.price&&g.price!=null?Math.round((1-g.price/g.originalPrice)*100):0;
  return `<article class="home-card"><button class="home-card-open" data-home-game="${esc(g.id)}" aria-label="Подробнее: ${esc(g.name)}"><span class="home-card-art"><span class="home-cover-fallback">o.</span>${image(g.image||g.cover)}${discount?`<b class="home-discount">−${discount}%</b>`:''}${own?`<span class="home-owned">${own.installed?'✓ На твоём ПК':'✓ В библиотеке'}</span>`:''}</span><span class="home-card-copy"><strong>${esc(g.name)}</strong><span class="home-card-bottom"><small>${esc(stores[g.store]||'Steam')}</small><b>${price||'Подробнее ↗'}</b></span></span></button>${g.reason?`<p class="home-reason"><span>✧ ИИ считает</span>${esc(g.reason)}</p>`:''}</article>`;
 }
 function skeleton(){return '<div class="home-skeleton" aria-hidden="true"></div>'.repeat(4);}
 function init(a){
  api=a;
  $('#home-view').innerHTML=`<div id="home-highlights" class="home-highlights"></div>
   <section class="home-discover" aria-labelledby="home-discover-title">
    <div class="home-discover-top"><div><p class="eyebrow">ORBIT / DISCOVER</p><h2 id="home-discover-title">Во что хочется поиграть?</h2></div><button class="home-ai-settings" data-home="ai-settings">✧ Настроить ИИ</button></div>
    <div class="home-search-tabs" role="tablist" aria-label="Способ поиска"><button id="home-mood-tab" role="tab" aria-selected="true" aria-controls="home-search-panel" data-home-tab="mood">✧ По настроению</button><button id="home-title-tab" role="tab" aria-selected="false" aria-controls="home-search-panel" tabindex="-1" data-home-tab="title">⌕ По названию</button></div>
    <form id="home-form"><div id="home-search-panel" role="tabpanel" aria-labelledby="home-mood-tab"><label class="home-input-label" for="home-query" id="home-query-label">Расскажи, чего хочется — Orbit подберёт варианты.</label><div class="home-input-row"><textarea id="home-query" rows="2" maxlength="1200" placeholder="Хочется поиграть с девушкой вдвоём. Что-нибудь несложное, с головоломками…" aria-describedby="home-privacy"></textarea><button id="home-submit" class="home-submit" type="submit"><span>Подобрать</span><b>↗</b></button></div>
    <div class="home-examples"><button type="button" data-home-example="Несложная игра вдвоём с девушкой">Вдвоём без спешки</button><button type="button" data-home-example="Кооперативные головоломки на двоих">Головоломки на двоих</button><button type="button" data-home-example="Песочница для одного, строить и исследовать">Своя песочница</button><button type="button" data-home-example="Какие есть новинки игр?">Новинки</button></div></div>
    <div class="home-filters"><label>Как играем <select id="home-mode"><option value="any">Неважно</option><option value="solo">Один</option><option value="local">Вдвоём на одном ПК</option><option value="online">Кооператив по сети</option></select></label><label class="home-check"><input type="checkbox" id="home-only"> Только мои игры</label><label class="home-check" id="home-share-label"><input type="checkbox" id="home-share" checked> Учитывать библиотеку</label></div>
    <p id="home-privacy">После «Подобрать» запрос и названия, жанры и режимы до 300 твоих игр отправятся в Gemini. Ключ — из настроек Orbit AI.</p></form>
    <div id="home-query-state" aria-live="polite"></div><div id="home-query-results"></div>
   </section>
   <section class="home-shelf"><div class="home-shelf-heading"><div><p class="eyebrow">ТВОЯ СЛЕДУЮЩАЯ ИГРА</p><h2>Вам может понравиться</h2></div><button class="quiet" data-home="focus">Подобрать точнее ↗</button></div><p id="home-for-you-note" class="home-section-note"></p><div id="home-for-you" class="home-card-grid"></div></section>
   <section class="home-shelf"><div class="home-shelf-heading"><div><p class="eyebrow">МЕНЬШЕ ЦЕНА. БОЛЬШЕ МИРОВ.</p><h2>Сейчас со скидкой</h2></div><button class="quiet" data-home="prices">Все предложения ↗</button></div><p id="home-region-note" class="home-section-note"></p><div id="home-deals" class="home-card-grid"></div></section>
   <section class="home-shelf"><div class="home-shelf-heading"><div><p class="eyebrow">НОВОЕ НА ВИТРИНЕ</p><h2>Свежие релизы Steam</h2></div><button class="quiet" data-home="refresh">Обновить ↻</button></div><div id="home-releases" class="home-card-grid"></div></section>
   <div id="home-feed-note" class="home-feed-note" role="status"></div>`;
  $('#home-form').addEventListener('submit',e=>{e.preventDefault();submit();});
  $('#home-query').addEventListener('keydown',e=>{if(e.key==='Enter'&&!e.shiftKey&&!e.isComposing){e.preventDefault();submit();}});
  $('#home-share').addEventListener('change',privacy);
  $('#home-only').addEventListener('change',()=>{if($('#home-only').checked&&!$('#home-share').checked)$('#home-share').checked=true;privacy();});
  $('#home-query').addEventListener('input',()=>{if(error){error='';drawQuery();}});
  document.addEventListener('click',e=>{
   const b=e.target.closest('button');if(!b||!api)return;const d=b.dataset;
   if(d.homeGame){open(d.homeGame);return;}
   if(d.homeTab){setTab(d.homeTab);return;}
   if(d.homeExample){setTab('mood');$('#home-query').value=d.homeExample;$('#home-query').focus();return;}
   switch(d.home){
    case'focus':focus();break;case'refresh':load(true);break;case'ai-settings':api.send('assistantSettings');break;case'prices':api.prices();break;case'region':api.priceSettings();break;
    case'cancel':cancelQuery();break;case'clear':cancelQuery();chat=[];results=[];reply='';error='';api.send('homeClear');drawQuery();break;
    case'retry':submit();break;case'detail-retry':if(active)open(active.id);break;
    case'library':{const g=active&&mine(active);if(g)api.openLibrary(g);break;}
    case'compare':if(active)api.compare(active,mine(active));break;
   }
  });
  $('.home-search-tabs').addEventListener('keydown',e=>{if(['ArrowLeft','ArrowRight','Home','End'].includes(e.key)){e.preventDefault();setTab(tab==='mood'?'title':'mood');$('#home-'+tab+'-tab').focus();}});
  document.addEventListener('error',e=>{if(e.target.tagName==='IMG'&&(e.target.closest('#home-view')||e.target.closest('[data-kind="home-detail"]')))e.target.hidden=true;},true);
 }
 function privacy(){$('#home-share-label').hidden=tab!=='mood';$('#home-privacy').textContent=tab==='title'?'Поиск по названиям в твоей библиотеке и Steam. Gemini не используется.':$('#home-share').checked?'После «Подобрать» запрос и названия, жанры и режимы до 300 твоих игр отправятся в Gemini. Ключ — из настроек Orbit AI.':'В Gemini отправятся только запрос и история этого диалога. Названия из библиотеки не передаются.';}
 function setTab(value){tab=value==='title'?'title':'mood';for(const t of ['mood','title']){const b=$('#home-'+t+'-tab');b.setAttribute('aria-selected',String(tab===t));b.tabIndex=tab===t?0:-1;}$('#home-search-panel').setAttribute('aria-labelledby','home-'+tab+'-tab');$('#home-query').placeholder=tab==='title'?'Например, Batman: Arkham Knight':'Хочется поиграть с девушкой вдвоём. Что-нибудь несложное, с головоломками…';$('#home-query-label').textContent=tab==='title'?'Введи название игры.':'Расскажи, чего хочется — Orbit подберёт варианты.';$('.home-examples').hidden=tab==='title';privacy();drawQuery();}
 function focus(){$('#home-query').scrollIntoView({block:'center',behavior:document.documentElement.classList.contains('motion-off')||matchMedia('(prefers-reduced-motion:reduce)').matches?'instant':'smooth'});$('#home-query').focus({preventScroll:true});}
 function load(force=false){if(feedBusy&&!force)return;loaded=true;feedBusy=true;feedError='';drawFeed();if(api.demo()){feedBusy=false;const games=api.groups().map(local);feed={deals:games.slice(0,4),popular:games.slice(4,8),releases:[],regionSet:false,calendar:{events:[],message:'Даты появятся из официального календаря Steam.'},message:'Демонстрация интерфейса: игры — примеры, актуальные цены и даты не загружены.'};drawFeed();return;}request('feed','homeLoad',{force});}
 function update(){const demo=api.demo();if(wasDemo!==demo){if(wasDemo!==null){for(const lane of ['feed','query','detail']){expected[lane]='reset-'+(++seq);api.send('homeCancel',{lane});}}wasDemo=demo;loaded=false;feed=null;feedBusy=false;results=[];chat=[];reply='';error='';busy=false;cards.clear();drawQuery();}if(api.isHome()&&!loaded)load();else drawFeed();}
 function drawFeed(){
  const games=api.groups(),choices=[...games].sort((a,b)=>Number(b.favorite)-Number(a.favorite)||Number(!b.playtime)-Number(!a.playtime)).slice(0,4).map(local);
  // Prefer favourites, then games waiting for their first session.
  choices.sort((a,b)=>Number(mine(b)?.favorite)-Number(mine(a)?.favorite));
  const featured=(feed?.deals||[])[0]||(feed?.popular||[])[0]||choices[0],calendar=feed?.calendar;
  const today=new Date().toLocaleDateString('sv-SE'),events=(calendar?.events||[]).filter(e=>e.end>=today).sort((a,b)=>a.start.localeCompare(b.start)).slice(0,3);
  const date=s=>{const d=new Date(s+'T12:00:00');return Number.isNaN(+d)?'':d.toLocaleDateString('ru-RU',{day:'numeric',month:'short'});};
  patch($('#home-highlights'),`<section class="home-featured ${featured?'has-game':''}">${featured?image(featured.image||featured.cover,'home-featured-art'):''}<div class="home-orbits" aria-hidden="true"><i></i><i></i><i></i></div><div class="home-featured-copy"><p class="home-feature-label"><i></i>${featured?'ОТКРОЙ НОВЫЙ МИР':'ТВОЁ ВРЕМЯ ИГРАТЬ'}</p><h2>${featured?esc(featured.name):'Твой следующий<br>мир — здесь.'}</h2><p>${featured?(mine(featured)?'Уже в твоей коллекции. Осталось выбрать настроение.':'Присмотрись к игре. Описание и предложения — в один клик.'):'Вся коллекция, интересные игры и идеи для вечера — на одной орбите.'}</p><div><button class="primary" ${featured?`data-home-game="${esc(remember(featured).id)}"`:'data-home="focus"'}>${featured?'Открыть игру':'Найти свою игру'} <span>↗</span></button>${featured&&money(featured)?`<span class="home-feature-price">${money(featured)}<small>Steam · витрина</small></span>`:''}</div></div><span class="home-feature-number">01 / ORBIT</span></section>
   <section class="home-calendar"><div class="home-calendar-heading"><span>НА ГОРИЗОНТЕ</span><span>↗</span></div><h2>Ближайшие<br>распродажи</h2><div class="home-events">${events.length?events.map(e=>`<button data-home="prices" class="home-event"><span class="home-event-date">${date(e.start)}<small>${e.start<=today?(e.source==='epic'?'EPIC · СЕЙЧАС':'STEAM · СЕЙЧАС'):(e.source==='epic'?'EPIC':'STEAM')}</small></span><span><strong>${esc(e.name)}</strong><small>до ${date(e.end)}</small></span></button>`).join(''):`<p class="home-calendar-empty">${feedBusy?'Сверяем официальный календарь…':'Объявленные даты будут здесь. Открой магазин, чтобы проверить календарь.'}</p>`}</div><p class="home-calendar-note">${calendar?.cached?'Сохранённые даты · проверь анонс':'Календарь магазинов'}. Участие и цены отдельных игр пока не объявлены.</p></section>`);
  $('#home-for-you-note').textContent=choices.length?'Из твоей коллекции: избранные и игры, к которым можно вернуться. Для подбора по настроению напиши Orbit выше.':'Популярное в Steam. Подключи аккаунты, чтобы видеть здесь и свою коллекцию.';
  patch($('#home-for-you'),(choices.length?choices:feed?.popular||[]).slice(0,4).map(card).join('')||((feedBusy||!api.initialLoaded?.())?skeleton():'<div class="home-empty-inline"><p>Твоя подборка появится здесь.</p><button class="secondary" data-cmd="accounts">Подключить магазины</button></div>'));
  $('#home-region-note').textContent=feed?.message||'Загружаем предложения Steam…';
  patch($('#home-deals'),(feed?.deals||[]).map(card).join('')||((feedBusy||!api.initialLoaded?.())?skeleton():'<p class="home-empty-inline">Предложения не загрузились. Можно повторить запрос или открыть магазин.</p>'));
  patch($('#home-releases'),(feed?.releases||[]).map(card).join('')||((feedBusy||!api.initialLoaded?.())?skeleton():`<p class="home-empty-inline">${api.demo()?'Демонстрация: актуальные новинки не загружены.':'Сейчас нет подтверждённых новинок. Попробуй обновить витрину позже.'}</p>`));
  patch($('#home-feed-note'),`<span>${esc(feedError||feed?.message||'Получаем каталог Steam…')}</span><button class="quiet" data-home="region">Регион и валюта</button>${feedError?'<button class="quiet" data-home="refresh">Повторить</button>':''}`);
 }
 function submit(){
  if(busy)return;const query=$('#home-query').value.trim();if(query.length<3||query.length>1200){error='Напиши хотя бы 3 символа.';drawQuery();return;}
  if(tab==='mood'&&$('#home-only').checked&&!$('#home-share').checked){error='Включи «Учитывать библиотеку» или отключи «Только мои игры».';drawQuery();return;}
  lastQuery=query;error='';reply='';results=[];busy=true;drawQuery();
  if(api.demo()){
   busy=false;results=tab==='title'?api.groups().filter(g=>OrbitCore.titleKey(g.name).includes(OrbitCore.titleKey(query))).map(local):api.groups().slice(0,3).map(local);
   reply=tab==='title'?'Совпадения в демонстрационной коллекции.':'Это пример выдачи, не ответ Gemini. Живой подбор заработает после настройки ИИ в Orbit.exe.';chat.push({query,reply,ai:tab==='mood'});drawQuery();return;
  }
  request('query',tab==='title'?'homeSearch':'homeRecommend',{query,mode:$('#home-mode').value,ownedOnly:$('#home-only').checked,shareLibrary:$('#home-share').checked});
 }
 function cancelQuery(){expected.query='cancel-'+(++seq);api.send('homeCancel',{lane:'query'});busy=false;error='Подбор остановлен.';drawQuery();}
 function drawQuery(){
  $('#home-submit').disabled=busy;$('#home-submit span').textContent=busy?'Ищем…':tab==='title'?'Найти':'Подобрать';
  const past=chat.slice(-4,-1);
  patch($('#home-query-state'),`${past.length?`<details class="home-history"><summary>Предыдущие сообщения · ${past.length}</summary>${past.map(m=>`<p><b>Ты:</b> ${esc(m.query)}</p><p><b>Orbit:</b> ${esc(m.reply)}</p>`).join('')}</details>`:''}${busy?`<div class="home-status"><span class="home-spinner"></span><div><strong>${tab==='mood'?'Ищем подходящие миры':'Ищем игры'}</strong><p>${tab==='mood'?'Gemini подбирает варианты, Orbit сверяет каталог и способы игры.':'Проверяем библиотеку и каталог Steam.'}</p></div><button class="quiet" data-home="cancel">Остановить</button></div>`:error?`<div class="home-error" role="alert">${esc(error)} <button class="quiet" data-home="retry">Повторить</button></div>`:reply?`<div class="home-answer"><p class="home-question">${esc(lastQuery)}</p><div><b>✧ Orbit</b><button class="quiet" data-home="clear">Очистить диалог</button></div><p>${esc(reply)}</p>${chat.at(-1)?.ai?'<small>Подбор ИИ · названия и режимы проверены по доступным данным каталога. Оценка сложности субъективна.</small>':''}</div>`:''}`);
  patch($('#home-query-results'),results.length?`<div class="home-results-label">Нашлось для тебя <span>${results.length}</span></div><div class="home-card-grid home-result-grid">${results.map(card).join('')}</div>`:'');
 }
 function open(id){
  const g=cards.get(id)||api.groups().map(local).find(g=>g.id===id);if(!g)return;active=g;detailError='';detailBusy=!api.demo();drawDetail(false);
  if(api.demo()){detailBusy=false;active={...g,description:g.description||'Это демонстрационная карточка. В приложении здесь появятся описание, дата релиза и режимы игры из каталога или твоей библиотеки.'};drawDetail(true);return;}
  request('detail','homeDetails',{id:g.id});
 }
 function drawDetail(keep=true){
  if(!active)return;const g=active,own=mine(g);
  api.modal('home-detail',`<div class="home-detail-hero">${image(g.image||g.cover)}<div><p class="eyebrow">${own?'ТВОЯ КОЛЛЕКЦИЯ':'КАТАЛОГ STEAM'}</p><h2 id="modal-title">${esc(g.name)}</h2></div></div><div class="modal-body home-detail-body"><div class="home-detail-actions">${own?'<button class="primary" data-home="library">Открыть в библиотеке ↗</button>':''}<button class="${own?'secondary':'primary'}" data-home="compare" ${!own&&!g.priceToken&&!api.demo()?'disabled':''}>Цены и распродажи ↗</button><span>${own?esc(stores[own.selected.store]):'Steam'}</span></div>
   ${detailBusy?'<div class="home-status" role="status"><span class="home-spinner"></span><p>Получаем описание и способы игры…</p></div>':''}
   ${detailError?`<p class="home-error" role="alert">${esc(detailError)} <button class="quiet" data-home="detail-retry">Повторить</button></p>`:''}
   ${g.reason?`<div class="home-detail-reason"><b>✧ Почему предложил ИИ</b><p>${esc(g.reason)}</p></div>`:''}
   <h3>Об игре</h3><p class="home-description">${esc(g.description||(detailBusy?'':detailError?'Описание не получено.':'У этой игры пока нет описания. Можно обновить метаданные в библиотеке.'))}</p>
   <div class="home-feature-tags">${(g.features||[]).filter(f=>features[f]||/кооп|совмест|одного/i.test(f)).slice(0,8).map(f=>`<span>${esc(features[f]||f)}</span>`).join('')}</div><div class="genres">${(g.genres||[]).slice(0,8).map(x=>`<span>${esc(x)}</span>`).join('')}</div>
   ${g.release?`<p class="home-release">Дата релиза в каталоге: ${esc(g.release)}</p>`:''}
   <p class="note">${own?'Наличие — по последнему импорту аккаунтов. Выбор копии, запуск и установка доступны в библиотеке.':'Описание и режимы — данные Steam. Текущая цена и доступность проверяются в «Цены и распродажи».'}</p></div>`,keep);
 }
 function receive(m){
  if(!m.kind.startsWith('home'))return false;
  if(api.demo())return true;
  if(m.kind==='homeFeed'&&m.requestId===expected.feed){feedBusy=false;feed=m.data;regionSignature=JSON.stringify({country:feed.country||'',currency:feed.currency||''});aiReady=!!m.aiReady;drawFeed();return true;}
  if(m.kind==='homeResults'&&m.requestId===expected.query){busy=false;results=m.games||[];reply=m.reply||'';error='';chat.push({query:lastQuery,reply,ai:!!m.ai});if(chat.length>5)chat.shift();drawQuery();return true;}
  if(m.kind==='homeDetail'&&m.requestId===expected.detail&&api.isDetail()){detailBusy=false;active={...active,...m.game,reason:active?.reason||m.game.reason};remember(active);drawDetail();return true;}
  if(m.kind==='homeError'&&m.requestId===expected[m.lane]){if(m.lane==='feed'){feedBusy=false;feedError=m.message;drawFeed();}if(m.lane==='query'){busy=false;error=m.message;drawQuery();}if(m.lane==='detail'&&api.isDetail()){detailBusy=false;detailError=m.message;drawDetail();}return true;}
  return true;
 }
 function accountsChanged(accounts){const signature=JSON.stringify({country:accounts?.steam?.country||'',currency:accounts?.steam?.currency||''});if(regionSignature!==signature){regionSignature=signature;if(loaded&&!api.demo()){if(busy)cancelQuery();results=[];reply='';error='';drawQuery();feed=null;feedBusy=false;load(true);}}}
 function closed(){expected.detail='close-'+(++seq);api.send('homeCancel',{lane:'detail'});active=null;detailBusy=false;}
 window.OrbitHome={init,update,receive,closed,focus,accountsChanged,refresh:()=>load(true)};
})();
