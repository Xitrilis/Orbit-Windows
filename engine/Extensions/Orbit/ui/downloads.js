(()=>{
 'use strict';
 const $=s=>document.querySelector(s),finite=v=>typeof v==='number'&&Number.isFinite(v)&&v>=0;
 const bytes=v=>!finite(v)?'—':v>=1e9?(v/1e9).toLocaleString('ru',{maximumFractionDigits:2})+' ГБ':v>=1e6?(v/1e6).toLocaleString('ru',{maximumFractionDigits:1})+' МБ':v>=1000?(v/1000).toLocaleString('ru',{maximumFractionDigits:1})+' КБ':Math.round(v)+' Б';
 const speed=v=>!finite(v)?'—':bytes(v)+'/с';
 const time=v=>!finite(v)?'—':v>=86400?'> 24 ч':v>=3600?Math.floor(v/3600)+' ч '+Math.floor(v%3600/60)+' мин':v>=60?Math.floor(v/60)+' мин '+Math.floor(v%60)+' с':Math.ceil(v)+' с';
 const phases={downloading:'Скачивается',processing:'Установка файлов',preparing:'Подготовка',waiting:'Ожидаем данные',attention:'Нужна проверка',external:'В клиенте магазина',complete:'Установлена'};
 let api,jobs=[],selected='',open=false,demo=false,queueKey='',returnFocus=null,hideAnimation,plot=[];
 const current=()=>jobs.find(j=>j.id===selected),phase=j=>j.state==='complete'?'complete':j.state==='attention'?'attention':j.state==='preparing'?'preparing':j.metrics?.phase||'waiting';
 const store=j=>OrbitCore.stores.find(s=>s.id===j.store)?.name||'Магазин';
 const text=(id,v)=>{const e=$(id);if(e&&e.textContent!==String(v))e.textContent=v;};
 function animate(el,frames,duration=260){if(!el?.animate||document.documentElement.classList.contains('motion-off')||matchMedia('(prefers-reduced-motion:reduce)').matches)return null;return el.animate(frames,{duration,easing:'cubic-bezier(.22,.8,.25,1)'});}
 function init(a){api=a;$('#dl-cover').onerror=e=>e.target.hidden=true;$('#dl-chart').addEventListener('pointermove',hover);$('#dl-chart').addEventListener('pointerleave',()=>$('#dl-tooltip').hidden=true);render();}
 function update(next,isDemo=false){jobs=Array.isArray(next)?next.slice(0,100):[];demo=isDemo;if(!current())selected=(jobs.find(j=>phase(j)==='downloading')||jobs.find(j=>j.state!=='complete')||jobs[0])?.id||'';render();}
 function show(){
  if(open)return;hideAnimation?.cancel();hideAnimation=null;returnFocus=document.activeElement;open=true;
  const view=$('#download-view');view.hidden=false;$('main').inert=true;$('main').setAttribute('aria-hidden','true');$('.workspace').classList.add('downloads-open');$('.download-dock-button').setAttribute('aria-expanded','true');render();
  animate(view,[{opacity:0,transform:'translateY(12px)'},{opacity:1,transform:'translateY(0)'}]);view.querySelector('[data-download="back"]').focus({preventScroll:true});
 }
 function hide(focus=true){
  if(!open)return;open=false;$('main').inert=false;$('main').removeAttribute('aria-hidden');$('.workspace').classList.remove('downloads-open');$('.download-dock-button').setAttribute('aria-expanded','false');
  const view=$('#download-view');hideAnimation=animate(view,[{opacity:1,transform:'translateY(0)'},{opacity:0,transform:'translateY(8px)'}],160);
  view.style.pointerEvents='none';if(hideAnimation)hideAnimation.onfinish=()=>{if(!open)view.hidden=true;view.style.pointerEvents='';};else{view.hidden=true;view.style.pointerEvents='';}if(focus&&returnFocus?.isConnected)returnFocus.focus({preventScroll:true});
 }
 function render(){
  if(!api)return;renderDock();if(!open)return;$('#download-view').style.pointerEvents='';$('.dl-demo').hidden=!demo;$('#dl-empty').hidden=jobs.length>0;$('#dl-content').hidden=jobs.length===0;const j=current();if(!j)return;
  const m=j.metrics||{},p=phase(j);text('#dl-store',store(j));text('#dl-phase',phases[p]||'Ожидание');$('#dl-phase').dataset.phase=p;text('#dl-name',j.name);text('#dl-path',j.path||'Папка уточняется в клиенте');$('#dl-path').title=j.path||'';
  const cover=$('#dl-cover');cover.style.cssText=OrbitCore.cropStyle(j.coverCrop||{});if(cover.getAttribute('src')!==(j.cover||'')){if(j.cover){cover.src=j.cover;cover.hidden=false;}else{cover.removeAttribute('src');cover.hidden=true;}}
  const percent=j.state==='complete'?100:finite(m.percent)?m.percent:finite(j.percent)?j.percent:null,bar=$('.dl-progress');bar.classList.toggle('indeterminate',percent===null&&p==='preparing');
  if(percent===null)bar.removeAttribute('aria-valuenow');else bar.setAttribute('aria-valuenow',Math.min(100,percent));
  $('#dl-progress-fill').style.width=percent===null?'0':Math.min(100,percent)+'%';
  text('#dl-percent',percent===null?'Объём уточняется':percent.toLocaleString('ru',{maximumFractionDigits:1})+'%');
  text('#dl-progress-label',p==='processing'?'Скачано · установка ещё идёт':p==='complete'?'Готово к запуску':'Загружено');
  text('#dl-volume',finite(m.downloaded)&&finite(m.total)?bytes(m.downloaded)+' / '+bytes(m.total):j.message||'Ожидаем данные клиента');
  text('#dl-staged',m.toStage>0&&finite(m.staged)?'Обработано '+bytes(m.staged)+' / '+bytes(m.toStage):'');
  text('#dl-speed',speed(p==='complete'||p==='processing'?null:m.speed));text('#dl-remaining',p==='complete'?'Готово':bytes(m.remaining));text('#dl-eta',p==='complete'?'Готово':p==='processing'?'Установка':finite(m.etaSeconds)?'≈ '+time(m.etaSeconds):'Уточняется');
  text('#dl-elapsed',time(m.elapsedSeconds));text('#dl-elapsed-label',j.createdAt?'ПРОШЛО':'НАБЛЮДАЕМ');text('#dl-elapsed-note',j.createdAt?'с запуска установки':'с открытия Orbit');text('#dl-peak',speed(m.peak));
  text('#dl-source',(m.source?m.source+' · ':'')+(m.note||'Подробные показатели пока недоступны.'));
  text('#dl-client','Открыть '+store(j)+' ↗');$('#dl-stop').hidden=j.state!=='preparing';$('#dl-play').hidden=j.state!=='complete';$('#dl-dismiss').hidden=j.state!=='complete';drawGraph(m);renderQueue();
 }
 function renderDock(){
  const active=jobs.filter(j=>j.state!=='complete'),j=active.find(j=>phase(j)==='downloading')||active[0],m=j?.metrics||{};
  text('#dock-title',j?j.name:'Загрузки');text('#dock-subtitle',j?(demo?'ДЕМО · ':'')+store(j)+' · '+(phases[phase(j)]||'Ожидание')+(active.length>1?' · ещё '+(active.length-1):''):jobs.length?'Все установки завершены':'Нет активных установок');
  text('#dock-speed',j?speed(m.speed):'');text('#dock-eta',j&&finite(m.etaSeconds)?'≈ '+time(m.etaSeconds):'');
  const percent=finite(m.percent)?m.percent:finite(j?.percent)?j.percent:0;$('#dock-progress-fill').style.width=Math.min(100,percent)+'%';$('.dock-progress').hidden=!j;$('.download-dock-button').classList.toggle('has-download',!!j);
 }
 function renderQueue(){
  const key=JSON.stringify(jobs.map(j=>[j.id,j.name,j.store,j.cover]));
  if(key!==queueKey){queueKey=key;const fragment=document.createDocumentFragment();for(const j of jobs){
   const row=document.createElement('button');row.className='dl-queue-item';row.dataset.downloadId=j.id;
   row.innerHTML='<span class="dl-queue-art"></span><span class="dl-queue-name"><strong></strong><small></small></span><span class="dl-queue-state"></span><span class="dl-queue-size"></span><span class="dl-queue-arrow">→</span>';
   row.querySelector('strong').textContent=j.name;row.querySelector('small').textContent=store(j);
   if(j.cover){const img=document.createElement('img');img.src=j.cover;img.alt='';img.loading='lazy';img.onerror=()=>img.hidden=true;row.querySelector('.dl-queue-art').append(img);}
   fragment.append(row);
  }$('#dl-queue').replaceChildren(fragment);}
  text('#dl-count',jobs.length);document.querySelectorAll('.dl-queue-item').forEach((e,i)=>{const j=jobs[i],m=j.metrics||{};const art=e.querySelector('img');if(art)art.style.cssText=OrbitCore.cropStyle(j.coverCrop||{});e.classList.toggle('selected',j.id===selected);e.setAttribute('aria-pressed',j.id===selected);e.querySelector('.dl-queue-state').textContent=phases[phase(j)]||'Ожидание';e.querySelector('.dl-queue-size').textContent=j.state==='complete'?'Готово':finite(m.percent)?m.percent.toLocaleString('ru',{maximumFractionDigits:1})+'%':'—';});
 }
 function drawGraph(m){
  plot=(m.history||[]).filter(p=>finite(p.at)&&(p.speed==null||finite(p.speed))).slice(-120);const values=plot.filter(p=>finite(p.speed)),top=Math.max(1e6,...values.map(p=>p.speed))*1.15;
  const end=plot.length?plot[plot.length-1].at:Date.now()/1000,start=end-300;
  const points=plot.map(p=>({x:Math.max(0,Math.min(800,(p.at-start)/300*800)),y:finite(p.speed)?158-Math.min(1,p.speed/top)*154:null,at:p.at}));let segments=[],segment=[];
  for(let i=0;i<points.length;i++){const p=points[i];if(p.y===null||(i>0&&p.at-points[i-1].at>15)){if(segment.length)segments.push(segment);segment=[];}if(p.y!==null)segment.push(p);}if(segment.length)segments.push(segment);
  const line=segments.filter(s=>s.length>1).map(s=>'M'+s.map(p=>p.x.toFixed(2)+','+p.y.toFixed(2)).join(' L')).join(' ');
  const area=segments.filter(s=>s.length>1).map(s=>'M'+s.map(p=>p.x.toFixed(2)+','+p.y.toFixed(2)).join(' L')+' L'+s.at(-1).x.toFixed(2)+',158 L'+s[0].x.toFixed(2)+',158 Z').join(' ');
  $('#dl-line').setAttribute('d',line);$('#dl-area').setAttribute('d',area);text('#dl-scale-top',speed(top));text('#dl-scale-mid',speed(top/2));$('#dl-chart-placeholder').hidden=!!line;
  const last=points.at(-1);$('#dl-end').setAttribute('visibility',last?.y!=null?'visible':'hidden');if(last?.y!=null){$('#dl-end').setAttribute('cx',last.x);$('#dl-end').setAttribute('cy',last.y);}
 }
 function hover(e){
  const el=$('#dl-tooltip'),svg=$('#dl-svg').getBoundingClientRect();if(!plot.length){el.hidden=true;return;}
  const t=plot.at(-1).at-300+Math.max(0,Math.min(1,(e.clientX-svg.left)/svg.width))*300,p=plot.reduce((a,b)=>Math.abs(a.at-t)<Math.abs(b.at-t)?a:b);
  if(Math.abs(p.at-t)>10||!finite(p.speed)){el.hidden=true;return;}
  el.textContent=new Date(p.at*1000).toLocaleTimeString('ru',{hour:'2-digit',minute:'2-digit',second:'2-digit'})+' · '+speed(p.speed);el.hidden=false;el.style.left=Math.max(64,Math.min($('#dl-chart').clientWidth-155,e.clientX-$('#dl-chart').getBoundingClientRect().left-60))+'px';
 }
 document.addEventListener('click',e=>{
  const b=e.target.closest('button');if(!b||!api)return;const d=b.dataset;
  if(d.downloadId){selected=d.downloadId;render();animate($('.dl-active-copy'),[{opacity:.35,transform:'translateY(4px)'},{opacity:1,transform:'translateY(0)'}],180);return;}
  const j=current();switch(d.download){case'back':hide();break;case'client':if(j)api.send('client',{store:j.store});break;case'stop':if(j&&j.state==='preparing')api.send('cancelInstall',{id:j.id});break;case'play':if(j&&j.state==='complete')api.send('play',{id:j.gameId});break;case'dismiss':if(j&&j.state==='complete')api.send('dismissJob',{id:j.id});break;}
 });
 window.OrbitDownloads={init,update,show,hide,isOpen:()=>open,format:{bytes,speed,time}};
})();
