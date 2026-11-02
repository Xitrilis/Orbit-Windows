using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Orbit;
class OrbitBootstrap {
 [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [STAThread]static void Main(string[] args) {
  try{SetProcessDPIAware();}catch{}
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  var root=AppDomain.CurrentDomain.BaseDirectory;var data=Path.Combine(root,"engine","OrbitData");
  var log=new DiagnosticsLog(Path.Combine(data,"Logs"),"bootstrap");log.Record("startup.begin",component:"bootstrap");
  AppDomain.CurrentDomain.UnhandledException+=(sender,e)=>log.Record("app.unhandled",e.ExceptionObject as Exception,"bootstrap");
  try {
   string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()))).Replace("-","");
   bool first;using(var gate=new Mutex(true,"Local\\Orbit-"+key,out first)) {
    if(!first){Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"show.request"),DateTime.UtcNow.ToString("O"));return;}
    var engine=Path.Combine(root,"engine");var exe=Path.Combine(engine,"Playnite.DesktopApp.exe");bool diagnose=args.Contains("--diagnose"),running=false;
    if(!diagnose)foreach(var p in Process.GetProcessesByName("Playnite.DesktopApp"))using(p)try {
     if(!string.Equals(p.MainModule.FileName,exe,StringComparison.OrdinalIgnoreCase)) {
      MessageBox.Show("Сначала закрой отдельно запущенный Playnite. Orbit использует собственную копию основы, чтобы не менять твою прежнюю библиотеку.","Orbit",MessageBoxButtons.OK,MessageBoxIcon.Information);return;
     }
     running=true;
    }catch{}
    if(running){Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"show.request"),DateTime.UtcNow.ToString("O"));return;}
    var issues=RunStartup(root,exe,diagnose,log);
    if(issues==null){log.Record("startup.cancelled",component:"bootstrap");return;}
    string bundle=diagnose?DiagnosticBundle(root,log,issues):null;
    if(issues.Count>0) {
     var report=Report(root,issues);
     MessageBox.Show("Сборка Orbit распакована не полностью или её файлы изменились.\n\n"+string.Join("\n",issues.Take(5))+"\n\nРаспакуй новый архив целиком в новую папку. Старую папку с настройками пока сохрани.\n\n"+ReportLocation(report)+(bundle!=null?"\nZIP для разбора: "+bundle:""),"Orbit — проверка запуска",MessageBoxButtons.OK,MessageBoxIcon.Error);return;
    }
    if(diagnose) {
     var report=Report(root,new[]{"Все встроенные компоненты прошли проверку размеров и SHA-256. Это не проверяет драйверы и блокировки Windows."});
     MessageBox.Show("Файлы сборки целые.\n\n"+(bundle!=null?"Для разбора ошибки пришли созданный ZIP. Он содержит технические события без ключей и переписки.":"Не удалось сохранить ZIP. Проверь доступ к папке Orbit; текстовый результат проверки указан ниже.")+"\n\n"+ReportLocation(report)+(bundle!=null?"\nZIP для разбора: "+bundle:""),"Orbit — проверка запуска",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
   }
  }catch(Exception e) {
   log.Record("startup.failed",e,"bootstrap");var report=Report(root,new[]{e.GetType().Name+": "+e.Message,"HResult: 0x"+e.HResult.ToString("X8")});
   MessageBox.Show("Не удалось запустить Orbit.\n\n"+e.Message+"\n\n"+ReportLocation(report),"Orbit — запуск",MessageBoxButtons.OK,MessageBoxIcon.Error);
  }
 }
 static List<string> RunStartup(string root,string exe,bool diagnose,DiagnosticsLog log) {
  List<string> result=null;Exception failure=null;StartupTicket ticket=null;bool launched=false;
  var data=Path.Combine(root,"engine","OrbitData");
  using(var form=new StartupWindow(root)) {
   var cancel=form.Cancellation.Token;
   form.FormClosing+=(s,e)=>{if(!form.Finishing&&launched&&ticket!=null)try{StartupHandoff.Release(data,ticket.Token,"hide");}catch{}};
   form.Shown+=async(s,e)=>{
    try {
     var update=new Progress<int>(n=>{if(!form.IsDisposed){form.Progress=n;form.Status="Проверяем компоненты… "+n+"%";form.Invalidate();}});
     using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Orbit.RuntimeFiles"))
     using(var reader=stream==null?null:new StreamReader(stream)) {
      var manifest=reader?.ReadToEnd();int previous=-1;
      result=await Task.Run(()=>StartupIntegrity.Check(root,manifest,cancel,(done,total)=>{int value=total>0?done*100/total:0;if(value!=previous){previous=value;((IProgress<int>)update).Report(value);}}));
     }
     if(cancel.IsCancellationRequested||form.IsDisposed)return;
     log.Record(result.Count>0?"startup.integrity.failed":"startup.integrity.ok",component:"bootstrap",number:result.Count);
     if(result.Count>0||diagnose)return;
     var engine=Path.GetDirectoryName(exe);var config=Path.Combine(engine,"config.json");
     if(!File.Exists(config))File.WriteAllText(config,"{\"FirstTimeWizardComplete\":true,\"Language\":\"ru_RU\",\"EnableTray\":false,\"CloseToTray\":true,\"DatabasePath\":\"library\"}");
     ticket=form.Ticket();StartupHandoff.Write(data,ticket);
     try{File.Delete(Path.Combine(data,"show.request"));}catch{}
     form.Status="Открываем библиотеку…";form.Progress=-1;form.Invalidate();log.Record("startup.launch",component:"bootstrap");
     using(var process=Process.Start(new ProcessStartInfo(exe,"--startdesktop --hidesplashscreen --startclosedtotray"){WorkingDirectory=engine,UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden})) {
      if(process==null)throw new InvalidOperationException("Windows не запустила основу Orbit.");launched=true;var watch=Stopwatch.StartNew();
      while(!StartupHandoff.IsReady(data,ticket.Token)) {
       cancel.ThrowIfCancellationRequested();if(StartupHandoff.Released(data,ticket.Token)=="hide")return;if(process.HasExited)throw new InvalidOperationException("Основа Orbit завершилась до открытия интерфейса. Запусти Orbit-Diagnostics.cmd и сохрани отчёт.");
       if(watch.Elapsed>TimeSpan.FromSeconds(75))throw new TimeoutException("Интерфейс Orbit не ответил за 75 секунд. Проверь отчёт запуска; повторное открытие Orbit вернёт уже работающий процесс.");
       await Task.Delay(100,cancel);
      }
      // The browser acknowledged two rendered frames. Keep the desktop covered until then.
      await Task.Delay(80,cancel);var mode=StartupHandoff.Released(data,ticket.Token)=="hide"?"hide":form.HasForeground?"activate":"background";
      form.Finishing=true;form.Hide();StartupHandoff.Release(data,ticket.Token,mode);log.Record("startup.handoff.complete",component:"bootstrap",number:(int)Math.Min(1000000,watch.ElapsedMilliseconds));
     }
    }catch(OperationCanceledException){}catch(Exception ex){failure=ex;if(ticket!=null&&launched)try{StartupHandoff.Release(data,ticket.Token,"background");}catch{}}
    finally {if(!form.IsDisposed){form.Finishing=true;form.Close();}}
   };
   form.ShowDialog();if(cancel.IsCancellationRequested)result=null;
  }
  if(ticket!=null&&!launched)StartupHandoff.Cleanup(data,ticket.Token);
  if(failure!=null)throw failure;return result;
 }
 static string DiagnosticBundle(string root,DiagnosticsLog log,List<string> issues) {
  foreach(var directory in new[]{root,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Orbit")})try {
   Directory.CreateDirectory(directory);var file=Path.Combine(directory,"Orbit-report-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".zip");
   SupportBundle.Create(Path.Combine(root,"engine"),file,log,new SupportContext{integrityIssues=issues.Count,integrityComponents=IntegrityComponents(issues)});log.Record("report.created",component:"bootstrap");return file;
  }catch(Exception e){log.Record("report.failed",e,"bootstrap");}return null;
 }
 static string[] IntegrityComponents(List<string> issues) {
  try{using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Orbit.RuntimeFiles"))using(var reader=new StreamReader(stream))
   return reader.ReadToEnd().Split('\n').Select(line=>line.TrimEnd('\r').Split('\t')).Where(parts=>parts.Length==3&&issues.Any(issue=>issue.StartsWith(parts[2]+" — ",StringComparison.Ordinal))).Select(parts=>parts[2]).Take(30).ToArray();
  }catch{return new string[0];}
 }
 static string ReportLocation(string file)=>file==null?"Не удалось сохранить отчёт проверки.":"Отчёт проверки: "+file;
 static string Report(string root,IEnumerable<string> lines) {
  var text="Orbit 0.19 — проверка запуска\r\n"+DateTimeOffset.Now.ToString("O")+"\r\nCLR: "+Environment.Version+"; Windows x64: "+Environment.Is64BitOperatingSystem+"\r\n\r\n"+string.Join("\r\n",lines);
  foreach(var folder in new[]{root,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Orbit")})try {
   Directory.CreateDirectory(folder);var file=Path.Combine(folder,"Orbit-startup-report.txt");File.WriteAllText(file,text);return file;
  }catch{}return null;
 }
}
