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
  const libGames=(window.data?.games||[]).slice(0,36);
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
      <small>${esc(a.currency||'RUB')}</small>
      ${icon('settings')}
    </button>
  </div>
  ${!results?`<div class="store-nav-tabs">
    <button class="store-tab-btn ${storeTab==='deals'?'active':''}" data-store-tab="deals">🔥 Горячие скидки</button>
    <button class="store-tab-btn ${storeTab==='calendar'?'active':''}" data-store-tab="calendar">📅 Календарь распродаж</button>
    <button class="store-tab-btn ${storeTab==='library'?'active':''}" data-store-tab="library">🎮 Моя библиотека (${libGames.length})</button>
  </div>`:''}
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
          <p class="price-product-native">${p.preview?original(p.preview):'Узнать цену'}</p>
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

 const CURATED_DEALS = [
  { name: 'Cyberpunk 2077', token: 'cp2077', productType: 'RPG / Открытый мир', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1091500/header.jpg', url: 'https://store.steampowered.com/app/1091500/', discount: 50, oldRub: 2999, rub: 1499, stores: [{ store: 'steam', price: '1 499 ₽', old: '2 999 ₽', discount: 50 }, { store: 'epic', price: '1 199 ₽', old: '2 999 ₽', discount: 60, cheaper: true, diff: 'на 300 ₽ дешевле' }], tags: ['compare', 'hits', 'steep'] },
  { name: "Baldur's Gate 3", token: 'bg3', productType: 'Партийная RPG / Хит года', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1086940/header.jpg', url: 'https://store.steampowered.com/app/1086940/', discount: 20, oldRub: 2499, rub: 1999, stores: [{ store: 'steam', price: '1 999 ₽', old: '2 499 ₽', discount: 20 }, { store: 'gog', price: '1 999 ₽', old: '2 499 ₽', discount: 20, note: 'DRM-Free' }], tags: ['compare', 'hits'] },
  { name: 'The Witcher 3: Wild Hunt — Complete Edition', token: 'witcher3', productType: 'Экшен-RPG / Шедевр', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/292030/header.jpg', url: 'https://store.steampowered.com/app/292030/', discount: 75, oldRub: 1999, rub: 499, stores: [{ store: 'steam', price: '499 ₽', old: '1 999 ₽', discount: 75 }, { store: 'epic', price: '399 ₽', old: '1 999 ₽', discount: 80, cheaper: true, diff: 'на 100 ₽ дешевле' }, { store: 'gog', price: '449 ₽', old: '1 999 ₽', discount: 77 }], tags: ['compare', 'hits', 'steep'] },
  { name: 'Red Dead Redemption 2', token: 'rdr2', productType: 'Вестерн / Открытый мир', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1174180/header.jpg', url: 'https://store.steampowered.com/app/1174180/', discount: 67, oldRub: 3999, rub: 1319, stores: [{ store: 'steam', price: '1 319 ₽', old: '3 999 ₽', discount: 67 }, { store: 'epic', price: '999 ₽', old: '3 999 ₽', discount: 75, cheaper: true, diff: 'на 320 ₽ дешевле' }], tags: ['compare', 'hits', 'steep'] },
  { name: 'Grand Theft Auto V: Premium Edition', token: 'gta5', productType: 'Экшен / Открытый мир', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/271590/header.jpg', url: 'https://store.steampowered.com/app/271590/', discount: 63, oldRub: 1999, rub: 749, stores: [{ store: 'steam', price: '749 ₽', old: '1 999 ₽', discount: 63 }, { store: 'epic', price: '699 ₽', old: '1 999 ₽', discount: 65, cheaper: true, diff: 'на 50 ₽ дешевле' }], tags: ['compare', 'hits', 'steep'] },
  { name: 'Elden Ring', token: 'eldenring', productType: 'Souls-like / RPG', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1245620/header.jpg', url: 'https://store.steampowered.com/app/1245620/', discount: 40, oldRub: 3999, rub: 2399, stores: [{ store: 'steam', price: '2 399 ₽', old: '3 999 ₽', discount: 40 }, { store: 'plati', price: '1 890 ₽', old: '3 999 ₽', discount: 52, cheaper: true, diff: 'ключ на 509 ₽ дешевле' }], tags: ['hits'] },
  { name: 'Hogwarts Legacy', token: 'hogwarts', productType: 'Приключения / RPG', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/990080/header.jpg', url: 'https://store.steampowered.com/app/990080/', discount: 70, oldRub: 3999, rub: 1199, stores: [{ store: 'steam', price: '1 499 ₽', old: '3 999 ₽', discount: 62 }, { store: 'epic', price: '1 199 ₽', old: '3 999 ₽', discount: 70, cheaper: true, diff: 'на 300 ₽ дешевле' }], tags: ['compare', 'hits', 'steep'] },
  { name: 'God of War', token: 'gow', productType: 'Экшен / Приключения', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1593500/header.jpg', url: 'https://store.steampowered.com/app/1593500/', discount: 60, oldRub: 3149, rub: 1259, stores: [{ store: 'steam', price: '1 574 ₽', old: '3 149 ₽', discount: 50 }, { store: 'epic', price: '1 259 ₽', old: '3 149 ₽', discount: 60, cheaper: true, diff: 'на 315 ₽ дешевле' }, { store: 'gog', price: '1 574 ₽', old: '3 149 ₽', discount: 50 }], tags: ['compare', 'hits', 'steep'] },
  { name: 'Resident Evil 4 Remake', token: 're4r', productType: 'Survival Horror / Экшен', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/2050650/header.jpg', url: 'https://store.steampowered.com/app/2050650/', discount: 50, oldRub: 3999, rub: 1999, stores: [{ store: 'steam', price: '1 999 ₽', old: '3 999 ₽', discount: 50 }, { store: 'funpay', price: '1 450 ₽', old: '3 999 ₽', discount: 63, cheaper: true, diff: 'подарок на 549 ₽ дешевле' }], tags: ['hits'] },
  { name: 'S.T.A.L.K.E.R. 2: Heart of Chornobyl', token: 'stalker2', productType: 'Шутер / Постапокалипсис', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1643320/header.jpg', url: 'https://store.steampowered.com/app/1643320/', discount: 15, oldRub: 2999, rub: 2499, stores: [{ store: 'steam', price: '2 499 ₽', old: '2 999 ₽', discount: 15 }, { store: 'gog', price: '2 499 ₽', old: '2 999 ₽', discount: 15, note: 'DRM-Free' }], tags: ['compare', 'hits'] },
  { name: 'Helldivers 2', token: 'helldivers2', productType: 'Кооперативный шутер', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/553850/header.jpg', url: 'https://store.steampowered.com/app/553850/', discount: 20, oldRub: 3499, rub: 2799, stores: [{ store: 'steam', price: '2 799 ₽', old: '3 499 ₽', discount: 20 }, { store: 'plati', price: '2 450 ₽', old: '3 499 ₽', discount: 30, cheaper: true, diff: 'на 349 ₽ дешевле' }], tags: ['hits'] },
  { name: 'Black Myth: Wukong', token: 'wukong', productType: 'Action-RPG / Хит', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/2358720/header.jpg', url: 'https://store.steampowered.com/app/2358720/', discount: 10, oldRub: 3899, rub: 3499, stores: [{ store: 'steam', price: '3 499 ₽', old: '3 899 ₽', discount: 10 }, { store: 'epic', price: '3 199 ₽', old: '3 899 ₽', discount: 18, cheaper: true, diff: 'на 300 ₽ дешевле' }], tags: ['compare', 'hits'] },
  { name: 'Dying Light 2 Stay Human', token: 'dl2', productType: 'Экшен / Зомби-паркур', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/534380/header.jpg', url: 'https://store.steampowered.com/app/534380/', discount: 60, oldRub: 2499, rub: 999, stores: [{ store: 'steam', price: '1 249 ₽', old: '2 499 ₽', discount: 50 }, { store: 'epic', price: '999 ₽', old: '2 499 ₽', discount: 60, cheaper: true, diff: 'на 250 ₽ дешевле' }], tags: ['compare', 'hits', 'steep'] },
  { name: 'Metro Exodus — Gold Edition', token: 'metroexodus', productType: 'Постапокалипсис / Шутер', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/412020/header.jpg', url: 'https://store.steampowered.com/app/412020/', discount: 80, oldRub: 1599, rub: 319, stores: [{ store: 'steam', price: '319 ₽', old: '1 599 ₽', discount: 80 }, { store: 'gog', price: '299 ₽', old: '1 599 ₽', discount: 81, cheaper: true, diff: 'на 20 ₽ дешевле' }, { store: 'epic', price: '319 ₽', old: '1 599 ₽', discount: 80 }], tags: ['compare', 'hits', 'steep'] },
  { name: 'Detroit: Become Human', token: 'detroit', productType: 'Интерактивное кино / Sci-Fi', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1222140/header.jpg', url: 'https://store.steampowered.com/app/1222140/', discount: 68, oldRub: 1628, rub: 520, stores: [{ store: 'steam', price: '651 ₽', old: '1 628 ₽', discount: 60 }, { store: 'epic', price: '520 ₽', old: '1 628 ₽', discount: 68, cheaper: true, diff: 'на 131 ₽ дешевле' }], tags: ['compare', 'steep'] },
  { name: 'DOOM Eternal', token: 'doometernal', productType: 'Драйвовый шутер', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/782330/header.jpg', url: 'https://store.steampowered.com/app/782330/', discount: 75, oldRub: 1999, rub: 499, stores: [{ store: 'steam', price: '499 ₽', old: '1 999 ₽', discount: 75 }, { store: 'gog', price: '499 ₽', old: '1 999 ₽', discount: 75, note: 'DRM-Free' }], tags: ['compare', 'steep'] },
  { name: 'Control Ultimate Edition', token: 'control', productType: 'Мистический экшен', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/870780/header.jpg', url: 'https://store.steampowered.com/app/870780/', discount: 80, oldRub: 1599, rub: 319, stores: [{ store: 'steam', price: '399 ₽', old: '1 599 ₽', discount: 75 }, { store: 'epic', price: '319 ₽', old: '1 599 ₽', discount: 80, cheaper: true, diff: 'на 80 ₽ дешевле' }, { store: 'gog', price: '399 ₽', old: '1 599 ₽', discount: 75 }], tags: ['compare', 'steep'] },
  { name: 'Days Gone', token: 'daysgone', productType: 'Зомби / Открытый мир', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1259420/header.jpg', url: 'https://store.steampowered.com/app/1259420/', discount: 75, oldRub: 3199, rub: 799, stores: [{ store: 'steam', price: '899 ₽', old: '3 199 ₽', discount: 72 }, { store: 'epic', price: '799 ₽', old: '3 199 ₽', discount: 75, cheaper: true, diff: 'на 100 ₽ дешевле' }], tags: ['compare', 'hits', 'steep'] },
  { name: "Marvel's Spider-Man Remastered", token: 'spiderman', productType: 'Супергеройский экшен', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1817070/header.jpg', url: 'https://store.steampowered.com/app/1817070/', discount: 50, oldRub: 3999, rub: 1999, stores: [{ store: 'steam', price: '1 999 ₽', old: '3 999 ₽', discount: 50 }, { store: 'epic', price: '1 699 ₽', old: '3 999 ₽', discount: 57, cheaper: true, diff: 'на 300 ₽ дешевле' }], tags: ['compare', 'hits'] },
  { name: 'Ghost of Tsushima Director’s Cut', token: 'tsushima', productType: 'Самурайский экшен', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/2215430/header.jpg', url: 'https://store.steampowered.com/app/2215430/', discount: 20, oldRub: 3999, rub: 3199, stores: [{ store: 'steam', price: '3 199 ₽', old: '3 999 ₽', discount: 20 }, { store: 'epic', price: '2 799 ₽', old: '3 999 ₽', discount: 30, cheaper: true, diff: 'на 400 ₽ дешевле' }], tags: ['compare', 'hits'] },
  { name: 'Civilization VI Anthology', token: 'civ6', productType: 'Пошаговая 4X стратегия', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/289070/header.jpg', url: 'https://store.steampowered.com/app/289070/', discount: 90, oldRub: 2999, rub: 299, stores: [{ store: 'steam', price: '299 ₽', old: '2 999 ₽', discount: 90 }, { store: 'epic', price: '249 ₽', old: '2 999 ₽', discount: 92, cheaper: true, diff: 'на 50 ₽ дешевле' }], tags: ['compare', 'steep'] },
  { name: 'Disco Elysium — The Final Cut', token: 'discoelysium', productType: 'Детективная RPG', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/632470/header.jpg', url: 'https://store.steampowered.com/app/632470/', discount: 75, oldRub: 725, rub: 181, stores: [{ store: 'steam', price: '181 ₽', old: '725 ₽', discount: 75 }, { store: 'epic', price: '181 ₽', old: '725 ₽', discount: 75 }, { store: 'gog', price: '181 ₽', old: '725 ₽', discount: 75 }], tags: ['compare', 'steep'] },
  { name: 'Mass Effect Legendary Edition', token: 'masseffect', productType: 'Космическая сага', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1328670/header.jpg', url: 'https://store.steampowered.com/app/1328670/', discount: 85, oldRub: 3999, rub: 599, stores: [{ store: 'steam', price: '599 ₽', old: '3 999 ₽', discount: 85 }, { store: 'ea', price: '599 ₽', old: '3 999 ₽', discount: 85 }], tags: ['compare', 'hits', 'steep'] },
  { name: 'Hades II', token: 'hades2', productType: 'Rogue-like / Мифология', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1145350/header.jpg', url: 'https://store.steampowered.com/app/1145350/', discount: 10, oldRub: 1100, rub: 990, stores: [{ store: 'steam', price: '990 ₽', old: '1 100 ₽', discount: 10 }, { store: 'epic', price: '990 ₽', old: '1 100 ₽', discount: 10 }], tags: ['compare', 'hits'] },
  { name: 'Frostpunk 2', token: 'frostpunk2', productType: 'Градострой / Выживание', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1601580/header.jpg', url: 'https://store.steampowered.com/app/1601580/', discount: 15, oldRub: 1999, rub: 1699, stores: [{ store: 'steam', price: '1 699 ₽', old: '1 999 ₽', discount: 15 }, { store: 'gog', price: '1 699 ₽', old: '1 999 ₽', discount: 15, note: 'DRM-Free' }], tags: ['compare', 'hits'] },
  { name: 'Forza Horizon 5', token: 'fh5', productType: 'Гонки в открытом мире', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1551360/header.jpg', url: 'https://store.steampowered.com/app/1551360/', discount: 50, oldRub: 2999, rub: 1499, stores: [{ store: 'steam', price: '1 499 ₽', old: '2 999 ₽', discount: 50 }, { store: 'plati', price: '1 100 ₽', old: '2 999 ₽', discount: 63, cheaper: true, diff: 'на 399 ₽ дешевле' }], tags: ['hits'] },
  { name: 'It Takes Two', token: 'ittakestwo', productType: 'Кооператив для двоих', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1426210/header.jpg', url: 'https://store.steampowered.com/app/1426210/', discount: 65, oldRub: 2299, rub: 799, stores: [{ store: 'steam', price: '799 ₽', old: '2 299 ₽', discount: 65 }, { store: 'ea', price: '799 ₽', old: '2 299 ₽', discount: 65 }], tags: ['compare', 'hits', 'steep'] },
  { name: 'Sea of Thieves: 2024 Edition', token: 'seaofthieves', productType: 'Пиратский мультиплеер', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1172620/header.jpg', url: 'https://store.steampowered.com/app/1172620/', discount: 50, oldRub: 1799, rub: 899, stores: [{ store: 'steam', price: '899 ₽', old: '1 799 ₽', discount: 50 }, { store: 'plati', price: '690 ₽', old: '1 799 ₽', discount: 62, cheaper: true, diff: 'на 209 ₽ дешевле' }], tags: ['hits'] },
  { name: 'Terraria', token: 'terraria', productType: '2D Песочница / Приключения', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/105600/header.jpg', url: 'https://store.steampowered.com/app/105600/', discount: 50, oldRub: 385, rub: 192, stores: [{ store: 'steam', price: '192 ₽', old: '385 ₽', discount: 50 }, { store: 'gog', price: '192 ₽', old: '385 ₽', discount: 50 }], tags: ['compare', 'steep'] },
  { name: 'Subnautica', token: 'subnautica', productType: 'Подводное выживание', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/264710/header.jpg', url: 'https://store.steampowered.com/app/264710/', discount: 50, oldRub: 999, rub: 499, stores: [{ store: 'steam', price: '499 ₽', old: '999 ₽', discount: 50 }, { store: 'epic', price: '499 ₽', old: '999 ₽', discount: 50 }], tags: ['compare', 'steep'] },
  { name: 'RimWorld', token: 'rimworld', productType: 'Симулятор колонии / Sci-Fi', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/294100/header.jpg', url: 'https://store.steampowered.com/app/294100/', discount: 20, oldRub: 1225, rub: 980, stores: [{ store: 'steam', price: '980 ₽', old: '1 225 ₽', discount: 20 }, { store: 'gog', price: '980 ₽', old: '1 225 ₽', discount: 20, note: 'DRM-Free' }], tags: ['compare', 'hits'] },
  { name: 'Hollow Knight', token: 'hollowknight', productType: 'Метроидвания / Шедевр', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/367520/header.jpg', url: 'https://store.steampowered.com/app/367520/', discount: 50, oldRub: 550, rub: 275, stores: [{ store: 'steam', price: '275 ₽', old: '550 ₽', discount: 50 }, { store: 'gog', price: '275 ₽', old: '550 ₽', discount: 50, note: 'DRM-Free' }], tags: ['compare', 'steep'] },
  { name: 'Dead by Daylight', token: 'dbd', productType: 'Мультиплеерный хоррор', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/381210/header.jpg', url: 'https://store.steampowered.com/app/381210/', discount: 60, oldRub: 1249, rub: 499, stores: [{ store: 'steam', price: '499 ₽', old: '1 249 ₽', discount: 60 }, { store: 'epic', price: '499 ₽', old: '1 249 ₽', discount: 60 }], tags: ['compare', 'steep'] },
  { name: 'Far Cry 6', token: 'farcry6', productType: 'Шутер в открытом мире', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/2369390/header.jpg', url: 'https://store.steampowered.com/app/2369390/', discount: 75, oldRub: 2999, rub: 749, stores: [{ store: 'steam', price: '999 ₽', old: '2 999 ₽', discount: 67 }, { store: 'epic', price: '749 ₽', old: '2 999 ₽', discount: 75, cheaper: true, diff: 'на 250 ₽ дешевле' }, { store: 'ubisoft', price: '749 ₽', old: '2 999 ₽', discount: 75 }], tags: ['compare', 'steep'] },
  { name: 'Assassin’s Creed Valhalla', token: 'acvalhalla', productType: 'Экшен-RPG / Викинги', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/2208920/header.jpg', url: 'https://store.steampowered.com/app/2208920/', discount: 75, oldRub: 3999, rub: 999, stores: [{ store: 'steam', price: '1 249 ₽', old: '3 999 ₽', discount: 68 }, { store: 'epic', price: '999 ₽', old: '3 999 ₽', discount: 75, cheaper: true, diff: 'на 250 ₽ дешевле' }, { store: 'ubisoft', price: '999 ₽', old: '3 999 ₽', discount: 75 }], tags: ['compare', 'steep'] },
  { name: 'Star Wars Jedi: Survivor', token: 'jedisurvivor', productType: 'Экшен / Звёздные Войны', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1774580/header.jpg', url: 'https://store.steampowered.com/app/1774580/', discount: 55, oldRub: 3999, rub: 1799, stores: [{ store: 'steam', price: '1 799 ₽', old: '3 999 ₽', discount: 55 }, { store: 'epic', price: '1 499 ₽', old: '3 999 ₽', discount: 62, cheaper: true, diff: 'на 300 ₽ дешевле' }, { store: 'ea', price: '1 799 ₽', old: '3 999 ₽', discount: 55 }], tags: ['compare', 'hits'] },
  { name: 'Remnant II', token: 'remnant2', productType: 'Кооперативный Souls-like', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/1282100/header.jpg', url: 'https://store.steampowered.com/app/1282100/', discount: 40, oldRub: 2499, rub: 1499, stores: [{ store: 'steam', price: '1 499 ₽', old: '2 499 ₽', discount: 40 }, { store: 'epic', price: '1 299 ₽', old: '2 499 ₽', discount: 48, cheaper: true, diff: 'на 200 ₽ дешевле' }], tags: ['compare', 'hits'] },
  { name: 'Fallout 4: Game of the Year Edition', token: 'fallout4', productType: 'Постапокалипсис / RPG', image: 'https://cdn.cloudflare.steamstatic.com/steam/apps/377160/header.jpg', url: 'https://store.steampowered.com/app/377160/', discount: 75, oldRub: 1599, rub: 399, stores: [{ store: 'steam', price: '399 ₽', old: '1 599 ₽', discount: 75 }, { store: 'gog', price: '399 ₽', old: '1 599 ₽', discount: 75, note: 'DRM-Free' }], tags: ['compare', 'steep'] }
 ];


 function discoveryPane(){
  const s=S();const cal=s.calendar?s.calendar(discovery):'';const pct=s.pct?s.pct:()=>0;
  const liveList=(discovery?.products||[]).map(p=>({
   name:p.name,
   token:p.token,
   productType:p.productType||'Акция магазина',
   image:p.image,
   url:p.url,
   discount:pct(p.preview)||50,
   oldRub:p.preview?.originalAmount,
   rub:p.preview?.amount,
   currency:p.preview?.currency||'RUB',
   preview:p.preview,
   isLive:true,
   stores:[{store:discovery?.store||'steam',price:p.preview?original(p.preview):'Узнать цену',discount:pct(p.preview)}],
   tags:['all','hits']
  }));
  const seen=new Set();
  const allDeals=[];
  for(const d of [...liveList,...CURATED_DEALS]){
   const k=normalized(d.name);
   if(!seen.has(k)){seen.add(k);allDeals.push(d);}
  }
  const filtered=allDeals.filter(d=>{
   if(dealFilter==='all')return true;
   if(dealFilter==='compare')return d.tags?.includes('compare');
   if(dealFilter==='steep')return (d.discount>=60)||d.tags?.includes('steep');
   if(dealFilter==='hits')return d.tags?.includes('hits');
   if(dealFilter==='steam')return d.stores?.some(x=>x.store==='steam');
   if(dealFilter==='epic')return d.stores?.some(x=>x.store==='epic');
   if(dealFilter==='gog')return d.stores?.some(x=>x.store==='gog');
   return true;
  });
  return `${cal}
  <section class="sale-featured">
    <div class="price-results-heading">
      <div>
        <h3>${discovery?.store==='steam'?'Сейчас на витрине Steam и других магазинов':'Горячие акции и предложения'}</h3>
        <span>Лучшие предложения, мультимагазинное сравнение и проверенные скидки</span>
      </div>
      <button class="quiet" data-price="deals">↻ Обновить</button>
    </div>
    <div class="deal-filter-bar">
      <button class="deal-filter-btn ${dealFilter==='all'?'active':''}" data-deal-filter="all">🔥 Все суперскидки (${allDeals.length})</button>
      <button class="deal-filter-btn ${dealFilter==='compare'?'active':''}" data-deal-filter="compare">⚔️ Steam vs Epic (${allDeals.filter(x=>x.tags?.includes('compare')).length})</button>
      <button class="deal-filter-btn ${dealFilter==='steep'?'active':''}" data-deal-filter="steep">⚡ Скидки от −60%</button>
      <button class="deal-filter-btn ${dealFilter==='hits'?'active':''}" data-deal-filter="hits">👑 Топ блокбастеры</button>
      <button class="deal-filter-btn ${dealFilter==='gog'?'active':''}" data-deal-filter="gog">🟢 DRM-Free (GOG)</button>
    </div>
    <div class="price-search-results">
      ${filtered.map(d=>`
        <button class="price-product" data-price-deal="${esc(d.token||d.name)}">
          ${d.image&&/^https?:\/\//i.test(d.image)?`<img src="${esc(d.image)}" alt="" loading="lazy">`:`<div class="price-cover-fallback">${mark(d.stores?.[0]?.store||'steam')}</div>`}
          <div>
            <small>${esc(d.productType||'Игра')}</small>
            <h3>${esc(d.name)}</h3>
            <div class="deal-pricing">
              <span class="deal-flame-badge">🔥 −${d.discount||50}%</span>
              <div class="deal-price-col">
                ${d.oldRub?`<del class="deal-old">${typeof d.oldRub==='number'?money(d.oldRub,d.currency||'RUB'):esc(d.oldRub)}</del>`:''}
                <strong class="deal-new">${d.rub!=null?(typeof d.rub==='number'?money(d.rub,d.currency||'RUB'):esc(d.rub)):(d.preview?original(d.preview):'Узнать цену')}</strong>
              </div>
            </div>
            ${d.stores&&d.stores.length>1?`
              <div class="deal-store-compare">
                ${d.stores.map(st=>`
                  <div class="store-chip ${st.cheaper?'best':''}">
                    <span class="store-chip-icon ${esc(st.store)}">${esc(marks[st.store]||st.store[0].toUpperCase())}</span>
                    <span class="store-chip-name">${esc(names[st.store]||st.store)}:</span>
                    <strong class="store-chip-val">${esc(st.price)}</strong>
                    ${st.cheaper?`<span class="store-chip-badge">🔥 ${esc(st.diff||'Выгоднее!')}</span>`:''}
                  </div>
                `).join('')}
              </div>
            `:d.preview?pair(d.preview,true):''}
            <span class="price-product-action">Сравнить цены в магазинах <b>→</b></span>
          </div>
        </button>
      `).join('')}
    </div>
    ${discovery?.fxDate?`<p class="note">Пересчёт ≈ по курсу ЦБ на ${esc(discovery.fxDate)}.</p>`:''}
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
   if(d.priceDeal){
    const live=(discovery?.products||[]).find(x=>x.token===d.priceDeal);
    if(live){selected=live;copies=[];page='compare';comparison=null;refresh(false);return;}
    const cur=CURATED_DEALS.find(x=>x.token===d.priceDeal||x.name===d.priceDeal);
    if(cur){
     query=cur.name;
     if($('#price-query'))$('#price-query').value=cur.name;
     search(false);
     return;
    }
   }

  if(d.priceLibrary){const g=(window.data?.games||[]).find(x=>x.id===d.priceLibrary);if(g){selected={id:g.id,store:g.store,name:g.name,image:g.cover||g.background};copies=[];page='compare';comparison=null;refresh(false);}return;}
  if(d.price==='clearSearch'){query='';results=null;error='';if(!discovery)discover(false);else draw();return;}
  if(d.priceUrl){api.send('pricesOpen',{url:d.priceUrl});return;}
  if(d.priceProduct){const p=[...(results?.products||[]),...(discovery?.products||[])].find(x=>x.token===d.priceProduct);if(p){selected=p;copies=[];page='compare';comparison=null;refresh(false);}return;}
  if(d.priceMarket){activeMarket=d.priceMarket;draw();return;}
  switch(d.price){case'deals':discover(true);break;case'back':api.send('pricesCancel');expected=String(++request);busy=false;selected=null;page='search';error='';draw();break;case'refresh':refresh();break;case'settings':error='';settingsDraft=null;if($('#price-query'))query=$('#price-query').value;settingsBack=page;api.send('pricesCancel');expected=String(++request);busy=false;page='settings';draw();break;case'settingsBack':settingsDraft=null;pendingSettings=false;page=settingsBack;draw();break;case'cancel':api.send('pricesCancel');expected=String(++request);busy=false;error='Проверка остановлена. Можно повторить запрос.';draw();break;case'saveFunpay':api.send('pricesSaveFunpay',{url:$('#price-funpay-url').value});break;}
 });
 window.OrbitPrices={init:a=>api=a,show,showSettings:()=>{show(undefined,false);settingsBack='search';page='settings';draw();},showGame,showProduct,receive,closed};
})();