const {chromium}=require('playwright');
const path=require('node:path'),fs=require('node:fs'),{pathToFileURL}=require('node:url'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),qa=path.join(root,'qa');fs.mkdirSync(qa,{recursive:true});
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.ORBIT_CHROME_PATH||undefined,args:['--no-sandbox']});
 try{
  const page=await browser.newPage({viewport:{width:1360,height:1000}}),errors=[];
  page.on('pageerror',e=>errors.push(String(e)));
  await page.emulateMedia({reducedMotion:'reduce'});
  await page.addInitScript(()=>{window.sent=[];window.CefSharp={PostMessage:s=>window.sent.push(JSON.parse(s))};});
  const url=pathToFileURL(path.join(root,'engine/Extensions/Orbit/ui/index.html')).href;
  await page.goto(url);const receive=m=>page.evaluate(v=>window.Orbit.receive(v),m),last=k=>page.evaluate(k=>window.sent.filter(m=>m.kind===k).at(-1),k);
  await page.locator('[data-cmd="wallet"]').click();assert.equal((await last('walletSettings')).kind,'walletSettings');
  await receive({kind:'walletSettings',settings:{region:'RU',method:'sbp',rememberLogin:false,login:''}});
  assert.equal(await page.locator('input[autocomplete="cc-number"],input[autocomplete="cc-csc"]').count(),0);
  await page.locator('[data-wallet-provider="gamemoney"]').click();assert.match(await page.locator('.wallet-error').textContent(),/Введи логин/);
  await page.locator('#wallet-login').fill('orbit_test_user');await page.locator('#wallet-remember').check();
  await page.locator('[data-wallet="save"]').click();let save=await last('walletSave');assert.equal(save.settings.login,'orbit_test_user');
  await receive({kind:'walletError',message:'Нет доступа к папке'});assert.equal(await page.locator('#wallet-login').inputValue(),'orbit_test_user');assert.equal(await page.locator('[data-wallet="save"]').isEnabled(),true);
  await page.locator('[data-wallet="save"]').click();await receive({kind:'walletSaved',settings:save.settings});
  await page.locator('[data-wallet="quote"]').click();const first=await last('walletQuote');assert.equal(first.amount,'1000');
  // Editing while loading must cancel, re-enable calculate, and ignore the late response.
  await page.locator('#wallet-amount').fill('3000');assert.equal(await page.locator('[data-wallet="quote"]').isEnabled(),true);
  const fixture=(region='RU')=>({region,principal:1000,checkedAt:new Date().toISOString(),expiresAt:new Date(Date.now()+300000).toISOString(),fxDate:'2026-10-24',rubPerUnit:{RUB:1,KZT:.2},quotes:['sbp','card'].map(method=>({provider:'gamemoney',method,currency:region==='RU'?'RUB':'KZT',payRub:method==='sbp'?1030:1080,receive:region==='RU'?1000:5000,receiveRub:1000,receiveKzt:5000,serviceFee:2,paymentFee:method==='sbp'?1:6,extraPercent:method==='sbp'?3:8}))});
  await receive({kind:'walletQuote',requestId:first.requestId,data:fixture()});assert.equal(await page.locator('.wallet-total').count(),0);
  await page.locator('[data-wallet-amount="1000"]').click();await page.locator('[data-wallet="quote"]').click();await receive({kind:'walletQuote',requestId:(await last('walletQuote')).requestId,data:fixture()});
  assert.match(await page.locator('.wallet-total').textContent(),/1\s?030/);assert.match(await page.locator('.wallet-saving').textContent(),/50/);
  await page.locator('#modal').evaluate(e=>e.scrollTop=0);await page.screenshot({path:path.join(qa,'wallet-rub.png'),fullPage:true,animations:'disabled'});
  await page.locator('.wallet-methods [data-wallet-method="card"]').click();assert.match(await page.locator('.wallet-total').textContent(),/1\s?080/);
  await page.locator('.wallet-saving').click();assert.match(await page.locator('.wallet-total').textContent(),/1\s?030/);
  await page.locator('[data-wallet="compare"]').click();await page.locator('#wallet-manual-pay').fill('1100');await page.locator('#wallet-manual-receive').fill('1000');await page.locator('#wallet-manual-extra').fill('50');assert.match(await page.locator('#wallet-manual-result').textContent(),/15.00%/);assert.match(await page.locator('#wallet-manual-result').textContent(),/у GAMEMONEY меньше/);
  await page.screenshot({path:path.join(qa,'wallet-compare.png'),fullPage:true});
  await page.locator('#wallet-manual-pay').fill('1000');await page.locator('#wallet-manual-extra').fill('0');assert.match(await page.locator('#wallet-manual-result').textContent(),/у МТС меньше/);
  await page.locator('[data-wallet="back"]').click();await page.locator('#wallet-region').selectOption('KZ');assert.equal(await page.locator('.wallet-total').count(),0);
  await page.locator('[data-wallet="quote"]').click();await receive({kind:'walletQuote',requestId:(await last('walletQuote')).requestId,data:fixture('KZ')});assert.match(await page.locator('.wallet-receive').textContent(),/₸/);
  // Do not hard-code SBP as cheapest if a provider changes the fee schedule.
  const cheaperCard=fixture('KZ');cheaperCard.quotes[1].payRub=1010;cheaperCard.quotes[1].extraPercent=1;
  await receive({kind:'walletQuote',requestId:(await last('walletQuote')).requestId,data:cheaperCard});
  assert.equal(await page.locator('.wallet-saving').getAttribute('data-wallet-method'),'card');
  await page.locator('.wallet-saving').click();assert.match(await page.locator('.wallet-total').textContent(),/1\s?010/);
  await receive({kind:'walletQuote',requestId:(await last('walletQuote')).requestId,data:fixture('KZ')});await page.locator('.wallet-saving').click();
  await page.locator('[data-wallet-provider="gamemoney"]').click();assert.equal(await page.locator('#wallet-go').isDisabled(),true);assert.equal(await page.locator('.wallet-review').textContent().then(t=>t.includes('orbit_test_user')),true);
  await page.screenshot({path:path.join(qa,'wallet-review.png'),fullPage:true});
  await page.locator('#wallet-confirm').check();await page.locator('#wallet-go').click();const open=await last('walletOpen');assert.deepEqual(open,{kind:'walletOpen',target:'gamemoney'});
  assert.equal(await page.locator('.wallet-opened').count(),0);await receive({kind:'walletOpened',target:'gamemoney'});assert.match(await page.locator('.wallet-opened').textContent(),/ещё не подтверждение оплаты/);
  await page.locator('[data-wallet="back"]').click();await page.locator('#wallet-remember').uncheck();await page.locator('[data-wallet="save"]').click();save=await last('walletSave');assert.equal(save.settings.login,'');await receive({kind:'walletSaved',settings:save.settings});
  await receive({kind:'walletError',message:'<img src=x onerror=alert(1)>'});assert.equal(await page.locator('.wallet-error img').count(),0);
  await page.setViewportSize({width:900,height:700});await page.screenshot({path:path.join(qa,'wallet-900.png'),fullPage:true});assert.equal(await page.locator('#modal').evaluate(e=>e.scrollWidth<=e.clientWidth+1),true);
  await page.locator('[data-cmd="closeModal"]').click();await receive({kind:'walletQuote',requestId:(await last('walletQuote')).requestId,data:fixture()});await page.locator('#modal-backdrop').waitFor({state:'hidden'});assert.equal(await page.locator('#modal-backdrop').isVisible(),false);
  await page.goto(url+'?demo=1');await page.locator('[data-cmd="wallet"]').click();await page.locator('[data-wallet="quote"]').click();assert.match(await page.locator('.wallet-error').textContent(),/Демонстрация/);assert.equal(await page.evaluate(()=>window.sent.some(x=>x.kind.startsWith('wallet'))),false);
  // Ordinary browser preview has a useful error rather than a permanent spinner.
  await page.evaluate(()=>delete window.CefSharp);await page.goto(url);await page.evaluate(()=>delete window.CefSharp);await page.locator('[data-cmd="wallet"]').click();await page.locator('[data-wallet="quote"]').click();assert.match(await page.locator('.wallet-error').textContent(),/Orbit.exe/);
  assert.deepEqual(errors,[]);console.log('PASS wallet UI: live-data bridge, fee/method switch, RU/KZ, manual comparison, login/save failure, card boundary, review, no fake payment, stale responses, demo isolation, 900px');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
