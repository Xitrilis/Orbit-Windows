using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Playnite.SDK.Models;
using Forms=System.Windows.Forms;
namespace Orbit {
 public sealed partial class OrbitPlugin {
  DiagnosticsLog diagnosticLog;bool exportingSupport;DateTime lastUiError=DateTime.MinValue;
  void Safe(Action action) {
   if(exiting)return;
   try{action();}catch(Exception ex) when(!(ex is OutOfMemoryException)&&!(ex is AccessViolationException)){
    var code=diagnosticLog?.Record("ui.callback.failed",ex);
    if(DateTime.UtcNow-lastUiError>TimeSpan.FromSeconds(15)){lastUiError=DateTime.UtcNow;try{Send(new{kind="error",message="Не удалось выполнить действие. Отчёт доступен в Настройках → Помощь.",diagnosticCode=code});}catch{}}
   }
  }
  void Unhandled(object sender,UnhandledExceptionEventArgs args){diagnosticLog?.Record("app.unhandled",args.ExceptionObject as Exception);}
  void DispatcherFailed(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args){diagnosticLog?.Record("app.unhandled",args.Exception);/* Keep Playnite's own crash handling; never mark arbitrary failures handled. */}
  void TaskFailed(object sender,UnobservedTaskExceptionEventArgs args){diagnosticLog?.Record("app.task.failed",args.Exception);}
  void LogResponse(JObject message) {
   var kind=(string)message["kind"];
   if(kind!=null&&(kind=="error"||kind.EndsWith("Error",StringComparison.Ordinal))&&message["diagnosticCode"]==null) {
    var component=new[]{"home","prices","wallet","artwork","assistant","downloads","support"}.FirstOrDefault(x=>kind.StartsWith(x,StringComparison.Ordinal))??"host";
    message["diagnosticCode"]=diagnosticLog?.Record("operation.failed",component:component);
   }
  }
  SupportContext SupportState()=>new SupportContext{games=PlayniteApi.Database.Games.Count,installed=PlayniteApi.Database.Games.Count(g=>g.IsInstalled),sessions=playHistory?.Snapshot().Length,downloads=jobs.Count,hiddenClients=backgroundClients?.HiddenStores.Length,backgroundEnabled=(bool?)prefs["backgroundClients"]!=false};
  async void SupportStatus(string requestId) {
   try {var events=await Task.Run(()=>DiagnosticsLog.Read(diagnosticLog.DirectoryPath));if(exiting)return;
   Send(new{kind="supportStatus",requestId,version=typeof(OrbitPlugin).Assembly.GetName().Version.ToString(),writable=diagnosticLog.Writable,
    recent=events.Where(e=>e.name.Contains("failed")||e.name.Contains("warning")||e.name.EndsWith(".recovered",StringComparison.Ordinal)||e.name=="app.unhandled"||e.name=="ui.error").Reverse().Take(6).Select(e=>new{e.timeUtc,e.id,e.name,e.component})});
   }catch(Exception ex){diagnosticLog?.Record("report.failed",ex,"support");if(!exiting)Send(new{kind="supportError",requestId,message="Не удалось прочитать журнал. Повтори запрос."});}
  }
  async void ExportSupport(string requestId=null) {
   if(exportingSupport){Send(new{kind="supportError",requestId,message="Отчёт уже создаётся."});return;}
   exportingSupport=true;
   try {
    using(var dialog=new Forms.SaveFileDialog{Title="Сохранить отчёт Orbit",Filter="ZIP-архив отчёта (*.zip)|*.zip",DefaultExt="zip",AddExtension=true,OverwritePrompt=true,FileName="Orbit-report-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"}) {
     if(dialog.ShowDialog(new WindowOwner(window))!=Forms.DialogResult.OK){Send(new{kind="supportCancelled",requestId});return;}
     var destination=dialog.FileName;var context=SupportState();
     await Task.Run(()=>SupportBundle.Create(root,destination,diagnosticLog,context));
     diagnosticLog.Record("report.created",component:"support");
     if(!exiting){if(requestId==null)Forms.MessageBox.Show("Отчёт сохранён: "+destination+"\nПрикрепи этот ZIP в чат для разбора ошибки.","Orbit",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Information);else Send(new{kind="supportSaved",requestId,fileName=Path.GetFileName(destination)});}
    }
   }catch(Exception ex){diagnosticLog?.Record("report.failed",ex,"support");if(!exiting){if(requestId==null)Forms.MessageBox.Show("Не удалось сохранить отчёт. Выбери другую доступную папку.","Orbit",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Error);else Send(new{kind="supportError",requestId,message="Не удалось сохранить отчёт. Выбери другую доступную папку."});}}
   finally{exportingSupport=false;}
  }
  async void IntegrityCheck() {
   try {
    var orbitRoot = Path.GetFullPath(Path.Combine(root, ".."));
    var manifestFile = Path.Combine(orbitRoot, "RuntimeFiles.sha256");
    if (!File.Exists(manifestFile)) { if (!exiting) Send(new { kind = "toast", message = "Манифест RuntimeFiles.sha256 не найден рядом с Orbit.exe." }); return; }
    var manifest = File.ReadAllText(manifestFile);
    var issues = await Task.Run(() => StartupIntegrity.Check(orbitRoot, manifest, CancellationToken.None));
    if (exiting) return;
    diagnosticLog?.Record(issues.Count > 0 ? "startup.integrity.failed" : "startup.integrity.ok", component: "support", number: issues.Count);
    if (issues.Count == 0) { Send(new { kind = "toast", message = "Целостность файлов Orbit подтверждена." }); return; }
    Send(new { kind = "toast", message = "Найдено расхождений с манифестом: " + issues.Count + ". Подробности — в журнале." });
    PlayniteApi.Dialogs.ShowErrorMessage("Найдено расхождений: " + issues.Count + ".\n\n" + string.Join("\n", issues.Take(10)) + "\n\nПолный список — в журнале Orbit (Настройки → Поддержка).", "Orbit — проверка целостности");
   } catch (Exception e) { diagnosticLog?.Record("report.failed", e, "support"); if (!exiting) Send(new { kind = "toast", message = "Не удалось выполнить проверку целостности." }); }
  }
  void OpenLogs() {
   Directory.CreateDirectory(diagnosticLog.DirectoryPath);
   System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(diagnosticLog.DirectoryPath){UseShellExecute=true});
  }
 }
}
