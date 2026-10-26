const {chromium}=require('playwright'),path=require('node:path'),fs=require('node:fs'),assert=require('node:assert/strict'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'../..'),file=pathToFileURL(path.join(root,'engine/Extensions/Orbit/ui/index.html')).href;
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.ORBIT_CHROME_PATH||undefined,args:['--no-sandbox']});
 try{
  fs.mkdirSync(path.join(root,'qa'),{recursive:true});
  const page=await browser.newPage({viewport:{width:1360,height:950},timezoneId:'Europe/Moscow'}),errors=[];page.on('pageerror',e=>errors.push(String(e)));
  await page.clock.setFixedTime(new Date('2026-10-25T18:00:00Z'));
  await page.addInitScript(()=>{window.sent=[];window.CefSharp={PostMessage:s=>sent.push(JSON.parse(s))};});await page.goto(file);
  const receive=m=>page.evaluate(m=>Orbit.receive(m),m),last=k=>page.evaluate(k=>sent.filter(m=>m.kind===k).at(-1),k);
  const data=await page.evaluate(()=>OrbitDemo());
  data.activity=[
   {id:'s3',gameId:'demo-0',name:'Hogwarts Legacy',store:'steam',startedAt:'2026-10-25T10:05:00Z',state:'running'},
   {id:'s2',gameId:'demo-hog-epic',name:'Hogwarts Legacy',store:'epic',startedAt:'2026-10-24T20:10:00Z',endedAt:'2026-10-24T21:10:00Z',seconds:3600,state:'completed'},
   {id:'s1',gameId:'demo-0',name:'Hogwarts Legacy',store:'steam',startedAt:'2026-10-23T10:05:00Z',state:'interrupted'},
   {id:'removed',gameId:'gone',name:'<img src=x onerror=alert(1)>',store:'gog',startedAt:'2026-10-22T09:00:00Z',seconds:120,state:'completed'}
  ];data.backgroundClients={enabled:true,hiddenStores:['steam','gog']};await receive({kind:'snapshot',data});
  assert.equal(await page.locator('.recent-mini').count(),4);assert.match(await page.locator('.recent-mini').first().innerText(),/Hogwarts Legacy[\s\S]*13:05[\s\S]*В игре/);
  await page.locator('.recent-mini').first().click();assert.equal(await page.locator('.detail-history p').count(),3);assert.match(await page.locator('.detail-history').textContent(),/Epic Games/);
  await page.locator('[data-activity="all"]').click();assert.equal(await page.locator('#modal-backdrop').isVisible(),false);assert.equal(await page.locator('#activity-view').isVisible(),true);
  await page.locator('#activity-search').fill('Hogwarts');assert.equal(await page.locator('.recent-game').count(),1);assert.match(await page.locator('.recent-game').innerText(),/3 запуска/);
  await page.locator('[data-activity-mode="sessions"]').click();assert.equal(await page.locator('.activity-session').count(),3);assert.equal(await page.locator('.activity-day').count(),3);
  assert.match(await page.locator('.activity-session').last().innerText(),/Окончание сеанса неизвестно/);
  await page.locator('#activity-store').selectOption('epic');assert.equal(await page.locator('.activity-session').count(),1);assert.match(await page.locator('.activity-session').innerText(),/23:10/);
  await page.keyboard.press('Control+k');assert.equal(await page.locator('#activity-search').evaluate(e=>e===document.activeElement),true);
  data.activity[0]={...data.activity[0],state:'completed',endedAt:'2026-10-25T11:05:00Z',seconds:3600};await receive({kind:'snapshot',data});
  assert.equal(await page.locator('#activity-search').inputValue(),'Hogwarts');assert.equal(await page.locator('#activity-store').inputValue(),'epic');
  await page.locator('#activity-store').selectOption('');await page.locator('#activity-search').fill('');
  assert.equal(await page.locator('.activity-session-copy img').count(),0);assert.match(await page.locator('#activity-rows').textContent(),/Последняя дата из библиотеки/);
  assert.equal(await page.locator('.activity-removed').count(),1);
  await page.screenshot({path:path.join(root,'qa/activity-timeline.png')});
  await page.locator('.activity-session [data-activity-game="demo-0"]').first().click();await page.locator('.detail-actions [data-action]').click();assert.equal((await last('play')).id,'demo-0');
  assert.equal(await page.locator('.detail-history p').count(),3,'Play click does not create a successful session in UI');await page.keyboard.press('Escape');await page.waitForTimeout(180);
  await page.locator('[data-cmd="settings"]').click();assert.equal(await page.locator('[data-cmd="backgroundClients"]').getAttribute('aria-checked'),'true');
  assert.match(await page.locator('#clients-status').textContent(),/Steam, GOG/);
  await page.locator('[data-cmd="backgroundClients"]').click();assert.equal((await last('prefs')).patch.backgroundClients,false);
  await page.locator('[data-cmd="showClients"]').click();assert.ok(await last('showClients'));await page.locator('[data-cmd="hideClients"]').click();assert.ok(await last('hideClients'));
  data.prefs.backgroundClients=false;data.backgroundClients.hiddenStores=[];await receive({kind:'snapshot',data});assert.match(await page.locator('#clients-status').textContent(),/Скрытых Orbit окон сейчас нет/);
  await page.screenshot({path:path.join(root,'qa/activity-settings.png')});await page.keyboard.press('Escape');await page.waitForTimeout(180);
  await page.setViewportSize({width:880,height:640});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth&&document.querySelector('main').scrollWidth<=document.querySelector('main').clientWidth),true);
  await page.screenshot({path:path.join(root,'qa/activity-small.png')});
  const elapsed=await page.evaluate(()=>{const games=Array.from({length:1000},(_,i)=>({id:'large-'+i,name:'Large Game '+i,store:'steam',installed:false,genres:[]})),activity=Array.from({length:2000},(_,i)=>({id:'session-'+i,gameId:'large-'+(i%1000),name:'Large Game '+(i%1000),store:'steam',startedAt:new Date(Date.UTC(2026,9,25)-i*60000).toISOString(),state:'completed',seconds:60}));const start=performance.now();Orbit.receive({kind:'snapshot',data:{games,activity,prefs:{},stores:[],downloads:[]}});return performance.now()-start;});
  assert.equal(await page.locator('.activity-session').count(),80);await page.locator('#activity-search').fill('Large Game 999');assert.equal(await page.locator('.activity-session').count(),2);await page.locator('[data-activity-mode="games"]').click();assert.equal(await page.locator('.recent-game').count(),1);assert.match(await page.locator('.recent-game').innerText(),/2 запуска/);await page.locator('#activity-search').fill('');console.log('Large-library snapshot (1000 games / 2000 sessions): '+Math.round(elapsed)+' ms');
  await receive({kind:'snapshot',data:{games:[],prefs:{},activity:[],activityWarning:'<img src=x onerror=alert(1)>'}});assert.equal(await page.locator('#activity-warning img').count(),0);assert.equal(await page.locator('#activity-empty').isVisible(),true);
  await page.goto(file+'?demo=1');await page.locator('[data-cmd="settings"]').click();await page.locator('[data-cmd="showClients"]').click();await page.locator('[data-cmd="hideClients"]').click();assert.equal(await page.evaluate(()=>sent.some(x=>['showClients','hideClients'].includes(x.kind))),false);
  assert.deepEqual(errors,[]);console.log('ACTIVITY UI PASS: home shelf, local dates, merged history, session timeline, filters, preserved state, removed games, escaped text, play bridge, background settings/commands, 880px and demo isolation. Native bridge simulated.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
