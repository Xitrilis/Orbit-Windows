using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using Orbit;
class OrbitBootstrap {
 [STAThread]static void Main(string[] args) {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  var root=AppDomain.CurrentDomain.BaseDirectory;
  var log=new DiagnosticsLog(Path.Combine(root,"engine","OrbitData","Logs"),"bootstrap");log.Record("startup.begin",component:"bootstrap");
  AppDomain.CurrentDomain.UnhandledException += (sender,e) => log.Record("app.unhandled",e.ExceptionObject as Exception,"bootstrap");
  try {
   var engine=Path.Combine(root,"engine");var exe=Path.Combine(engine,"Playnite.DesktopApp.exe");bool diagnose=args.Contains("--diagnose"),running=false;
   if(!diagnose)foreach(var p in Process.GetProcessesByName("Playnite.DesktopApp"))using(p)try {
    if(!string.Equals(p.MainModule.FileName,exe,StringComparison.OrdinalIgnoreCase)) {
     MessageBox.Show("Сначала закрой отдельно запущенный Playnite. Orbit использует собственную копию основы, чтобы не менять твою прежнюю библиотеку.","Orbit",MessageBoxButtons.OK,MessageBoxIcon.Information);return;
    }
    running=true;
   }catch{}
   if(!running) {
    var issues=Validate(root);if(issues==null){log.Record("startup.cancelled",component:"bootstrap");return;}
    log.Record(issues.Count>0?"startup.integrity.failed":"startup.integrity.ok",component:"bootstrap",number:issues.Count);
    string bundle=diagnose?DiagnosticBundle(root,log,issues):null;
    if(issues.Count>0) {
     var report=Report(root,issues);
     MessageBox.Show("Сборка Orbit распакована не полностью или её файлы изменились.\n\n"+string.Join("\n",issues.Take(5))+"\n\nРаспакуй новый архив целиком в новую папку. Старую папку с настройками пока сохрани.\n\n"+ReportLocation(report)+(bundle!=null?"\nZIP для разбора: "+bundle:""),"Orbit — проверка запуска",MessageBoxButtons.OK,MessageBoxIcon.Error);return;
    }
    if(diagnose) {
     var report=Report(root,new[]{"Все встроенные компоненты прошли проверку размеров и SHA-256. Это не проверяет драйверы и блокировки Windows."});
     MessageBox.Show("Файлы сборки целые.\n\n"+(bundle!=null?"Для разбора ошибки пришли созданный ZIP. Он содержит технические события без ключей и переписки.":"Не удалось сохранить ZIP. Проверь доступ к папке Orbit; текстовый результат проверки указан ниже.")+"\n\n"+ReportLocation(report)+(bundle!=null?"\nZIP для разбора: "+bundle:""),"Orbit — проверка запуска",MessageBoxButtons.OK,MessageBoxIcon.Information);return;
    }
   }
   var config=Path.Combine(engine,"config.json");
   if(!File.Exists(config))File.WriteAllText(config,"{\"FirstTimeWizardComplete\":true,\"Language\":\"ru_RU\",\"EnableTray\":false,\"CloseToTray\":true,\"DatabasePath\":\"library\"}");
   var orbitData=Path.Combine(engine,"OrbitData");Directory.CreateDirectory(orbitData);File.WriteAllText(Path.Combine(orbitData,"show.request"),DateTime.UtcNow.ToString("O"));
   if(!running){log.Record("startup.launch",component:"bootstrap");Process.Start(new ProcessStartInfo(exe,"--hidesplashscreen --startclosedtotray"){WorkingDirectory=engine,UseShellExecute=true});}
  }catch(Exception e) {
   log.Record("startup.failed",e,"bootstrap");
   var report=Report(root,new[]{e.GetType().Name+": "+e.Message,"HResult: 0x"+e.HResult.ToString("X8")});
   MessageBox.Show("Не удалось запустить Orbit.\n\n"+e.Message+"\n\n"+ReportLocation(report),"Orbit — запуск",MessageBoxButtons.OK,MessageBoxIcon.Error);
  }
 }
 static List<string> Validate(string root) {
  List<string> result=null;
  using(var cancel=new CancellationTokenSource())
  using(var form=new Form{Text="Orbit",ClientSize=new Size(470,172),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=Color.FromArgb(16,23,29),ForeColor=Color.FromArgb(205,228,216),Font=new Font("Segoe UI",10),ShowInTaskbar=true}) {
   try{form.Icon=new Icon(Path.Combine(root,"orbit.ico"));}catch{}
   var title=new Label{Text="orbit.",Font=new Font("Segoe UI",23,FontStyle.Bold),Location=new Point(25,18),AutoSize=true};
   var status=new Label{Text="Проверяем файлы перед запуском…",Location=new Point(27,78),Size=new Size(420,26)};
   var progress=new ProgressBar{Location=new Point(28,122),Size=new Size(414,5),Style=ProgressBarStyle.Continuous};
   form.Controls.Add(title);form.Controls.Add(status);form.Controls.Add(progress);
   form.FormClosing+=(s,e)=>cancel.Cancel();
   form.Shown+=async(s,e)=>{
    var update=new Progress<int>(n=>{if(!form.IsDisposed)progress.Value=Math.Max(0,Math.Min(100,n));});
    try {
     using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Orbit.RuntimeFiles"))
     using(var reader=stream==null?null:new StreamReader(stream)) {
      var manifest=reader?.ReadToEnd();
      result=await Task.Run(()=>StartupIntegrity.Check(root,manifest,cancel.Token,(done,total)=>((IProgress<int>)update).Report(total>0?Math.Min(100,done*100/total):0)));
     }
    }catch(OperationCanceledException){}catch{result=new List<string>{"Не удалось проверить компоненты Orbit."};}
    if(!form.IsDisposed)form.Close();
   };
   form.ShowDialog();
  }
  return result;
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
  var text="Orbit 0.11 — проверка запуска\r\n"+DateTimeOffset.Now.ToString("O")+"\r\nCLR: "+Environment.Version+"; Windows x64: "+Environment.Is64BitOperatingSystem+"\r\n\r\n"+string.Join("\r\n",lines);
  foreach(var folder in new[]{root,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Orbit")})try {
   Directory.CreateDirectory(folder);var file=Path.Combine(folder,"Orbit-startup-report.txt");File.WriteAllText(file,text);return file;
  }catch{}return null;
 }
}
