const {chromium}=require('playwright'),assert=require('node:assert/strict'),path=require('node:path'),{pathToFileURL}=require('node:url');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.ORBIT_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:880,height:640}}),errors=[];
  page.on('pageerror',e=>errors.push(String(e)));await page.route('https://**/*',r=>r.abort());
  await page.addInitScript(()=>{window.sent=[];window.CefSharp={PostMessage:s=>sent.push(JSON.parse(s))};});
  await page.goto(pathToFileURL(path.resolve(__dirname,'../../engine/Extensions/Orbit/ui/index.html')).href);
  const receive=m=>page.evaluate(m=>Orbit.receive(m),m),last=k=>page.evaluate(k=>sent.filter(m=>m.kind===k).at(-1),k);
  const id='12345678-0000-0000-0000-123456789012';
  await receive({kind:'snapshot',data:{games:[{id,name:'Cyberpunk 2077',store:'steam',installed:true}],prefs:{},stores:[],downloads:[]}});
  await page.locator('[data-section=all]').click();await page.locator('#search').evaluate(el=>el.dispatchEvent(new KeyboardEvent('keydown',{code:'KeyF',key:'f',ctrlKey:true,bubbles:true})));assert.ok(await page.locator('#quick-search-backdrop').isHidden());await page.locator('#sort').focus();
  await page.evaluate(()=>document.dispatchEvent(new KeyboardEvent('keydown',{code:'KeyF',key:'а',ctrlKey:true,bubbles:true})));
  await page.locator('#quick-search-input').fill('кибер');
  assert.equal(await page.locator('.quick-search-item[data-qs-kind=library]').count(),1);
  await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');
  assert.equal((await last('play')).id,id);assert.ok(await page.locator('#quick-search-backdrop').isHidden());
  assert.ok(await page.locator('#sort').evaluate(el=>document.activeElement===el));
  await page.keyboard.press('Control+f');await page.locator('#quick-search-input').fill('alpha');await page.waitForTimeout(500);
  const stale=await last('pricesSearch');assert.ok(stale.requestId.startsWith('qs-'));
  await page.locator('#quick-search-input').fill('beta');await page.waitForTimeout(500);const current=await last('pricesSearch');
  const response=(request,name)=>({kind:'pricesSearchResult',requestId:request.requestId,data:{products:[{token:name,id:name,name,store:'steam'}]}});
  await receive(response(stale,'STALE RESULT'));assert.doesNotMatch(await page.locator('#quick-search-results').innerText(),/STALE RESULT/);
  await receive(response(current,'CURRENT RESULT'));assert.match(await page.locator('#quick-search-results').innerText(),/CURRENT RESULT/);
  await page.screenshot({path:path.resolve(__dirname,'../../qa/redesign-search.png')});
  const box=await page.locator('.quick-search-box').boundingBox();assert.ok(box.x>=0&&box.x+box.width<=880);
  await page.keyboard.press('Escape');await receive(response(current,'LATE RESULT'));assert.ok(await page.locator('#quick-search-backdrop').isHidden());
  assert.deepEqual(errors,[]);console.log('PASS redesign search');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
