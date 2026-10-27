using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Orbit;
class OrbitBootstrap {
 sealed class SplashForm:Form {
  public SplashForm(){
   DoubleBuffered=true;
   SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);
  }
 }
 [DllImport("dwmapi.dll",PreserveSig=true)]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int attrValue,int attrSize);
 [DllImport("user32.dll")]static extern bool ReleaseCapture();
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hWnd,int Msg,IntPtr wParam,IntPtr lParam);
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
  using(var form=new SplashForm{
   Text="Orbit",ClientSize=new Size(500,210),StartPosition=FormStartPosition.CenterScreen,
   FormBorderStyle=FormBorderStyle.None,BackColor=Color.FromArgb(14,18,24),
   ForeColor=Color.FromArgb(205,228,216),Font=new Font("Segoe UI",10),
   ShowInTaskbar=true
  }) {
   try{form.Icon=new Icon(Path.Combine(root,"orbit.ico"));}catch{}
   try{
    int round=2;DwmSetWindowAttribute(form.Handle,33,ref round,4);
    int dark=1;DwmSetWindowAttribute(form.Handle,20,ref dark,4);
   }catch{}

   form.MouseDown+=(s,e)=>{
    if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(form.Handle,0xA1,(IntPtr)2,IntPtr.Zero);}
   };

   int currentProgress=0;
   string currentStatus="Проверка целостности компонентов…";

   form.Paint+=(s,e)=>{
    var g=e.Graphics;
    g.SmoothingMode=SmoothingMode.AntiAlias;
    g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

    using(var borderPen=new Pen(Color.FromArgb(34,44,58),1)){
     g.DrawRectangle(borderPen,0,0,form.Width-1,form.Height-1);
    }
    using(var accentBrush=new LinearGradientBrush(new Point(0,0),new Point(form.Width,0),Color.FromArgb(70,200,160),Color.FromArgb(165,228,209)))
    using(var accentPen=new Pen(accentBrush,2)){
     g.DrawLine(accentPen,0,0,form.Width,0);
    }

    int iconX=32,iconY=28;
    using(var orbitPen=new Pen(Color.FromArgb(110,165,228,209),2f)){
     var state=g.Save();
     g.TranslateTransform(iconX+18,iconY+18);
     g.RotateTransform(-35);
     g.DrawEllipse(orbitPen,-18,-9,36,18);
     g.Restore(state);
    }
    using(var planetBrush=new SolidBrush(Color.FromArgb(165,228,209))){
     g.FillEllipse(planetBrush,iconX+13,iconY+13,10,10);
    }
    using(var satBrush=new SolidBrush(Color.FromArgb(230,250,245))){
     g.FillEllipse(satBrush,iconX+28,iconY+5,5,5);
    }

    using(var eyebrowFont=new Font("Segoe UI",8.5f,FontStyle.Bold))
    using(var eyebrowBrush=new SolidBrush(Color.FromArgb(110,145,140))){
     g.DrawString("ORBIT / LAUNCHER",eyebrowFont,eyebrowBrush,80,24);
    }

    using(var titleFont=new Font("Segoe UI",20f,FontStyle.Bold))
    using(var titleBrush=new SolidBrush(Color.FromArgb(235,245,242)))
    using(var dotBrush=new SolidBrush(Color.FromArgb(165,228,209))){
     g.DrawString("orbit",titleFont,titleBrush,78,38);
     var titleSize=g.MeasureString("orbit",titleFont);
     g.DrawString(".",titleFont,dotBrush,78+titleSize.Width-7,38);
    }

    using(var statusFont=new Font("Segoe UI",9.5f,FontStyle.Regular))
    using(var statusBrush=new SolidBrush(Color.FromArgb(155,175,185))){
     g.DrawString(currentStatus,statusFont,statusBrush,32,118);
    }

    using(var pctFont=new Font("Segoe UI",10f,FontStyle.Bold))
    using(var pctBrush=new SolidBrush(Color.FromArgb(165,228,209))){
     string pctStr=currentProgress+"%";
     var pctSize=g.MeasureString(pctStr,pctFont);
     g.DrawString(pctStr,pctFont,pctBrush,form.Width-32-pctSize.Width,117);
    }

    int barX=32,barY=146,barW=form.Width-64,barH=6;
    using(var trackBrush=new SolidBrush(Color.FromArgb(24,32,42))){
     g.FillRectangle(trackBrush,barX,barY,barW,barH);
    }
    int fillW=Math.Max(0,Math.Min(barW,(int)(barW*(currentProgress/100.0))));
    if(fillW>0){
     using(var fillBrush=new LinearGradientBrush(new Point(barX,barY),new Point(barX+barW,barY),Color.FromArgb(50,185,145),Color.FromArgb(165,228,209))){
      g.FillRectangle(fillBrush,barX,barY,fillW,barH);
     }
     using(var headBrush=new SolidBrush(Color.FromArgb(230,255,248))){
      g.FillEllipse(headBrush,barX+fillW-3,barY-1,6,8);
     }
    }

    using(var footFont=new Font("Segoe UI",8.5f,FontStyle.Regular))
    using(var footBrush=new SolidBrush(Color.FromArgb(75,95,105))){
     g.DrawString("Все твои игры в одном месте · Проверка файлов",footFont,footBrush,32,170);
    }
   };

   form.FormClosing+=(s,e)=>cancel.Cancel();
   form.Shown+=async(s,e)=>{
    var update=new Progress<int>(n=>{
     if(!form.IsDisposed){
      currentProgress=Math.Max(0,Math.Min(100,n));
      if(currentProgress<30)currentStatus="Проверка манифеста и целостности…";
      else if(currentProgress<80)currentStatus="Сверка игровых компонентов…";
      else currentStatus="Запуск Orbit…";
      form.Invalidate();
     }
    });
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
