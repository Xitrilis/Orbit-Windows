const {chromium}=require('playwright'),path=require('path'),assert=require('node:assert/strict'),{pathToFileURL}=require('url');
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.ORBIT_CHROME_PATH,args:['--no-sandbox']});try{
 const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(String(e)));
 for(const [width,height] of [[880,640],[1024,640],[1366,768],[1920,1080],[2560,1440]]){
 await page.setViewportSize({width,height});await page.goto(pathToFileURL(path.resolve(__dirname,'../../engine/Extensions/Orbit/ui/index.html')).href+'?demo=1');await page.waitForSelector('#app-splash',{state:'detached'});
 await page.locator('[data-section="friends"]').click();
 for(const id of ['friend-lobby-input','friend-server-input']){const v=await page.locator('#'+id).evaluate(e=>({height:e.getBoundingClientRect().height,radius:parseFloat(getComputedStyle(e).borderRadius),font:parseFloat(getComputedStyle(e).fontSize)}));assert.ok(v.height>=36&&v.radius>=6&&v.font>=11,JSON.stringify(v));}
 await page.keyboard.press('Escape');await page.waitForTimeout(500);await page.locator('[data-section="minecraft"]').click();await page.locator('[data-hz="horizon"]').click();
 await page.locator('[data-hz="download"]').click();assert.equal(await page.locator('#toast').isVisible(),true);await page.locator('[data-hz="settings"]').click();assert.equal(await page.locator('#toast').isVisible(),false);await page.keyboard.press('Escape');await page.waitForTimeout(500);
 const bad=await page.evaluate(()=>[...document.querySelectorAll('input,select,button')].filter(e=>e.getBoundingClientRect().width&&e.getBoundingClientRect().height).filter(e=>parseFloat(getComputedStyle(e).fontSize)<11).map(e=>e.id||e.className));assert.deepEqual(bad,[]);
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await page.evaluate(()=>document.documentElement.classList.add('effects-lite'));await page.locator('[data-section="all"]').click();await page.locator('#search').focus();assert.equal(await page.locator('.search').evaluate(e=>getComputedStyle(e).outlineStyle),'solid');
 await page.evaluate(()=>{const s=document.querySelector('.sidebar-scroll');s.scrollTop=s.scrollHeight;});await page.waitForTimeout(100);assert.equal(await page.locator('.sidebar-scroll').getAttribute('data-at-end'),'true');
 }
 assert.equal(await page.locator('#ui-health-notice').count(),0);assert.deepEqual(errors,[]);console.log('Form audit passed: five sizes, friends fields, minimum control font, Horizon modal/toast, light-mode focus, sidebar end, no page overflow');
 }finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1)});
