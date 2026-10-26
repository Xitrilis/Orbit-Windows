const {chromium}=require('playwright');
const path=require('node:path'),fs=require('node:fs'),assert=require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'../..');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.ORBIT_CHROME_PATH||undefined,args:['--no-sandbox']});
 try{
  const page=await browser.newPage({viewport:{width:500,height:900}});const errors=[];page.on('pageerror',e=>errors.push(String(e)));
  await page.addInitScript(()=>{window.sent=[];window.CefSharp={PostMessage:s=>window.sent.push(JSON.parse(s))};});
  const url=pathToFileURL(path.join(root,'engine/Extensions/Orbit/ui/assistant.html')).href;
  await page.goto(url);assert.equal((await page.evaluate(()=>window.sent[0])).kind,'ready');
  const last=k=>page.evaluate(kind=>window.sent.filter(x=>x.kind===kind).at(-1),k);
  const receive=m=>page.evaluate(message=>window.OrbitAssistant.receive(message),m);
  const state={kind:'state',selected:{id:'game-1',name:'Hogwarts Legacy',store:'steam',installed:true},games:[{id:'game-1',name:'Hogwarts Legacy',store:'steam',installed:true}],settings:{hasKey:true,model:'gemini-test-flash',shortcut:'ctrlShiftSpace',noSpoilers:true,hotkeyStatus:'Горячая клавиша активна.'},models:[],history:[],frame:{url:'assets/990080-hero.jpg',width:1600,height:900,capturedAt:'12:34:56'},notice:'Тестовый кадр. Реального захвата и подключения нет.'};
  await receive(state);fs.mkdirSync(path.join(root,'qa'),{recursive:true});await page.screenshot({path:path.join(root,'qa/assistant-chat.png')});
  await page.locator('.suggestion').first().click();assert.equal(await last('ask'),undefined);
  await page.locator('#attach-frame').uncheck();await page.locator('[data-cmd="ask"]').click();assert.equal((await last('ask')).includeFrame,false);assert.equal((await last('ask')).includeDiagnostics,false);
  await page.locator('#attach-frame').check();await page.locator('#question').fill('Объясни кадр');await page.keyboard.press('Control+Enter');assert.equal((await last('ask')).includeFrame,true);
  await receive({...state,busy:true,pendingQuestion:'Объясни кадр'});await page.locator('[data-cmd="cancel"]').click();assert.equal((await last('cancel')).kind,'cancel');
  await receive({...state,history:[{role:'user',text:'Объясни кадр',image:true},{role:'model',text:'<img src=x onerror=alert(1)> **Это ответ**'}]});assert.equal(await page.locator('.message-content img').count(),0);assert.match(await page.locator('.message.model').textContent(),/Это ответ/);
  await page.locator('[data-cmd="removeFrame"]').click();assert.equal((await last('removeFrame')).kind,'removeFrame');
  const report={gameId:'game-1',gameName:'Hogwarts Legacy',checkedAt:'23.10.2026 12:30',checks:[{title:'Папка игры',state:'ok',detail:'D:\\Games\\Hogwarts Legacy'},{title:'События Windows',state:'warning',detail:'Найдено одно сообщение об ошибке.'}],events:[{id:1000,time:'12:20',source:'Application Error',text:'<script>bad()</script>'}],note:'Отсутствие событий не доказывает отсутствие ошибок.'};
  await receive({...state,diagnostic:report});await page.locator('[data-tab="diagnostics"]').click();assert.equal(await page.locator('.check').count(),2);assert.equal(await page.locator('.event script').count(),0);await page.screenshot({path:path.join(root,'qa/assistant-diagnostics.png')});
  await page.locator('[data-cmd="askReport"]').click();assert.equal(await page.locator('#attach-report').isChecked(),true);assert.equal((await last('ask')).includeDiagnostics,false);await page.locator('[data-cmd="ask"]').click();assert.equal((await last('ask')).includeDiagnostics,true);
  await page.locator('[data-tab="settings"]').click();await page.locator('#api-key').fill('not-a-real-key');await page.locator('#hotkey').selectOption('ctrlAltSpace');await page.locator('[data-cmd="saveTest"]').click();assert.equal((await last('saveSettings')).key,'not-a-real-key');assert.equal((await last('saveSettings')).test,true);assert.equal(await page.locator('#api-key').inputValue(),'');assert.equal((await last('saveSettings')).shortcut,'ctrlAltSpace');await page.screenshot({path:path.join(root,'qa/assistant-settings.png')});
  await page.keyboard.press('Escape');assert.equal((await last('hide')).kind,'hide');
  await page.setViewportSize({width:400,height:600});await page.locator('[data-tab="chat"]').click();assert.equal(await page.locator('#question').isVisible(),true);assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:path.join(root,'qa/assistant-small.png')});
  await page.goto(url+'?demo=1');await page.locator('.suggestion').first().click();await page.locator('[data-cmd="ask"]').click();assert.equal(await page.evaluate(()=>window.sent.length),0);assert.equal(await page.locator('#demo-label').isVisible(),true);
  assert.deepEqual(errors,[]);console.log('Assistant UI PASS: preview, explicit attachments, draft-only suggestions, cancellation, escaped answers/logs, diagnostic consent, key field clearing, settings, keyboard, compact window and demo isolation.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
