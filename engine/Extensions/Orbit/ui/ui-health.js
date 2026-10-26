(()=>{
 'use strict';
 const files=new Set(['journal-model.js','journal.js','minecraft.js','core.js','app.js','home.js','prices.js','sales.js','wallet.js','artwork.js','activity.js','downloads.js','support.js','ui-health.js','assistant.js']);
 const sent=new Map();let last=null;
 function record(type,component,line){
  component=files.has(component)?component:'browser';line=Number.isFinite(line)?Math.max(0,Math.min(1000000,Math.floor(line))):0;
  const key=type+':'+component+':'+line,now=Date.now();if(now-(sent.get(key)||0)<10000)return;if(sent.size>100)sent.clear();sent.set(key,now);
  // Never send error messages, stack strings, URLs, rejection values, DOM content or console output.
  last={type,component,line};if(new URLSearchParams(location.search).get('demo')!=='1'&&window.CefSharp?.PostMessage)try{CefSharp.PostMessage(JSON.stringify({kind:'uiFault',...last}));}catch{}
  if(document.readyState!=='loading')show();
 }
 function show(){
  if(!last||document.getElementById('ui-health-notice'))return;
  const box=document.createElement('div');box.id='ui-health-notice';box.setAttribute('role','alert');box.className='ui-health-notice';
  const text=document.createElement('span');text.textContent='Произошла ошибка интерфейса. Сохрани отчёт, если что-то не работает.';
  const report=document.createElement('button');report.textContent='Сохранить отчёт';report.addEventListener('click',()=>{
   if(window.OrbitSupport?.ready())OrbitSupport.show();
   else if(new URLSearchParams(location.search).get('demo')!=='1'&&window.CefSharp?.PostMessage)try{CefSharp.PostMessage(JSON.stringify({kind:'supportExport'}));}catch{}
  });
  const close=document.createElement('button');close.textContent='×';close.setAttribute('aria-label','Скрыть сообщение');close.addEventListener('click',()=>box.remove());
  box.append(text,report,close);document.body.append(box);
 }
 addEventListener('error',e=>{
  if(e.target!==window){if(e.target?.tagName==='SCRIPT')record('resource',(e.target.src||'').split(/[?#]/)[0].split('/').pop(),0);return;}
  record('error',(e.filename||'').split(/[?#]/)[0].split('/').pop(),e.lineno);
 },true);
 addEventListener('unhandledrejection',()=>record('promise','browser',0));
 document.addEventListener('DOMContentLoaded',show);
})();
