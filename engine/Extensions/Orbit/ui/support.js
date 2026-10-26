(()=>{
 'use strict';
 const $=s=>document.querySelector(s),esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 let api,statusRequest='',exportRequest='',serial=0,timer;
 const label=name=>({'ui.error':'Ошибка интерфейса','ui.promise.failed':'Ошибка фонового запроса интерфейса','ui.resource.failed':'Не загрузился модуль интерфейса','browser.load.failed':'Не загрузилась страница Orbit','ui.callback.failed':'Не выполнено действие интерфейса','ui.command.failed':'Ошибка команды','app.start.failed':'Ошибка открытия Orbit','app.unhandled':'Необработанная ошибка','app.task.failed':'Ошибка фоновой задачи','startup.failed':'Ошибка запуска','startup.integrity.failed':'Повреждён компонент сборки','operation.failed':'Не завершена операция','downloads.failed':'Не обновились показатели загрузки','install.failed':'Ошибка подготовки установки','history.warning':'Проблема хранения истории','clients.warning':'Проблема сохранения состояния окон','report.failed':'Не сохранён отчёт'}[name]||'Техническое событие');
 const id=()=> 'support-'+Date.now()+'-'+(++serial);
 function show(){
  if(!api)return;
  api.modal('support',`<div class="modal-body support-body"><p class="eyebrow">ORBIT / SUPPORT</p><h2 id="modal-title">Разберёмся вместе.</h2><p class="intro">Если что-то пошло не так, сохрани отчёт и прикрепи ZIP в чат.</p><div class="support-summary"><span class="support-mark">⌁</span><div><strong>Локальный журнал</strong><p id="support-log-state">Проверяем доступность…</p></div><span id="support-version">0.11</span></div><div class="support-privacy"><strong>Что попадёт в отчёт</strong><p>Версия Orbit, параметры среды, количество игр и технические события: время, код, тип ошибки.</p><p>Без API-ключей, паролей, cookies, платёжных данных, названий игр, заметок, путей сборок Minecraft, переписки с ИИ и снимков экрана. Исходный журнал Playnite не прикладывается.</p></div><div class="support-actions"><button class="primary" data-support="export">Сохранить отчёт ZIP</button><button class="secondary" data-support="logs">Папка логов ↗</button></div><p id="support-result" role="status"></p><div class="support-events-heading"><h3>Последние неполадки</h3><button class="quiet" data-support="refresh">Обновить</button></div><div id="support-events"><p class="note">Загружаем события…</p></div><p class="note support-bottom">Ничего не отправляется автоматически. Журнал ограничен по размеру и обновляется по кругу. Если Orbit не открывается — запусти Orbit-Diagnostics.cmd из папки приложения.</p></div>`);
  if(exportRequest){$('#support-result').textContent='Выбери место сохранения в окне Windows. Если окно закрыто — ждём завершения отчёта.';$('[data-support="export"]').disabled=true;}
  refresh();
 }
 function refresh(){
  if(api.demo()||!window.CefSharp?.PostMessage){$('#support-log-state').textContent='Доступен в Orbit.exe на Windows';$('#support-events').innerHTML='<p class="note">В демонстрации события и файлы не создаются.</p>';return;}
  statusRequest=id();clearTimeout(timer);timer=setTimeout(()=>{if(api.isOpen()&&$('#support-log-state'))$('#support-log-state').textContent='Ответ не получен. Можно повторить запрос или воспользоваться Orbit-Diagnostics.cmd.';},10000);api.send('supportStatus',{requestId:statusRequest});
 }
 function receive(m){
  if(!['supportStatus','supportSaved','supportCancelled','supportError'].includes(m.kind))return false;
  if(m.kind==='supportStatus'){
   if(m.requestId!==statusRequest||!api.isOpen())return true;clearTimeout(timer);
   $('#support-version').textContent=m.version||'0.11';$('#support-log-state').textContent=m.writable?'Записывается только на этом ПК':'Не удаётся записывать лог. Проверь доступ к папке Orbit.';
   $('#support-events').innerHTML=(m.recent||[]).length?m.recent.map(e=>`<article class="support-event"><div><strong>${esc(label(e.name))}</strong><small>${esc(e.component)} · ${esc(new Date(e.timeUtc).toLocaleString('ru-RU'))}</small></div><code>${esc(String(e.id||'').slice(0,8))}</code></article>`).join(''):'<p class="support-ok">В доступном журнале неполадок не найдено.</p>';return true;
  }
  if(m.kind==='supportError'&&m.requestId===statusRequest){clearTimeout(timer);if(api.isOpen()){$('#support-log-state').textContent=m.message||'Не удалось прочитать журнал.';$('#support-events').textContent='Повтори запрос кнопкой «Обновить».';}return true;}
  if(m.requestId!==exportRequest)return true;exportRequest='';
  const text=m.kind==='supportSaved'?'Отчёт сохранён: '+m.fileName+'. Прикрепи этот ZIP в чат.':m.kind==='supportCancelled'?'Сохранение отменено.':m.message||'Не удалось сохранить отчёт.';
  if(api.isOpen()){$('#support-result').textContent=text;$('[data-support="export"]').disabled=false;}else api.toast(text);
  return true;
 }
 function init(a){api=a;document.addEventListener('click',e=>{
  const action=e.target.closest('[data-support]')?.dataset.support;if(!action)return;
  if(action==='refresh'){refresh();return;}
  if(api.demo()||!window.CefSharp?.PostMessage){$('#support-result').textContent='Отчёты доступны в Orbit.exe на Windows. Демонстрация ничего не сохраняет.';return;}
  if(action==='logs'){api.send('supportOpenLogs');return;}
  if(action==='export'&&!exportRequest){exportRequest=id();$('[data-support="export"]').disabled=true;$('#support-result').textContent='Выбери место сохранения в окне Windows.';api.send('supportExport',{requestId:exportRequest});}
 });}
 window.OrbitSupport={init,show,receive,ready:()=>!!api};
})();
