const {chromium}=require('playwright'),assert=require('node:assert/strict'),path=require('node:path'),{pathToFileURL}=require('node:url');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.ORBIT_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:1360,height:900}}),errors=[];
  page.on('pageerror',e=>errors.push(String(e)));await page.route('https://**/*',r=>r.abort());
  await page.addInitScript(()=>{window.sent=[];window.CefSharp={PostMessage:s=>sent.push(JSON.parse(s))};});
  await page.goto(pathToFileURL(path.resolve(__dirname,'../../engine/Extensions/Orbit/ui/index.html')).href);
  const receive=m=>page.evaluate(m=>Orbit.receive(m),m);
  const games=Array.from({length:400},(_,i)=>({id:`00000000-0000-0000-0000-${String(i).padStart(12,'0')}`,name:`Game ${String(i).padStart(3,'0')}`,store:'steam',installed:i<10}));
  await receive({kind:'snapshot',data:{games,prefs:{},stores:[],downloads:[]}});
  await page.locator('[data-section="all"]').click();await page.waitForTimeout(500);
  assert.equal(await page.locator('main').evaluate(el=>getComputedStyle(el,'::-webkit-scrollbar').width),'14px');
  assert.equal(await page.locator('#games .game-card').count(),72);
  await page.evaluate(()=>window.firstCard=document.querySelector('#games').firstElementChild);
  for(let i=0;i<6;i++){await page.locator('main').evaluate(el=>el.scrollTop=el.scrollHeight);await page.waitForTimeout(200);}
  assert.equal(await page.locator('#games .game-card').count(),400);
  assert.ok(await page.evaluate(()=>firstCard===document.querySelector('#games').firstElementChild));
  assert.ok(await page.locator('#more').isHidden());
  await page.locator('main').evaluate(el=>el.scrollTop=0);
  await page.locator('#sort').dispatchEvent('change');
  assert.equal(await page.locator('#games .game-card').count(),72,'reset same ordering after append');
  await page.locator('main').evaluate(el=>el.scrollTop=0);await page.locator('#search').fill('Game 399');
  assert.equal(await page.locator('#games .game-card').count(),1);
  await page.locator('#search').fill('');assert.equal(await page.locator('#games .game-card').count(),72);
  await page.screenshot({path:path.resolve(__dirname,'../../qa/ux-library.png')});
  await page.locator('[data-cmd="prices"]').click();
  await receive({kind:'pricesSettings',settings:{accounts:{steam:{country:'KZ',currency:'KZT'}}}});
  assert.equal(await page.locator('.price-setup').count(),0);
  await page.locator('[data-price="settings"]').first().click();
  assert.match(await page.locator('#modal').textContent(),/Общий регион витрин: Казахстан/);
  assert.equal(await page.locator('[data-price-country="epic"]').getAttribute('placeholder'),'KZ');
  const report={store:'steam',sources:[{source:'steam',offers:[{source:'steam',amount:5800,currency:'KZT',rub:950,country:'KZ',official:true}]},{source:'gog',offers:[{source:'gog',amount:20,currency:'USD',rub:1800,country:'KZ',official:true}]}]};
  await page.evaluate(data=>document.querySelector('#modal').innerHTML=OrbitSales.rows(data),report);
  assert.match(await page.locator('.sale-summary>div').first().textContent(),/≈.*950.*₽/);
  assert.match(await page.locator('[data-sale-store="gog"] .sale-shop-value').textContent(),/≈.*1\s?800.*₽/);
  await page.screenshot({path:path.resolve(__dirname,'../../qa/ux-prices.png')});
  report.sources.forEach(s=>delete s.offers[0].rub);
  await page.evaluate(data=>document.querySelector('#modal').innerHTML=OrbitSales.rows(data),report);
  assert.equal(await page.locator('.sale-rub-estimate').count(),0);
  assert.match(await page.locator('.sale-shop-value').first().textContent(),/Пересчёт недоступен/);
  assert.deepEqual(errors,[]);console.log('PASS UX UI');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
