const {chromium}=require('playwright'),path=require('node:path'),fs=require('node:fs'),assert=require('node:assert/strict'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'../..'),file=pathToFileURL(path.join(root,'engine/Extensions/Orbit/ui/index.html')).href;
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.ORBIT_CHROME_PATH||undefined,args:['--no-sandbox']});
 try{
  const page=await browser.newPage({viewport:{width:1360,height:950},timezoneId:'Europe/Moscow'}),errors=[];page.on('pageerror',e=>errors.push(String(e)));
  await page.addInitScript(()=>{window.sent=[];window.CefSharp={PostMessage:s=>sent.push(JSON.parse(s))};});await page.goto(file);
  const receive=m=>page.evaluate(m=>Orbit.receive(m),m),last=k=>page.evaluate(k=>sent.filter(m=>m.kind===k).at(-1),k);
  await page.locator('[data-cmd="settings"]').click();await page.locator('[data-cmd="support"]').click();let request=await last('supportStatus');
  assert.ok(request.requestId);assert.match(await page.locator('.support-privacy').innerText(),/Без API-ключей/);
  await receive({kind:'supportStatus',requestId:request.requestId,version:'0.10.0.0',writable:true,recent:[{timeUtc:'2026-10-26T08:00:00Z',id:'0123456789abcdef',name:'ui.callback.failed',component:'<img src=x onerror=alert(1)>'}]});
  assert.equal(await page.locator('.support-event img').count(),0);assert.match(await page.locator('.support-event').innerText(),/11:00:00/);assert.match(await page.locator('.support-event code').innerText(),/01234567/);
  await page.locator('[data-support="logs"]').click();assert.ok(await last('supportOpenLogs'));
  await page.locator('[data-support="export"]').click();let exportRequest=await last('supportExport');assert.equal(await page.locator('[data-support="export"]').isDisabled(),true);
  await receive({kind:'supportSaved',requestId:'stale',fileName:'wrong.zip'});assert.equal(await page.locator('[data-support="export"]').isDisabled(),true);
  await receive({kind:'supportCancelled',requestId:exportRequest.requestId});assert.equal(await page.locator('[data-support="export"]').isDisabled(),false);assert.match(await page.locator('#support-result').innerText(),/отменено/);
  await page.locator('[data-support="export"]').click();exportRequest=await last('supportExport');await receive({kind:'supportError',requestId:exportRequest.requestId,message:'<script>disk failure</script>'});assert.equal(await page.locator('#support-result script').count(),0);assert.equal(await page.locator('[data-support="export"]').isDisabled(),false);
  await page.locator('[data-support="export"]').click();exportRequest=await last('supportExport');await receive({kind:'supportSaved',requestId:exportRequest.requestId,fileName:'Orbit-report.zip'});assert.match(await page.locator('#support-result').innerText(),/Orbit-report.zip/);
  await page.locator('[data-support="refresh"]').click();const current=await last('supportStatus');await receive({kind:'supportStatus',requestId:request.requestId,version:'old',writable:true,recent:[]});assert.equal(await page.locator('#support-version').innerText(),'0.10.0.0');
  await receive({kind:'supportStatus',requestId:current.requestId,version:'0.10.0.0',writable:false,recent:[]});assert.match(await page.locator('#support-log-state').innerText(),/Не удаётся/);assert.equal(await page.locator('.support-ok').count(),1);
  fs.mkdirSync(path.join(root,'qa'),{recursive:true});await page.screenshot({path:path.join(root,'qa/support.png')});
  await page.setViewportSize({width:880,height:640});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.locator('[data-support="export"]').scrollIntoViewIfNeeded();assert.equal(await page.locator('[data-support="export"]').isVisible(),true);
  await page.keyboard.press('Escape');await page.waitForTimeout(180);
  await page.evaluate(()=>{const error=new Error('APIKEY=DO_NOT_SEND');dispatchEvent(new ErrorEvent('error',{message:error.message,error,filename:'file:///C:/Users/private/app.js?token=DO_NOT_SEND',lineno:42}));});
  const fault=await last('uiFault');assert.deepEqual(fault,{kind:'uiFault',type:'error',component:'app.js',line:42});assert.equal(await page.locator('#ui-health-notice').count(),1);
  const before=await page.evaluate(()=>sent.filter(x=>x.kind==='uiFault').length);
  await page.evaluate(()=>dispatchEvent(new ErrorEvent('error',{filename:'file:///app.js',lineno:42})));assert.equal(await page.evaluate(()=>sent.filter(x=>x.kind==='uiFault').length),before);
  await page.evaluate(()=>dispatchEvent(new PromiseRejectionEvent('unhandledrejection',{promise:Promise.resolve(),reason:{secret:'DO_NOT_SEND'}})));assert.equal((await last('uiFault')).type,'promise');assert.doesNotMatch(JSON.stringify(await page.evaluate(()=>sent)),/DO_NOT_SEND/);
  await page.locator('#ui-health-notice button').first().click();assert.equal(await page.locator('#modal').getAttribute('data-kind'),'support');
  await page.goto(file+'?demo=1');await page.locator('[data-cmd="settings"]').click();await page.locator('[data-cmd="support"]').click();await page.locator('[data-support="export"]').click();await page.locator('[data-support="logs"]').click();await page.evaluate(()=>dispatchEvent(new ErrorEvent('error',{filename:'file:///app.js',lineno:8})));assert.equal(await page.evaluate(()=>sent.some(x=>x.kind.startsWith('support')||x.kind==='uiFault')),false);
  // If app.js cannot initialize, the independent error notice can still invoke the native report dialog.
  await page.route('**/app.js',route=>route.abort());await page.goto(file);await page.locator('#ui-health-notice button').first().click();assert.ok(await last('supportExport'));
  assert.deepEqual(errors,[]);console.log('SUPPORT UI PASS: status, safe events, local time, export/cancel/retry/stale IDs, demo isolation, 880px, privacy of JS errors, throttling, promise errors and report fallback when app.js fails. Native dialogs simulated.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
