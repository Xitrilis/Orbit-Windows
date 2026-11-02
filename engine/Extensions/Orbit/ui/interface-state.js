(()=>{'use strict';
 const scroll=document.querySelector('.sidebar-scroll');
 if(scroll){const wrap=document.createElement('div');wrap.className='sidebar-scroll-wrap';scroll.before(wrap);wrap.append(scroll);const hint=document.createElement('span');hint.className='sidebar-scroll-hint';hint.textContent='Ещё ниже ↓';hint.setAttribute('aria-hidden','true');wrap.append(hint);
 const update=()=>{const overflow=scroll.scrollHeight>scroll.clientHeight+4;const end=!overflow||scroll.scrollTop+scroll.clientHeight>=scroll.scrollHeight-4;scroll.dataset.overflow=String(overflow);scroll.dataset.atEnd=String(end);hint.hidden=end;};
 let frame=0;const schedule=()=>{if(!frame)frame=requestAnimationFrame(()=>{frame=0;update();});};scroll.addEventListener('scroll',schedule,{passive:true});new ResizeObserver(schedule).observe(scroll);new MutationObserver(schedule).observe(scroll,{childList:true,subtree:true});schedule();}
 const pause=()=>document.documentElement.classList.toggle('ui-background',document.hidden||!document.hasFocus());window.addEventListener('blur',pause);window.addEventListener('focus',pause);document.addEventListener('visibilitychange',pause);
 document.addEventListener('click',e=>{if(e.target.closest('[data-section],[data-store]')){const t=document.querySelector('#toast');if(t)t.hidden=true;}},true);
})();
