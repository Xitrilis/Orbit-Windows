(()=>{
 'use strict';
 const $=s=>document.querySelector(s),esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const slots=['icon','cover','background'],labels={icon:'Значок',cover:'Обложка',background:'Баннер'},hints={icon:'Квадратный значок · PNG с прозрачностью или ICO',cover:'Вертикальная обложка · удобно 2:3',background:'Широкий фон · удобно 16:9 или шире'};
 let api,group,draft,baseline,selected='background',session='',openRequest='',pickRequest='',saveRequest='',busy=false,ready=false,active=false,generation=0,error='',fileName='';
 const request=()=>Date.now().toString(36)+'-'+Math.random().toString(36).slice(2);
 const clean=a=>({url:a?.url||'',original:a?.original??a?.url??'',custom:!!a?.custom,x:Number.isFinite(a?.x)?a.x:50,y:Number.isFinite(a?.y)?a.y:50,zoom:Number.isFinite(a?.zoom)?a.zoom:1,mode:'keep',token:null});
 const dirty=()=>JSON.stringify(draft)!==baseline;
 function init(a){api=a;$('#art-demo-file').addEventListener('change',demoFile);}
 function show(g){
  if(!g)return;if($('#toast'))$('#toast').hidden=true;closed();generation++;active=true;group=g;selected='background';ready=api.demo();error='';fileName='';
  draft=Object.fromEntries(slots.map(s=>[s,clean(g.artwork?.[s]||{url:s==='icon'?g.icon:s==='cover'?g.cover:g.background})]));baseline=JSON.stringify(draft);
  const choices=slots.map(s=>'<div class="art-choice" data-art-choice="'+s+'"><button class="art-select" data-art-slot="'+s+'"><span class="art-thumb art-thumb-'+s+'"><img id="art-thumb-'+s+'" alt="" hidden><b>'+(s==='icon'?'o.':s==='cover'?'▧':'▱')+'</b></span><span><strong>'+labels[s]+'</strong><small id="art-kind-'+s+'"></small></span></button><button class="art-replace" data-art-upload="'+s+'" aria-label="Заменить '+labels[s].toLowerCase()+'" title="Выбрать изображение">+</button></div>').join('');
  api.modal('artwork','<div class="artwork-body"><div class="art-header"><p class="eyebrow">ТВОЯ КОЛЛЕКЦИЯ / ОФОРМЛЕНИЕ</p><h2 id="modal-title">Сделай игру своей<span>.</span></h2><p class="intro">'+esc(g.name)+' · '+(g.copies.length>1?'Применится ко всем '+g.copies.length+' копиям этой карточки.':'Только для этой игры.')+'</p></div>'+(api.demo()?'<p class="art-demo">Демонстрация · изменения сохраняются только до закрытия этого сеанса.</p>':'')+'<div class="art-layout"><div class="art-live"><img id="art-live-background" class="art-live-bg" alt="" hidden><div class="art-live-shade"></div><span class="art-preview-label">ПРЕДПРОСМОТР</span><div class="art-live-copy"><span class="art-live-icon"><img id="art-live-icon" alt="" hidden><b>o.</b></span><div><small>ТВОЯ КОЛЛЕКЦИЯ</small><h3>'+esc(g.name)+'</h3><span>Твоё оформление. Твои правила.</span></div></div><div class="art-live-cover"><img id="art-live-cover" alt="" hidden><span>ORBIT</span></div></div><div class="art-choices" role="group" aria-label="Элемент оформления">'+choices+'<p>Нажми +, чтобы выбрать картинку.<br>Настрой кадр с помощью ползунков.</p></div></div><div class="art-controls"><div class="art-controls-head"><div><h3 id="art-selected-title"></h3><p id="art-hint"></p></div><button class="secondary" data-art="upload">Выбрать файл</button><button class="quiet" data-art="reset">Сбросить</button></div><div class="art-sliders"><label><span>По горизонтали <output id="art-x-value"></output></span><input id="art-x" type="range" min="0" max="100" step="1" aria-label="Положение по горизонтали"></label><label><span>По вертикали <output id="art-y-value"></output></span><input id="art-y" type="range" min="0" max="100" step="1" aria-label="Положение по вертикали"></label><label><span>Масштаб <output id="art-zoom-value"></output></span><input id="art-zoom" type="range" min="1" max="2" step=".01" aria-label="Масштаб изображения"></label></div></div><div class="art-status" role="status" id="art-status"></div><p class="art-note">PNG, JPG, BMP до 20 МБ; для значка — также ICO. Исходные изображения магазинов сохраняются. Изменения применятся после нажатия «Сохранить».</p></div><div class="art-footer"><button class="quiet" data-art="resetAll">Вернуть всё исходное</button><div><button class="secondary" data-art="cancel">Отменить</button><button class="primary" data-art="save">Сохранить оформление</button></div></div>');
  render();if(!api.demo()){openRequest=request();api.send('artworkOpen',{id:g.selected.id,requestId:openRequest});}
 }
 function closed(){generation++;if(session&&api&&!api.demo())api.send('artworkCancel',{session});session='';openRequest='';pickRequest='';saveRequest='';active=false;busy=false;ready=false;}
 function render(){
  if(!active||!api.isOpen())return;
  for(const s of slots){
   const a=draft[s];for(const id of ['#art-live-'+s,'#art-thumb-'+s]){const img=$(id);if(img.getAttribute('src')!==a.url){if(a.url)img.src=a.url;else img.removeAttribute('src');img.hidden=!a.url;img.onerror=()=>{img.hidden=true;};}img.style.cssText=OrbitCore.cropStyle(a);}
   const choice=$('[data-art-choice="'+s+'"]');choice.classList.toggle('selected',s===selected);choice.querySelector('.art-select').setAttribute('aria-pressed',String(s===selected));
   $('#art-kind-'+s).textContent=a.custom?'Своё изображение':a.x!==50||a.y!==50||a.zoom!==1?'Свой кадр':'Из магазина';
  }
  const a=draft[selected];$('#art-selected-title').textContent=labels[selected];$('#art-hint').textContent=hints[selected];
  for(const k of ['x','y','zoom']){const input=$('#art-'+k);if(document.activeElement!==input)input.value=a[k];$('#art-'+k+'-value').textContent=k==='zoom'?Math.round(a[k]*100)+'%':Math.round(a[k])+'%';input.disabled=busy||!ready||!a.url;}
  const canChange=ready&&!busy;document.querySelectorAll('[data-art-upload],[data-art="upload"],[data-art="reset"],[data-art="resetAll"]').forEach(b=>b.disabled=!canChange);
  $('[data-art="save"]').disabled=!canChange||!dirty();$('[data-art="save"]').textContent=saveRequest?'Сохраняем…':'Сохранить оформление';
  const status=$('#art-status');status.classList.toggle('error',!!error);status.textContent=error||(busy?'Подготавливаем изображение…':!ready?'Открываем настройки…':fileName?'Выбрано: '+fileName:dirty()?'Есть несохранённые изменения.':'Выбери изображение или измени положение кадра.');
 }
 function pick(slot=selected){if(!ready||busy)return;selected=slot;error='';render();if(api.demo()){$('#art-demo-file').value='';$('#art-demo-file').click();return;}busy=true;pickRequest=request();render();api.send('artworkPick',{session,slot,requestId:pickRequest});}
 async function demoFile(e){
  const file=e.target.files?.[0],stamp=generation,slot=selected;if(!file||!active)return;error='';busy=true;render();
  try{
   if(file.size>20*1024*1024||!/\.(png|jpe?g|bmp|ico)$/i.test(file.name)||(slot!=='icon'&&/\.ico$/i.test(file.name)))throw new Error('Выбери PNG, JPG или BMP до 20 МБ. ICO подходит для значка.');
   const raw=await new Promise((resolve,reject)=>{const r=new FileReader();r.onload=()=>resolve(r.result);r.onerror=reject;r.readAsDataURL(file);});
   const img=await new Promise((resolve,reject)=>{const i=new Image();i.onload=()=>resolve(i);i.onerror=()=>reject(new Error('Не удалось прочитать изображение.'));i.src=raw;});
   if(img.naturalWidth*img.naturalHeight>40e6)throw new Error('Выбери изображение до 40 мегапикселей.');
   const max=slot==='icon'?512:slot==='cover'?1800:2560,scale=Math.min(1,max/Math.max(img.naturalWidth,img.naturalHeight)),canvas=document.createElement('canvas');canvas.width=Math.max(1,Math.round(img.naturalWidth*scale));canvas.height=Math.max(1,Math.round(img.naturalHeight*scale));canvas.getContext('2d').drawImage(img,0,0,canvas.width,canvas.height);
   if(stamp!==generation||!active)return;draft[slot]={...draft[slot],url:canvas.toDataURL('image/png'),custom:true,mode:'set',token:'demo',x:50,y:50,zoom:1};fileName=file.name;
  }catch(err){if(stamp===generation)error=err.message||'Не удалось открыть изображение.';}finally{if(stamp===generation){busy=false;render();}}
 }
 function reset(slot){draft[slot]={...draft[slot],url:draft[slot].original,custom:false,mode:'original',token:null,x:50,y:50,zoom:1};error='';fileName='';}
 function save(){
  if(!ready||busy||!dirty())return;
  if(api.demo()){const result=JSON.parse(JSON.stringify(draft)),key=group.key;api.saveDemo(group,result);closed();api.details(key);api.toast('Оформление изменено в демонстрации.');return;}
  error='';busy=true;saveRequest=request();const changes=Object.fromEntries(slots.map(s=>{const a=draft[s];return[s,{mode:a.mode,token:a.token,x:a.x,y:a.y,zoom:a.zoom}];}));render();api.send('artworkSave',{session,requestId:saveRequest,ids:group.copies.map(g=>g.id),changes});
 }
 function receive(m){
  if(!m.kind?.startsWith('artwork'))return false;
  if(m.kind==='artworkOpened'){
   if(!active||!api.isOpen()||m.requestId!==openRequest){if(m.session)api.send('artworkCancel',{session:m.session});return true;}
   session=m.session;draft=Object.fromEntries(slots.map(s=>[s,clean({...m.artwork?.[s],url:m.artwork?.[s]?.url||draft[s].url,original:m.artwork?.[s]?.original||draft[s].original})]));baseline=JSON.stringify(draft);ready=true;render();return true;
  }
  if(!active||!api.isOpen()||(m.session&&m.session!==session))return true;
  if(m.kind==='artworkPicked'&&m.requestId===pickRequest){busy=false;pickRequest='';if(!m.cancelled&&slots.includes(m.slot)){draft[m.slot]={...draft[m.slot],url:m.url,custom:true,token:m.token,mode:'set',x:50,y:50,zoom:1};fileName=m.name||'';}render();}
  else if(m.kind==='artworkSaved'&&m.requestId===saveRequest){const key=group.key;session='';closed();api.details(key);api.toast('Оформление сохранено.');}
  else if(m.kind==='artworkError'&&[openRequest,pickRequest,saveRequest].includes(m.requestId)){error=m.message||'Не удалось изменить оформление.';busy=false;pickRequest='';saveRequest='';render();}return true;
 }
 document.addEventListener('click',e=>{
  if(!active||!api?.isOpen())return;const b=e.target.closest('button');if(!b)return;
  if(b.dataset.artSlot){selected=b.dataset.artSlot;fileName='';render();}if(b.dataset.artUpload)pick(b.dataset.artUpload);
  switch(b.dataset.art){case'upload':pick();break;case'reset':reset(selected);render();break;case'resetAll':slots.forEach(reset);render();break;case'cancel':{const k=group.key;closed();api.details(k);break;}case'save':save();break;}
 });
 document.addEventListener('input',e=>{if(!active||!ready||busy||!api.isOpen())return;const key=e.target.id?.replace('art-','');if(['x','y','zoom'].includes(key)){draft[selected][key]=Number(e.target.value);if(draft[selected].mode==='keep')draft[selected].mode='adjust';error='';render();}});
 window.OrbitArtwork={init,show,closed,receive};
})();
