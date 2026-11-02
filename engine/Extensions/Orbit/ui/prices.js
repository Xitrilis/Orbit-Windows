(()=>{
 'use strict';
 const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])), $=s=>document.querySelector(s);
 const names={steam:'Steam',epic:'Epic Games',gog:'GOG',ea:'EA app',ubisoft:'Ubisoft Connect',battlenet:'Battle.net',funpay:'FunPay',plati:'Plati.Market'};
 const marks={steam:'S',epic:'E',gog:'gog',ea:'EA',ubisoft:'U',battlenet:'B',funpay:'F',plati:'P'};
 const countries={RU:'Россия',KZ:'Казахстан',BY:'Беларусь',UA:'Украина',TR:'Турция',US:'США',DE:'Германия',PL:'Польша',GB:'Великобритания',GE:'Грузия',AM:'Армения'};
 const icon=n=>typeof window!=='undefined'&&window.OrbitIcon?window.OrbitIcon(n):(n==='search'?'<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 5 5"/></svg>':n==='settings'?'<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="m9 3-.5 2.5-2 .9L4 6l-1.5 3 1.8 1.8v2.4L2.5 15 4 18l2.5-.4 2 .9L9 21h6l.5-2.5 2-.9 2.5.4 1.5-3-1.8-1.8v-2.4L21.5 9 20 6l-2.5.4-2-.9L15 3Z"/></svg>':'');
 const dot=s=>typeof window!=='undefined'&&window.OrbitDot?window.OrbitDot(s):`<i class="store-dot ${esc(s)}"></i>`;
 const S=()=>typeof window!=='undefined'?(window.OrbitSales||{}):{};
 let requestTimer=null;
 let api,config={accounts:{},funpaySections:{}},page='search',suggestTimer=null,suggestSeq=0,suggestMap=new Map(),suggestItems=[],suggestQuery='',suggestIsOpen=false,activeSuggestIdx=-1,source='steam',query='',selected=null,storeTab='deals',dealFilter='all',copies=[],results=null,comparison=null,busy=false,error='',activeMarket='funpay',request=0,expected='',settingsBack='search',pendingSettings=false,lastPage='',settingsDraft=null,discovery=null,automaticStores=['steam','epic','gog','ea','ubisoft'];
 const account=()=>{const a=config.accounts?.[selected?.store||source]||{};return{country:a.country||config.accounts?.steam?.country||'',currency:a.currency||''};},country=c=>countries[c]||c||'Регион не выбран';
 const money=(v,c)=>window.OrbitMoney.format(v,c);
 const when=t=>t?new Date(t).toLocaleString('ru-RU',{day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit'}):'—';
 const mark=s=>`<b class="price-mark ${esc(s)}">${esc(marks[s]||s)}</b>`;
 const external=(url,label='Открыть источник')=>url?`<button class="price-link" data-price-url="${esc(url)}">${label} ↗</button>`:'';
 const normalized=s=>String(s||'').replace(/[™®]/g,'').normalize('NFKC').toLowerCase().replace(/[^\p{L}\p{N}]+/gu,' ').trim();
 function cancelMain(){clearTimeout(requestTimer);expected=String(++request);busy=false;api.send('pricesCancel');}
 function requestData(kind,p){
  closeSuggestions();clearTimeout(requestTimer);expected=String(++request);busy=true;error='';draw();
  requestTimer=setTimeout(()=>{if(!busy||!api?.isOpen())return;cancelMain();error='Проверка заняла слишком много времени. Повтори запрос или выбери другой магазин.';draw();},75000);
  api.send(kind,{...p,requestId:expected});
 }
 function show(store,load=true){cancelMain();closeSuggestions();pendingSettings=false;settingsDraft=null;if(names[store]&&store!=='funpay'&&store!=='plati')source=store;page='search';storeTab='deals';query='';results=null;discovery=null;selected=null;comparison=null;error='';busy=false;api.modal('prices','');api.send('pricesSettings');draw();if(load)discover(false);}
 function discover(force=false){page='search';selected=null;comparison=null;results=null;discovery=null;requestData('pricesDiscover',{store:source,force});}
 function showGame(game,otherCopies=[]){pendingSettings=false;selected={id:game.id,store:game.store,name:game.name,image:game.background||game.cover};copies=otherCopies;comparison=null;page='compare';api.modal('prices','');api.send('pricesSettings');refresh(false);}
 function showProduct(g){pendingSettings=false;selected={token:g.priceToken||g.token,id:g.appId||g.id,store:g.store||'steam',name:g.name,image:g.image};copies=[];comparison=null;page='compare';api.modal('prices','');api.send('pricesSettings');refresh(false);}
 function refresh(force=true){if(!selected)return;comparison=null;requestData('pricesCompare',{...(selected.token?{token:selected.token}:{id:selected.id}),force});}
 function search(force=false){if(query.trim().length<3){error='Введи хотя бы 3 символа названия.';draw();return;}page='search';selected=null;results=null;requestData('pricesSearch',{store:source||'steam',query:query.trim(),force});}
 function loading(){return '<div class="price-loading" role="status"><span class="price-spinner"></span><strong>Проверяем источники</strong><p>Получаем цены, условия предложений и курс валют.</p><button class="quiet" data-price="cancel">Остановить</button></div>';}
 function pair(o,small=false){const value=window.OrbitMoney.equivalents(o);return value?`<div class="price-pair ${small?'small':''}" aria-label="Пересчёт по курсу ЦБ">${esc(value)}</div>`:'';}
 function original(o){if(o.free&&o.amount===0)return 'Бесплатно';return `${o.from?'От ':''}${money(o.amount,o.currency)}${Number.isFinite(o.amount)&&o.originalAmount>o.amount?` <del>${money(o.originalAmount,o.currency)}</del>`:''}`;}
 function closeSuggestions(){
  if(suggestIsOpen)api?.send('pricesCancel',{lane:'suggest'});
  suggestIsOpen=false;suggestSeq++;activeSuggestIdx=-1;suggestItems=[];suggestMap.clear();clearTimeout(suggestTimer);
  const input=$('#price-query');input?.setAttribute('aria-expanded','false');input?.removeAttribute('aria-activedescendant');
  const box=$('#price-suggestions');if(box){box.hidden=true;box.innerHTML='';}
 }
 function handleSuggest(val){
  query=val;const q=val.trim();closeSuggestions();suggestQuery=q;
  if(!$('#price-suggestions')||q.length<2)return;
  suggestIsOpen=true;
  const localMatches=(discovery?.products||[]).filter(p=>p.name?.toLowerCase().includes(q.toLowerCase())).slice(0,8);
  renderSuggestions(localMatches);
  if(q.length<3)return;
  const reqSeq=++suggestSeq,reqStore=source;
  suggestTimer=setTimeout(()=>{
   if(!suggestIsOpen||suggestQuery!==q||source!==reqStore)return;
   api.send('pricesSearch',{store:reqStore,query:q,requestId:'suggest-'+reqSeq,force:false});
  },240);
 }
 function renderSuggestions(products){
  const box=$('#price-suggestions');
  if(!box||!suggestIsOpen)return;
  suggestItems=(products||[]).slice(0,8);suggestMap.clear();suggestItems.forEach(p=>suggestMap.set(p.token||p.id,p));
  $('#price-query')?.setAttribute('aria-expanded',String(suggestItems.length>0));
  if(!suggestItems.length){box.hidden=true;box.innerHTML='';return;}
  box.hidden=false;
  box.innerHTML=`<div class="price-suggest-list">${products.slice(0,8).map((p,i)=>{
   const o=p.preview;let priceText='В магазине ↗',disc=0;
   if(o){
    if(o.free)priceText='Бесплатно';
    else{
     priceText=money(o.amount,o.currency);
     disc=o.originalAmount>o.amount&&o.amount!=null?Math.round((1-o.amount/o.originalAmount)*100):0;
    }
   }
   return `<button type="button" id="price-suggest-${i}" role="option" aria-selected="${i===activeSuggestIdx}" tabindex="-1" class="price-suggest-item ${i===activeSuggestIdx?'active':''}" data-suggest-idx="${i}" data-price-suggest="${esc(p.token||p.id)}">
    <span class="price-suggest-art">${p.image?`<img src="${esc(p.image)}" alt="">`:mark(p.store||'steam')}</span>
    <span class="price-suggest-details">
     <strong class="price-suggest-name">${esc(p.name)}</strong>
     <small class="price-suggest-type">${esc(p.productType||(names[p.store||source]+' · Каталог'))}${o?.reference?' · Другая витрина: '+esc(o.country):''}</small>
    </span>
    <span class="price-suggest-price-box">
     ${disc>0?`<span class="price-suggest-disc">−${disc}%</span>`:''}
     <strong class="price-suggest-val">${esc(priceText)}</strong>
    </span>
   </button>`;
  }).join('')}</div>`;
 }
 function idlePane(){return `<div class="price-empty"><h3>${account().country?'Витрина пока не загружена':'Выбери регион цен'}</h3><p>${account().country?'Повтори загрузку или найди игру по названию.':'Укажи страну в настройках — цены зависят от региона магазина.'}</p><button class="secondary" data-price="${account().country?'deals':'settings'}">${account().country?'Загрузить витрину':'Настроить регион'}</button></div>`;}
 function productCard(p,checked=false){
  const o=p.preview,discount=S().pct?.(o)||0,store=p.store||source;
  return `<button class="price-product" data-price-product="${esc(p.token)}">
   <div class="price-product-art">${mark(store)}${p.image&&/^https:\/\//i.test(p.image)?`<img src="${esc(p.image)}" alt="" loading="lazy">`:''}</div>
   <div class="price-product-copy"><small>${esc(p.productType||'Игра')}</small><h3 title="${esc(p.name)}">${esc(p.name)}</h3>
    <div class="deal-pricing">${discount?`<span class="deal-flame-badge">−${discount}%</span>`:''}<p class="price-product-native">${o?original(o):'Цена недоступна'}</p></div>
    ${o?pair(o,true):''}
    ${p.priceMessage?`<p class="price-product-note" title="${esc(p.priceMessage)}">${esc(p.priceMessage)}</p>`:o?.provider==='EGData'?'<p class="price-product-note">Каталог EGData · проверь цену в Epic</p>':''}
    ${checked&&o?`<p class="price-checked">${esc(names[store])} · ${esc(when(o.checkedAt))}</p>`:''}
    <span class="price-product-action">Сравнить цены <b>→</b></span>
   </div></button>`;
 }
 function searchPane(){
  const a=account(),libGames=window.Orbit?.library()||[],inherited=source!=='steam'&&!config.accounts?.[source]?.country&&a.country;
  const library=`<section class="store-library-section"><div class="price-results-heading"><h3>Игры из твоей коллекции</h3><span>Все магазины · нажми, чтобы сравнить цены</span></div>${libGames.length?`<div class="store-library-grid">${libGames.map(g=>`<button class="store-lib-card" data-price-library="${esc(g.id)}">${g.cover?`<img src="${esc(g.cover)}" alt="" loading="lazy">`:`<div class="store-lib-fallback">${esc((g.name||'G')[0])}</div>`}<div><strong title="${esc(g.name)}">${esc(g.name)}</strong><small>${dot(g.store)} ${esc(names[g.store]||g.store)}</small></div><span class="store-lib-action">Цены →</span></button>`).join('')}</div>`:'<div class="price-empty"><h3>Здесь появятся твои игры</h3><p>Подключи библиотеку магазина в настройках Orbit. Пока можно искать игры в каталоге.</p></div>'}</section>`;
  return `<div class="store-heading"><div><p class="eyebrow">МАГАЗИН</p><h2 id="modal-title">Найди следующую игру</h2></div><label class="price-store-label">Витрина<select id="price-store" aria-label="Магазин">${Object.keys(names).slice(0,6).map(s=>`<option value="${s}" ${s===source?'selected':''}>${names[s]}</option>`).join('')}</select></label></div>
  <div class="store-topbar"><form id="price-search-form" class="price-search-unified"><span class="price-search-lens">${icon('search')}</span><input id="price-query" aria-label="Название игры" role="combobox" aria-autocomplete="list" aria-expanded="false" aria-controls="price-suggestions" placeholder="Название игры или издания…" maxlength="150" value="${esc(query)}" autocomplete="off"><button type="button" class="price-clear-btn" data-price="clearSearch" title="Очистить поиск" aria-label="Очистить поиск">✕</button><button class="primary price-submit-btn">Найти</button><div id="price-suggestions" class="price-suggestions" role="listbox" aria-label="Найденные игры" hidden></div></form>
   <button class="price-region-chip" data-price="settings" title="Регион поиска цен; не статус входа в аккаунт"><span>${esc(country(a.country))}</span><small>${inherited?'Из Steam':'Регион витрины'}</small>${icon('settings')}</button></div>
  <p class="store-region-note">Цены ${source==='epic'?'из Epic Games; если витрина не отвечает, используем каталог EGData с указанием источника.':'из публичной витрины '+esc(names[source])+'.'} ${['epic','ea','ubisoft'].includes(source)?'Открытый лаунчер не подтверждает регион цены. ':''}${['ea','ubisoft'].includes(source)?'Проверяем PC-издание и фактическую страну ответа магазина. ':''}Регион можно изменить справа.</p>
  ${!results?`<div class="store-nav-tabs"><button class="store-tab-btn ${storeTab==='deals'?'active':''}" data-store-tab="deals">Скидки</button><button class="store-tab-btn ${storeTab==='calendar'?'active':''}" data-store-tab="calendar">Календарь распродаж</button><button class="store-tab-btn ${storeTab==='library'?'active':''}" data-store-tab="library">Моя библиотека (${libGames.length})</button></div>`:''}
  ${!a.country?'<p class="price-setup">Укажи страну аккаунта в настройках цен.</p>':''}
  ${error?`<p class="price-error" role="alert">${esc(error)}</p>`:''}
  ${!results&&storeTab==='library'?library:busy?loading():results?`<div class="price-results-heading"><div><h3>Найдено ${results.products?.length||0} предложений</h3><span>по запросу «${esc(query)}» · ${esc(country(results.country||a.country))}</span></div><button class="quiet" data-price="clearSearch">← Вернуться к скидкам</button></div>${results.products?.length?`${results.message?`<p class="price-catalog-note" role="status">${esc(results.message)}</p>`:''}<div class="price-search-results">${results.products.map(p=>productCard(p)).join('')}</div>`:`<div class="price-empty"><h3>Ничего не найдено</h3><p>${esc(results.message||'Попробуй оригинальное название или другую витрину.')}</p></div>`}${results.fxError?`<p class="note">${esc(results.fxError)}</p>`:''}${external(results.url,'Открыть в браузере')}`:storeTab==='calendar'&&discovery?S().calendar?.(discovery)||idlePane():discovery?discoveryPane():idlePane()}`;
 }
 function discoveryPane(){
  const products=discovery?.products||[],filtered=products.filter(p=>dealFilter!=='steep'||(S().pct?.(p.preview)||0)>=60);
  return `<section class="sale-featured"><div class="price-results-heading"><div><h3>${discovery?.status==='unsupported'?'Каталог':'Скидки'} ${esc(names[source])}</h3><span>${esc(discovery?.message||'Цены витрины для выбранного региона.')}</span>${discovery?.checkedAt?`<small class="price-checked">Обновлено ${esc(when(discovery.checkedAt))}</small>`:''}</div><button class="quiet" data-price="deals">↻ Обновить</button></div>
  ${products.length?`<div class="deal-filter-bar"><button class="deal-filter-btn ${dealFilter==='all'?'active':''}" data-deal-filter="all">Все предложения (${products.length})</button><button class="deal-filter-btn ${dealFilter==='steep'?'active':''}" data-deal-filter="steep">Скидки от −60%</button></div>`:''}
  ${filtered.length?`<div class="price-search-results">${filtered.map(p=>productCard(p,true)).join('')}</div>`:`<div class="price-empty"><h3>${discovery?.status==='error'?'Не удалось загрузить скидки':discovery?.status==='unsupported'?'Найди конкретную игру':'Подтверждённых предложений нет'}</h3><p>${dealFilter==='steep'&&products.length?'Попробуй показать все скидки.':'Найди игру по названию, проверь регион или обнови витрину.'}</p>${discovery?.status==='error'?'<button class="secondary" data-price="deals">Повторить загрузку</button>':discovery?.status==='unsupported'?'<button class="secondary" data-price="focusSearch">Найти игру</button>':''}</div>`}
  ${discovery?.fxDate?`<p class="note">≈ Пересчёт в рубли и тенге по курсу ЦБ на ${esc(discovery.fxDate)}. Оплата в валюте магазина.</p>`:''}${discovery?.fxError?`<p class="note">${esc(discovery.fxError)}</p>`:''}</section>`;
 }

 function officialCard(s){const o=s?.offers?.[0];return `<section class="price-official"><div class="price-source-title">${mark(selected.store)}<div><p>ЦЕНА МАГАЗИНА</p><h3>${esc(names[selected.store]||selected.store)}</h3></div><span class="price-region-pill">${esc(country(o?.country||comparison?.country||account().country))}</span></div>${o?`<p class="price-official-name">${esc(o.name)}</p><p class="price-native">${original(o)} <small>исходная цена</small></p>${pair(o)}<p class="price-checked">Проверено ${esc(when(o.checkedAt))} · ${esc(o.currency||'бесплатная игра')}</p><p class="note">${esc(o.note)}</p>`:`<div class="price-no-quote"><strong>—</strong><p>${esc(s?.message||'Нет данных')}</p></div>`}<div class="price-source-bottom"><span>${o?.provider==='EGData'?'Данные EGData · проверь в Epic':'Непосредственно из витрины'}</span>${external(s?.url,'В магазин')}</div></section>`;}
 function offerCard(o){const s=S();return `<article class="market-offer"><div class="market-offer-copy">${s.marketTrigger?s.marketTrigger(comparison,o):''}<div class="market-tags"><span>${o.kind==='gift'?'Подарок':'Ключ'}</span><span>${esc(o.platform||'Платформа не указана')}</span><span>${esc(o.region||'Уточни регион')}</span>${o.edition?`<span>${esc(o.edition)}</span>`:''}</div><h4>${esc(o.name)}</h4><p class="market-seller">${esc(o.seller||'Продавец не указан')} <span>· ${esc(o.sellerInfo||'')}</span></p><p class="market-note">${esc(o.note)}</p></div><div class="market-offer-price"><strong>${original(o)}</strong>${pair(o,true)}${external(o.url,'К предложению')}<span>${esc(when(o.checkedAt))}</span></div></article>`;}
 function comparePane(){const data=comparison,market=data?.sources?.find(s=>s.source===activeMarket),s=S();return `
  <div class="price-compare-top"><button class="quiet" data-price="back">← К поиску</button><button class="quiet" data-price="findEdition">Найти точное издание</button><button class="quiet" data-price="settings">Регион: ${esc(country(data?.country||account().country))} ⚙</button></div>
  <div class="price-game-heading">${selected.image&&/^(https:|file:|assets\/)/.test(selected.image)?`<img src="${esc(selected.image)}" alt="">`:mark(selected.store)}<div><p class="eyebrow">СРАВНЕНИЕ ЦЕН</p><h2 id="modal-title">${esc(selected.name)}</h2><p>${esc(names[selected.store]||selected.store)} · ${esc(country(data?.country||account().country))}</p></div><button class="secondary" data-price="refresh" ${busy?'disabled':''}>↻ Обновить</button></div>
  ${copies.length>1?`<label class="price-copy-label">Копия для сравнения <select id="price-copy" aria-label="Копия для сравнения">${copies.map(c=>`<option value="${esc(c.id)}" ${c.id===selected.id?'selected':''}>${esc(names[c.store]||c.store)}</option>`).join('')}</select></label>`:''}
  ${error?`<p class="price-error" role="alert">${esc(error)}</p>`:''}${busy?loading():data?`${s.rows?s.rows(data):''}<details class="sale-source-detail"><summary>Подробнее о цене ${esc(names[selected.store]||selected.store)}</summary>${officialCard(data.sources.find(x=>x.source===selected.store))}</details>
  <section class="price-market"><div class="price-results-heading"><h3>На других площадках</h3><span>Ключи и подарки</span></div><div class="market-tabs">${['funpay','plati'].map(st=>`<button data-price-market="${st}" class="${st===activeMarket?'active':''}">${mark(st)}${names[st]}<small>${data.sources.find(x=>x.source===st)?.offers?.length||'—'}</small></button>`).join('')}</div><p class="price-market-description">${esc(market?.message||'Нет данных')}</p>${market?.offers?.length?market.offers.map(offerCard).join(''):`<div class="price-market-empty"><span>⌕</span><p>Подходящих предложений нет.<br>Можно проверить площадку вручную.</p></div>`}<div class="price-source-bottom"><span>${market?.excluded?`Отфильтровано ${market.excluded} неподходящих или неясных лотов`:'Данные площадки, условия продавца'}</span>${external(market?.url,'Открыть площадку')}</div></section>
  <div class="price-fx"><span>≈</span><div><strong>${data.fxDate?'Пересчёт по курсу ЦБ РФ на '+esc(data.fxDate):'Пересчёт недоступен'}</strong><p>${esc(data.fxError||'Это ориентир в рублях и тенге, а не вторая региональная цена. Комиссии и курс оплаты могут отличаться.')}</p></div>${external(data.fxUrl,'Курс')}</div>
  <details class="price-source-settings"><summary>Уточнить раздел FunPay для этой игры</summary><p class="note">Вставь ссылку на раздел ключей игры. Проверка названия, типа товара и региона продолжит работать.</p><div class="price-url-input"><input id="price-funpay-url" aria-label="Раздел ключей FunPay" placeholder="https://funpay.com/lots/номер/" value="${esc(config.funpaySections?.[normalized(selected.name)]||'')}"><button class="secondary" data-price="saveFunpay">Сохранить</button></div></details>`:''}`;}
 function settingsPane(){return `<div class="price-settings-heading"><h2 id="modal-title">Регион и цены</h2><p class="intro">${config.accounts?.steam?.country?'Общий регион витрин: '+esc(country(config.accounts.steam.country))+' — из настройки Steam.':'Укажи регион Steam один раз — он станет общим для остальных витрин.'} Для магазина с другой страной задай исключение ниже. Это регион поиска цен, а не подтверждение страны аккаунта. Валюта берётся из ответа магазина.</p></div><datalist id="price-countries">${Object.entries(countries).map(([v,n])=>`<option value="${v}">${n}</option>`).join('')}</datalist><datalist id="price-currencies">${['RUB','KZT','USD','EUR','BYN','UAH','TRY','PLN','GBP'].map(v=>`<option value="${v}"></option>`).join('')}</datalist>${error?`<p class="price-error" role="alert">${esc(error)}</p>`:''}<form id="price-settings-form"><div class="price-account-head"><span>Магазин</span><span>Страна · RU, KZ…</span><span>Валюта · необязательно</span></div>${Object.keys(names).slice(0,6).map(s=>{const a=(settingsDraft||config.accounts)?.[s]||{};return `<div class="price-account-row"><div>${mark(s)}<span>${names[s]}${!automaticStores.includes(s)?'<small>Цены пока не подключены</small>':['ea','ubisoft'].includes(s)?'<small>Публичная витрина · регион проверяется</small>':''}</span></div><input data-price-country="${s}" aria-label="Страна ${names[s]}" list="price-countries" placeholder="${s==='steam'?'Общий регион':esc(config.accounts?.steam?.country||'Как в Steam')}" maxlength="2" pattern="[A-Za-z]{2}" value="${esc(a.country||'')}"><input data-price-currency="${s}" aria-label="Валюта ${names[s]}" list="price-currencies" placeholder="Авто" maxlength="3" pattern="[A-Za-z]{3}" value="${esc(a.currency||'')}"></div>`;}).join('')}<p class="note">GOG: при пустой валюте запрашивается USD. Если магазин вернёт другую валюту, Orbit покажет её явно. Пересчёт в рубли и тенге показывается рядом с ценой магазина, когда курс доступен.</p><div class="price-settings-bottom"><button type="button" class="quiet" data-price="settingsBack">← Назад</button><button class="primary">Сохранить</button></div></form>`;}
 function draw(){
  if(!api||!api.isOpen())return;
  S().close?.();
  const input=$('#price-query'),keepFocus=document.activeElement===input,caret=input?.selectionStart;
  const reset=page!==lastPage;lastPage=page;
  try{
   api.modal('prices',`<div class="prices-body">${page==='settings'?settingsPane():page==='compare'?comparePane():searchPane()}</div>`,true);
   if(reset)$('#modal').scrollTop=0;
   if(keepFocus&&page==='search'){$('#price-query')?.focus({preventScroll:true});$('#price-query')?.setSelectionRange(caret,caret);}
  }catch(err){
   console.error('Prices draw error:',err);
   api.modal('prices',`<div class="prices-body"><p class="price-error" role="alert">Ошибка интерфейса магазина: ${esc(err?.message||err)}</p><button class="quiet" data-price="back">← Назад</button></div>`,true);
  }
 }
 function resume(){
  if(page==='compare'&&selected){if(comparison)draw();else refresh(false);}
  else{page='search';selected=null;if(results||discovery)draw();else if(query.trim().length>=3)search(false);else discover(false);}
 }
 function receive(m){
  if(!String(m.kind||'').startsWith('prices'))return false;
  if(typeof m.requestId==='string'&&m.requestId.startsWith('suggest-')){
   if(m.kind==='pricesSearchResult'&&suggestIsOpen&&m.requestId==='suggest-'+suggestSeq&&$('#price-query')?.value.trim()===suggestQuery)renderSuggestions(m.data?.products||[]);
   return true;
  }
  if(m.kind==='pricesSettingsError'){pendingSettings=false;error=m.message;draw();return true;}
  if(m.kind==='pricesSettings'){
   config=m.settings||{accounts:{}};if(Array.isArray(m.automaticStores))automaticStores=m.automaticStores.filter(s=>names[s]);
   if(pendingSettings){pendingSettings=false;settingsDraft=null;error='';page=settingsBack;comparison=null;results=null;discovery=null;if(api.isOpen())resume();}
   else if(api.isOpen()&&document.activeElement?.id!=='price-query')draw();
   return true;
  }
  if(m.requestId!==expected||!api.isOpen())return true;
  clearTimeout(requestTimer);busy=false;closeSuggestions();
  if(m.kind==='pricesError')error=m.message;
  else if(m.kind==='pricesSearchResult'){results={...m.data,url:m.url};page='search';}
  else if(m.kind==='pricesDiscovery'){discovery=m.data;results=null;page='search';}
  else if(m.kind==='pricesResult'){comparison=m.data;page='compare';}
  draw();return true;
 }
 function closed(){S().close?.();if(!api)return;closeSuggestions();pendingSettings=false;cancelMain();}
 document.addEventListener('input',e=>{if(e.target.id==='price-query')handleSuggest(e.target.value);});
 document.addEventListener('keydown',e=>{
  if(e.target.id!=='price-query')return;
  const items=Array.from(document.querySelectorAll('.price-suggest-item'));
  if(e.key==='Escape'&&suggestIsOpen){e.preventDefault();e.stopImmediatePropagation();closeSuggestions();return;}
  if((e.key==='ArrowDown'||e.key==='ArrowUp')&&items.length){
   e.preventDefault();activeSuggestIdx=activeSuggestIdx<0?(e.key==='ArrowDown'?0:items.length-1):(activeSuggestIdx+(e.key==='ArrowDown'?1:-1)+items.length)%items.length;
   items.forEach((it,i)=>{it.classList.toggle('active',i===activeSuggestIdx);it.setAttribute('aria-selected',String(i===activeSuggestIdx));});
   e.target.setAttribute('aria-activedescendant',items[activeSuggestIdx].id);items[activeSuggestIdx].scrollIntoView({block:'nearest'});
  }else if(e.key==='Enter'&&activeSuggestIdx>=0&&items[activeSuggestIdx]){e.preventDefault();items[activeSuggestIdx].click();}
 },true);
 document.addEventListener('submit',e=>{
  if(e.target.id==='price-search-form'){e.preventDefault();query=$('#price-query').value.trim();source=$('#price-store').value;closeSuggestions();search();}
  if(e.target.id==='price-settings-form'){
   e.preventDefault();if(pendingSettings)return;const accounts={};
   document.querySelectorAll('[data-price-country]').forEach(i=>{const st=i.dataset.priceCountry;accounts[st]={country:i.value.trim().toUpperCase(),currency:document.querySelector(`[data-price-currency="${st}"]`).value.trim().toUpperCase()};});
   cancelMain();settingsDraft=accounts;pendingSettings=true;e.target.querySelector('.primary').disabled=true;api.send('pricesSaveSettings',{accounts});
  }
 });
 document.addEventListener('change',e=>{
  if(e.target.id==='price-store'){query=$('#price-query').value;source=e.target.value;selected=null;results=null;discovery=null;error='';dealFilter='all';if(query.trim().length>=3)search(false);else discover(false);}
  if(e.target.id==='price-copy'){const g=copies.find(c=>c.id===e.target.value);if(g){selected={id:g.id,store:g.store,name:g.name,image:g.background||g.cover};refresh(false);}}
 });
 document.addEventListener('error',e=>{if(e.target.tagName==='IMG'&&e.target.closest('.prices-body')){e.target.hidden=true;}},true);
 document.addEventListener('click',e=>{
  const b=e.target.closest('button');if(!b||!api?.isOpen())return;const d=b.dataset;
  if(d.priceSuggest){const p=suggestMap.get(d.priceSuggest);closeSuggestions();if(p){selected=p;copies=[];page='compare';comparison=null;refresh(false);}return;}
  if(!e.target.closest('#price-search-form'))closeSuggestions();
  if(d.storeTab){storeTab=d.storeTab;draw();return;}
  if(d.dealFilter){dealFilter=d.dealFilter;draw();return;}
  if(d.priceLibrary){const g=(window.Orbit?.library()||[]).find(x=>x.id===d.priceLibrary);if(g){selected={id:g.id,store:g.store,name:g.name,image:g.cover||g.background};copies=[];page='compare';comparison=null;refresh(false);}return;}
  if(d.price==='clearSearch'){query='';results=null;error='';storeTab='deals';closeSuggestions();if(!discovery)discover(false);else{cancelMain();draw();}return;}
  if(d.priceUrl){api.send('pricesOpen',{url:d.priceUrl});return;}
  if(d.priceProduct){const p=[...(results?.products||[]),...(discovery?.products||[])].find(x=>x.token===d.priceProduct);if(p){selected=p;copies=[];page='compare';comparison=null;refresh(false);}return;}
  if(d.priceMarket){activeMarket=d.priceMarket;draw();return;}
  switch(d.price){
   case'focusSearch':$('#price-query')?.focus();break;
   case'deals':discover(true);break;
   case'back':cancelMain();selected=null;page='search';error='';resume();break;
   case'findEdition':source=selected.store;query=selected.name;selected=null;page='search';discovery=null;search(false);break;
   case'refresh':refresh();break;
   case'settings':error='';settingsDraft=null;if($('#price-query'))query=$('#price-query').value;settingsBack=page;cancelMain();page='settings';draw();break;
   case'settingsBack':settingsDraft=null;pendingSettings=false;page=settingsBack;error='';resume();break;
   case'cancel':cancelMain();error='Проверка остановлена. Можно повторить запрос.';draw();break;
   case'saveFunpay':api.send('pricesSaveFunpay',{url:$('#price-funpay-url').value});break;
  }
 });
 document.addEventListener('pointerdown',e=>{if(!e.target.closest('#price-search-form'))closeSuggestions();});
 window.OrbitPrices={init:a=>api=a,show,showSettings:()=>{show(undefined,false);settingsBack='search';page='settings';draw();},showGame,showProduct,receive,closed};
})();
