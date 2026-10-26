using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
namespace Orbit {
 [DataContract] public sealed class SupportContext {
  [DataMember]public string version=typeof(SupportContext).Assembly.GetName().Version.ToString();
  [DataMember]public string createdUtc=DateTime.UtcNow.ToString("O");
  [DataMember]public string osReportedByRuntime=Environment.OSVersion.Version.ToString();
  [DataMember]public string clr=Environment.Version.ToString();
  [DataMember]public bool os64=Environment.Is64BitOperatingSystem;
  [DataMember]public bool process64=Environment.Is64BitProcess;
  [DataMember]public int? games,installed,sessions,downloads,hiddenClients,integrityIssues;
  [DataMember]public bool? backgroundEnabled;
  [DataMember]public string[] integrityComponents=new string[0];
  [DataMember]public string logStatus;
 }
 [DataContract] public sealed class EngineSignals {
  [DataMember]public string status;
  [DataMember]public string[] exceptionTypes=new string[0],hresults=new string[0],components=new string[0];
 }
 public static class SupportBundle {
  // Only fixed signatures are extracted. Raw Playnite logs can contain account or browser data.
  static readonly string[] ExceptionTypes={"BadImageFormatException","DllNotFoundException","FileLoadException","FileNotFoundException","MissingMethodException","EntryPointNotFoundException","TypeLoadException","UnauthorizedAccessException","IOException","OutOfMemoryException","COMException","NullReferenceException","InvalidOperationException","JsonReaderException","TaskCanceledException","TimeoutException"};
  static readonly string[] ComponentNames={"libcef.dll","CefSharp.Core.Runtime.dll","CefSharp.Core.dll","CefSharp.Wpf.dll","CefSharp.dll","Playnite.BrowserProcess.exe","Orbit.Plugin.dll","Orbit.InstallHelper.exe","SDL3.dll","vcruntime140.dll","msvcp140.dll","Newtonsoft.Json.dll","AngleSharp.dll"};
  public static EngineSignals Signals(string engineRoot) {
   var result=new EngineSignals();
   try {
    var path=Path.Combine(engineRoot,"playnite.log");if(!File.Exists(path)){result.status="not-found";return result;}
    var text=DiagnosticsLog.Tail(path,1024*1024);result.status="last-1MiB-signatures-only";
    result.exceptionTypes=ExceptionTypes.Where(x=>text.Contains(x)).ToArray();
    result.components=ComponentNames.Where(x=>text.IndexOf(x,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
    result.hresults=Regex.Matches(text,@"\bHResult\s*[:=]\s*(0x[0-9A-Fa-f]{8})\b",RegexOptions.IgnoreCase).Cast<Match>().Select(x=>x.Groups[1].Value.ToUpperInvariant()).Distinct().Take(32).ToArray();
   }catch{result.status="unavailable";}return result;
  }
  public static string Create(string engineRoot,string output,DiagnosticsLog log,SupportContext context) {
   string temporary=output+".partial-"+Guid.NewGuid().ToString("N");
   try {
    var events=DiagnosticsLog.Read(log.DirectoryPath);context.logStatus=log.Writable?"available":"write-failed";
    using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
    using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,false)) {
     Put(zip,"summary.json",DiagnosticJson.Write(context));
     Put(zip,"orbit-events.jsonl",string.Join("\n",events.Select(DiagnosticJson.Write))+"\n");
     Put(zip,"playnite-signals.json",DiagnosticJson.Write(Signals(engineRoot)));
     Put(zip,"README.txt","Orbit — локальный отчёт для разбора ошибки.\r\nВерсия и среда, счётчики без названий игр, события и типы ошибок.\r\nТексты запросов Gemini, API-ключи, пароли, cookies, платёжные данные, скриншоты, история игр и исходный журнал Playnite не включаются.\r\nИз playnite.log извлечены только известные названия компонентов, типы исключений и коды ошибок. Это не полный журнал.\r\nНичего не отправлено автоматически. Пришли ZIP в чат, если нужна помощь.\r\n");
    }
    // The dialog explicitly allows replacement; a failed export must preserve an existing report.
    if(File.Exists(output))File.Replace(temporary,output,null);else File.Move(temporary,output);
    return output;
   }finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
  }
  static void Put(ZipArchive zip,string name,string text){using(var writer=new StreamWriter(zip.CreateEntry(name,CompressionLevel.Optimal).Open(),new UTF8Encoding(false)))writer.Write(text);}
 }
}
