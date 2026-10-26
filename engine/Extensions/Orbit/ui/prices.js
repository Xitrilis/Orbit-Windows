(()=>{
 'use strict';
 const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])), $=s=>document.querySelector(s);
 const names={steam:'Steam',epic:'Epic Games',gog:'GOG',ea:'EA app',ubisoft:'Ubisoft Connect',battlenet:'Battle.net',funpay:'FunPay',plati:'Plati.Market'};
 const marks={steam:'S',epic:'E',gog:'gog',ea:'EA',ubisoft:'U',battlenet:'B',funpay:'F',plati:'P'};
 const countries={RU:'Россия',KZ:'Казахстан',BY:'Беларусь',UA:'Украина',TR:'Турция',US:'США',DE:'Германия',PL:'Польша',GB:'Великобритания',GE:'Грузия',AM:'Армения'};
 const icon=n=>typeof window!=='undefined'&&window.OrbitIcon?window.OrbitIcon(n):(n==='search'?'<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 5 5"/></svg>':n==='settings'?'<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="m9 3-.5 2.5-2 .9L4 6l-1.5 3 1.8 1.8v2.4L2.5 15 4 18l2.5-.4 2 .9L9 21h6l.5-2.5 2-.9 2.5.4 1.5-3-1.8-1.8v-2.4L21.5 9 20 6l-2.5.4-2-.9L15 3Z"/></svg>':'');
 const dot=s=>typeof window!=='undefined'&&window.OrbitDot?window.OrbitDot(s):`<i class="store-dot ${esc(s)}"></i>`;
 const S=()=>typeof window!=='undefined'?(window.OrbitSales||{}):{};
 let api,config={accounts:{},funpaySections:{}},page='search',source='steam',query='',selected=null,storeTab='deals',dealFilter='all',copies=[],results=null,comparison=null,busy=false,error='',activeMarket='funpay',request=0,expected='',settingsBack='search',pendingSettings=false,lastPage='',settingsDraft=null,discovery=null;
 const account=()=>{const a=config.accounts?.[selected?.store||source];if(a&&a.country)return a;return{country:'RU',currency:'RUB'};},country=c=>countries[c]||c||'Регион не выбран';
 const money=(v,c)=>v==null?'—':new Intl.NumberFormat('ru-RU',{style:'currency',currency:c,currencyDisplay:'narrowSymbol',minimumFractionDigits:0,maximumFractionDigits:2}).format(v);
 const when=t=>t?new Date(t).toLocaleString('ru-RU',{day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit'}):'—';
 const mark=s=>`<b class="price-mark ${esc(s)}">${esc(marks[s]||s)}</b>`;
 const external=(url,label='Открыть источник')=>url?`<button class="price-link" data-price-url="${esc(url)}">${label} ↗</button>`:'';
 const normalized=s=>String(s||'').replace(/[™®]/g,'').normalize('NFKC').toLowerCase().replace(/[^\p{L}\p{N}]+/gu,' ').trim();
 function requestData(kind,p){expected=String(++request);busy=true;error='';draw();api.send(kind,{...p,requestId:expected});}
 function show(store,load=true){pendingSettings=false;settingsDraft=null;if(names[store]&&store!=='funpay'&&store!=='plati')source=store;page='search';selected=null;comparison=null;error='';busy=false;api.modal('prices','');api.send('pricesSettings');draw();if(load)discover(false);}
 function discover(force=false){page='search';selected=null;comparison=null;results=null;discovery=null;requestData('pricesDiscover',{store:source,force});}
 function showGame(game,otherCopies=[]){pendingSettings=false;selected={id:game.id,store:game.store,name:game.name,image:game.background||game.cover};copies=otherCopies;comparison=null;page='compare';api.modal('prices','');api.send('pricesSettings');refresh(false);}
 function showProduct(g){pendingSettings=false;selected={token:g.priceToken,id:g.appId,store:g.store||'steam',name:g.name,image:g.image};copies=[];comparison=null;page='compare';api.modal('prices','');api.send('pricesSettings');refresh(false);}
 function refresh(force=true){if(!selected)return;comparison=null;requestData('pricesCompare',{...(selected.token?{token:selected.token}:{id:selected.id}),force});}
 function search(force=false){if(query.trim().length<3){error='Введи хотя бы 3 символа названия.';draw();return;}page='search';selected=null;results=null;requestData('pricesSearch',{store:source||'steam',query:query.trim(),force});}
 function loading(){return '<div class="price-loading" role="status"><span class="price-spinner"></span><strong>Проверяем источники</strong><p>Получаем цены, условия предложений и курс валют.</p><button class="quiet" data-price="cancel">Остановить</button></div>';}
 function pair(o,small=false){return `<div class="price-pair ${small?'small':''}"><div><span>${o.currency==='RUB'?'ЦЕНА В РУБЛЯХ':'ПЕРЕСЧЁТ В РУБЛИ'}</span><strong>${o.rub!=null&&o.currency!=='RUB'?'≈ ':''}${money(o.rub,'RUB')}</strong></div><div><span>${o.currency==='KZT'?'ЦЕНА В ТЕНГЕ':'ПЕРЕСЧЁТ В ТЕНГЕ'}</span><strong>${o.kzt!=null&&o.currency!=='KZT'?'≈ ':''}${money(o.kzt,'KZT')}</strong></div></div>`;}
 function original(o){if(o.free)return 'Бесплатно';return `${o.from?'От ':''}${o.currency?money(o.amount,o.currency):'—'}${o.originalAmount>o.amount?` <del>${money(o.originalAmount,o.currency)}</del>`:''}`;}
 function searchPane(){
  const a=account();
  const libGames=window.Orbit?.library()||[];
  return `
  <div class="store-topbar">
    <form id="price-search-form" class="price-search-unified">
      <span class="price-search-lens">${icon('search')}</span>
      <input id="price-query" placeholder="Поиск игры по каталогу... (например, Cyberpunk, Witcher, GTA)" maxlength="150" value="${esc(query)}" autocomplete="off">
      ${query?`<button type="button" class="price-clear-btn" data-price="clearSearch" title="Очистить поиск">✕</button>`:''}
      <button class="primary price-submit-btn" ${busy?'disabled':''}>Найти</button>
    </form>
    <button class="price-region-chip" data-price="settings" title="Сменить регион и валюту">
      <span class="price-live-dot"></span>
      <span>${esc(country(a.country))}</span>
      <small>≈ RUB</small>
      ${icon('settings')}
    </button>
  </div>
  ${!results?`<div class="store-nav-tabs">
    <button class="store-tab-btn ${storeTab==='deals'?'active':''}" data-store-tab="deals">🔥 Горячие скидки</button>
    <button class="store-tab-btn ${storeTab==='calendar'?'active':''}" data-store-tab="calendar">📅 Календарь распродаж</button>
    <button class="store-tab-btn ${storeTab==='library'?'active':''}" data-store-tab="library">🎮 Моя библиотека (${libGames.length})</button>
  </div>`:''}
  ${!config.accounts?.[source]?.country?'<p class="price-setup">Укажи страну аккаунта в настройках цен.</p>':''}
  ${error?`<p class="price-error" role="alert">${esc(error)}</p>`:''}
  ${busy?loading():results?`
    <div class="price-results-heading">
      <div>
        <h3>Найдено ${results.products?.length||0} предложений</h3>
        <span>по запросу «${esc(query)}» · ${esc(country(results.country||a.country))}</span>
      </div>
      <button class="quiet" data-price="clearSearch">← Вернуться к скидкам</button>
    </div>
    ${results.products?.length?`<div class="price-search-results">${results.products.map(p=>`
      <button class="price-product" data-price-product="${esc(p.token)}">
        ${p.image&&/^https:\/\//i.test(p.image)?`<img src="${esc(p.image)}" alt="" loading="lazy">`:'<div class="price-cover-fallback">'+mark(source)+'</div>'}
        <div>
          <small>${esc(p.productType||'Игра')}</small>
          <h3>${esc(p.name)}</h3>
          <p class="price-product-native">${p.preview?original(p.preview):'Цена недоступна'}</p>
          ${p.preview?pair(p.preview,true):''}
          <span class="price-product-action">Сравнить цены <b>→</b></span>
        </div>
      </button>`).join('')}</div>`:`<div class="price-empty"><span>🔍</span><h3>Ничего не найдено</h3><p>Попробуй изменить запрос или найти игру в другом написании.</p></div>`}
    ${results.fxError?`<p class="note">${esc(results.fxError)}</p>`:''}
    ${external(results.url,'Открыть в браузере')}
  `:storeTab==='library'?`
    <section class="store-library-section">
      <div class="price-results-heading"><h3>Игры из твоей коллекции</h3><span>Нажми на игру для сравнения цен и скидок</span></div>
      <div class="store-library-grid">
        ${libGames.map(g=>`
          <button class="store-lib-card" data-price-library="${esc(g.id)}">
            ${g.cover?`<img src="${esc(g.cover)}" alt="" loading="lazy">`:`<div class="store-lib-fallback">${esc((g.name||'G')[0])}</div>`}
            <div>
              <strong>${esc(g.name)}</strong>
              <small>${dot(g.store)} ${esc(names[g.store]||g.store)}</small>
            </div>
            <span class="store-lib-action">Цены →</span>
          </button>`).join('')}
      </div>
    </section>
  `:storeTab==='calendar'?`${(discovery&&S().calendar)?S().calendar(discovery):loading()}`:
  discovery?discoveryPane():loading()
  }`;
 }

 function discoveryPane(){
  const sales=S(), products=discovery?.products||[], pct=sales.pct||(()=>0);
  const filtered=products.filter(p=>dealFilter!=='steep'||pct(p.preview)>=60);
  return `${sales.calendar?sales.calendar(discovery):''}<section class="sale-featured">
   <div class="price-results-heading"><div><h3>Витрина ${esc(names[source])}</h3><span>${esc(discovery?.message||'Цены появятся после ответа магазина.')}</span></div><button class="quiet" data-price="deals">↻ Обновить</button></div>
   <div class="deal-filter-bar"><button class="deal-filter-btn ${dealFilter==='all'?'active':''}" data-deal-filter="all">Все предложения (${products.length})</button><button class="deal-filter-btn ${dealFilter==='steep'?'active':''}" data-deal-filter="steep">Скидки от −60%</button></div>
   ${filtered.length?`<div class="price-search-results">${filtered.map(p=>{const o=p.preview,discount=pct(o);return `<button class="price-product" data-price-product="${esc(p.token)}">
    ${p.image&&/^https:\/\//i.test(p.image)?`<img src="${esc(p.image)}" alt="" loading="lazy">`:`<div class="price-cover-fallback">${mark(source)}</div>`}
    <div><small>${esc(p.productType||'Игра')}</small><h3>${esc(p.name)}</h3>
     <div class="deal-pricing">${discount>0?`<span class="deal-flame-badge">−${discount}%</span>`:''}<div class="deal-price-col">
     ${o&&o.rub!=null?`<strong class="deal-new">${o.currency!=='RUB'?'≈ ':''}${money(o.rub,'RUB')}</strong>`:''}
     <p class="price-product-native">${o?original(o):'Цена недоступна'}</p></div></div>
     ${o?`<p class="price-checked">${esc(names[source])} · ${esc(country(discovery?.country||account().country))} · ${esc(when(o.checkedAt))}</p>`:''}
     <span class="price-product-action">Проверить цены в магазинах →</span></div></button>`;}).join('')}</div>`:'<p class="price-empty">Подтверждённых предложений по этому фильтру нет. Попробуй поиск по названию или обнови витрину.</p>'}
   ${discovery?.fxDate?`<p class="note">≈ Пересчёт в рубли по курсу ЦБ на ${esc(discovery.fxDate)}. Оплата в валюте магазина.</p>`:''}
   ${discovery?.fxError?`<p class="note">${esc(discovery.fxError)}</p>`:''}
  </section>`;
 }

 function officialCard(s){const o=s?.offers?.[0];return `<section class="price-official"><div class="price-source-title">${mark(selected.store)}<div><p>ЦЕНА МАГАЗИНА</p><h3>${esc(names[selected.store]||selected.store)}</h3></div><span class="price-region-pill">${esc(country(comparison?.country||account().country))}</span></div>${o?`<p class="price-official-name">${esc(o.name)}</p><p class="price-native">${original(o)} <small>исходная цена</small></p>${pair(o)}<p class="price-checked">Проверено ${esc(when(o.checkedAt))} · ${esc(o.currency||'бесплатная игра')}</p><p class="note">${esc(o.note)}</p>`:`<div class="price-no-quote"><strong>—</strong><p>${esc(s?.message||'Нет данных')}</p></div>`}<div class="price-source-bottom"><span>Непосредственно из витрины</span>${external(s?.url,'В магазин')}</div></section>`;}
 function offerCard(o){const s=S();return `<article class="market-offer"><div class="market-offer-copy">${s.marketTrigger?s.marketTrigger(comparison,o):''}<div class="market-tags"><span>${o.kind==='gift'?'Подарок':'Ключ'}</span><span>${esc(o.platform||'Платформа не указана')}</span><span>${esc(o.region||'Уточни регион')}</span>${o.edition?`<span>${esc(o.edition)}</span>`:''}</div><h4>${esc(o.name)}</h4><p class="market-seller">${esc(o.seller||'Продавец не указан')} <span>· ${esc(o.sellerInfo||'')}</span></p><p class="market-note">${esc(o.note)}</p></div><div class="market-offer-price"><strong>${original(o)}</strong><small>${o.currency==='RUB'?`≈ ${money(o.kzt,'KZT')}`:o.currency==='KZT'?`≈ ${money(o.rub,'RUB')}`:`${money(o.rub,'RUB')} · ${money(o.kzt,'KZT')}`}</small>${external(o.url,'К предложению')}<span>${esc(when(o.checkedAt))}</span></div></article>`;}
 function comparePane(){const data=comparison,market=data?.sources?.find(s=>s.source===activeMarket),s=S();return `
  <div class="price-compare-top"><button class="quiet" data-price="back">← К поиску</button><button class="quiet" data-price="settings">Регион: ${esc(country(data?.country||account().country))} ⚙</button></div>
  <div class="price-game-heading">${selected.image&&/^(https:|file:|assets\/)/.test(selected.image)?`<img src="${esc(selected.image)}" alt="">`:mark(selected.store)}<div><p class="eyebrow">СРАВНЕНИЕ ЦЕН</p><h2 id="modal-title">${esc(selected.name)}</h2><p>${esc(names[selected.store]||selected.store)} · ${esc(country(data?.country||account().country))}</p></div><button class="secondary" data-price="refresh" ${busy?'disabled':''}>↻ Обновить</button></div>
  ${copies.length>1?`<label class="price-copy-label">Копия для сравнения <select id="price-copy" aria-label="Копия для сравнения">${copies.map(c=>`<option value="${esc(c.id)}" ${c.id===selected.id?'selected':''}>${esc(names[c.store]||c.store)}</option>`).join('')}</select></label>`:''}
  ${error?`<p class="price-error" role="alert">${esc(error)}</p>`:''}${busy?loading():data?`${s.rows?s.rows(data):''}<details class="sale-source-detail"><summary>Подробнее о цене ${esc(names[selected.store]||selected.store)}</summary>${officialCard(data.sources.find(x=>x.source===selected.store))}</details>
  <section class="price-market"><div class="price-results-heading"><h3>На других площадках</h3><span>Ключи и подарки</span></div><div class="market-tabs">${['funpay','plati'].map(st=>`<button data-price-market="${st}" class="${st===activeMarket?'active':''}">${mark(st)}${names[st]}<small>${data.sources.find(x=>x.source===st)?.offers?.length||'—'}</small></button>`).join('')}</div><p class="price-market-description">${esc(market?.message||'Нет данных')}</p>${market?.offers?.length?market.offers.map(offerCard).join(''):`<div class="price-market-empty"><span>⌕</span><p>Подходящих предложений нет.<br>Можно проверить площадку вручную.</p></div>`}<div class="price-source-bottom"><span>${market?.excluded?`Отфильтровано ${market.excluded} неподходящих или неясных лотов`:'Данные площадки, условия продавца'}</span>${external(market?.url,'Открыть площадку')}</div></section>
  <div class="price-fx"><span>≈</span><div><strong>${data.fxDate?'Пересчёт по курсу ЦБ РФ на '+esc(data.fxDate):'Пересчёт недоступен'}</strong><p>${esc(data.fxError||'Это ориентир в рублях и тенге, а не вторая региональная цена. Комиссии и курс оплаты могут отличаться.')}</p></div>${external(data.fxUrl,'Курс')}</div>
  <details class="price-source-settings"><summary>Уточнить раздел FunPay для этой игры</summary><p class="note">Вставь ссылку на раздел ключей игры. Проверка названия, типа товара и региона продолжит работать.</p><div class="price-url-input"><input id="price-funpay-url" aria-label="Раздел ключей FunPay" placeholder="https://funpay.com/lots/номер/" value="${esc(config.funpaySections?.[normalized(selected.name)]||'')}"><button class="secondary" data-price="saveFunpay">Сохранить</button></div></details>`:''}`;}
 function settingsPane(){return `<div class="price-settings-heading"><p class="eyebrow">АККАУНТЫ / ЦЕНЫ</p><h2 id="modal-title">Твой регион.<br>Твоя цена.</h2><p class="intro">Укажи страну каждого аккаунта. Orbit не меняет страну в магазинах и не определяет её по IP. Валюта нужна для сверки с ответом магазина.</p></div><datalist id="price-countries">${Object.entries(countries).map(([v,n])=>`<option value="${v}">${n}</option>`).join('')}</datalist><datalist id="price-currencies">${['RUB','KZT','USD','EUR','BYN','UAH','TRY','PLN','GBP'].map(v=>`<option value="${v}"></option>`).join('')}</datalist>${error?`<p class="price-error" role="alert">${esc(error)}</p>`:''}<form id="price-settings-form"><div class="price-account-head"><span>Магазин</span><span>Страна · RU, KZ…</span><span>Валюта · RUB, KZT…</span></div>${Object.keys(names).slice(0,6).map(s=>{const a=(settingsDraft||config.accounts)?.[s]||{};return `<div class="price-account-row"><div>${mark(s)}<span>${names[s]}${!['steam','epic','gog'].includes(s)?'<small>Цены пока не подключены</small>':''}</span></div><input data-price-country="${s}" aria-label="Страна ${names[s]}" list="price-countries" placeholder="Не выбрана" maxlength="2" pattern="[A-Za-z]{2}" value="${esc(a.country||'')}"><input data-price-currency="${s}" aria-label="Валюта ${names[s]}" list="price-currencies" placeholder="Авто" maxlength="3" pattern="[A-Za-z]{3}" value="${esc(a.currency||'')}"></div>`;}).join('')}<p class="note">GOG: при пустой валюте запрашивается USD. Если магазин вернёт другую валюту, Orbit покажет её явно. Для всех предложений дополнительно рассчитываются ₽ и ₸.</p><div class="price-settings-bottom"><button type="button" class="quiet" data-price="settingsBack">← Назад</button><button class="primary">Сохранить</button></div></form>`;}
 function draw(){
  if(!api||!api.isOpen())return;
  S().close?.();
  const reset=page!==lastPage;lastPage=page;
  try{
   api.modal('prices',`<div class="prices-body">${page==='settings'?settingsPane():page==='compare'?comparePane():searchPane()}</div>`,true);
   if(reset)$('#modal').scrollTop=0;
  }catch(err){
   console.error('Prices draw error:',err);
   api.modal('prices',`<div class="prices-body"><p class="price-error" role="alert">Ошибка интерфейса магазина: ${esc(err?.message||err)}</p><button class="quiet" data-price="back">← Назад</button></div>`,true);
  }
 }
 function receive(m){if(m.kind==='pricesSettingsError'){pendingSettings=false;error=m.message;draw();return true;}if(m.kind==='pricesSettings'){config=m.settings||{accounts:{}};if(pendingSettings){pendingSettings=false;settingsDraft=null;error='';page=settingsBack;comparison=null;results=null;discovery=null;if(page==='compare'&&selected){refresh();return true;}}if(api.isOpen())draw();return true;}if(!m.kind.startsWith('prices'))return false;if(m.requestId!==expected||!api.isOpen())return true;busy=false;if(m.kind==='pricesError')error=m.message;else if(m.kind==='pricesSearchResult'){results={...m.data,url:m.url};page='search';}else if(m.kind==='pricesDiscovery'){discovery=m.data;results=null;page='search';}else if(m.kind==='pricesResult'){comparison=m.data;page='compare';}draw();return true;}
 function closed(){S().close?.();if(!api)return;pendingSettings=false;expected=String(++request);busy=false;api.send('pricesCancel');}
 document.addEventListener('submit',e=>{if(e.target.id==='price-search-form'){e.preventDefault();query=$('#price-query').value.trim();if($('#price-store'))source=$('#price-store').value;search();}if(e.target.id==='price-settings-form'){e.preventDefault();const accounts={};document.querySelectorAll('[data-price-country]').forEach(i=>{const s=i.dataset.priceCountry;accounts[s]={country:i.value.trim().toUpperCase(),currency:document.querySelector(`[data-price-currency="${s}"]`).value.trim().toUpperCase()};});expected=String(++request);busy=false;settingsDraft=accounts;pendingSettings=true;api.send('pricesSaveSettings',{accounts});}});
 document.addEventListener('change',e=>{if(e.target.id==='price-store'){query=$('#price-query').value;source=e.target.value;results=null;error='';discover(false);}if(e.target.id==='price-copy'){const g=copies.find(c=>c.id===e.target.value);if(g){selected={id:g.id,store:g.store,name:g.name,image:g.background||g.cover};refresh(false);}}});
 document.addEventListener('click',e=>{const b=e.target.closest('button');if(!b||!api?.isOpen())return;const d=b.dataset;
  if(d.storeTab){storeTab=d.storeTab;draw();return;}

   if(d.dealFilter){dealFilter=d.dealFilter;draw();return;}
  if(d.priceLibrary){const g=(window.Orbit?.library()||[]).find(x=>x.id===d.priceLibrary);if(g){selected={id:g.id,store:g.store,name:g.name,image:g.cover||g.background};copies=[];page='compare';comparison=null;refresh(false);}return;}
  if(d.price==='clearSearch'){query='';results=null;error='';if(!discovery)discover(false);else draw();return;}
  if(d.priceUrl){api.send('pricesOpen',{url:d.priceUrl});return;}
  if(d.priceProduct){const p=[...(results?.products||[]),...(discovery?.products||[])].find(x=>x.token===d.priceProduct);if(p){selected=p;copies=[];page='compare';comparison=null;refresh(false);}return;}
  if(d.priceMarket){activeMarket=d.priceMarket;draw();return;}
  switch(d.price){case'deals':discover(true);break;case'back':api.send('pricesCancel');expected=String(++request);busy=false;selected=null;page='search';error='';draw();break;case'refresh':refresh();break;case'settings':error='';settingsDraft=null;if($('#price-query'))query=$('#price-query').value;settingsBack=page;api.send('pricesCancel');expected=String(++request);busy=false;page='settings';draw();break;case'settingsBack':settingsDraft=null;pendingSettings=false;page=settingsBack;draw();break;case'cancel':api.send('pricesCancel');expected=String(++request);busy=false;error='Проверка остановлена. Можно повторить запрос.';draw();break;case'saveFunpay':api.send('pricesSaveFunpay',{url:$('#price-funpay-url').value});break;}
 });
 window.OrbitPrices={init:a=>api=a,show,showSettings:()=>{show(undefined,false);settingsBack='search';page='settings';draw();},showGame,showProduct,receive,closed};
})();