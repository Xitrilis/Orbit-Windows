(()=>{'use strict';
 const $=s=>document.querySelector(s),root=document.documentElement;
 let send,loaded=false,rendered=false,started=false,finished=false,token=null,nativeWaiting=false,released=false,reduced=false,slowTimer,animations=[];
 const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
 function init(post){
  send=post;root.classList.add('orbit-starting');$('#app').inert=true;
  $('#startup-retry').addEventListener('click',()=>{status('Повторно запрашиваем библиотеку…');send('ready');});
  $('#startup-report').addEventListener('click',()=>send('supportExport',{}));
  slowTimer=setTimeout(()=>{if(finished||started)return;$('#startup-title').textContent='Загрузка задержалась';status('Интерфейс ещё не готов. Повтори запрос или сохрани отчёт.');$('#startup-actions').hidden=false;},25000);
 }
 function status(value){const el=$('#startup-status');if(el)el.textContent=value;}
 function ready(data){
  if(finished||started)return;
  if(!loaded){loaded=true;token=data?.startup?.token||null;nativeWaiting=!!(token&&data?.startup?.waiting);reduced=!!data?.prefs?.reducedMotion||matchMedia('(prefers-reduced-motion: reduce)').matches;}
  requestAnimationFrame(()=>requestAnimationFrame(()=>{
   if(finished||started)return;rendered=true;
   if(nativeWaiting&&!released){send('startupRendered',{token});return;}
   complete();
  }));
 }
 function receive(m){
  if(m.kind!=='startupComplete')return false;
  if(!nativeWaiting||m.token!==token||finished)return true;
  released=true;if(rendered)complete();return true;
 }
 function animate(el,frames,options){if(!el||!el.animate)return Promise.resolve();const animation=el.animate(frames,{fill:'forwards',...options});animations.push(animation);return animation.finished.catch(()=>{});}
 function finish(){
  if(finished)return;finished=true;clearTimeout(slowTimer);animations.forEach(a=>a.cancel());animations=[];
  root.classList.remove('orbit-starting','startup-revealing');$('#app-splash')?.remove();
  // Another dialog may have opened while the library was arriving.
  $('#app').inert=!$('#modal-backdrop').hidden||!!window.OrbitQuickSearch?.isOpen();
  document.dispatchEvent(new CustomEvent('orbit-startup-finished'));
 }
 async function complete(){
  if(started||finished||!rendered)return;started=true;clearTimeout(slowTimer);
  $('#startup-actions').hidden=true;$('#startup-title').textContent='Приятного использования';status('');$('#app-splash').classList.add('startup-ready');
  if(reduced){finish();return;}
  await pause(650);if(finished)return;
  const icon=$('#startup-logo'),target=$('.brand svg'),from=icon.getBoundingClientRect(),to=target.getBoundingClientRect(),dx=to.left-from.left,dy=to.top-from.top,scale=to.width/from.width;
  root.classList.add('startup-revealing');
  const disappear=[{opacity:1,transform:'translateY(0)'},{opacity:0,transform:'translateY(-6px)'}];
  const travel=[{transform:'translate(0,0) scale(1)'},{offset:.78,transform:`translate(${dx}px,${dy-7}px) scale(${scale*1.05})`},{transform:`translate(${dx}px,${dy}px) scale(${scale})`}];
  // A deadline also completes when Windows suspends painting a minimized window.
  const safety=setTimeout(finish,1600);
  await Promise.all([animate(icon,travel,{duration:780,easing:'cubic-bezier(.22,1,.36,1)'}),animate($('#startup-copy'),disappear,{duration:360}),animate($('#startup-track'),[{opacity:1},{opacity:0}],{duration:300}),animate($('#startup-surface'),[{opacity:1},{opacity:0}],{duration:500})]);
  clearTimeout(safety);finish();
 }
 addEventListener('resize',()=>{if(started&&!finished)finish();});
 document.addEventListener('visibilitychange',()=>{if(document.hidden&&started)finish();});
 window.OrbitStartup={init,loaded:ready,receive};
})();
