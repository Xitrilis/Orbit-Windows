const {chromium}=require('playwright');
const path=require('node:path'),fs=require('node:fs'),{pathToFileURL}=require('node:url'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),qa=path.join(root,'qa');fs.mkdirSync(qa,{recursive:true});
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.ORBIT_CHROME_PATH||undefined,args:['--no-sandbox']});
 try{
  const page=await browser.newPage({viewport:{width:1360,height:880}}),errors=[];page.on('pageerror',e=>errors.push(String(e)));
  await page.addInitScript(()=>{window.sent=[];window.CefSharp={PostMessage:s=>window.sent.push(JSON.parse(s))};});
  await page.route('https://fixture.invalid/**',r=>r.request().url().includes('broken')?r.abort():r.fulfill({path:path.join(root,'engine/Extensions/Orbit/ui/assets/1091500-hero.jpg'),contentType:'image/jpeg'}));
  await page.goto(pathToFileURL(path.join(root,'engine/Extensions/Orbit/ui/index.html')).href);
  const receive=m=>page.evaluate(v=>Orbit.receive(v),m),last=k=>page.evaluate(k=>sent.filter(m=>m.kind===k).at(-1),k);
  const game={id:'11111111-1111-1111-1111-111111111111',name:'Cyberpunk 2077',store:'epic',installed:true};
  const snapshot={kind:'snapshot',data:{games:[game],prefs:{},stores:[],downloads:[],notifications:[]}};
  await receive(snapshot);await page.locator('#app-splash').waitFor({state:'detached'});
  await page.locator('[data-cmd="prices"]').click();
  const config={accounts:{steam:{country:'KZ',currency:'KZT'},epic:{country:'',currency:''}},funpaySections:{}};
  await receive({kind:'pricesSettings',settings:config});
  await page.locator('#price-store').selectOption('epic');assert.equal((await last('pricesDiscover')).store,'epic');assert.match(await page.locator('.price-region-chip').innerText(),/Казахстан.*Из Steam/s);
  assert.equal(await page.locator('.price-live-dot').count(),0);
  await page.locator('[data-store-tab="library"]').click();assert.equal(await page.locator('.store-lib-card').count(),1);assert.equal(await page.locator('.price-loading').count(),0);
  const offer={source:'epic',official:true,name:'Cyberpunk 2077',currency:'KZT',amount:3959.7,originalAmount:13199,rub:791.94,kzt:3959.7,country:'KZ',url:'https://store.epicgames.com/p/cyberpunk-2077',checkedAt:'2026-10-29T12:00:00Z'};
  const products=[{token:'main',store:'epic',id:'offer-id',catalogNamespace:'ns',name:offer.name,image:'https://fixture.invalid/art.jpg',productType:'Игра',preview:offer},
   {token:'long',store:'epic',id:'long-id',name:'Очень длинное название игры: коллекционное расширенное издание со всеми дополнениями и сезонным пропуском',image:'https://fixture.invalid/broken.jpg',productType:'Издание',preview:{...offer,amount:9876543.21,originalAmount:12345678,rub:1975308.64,kzt:9876543.21}},
   {token:'free',store:'epic',id:'free-id',name:'Бесплатная игра',productType:'Игра',preview:{...offer,amount:0,originalAmount:0,rub:0,kzt:0,free:true}},
   {token:'missing',store:'epic',id:'missing-id',name:'Игра без подтверждённой цены',productType:'Игра',preview:null}];
  const discovery={store:'epic',country:'KZ',status:'ok',checkedAt:offer.checkedAt,message:'Тестовые данные для проверки интерфейса',fxDate:'2026-10-29',calendar:{events:[],checkedAt:offer.checkedAt,message:'Тест'},products};
  await receive({kind:'pricesDiscovery',requestId:(await last('pricesDiscover')).requestId,data:discovery});assert.equal(await page.locator('.store-lib-card').count(),1);
  await page.locator('[data-store-tab="deals"]').click();assert.equal(await page.locator('.price-product').count(),4);
  assert.match(await page.locator('[data-price-product="main"] .price-product-native').innerText(),/3\s?959,7.*₸/);
  assert.match(await page.locator('[data-price-product="main"] .price-pair').innerText(),/≈ 791,94/);
  assert.match(await page.locator('[data-price-product="free"] .price-product-native').innerText(),/Бесплатно/);
  assert.match(await page.locator('[data-price-product="missing"]').innerText(),/Цена недоступна/);
  assert.deepEqual(await page.locator('.price-product-native').evaluateAll(es=>[...new Set(es.map(e=>getComputedStyle(e).fontSize))]),['22px']);
  await page.locator('[data-price-product="long"] img').waitFor({state:'hidden'});
  for(const [width,height] of [[1360,880],[1024,768],[880,640]]){
   await page.setViewportSize({width,height});await page.waitForTimeout(200);
   assert.ok(await page.locator('#modal').evaluate(e=>e.scrollWidth<=e.clientWidth+1),'store modal '+width);
   assert.ok(await page.locator('.price-product').evaluateAll(es=>es.every(e=>e.scrollWidth<=e.clientWidth+1)),'card width '+width);
   await page.screenshot({path:path.join(qa,'store-audit-'+width+'.png'),fullPage:true});
  }
  await page.setViewportSize({width:1360,height:880});
  await page.locator('#price-query').fill('Cyberpunk');await page.waitForTimeout(300);const suggestion=await last('pricesSearch');assert.ok(suggestion.requestId.startsWith('suggest-'));
  await receive({kind:'pricesSearchResult',requestId:suggestion.requestId,data:{products:products.slice(0,2)}});
  await receive(snapshot);assert.equal(await page.locator('.price-suggest-item').count(),2);assert.equal(await page.locator('#price-query').inputValue(),'Cyberpunk');
  await page.locator('#price-query').press('ArrowUp');assert.equal(await page.locator('[aria-selected="true"].price-suggest-item').getAttribute('data-price-suggest'),'long');
  await page.locator('#price-query').press('ArrowDown');await page.locator('#price-query').press('Enter');assert.equal((await last('pricesCompare')).token,'main');
  const report={name:offer.name,store:'epic',country:'KZ',fxDate:'2026-10-29',sources:[{source:'epic',status:'ok',match:'id',url:offer.url,offers:[offer]},
   {source:'steam',status:'ok',url:'https://store.steampowered.com/app/1091500/',offers:[{...offer,source:'steam',currency:'RUB',amount:1500,rub:1500,kzt:7500}]},
   {source:'gog',status:'ok',url:'https://www.gog.com/',offers:[{...offer,source:'gog',currency:'USD',amount:19.99,rub:1799.1,kzt:8995.5}]},
   {source:'funpay',status:'empty',offers:[],message:'Нет подтверждённых предложений'}, {source:'plati',status:'error',offers:[],message:'Источник недоступен'}]};
  await receive({kind:'pricesResult',requestId:(await last('pricesCompare')).requestId,data:report});assert.match(await page.locator('[data-sale-store="steam"] .sale-shop-value').innerText(),/₸/);
  for(const [width,height] of [[1360,880],[880,640]]){await page.setViewportSize({width,height});assert.ok(await page.locator('#modal').evaluate(e=>e.scrollWidth<=e.clientWidth+1));await page.screenshot({path:path.join(qa,'store-compare-'+width+'.png'),fullPage:true});}
  await page.locator('[data-price="findEdition"]').click();const edition=await last('pricesSearch');assert.equal(edition.store,'epic');assert.equal(edition.query,'Cyberpunk 2077');
  await receive({kind:'pricesError',requestId:edition.requestId,message:'Нет соединения'});assert.equal(await page.locator('.price-loading').count(),0);assert.equal(await page.locator('[data-price="deals"]').count(),1);
  await page.locator('#price-query').fill('Cyber');await page.waitForTimeout(300);const stale=await last('pricesSearch');await page.locator('#price-query').fill('Cy');await receive({kind:'pricesSearchResult',requestId:stale.requestId,data:{products}});assert.equal(await page.locator('.price-suggest-item').count(),0);
  await page.locator('#price-query').fill('Cyberpunk');await page.waitForTimeout(300);await receive({kind:'pricesSearchResult',requestId:(await last('pricesSearch')).requestId,data:{products:[products[0]]}});
  await page.locator('#price-query').press('Escape');assert.equal(await page.locator('#price-suggestions').isHidden(),true);assert.equal(await page.locator('#modal-backdrop').isVisible(),true);assert.equal((await last('pricesCancel')).lane,'suggest');
  await page.locator('[data-price="clearSearch"]').click();const beforeSettings=await last('pricesDiscover');await page.locator('[data-price="settings"]').click();await page.locator('[data-price-country="steam"]').fill('RU');await page.locator('#price-settings-form .primary').click();
  await receive({kind:'pricesSettings',settings:{...config,accounts:(await last('pricesSaveSettings')).accounts}});const afterSettings=await last('pricesDiscover');assert.notEqual(beforeSettings.requestId,afterSettings.requestId);assert.equal(afterSettings.store,'epic');assert.match(await page.locator('.price-region-chip').innerText(),/Россия/);
  await receive({kind:'pricesDiscovery',requestId:beforeSettings.requestId,data:discovery});assert.equal(await page.locator('.price-loading').count(),1);
  await receive({kind:'pricesDiscovery',requestId:afterSettings.requestId,data:{...discovery,products:[],status:'error',message:'HTTP 503',checkedAt:null}});assert.equal(await page.locator('.price-loading').count(),0);assert.match(await page.locator('.price-empty').innerText(),/Повторить загрузку/);
  await page.locator('[data-price="deals"]').first().click();await page.locator('[data-price="settings"]').click();const beforeBack=await last('pricesDiscover');await page.locator('[data-price="settingsBack"]').click();assert.notEqual((await last('pricesDiscover')).requestId,beforeBack.requestId);
  await page.locator('[data-price="cancel"]').click();assert.equal(await page.locator('.price-loading').count(),0);
  await page.locator('#price-store').selectOption('gog');assert.equal((await last('pricesDiscover')).store,'gog');await receive({kind:'pricesDiscovery',requestId:(await last('pricesDiscover')).requestId,data:{...discovery,store:'gog',products:[{...products[0],preview:{...offer,currency:'?',amount:123}}]}});assert.equal(await page.locator('.price-product').count(),1);assert.equal(await page.locator('.price-error').count(),0);
  await page.keyboard.press('Escape');await receive({kind:'pricesDiscovery',requestId:(await last('pricesDiscover')).requestId,data:discovery});await page.locator('#modal-backdrop').waitFor({state:'hidden'});assert.equal(await page.locator('#modal-backdrop').isHidden(),true);
  assert.deepEqual(errors,[]);console.log('PASS store audit: Epic selection + region, library while loading, source price hierarchy, missing/free quotes, broken art, three viewports, suggestions across snapshots, keyboard + Escape, exact-edition search, offline retry, stale responses, settings save/back, cancellation, invalid currency.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
