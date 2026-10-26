const {chromium}=require('playwright'),path=require('node:path'),fs=require('node:fs'),assert=require('node:assert/strict'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'../..'),file=pathToFileURL(path.join(root,'engine/Extensions/Orbit/ui/index.html')).href,qa=path.join(root,'qa');
(async()=>{
 fs.mkdirSync(qa,{recursive:true});const browser=await chromium.launch({headless:true,executablePath:process.env.ORBIT_CHROME_PATH||undefined,args:['--no-sandbox']});
 try{
 const page=await browser.newPage({viewport:{width:1360,height:950}}),errors=[];page.on('pageerror',e=>errors.push(String(e)));
 await page.addInitScript(()=>{window.sent=[];window.CefSharp={PostMessage:s=>window.sent.push(JSON.parse(s))};});await page.goto(file);
 const receive=m=>page.evaluate(m=>Orbit.receive(m),m),last=kind=>page.evaluate(k=>sent.filter(m=>m.kind===k).at(-1),kind);
 const id='11111111-1111-1111-1111-111111111111',copy='22222222-2222-2222-2222-222222222222';
 const game={id,name:'Cyberpunk 2077',store:'steam',installed:true,cover:'assets/1091500.jpg',background:'assets/1091500-hero.jpg'},data={games:[game,{...game,id:copy,store:'gog'}],prefs:{},stores:[],downloads:[]};
 const visual=()=>Object.fromEntries(['icon','cover','background'].map(s=>[s,{url:s==='icon'?'':game[s],original:s==='icon'?'':game[s],custom:false,x:50,y:50,zoom:1}]));
 await receive({kind:'snapshot',data});await page.locator('[data-section="all"]').click();await page.locator('.poster').click();await page.locator('[data-customize]').click();
 let opened=await last('artworkOpen');assert.equal(opened.id,id);assert.equal(await page.locator('[data-art="save"]').isDisabled(),true);
 let session='session-1';await receive({kind:'artworkOpened',requestId:opened.requestId,session,artwork:visual()});
 await page.waitForTimeout(350);await page.screenshot({path:path.join(qa,'Orbit-artwork.png')});
 const change=async(selector,value)=>page.locator(selector).evaluate((e,v)=>{e.value=v;e.dispatchEvent(new Event('input',{bubbles:true}));},value);
 await change('#art-y','78');await change('#art-zoom','1.35');
 assert.match(await page.locator('#art-live-background').getAttribute('style'),/78%/);assert.match(await page.locator('#art-live-background').getAttribute('style'),/scale\(1.35\)/);
 await page.locator('[data-art-upload="cover"]').click();let picked=await last('artworkPick');assert.equal(picked.slot,'cover');
 await receive({kind:'artworkPicked',requestId:picked.requestId,session,cancelled:true});assert.equal(await page.locator('[data-art="upload"]').isEnabled(),true);
 await page.locator('[data-art-upload="icon"]').click();picked=await last('artworkPick');
 await receive({kind:'artworkPicked',requestId:picked.requestId,session,slot:'icon',token:'test-icon',url:'assets/1145360.jpg',name:'<img src=x onerror=alert(1)>.png'});
 assert.equal(await page.locator('#art-status img').count(),0);assert.match(await page.locator('#art-status').textContent(),/<img/);
 await page.locator('[data-art="save"]').click();let saved=await last('artworkSave');assert.deepEqual(new Set(saved.ids),new Set([id,copy]));assert.equal(saved.changes.icon.mode,'set');assert.equal(saved.changes.icon.token,'test-icon');assert.equal(saved.changes.background.y,78);assert.equal(saved.changes.background.mode,'adjust');assert.equal(saved.changes.cover.mode,'keep');
 await receive({kind:'artworkError',requestId:saved.requestId,session,message:'Нет места на диске'});assert.match(await page.locator('#art-status').textContent(),/Нет места/);assert.equal(await page.locator('[data-art="save"]').isEnabled(),true);
 await page.locator('[data-art="save"]').click();saved=await last('artworkSave');const art=visual();art.icon={url:'assets/1145360.jpg',original:'',custom:true,x:50,y:50,zoom:1};art.background.y=78;art.background.zoom=1.35;
 data.games.forEach(g=>{g.icon=art.icon.url;g.artwork=art;});await receive({kind:'snapshot',data});await receive({kind:'artworkSaved',requestId:saved.requestId,session});
 assert.equal(await page.locator('#modal').getAttribute('data-kind'),'detail');assert.equal(await page.locator('.detail-name img').getAttribute('src'),'assets/1145360.jpg');assert.equal(await page.locator('.game-custom-icon img').getAttribute('src'),'assets/1145360.jpg');
 await page.locator('[data-customize]').click();opened=await last('artworkOpen');session='session-2';await receive({kind:'artworkOpened',requestId:opened.requestId,session,artwork:art});
 await page.locator('[data-art="resetAll"]').click();assert.equal(await page.locator('#art-live-icon').isVisible(),false);
 await page.locator('[data-art="cancel"]').click();assert.equal((await last('artworkCancel')).session,session);assert.equal(await page.locator('.detail-name img').getAttribute('src'),'assets/1145360.jpg','Cancel must not modify saved icon');
 await page.locator('[data-customize]').click();opened=await last('artworkOpen');session='session-3';await receive({kind:'artworkOpened',requestId:opened.requestId,session,artwork:art});
 await page.locator('[data-art-upload="background"]').click();picked=await last('artworkPick');await page.keyboard.press('Escape');await page.locator('#modal-backdrop').waitFor({state:'hidden'});
 await receive({kind:'artworkPicked',requestId:picked.requestId,session,slot:'background',url:'assets/1145360.jpg',token:'late',name:'late.png'});assert.equal(await page.locator('#modal-backdrop').isHidden(),true,'Late import must not reopen editor');
 await page.locator('[data-view="list"]').click();assert.equal(await page.locator('.row-art-frame.is-icon img').getAttribute('src'),'assets/1145360.jpg');
 await page.goto(file+'?demo=1');await page.locator('[data-section="all"]').click();await page.locator('.poster').first().click();await page.locator('[data-customize]').click();
 // Exercise real browser decoding and canvas preview with a local fixture.
 await page.locator('[data-art-slot="icon"]').click();await page.locator('#art-demo-file').setInputFiles(path.join(root,'engine/Extensions/Orbit/ui/assets/1145360.jpg'));
 await page.waitForFunction(()=>document.querySelector('#art-live-icon').getAttribute('src')?.startsWith('data:image/png'));
 await page.locator('[data-art-slot="background"]').click();await change('#art-y','35');
 await page.locator('[data-art="save"]').click();assert.equal(await page.locator('#modal').getAttribute('data-kind'),'detail');assert.ok((await page.locator('.detail-name img').getAttribute('src')).startsWith('data:image/png'));
 assert.equal(await page.evaluate(()=>sent.length),0,'Demo never sends native messages');
 await page.locator('[data-customize]').click();await page.setViewportSize({width:880,height:640});await page.waitForTimeout(350);await page.screenshot({path:path.join(qa,'Orbit-artwork-small.png')});
 assert.equal(await page.locator('#modal').evaluate(e=>e.scrollWidth<=e.clientWidth+1),true);
 const box=await page.locator('[data-art="save"]').boundingBox();assert.ok(box.y>=0&&box.y+box.height<=640&&box.x+box.width<=880,'Save must remain visible at 880x640');
 await page.locator('[data-art="resetAll"]').click();await page.locator('[data-art="save"]').click();assert.equal(await page.locator('.detail-name img').count(),0,'Demo reset restores original icon');
 assert.deepEqual(errors,[]);console.log('Artwork UI PASS: crop preview, native file selection bridge, cancel/retry/save, duplicate copy IDs, icon display, reset, late replies, demo image decoding/isolation, 880x640 editor.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
