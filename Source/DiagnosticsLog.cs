using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
namespace Orbit {
 [DataContract] public sealed class SupportLogEntry {
  [DataMember]public string timeUtc;
  [DataMember]public string id;
  [DataMember]public string session;
  [DataMember]public string name;
  [DataMember]public string component;
  [DataMember]public string exceptionType;
  [DataMember]public string hresult;
  [DataMember]public string[] frames;
  [DataMember]public int number;
 }
 public static class DiagnosticJson {
  public static string Write<T>(T value){using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);return Encoding.UTF8.GetString(stream.ToArray());}}
  public static T Read<T>(string value){using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(value)))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}
 }
 public sealed class DiagnosticsLog {
  public const int MaxBytes=512*1024;
  static readonly HashSet<string> Names=new HashSet<string>{
   "startup.begin","startup.cancelled","startup.integrity.ok","startup.integrity.failed","startup.launch","startup.failed",
   "app.started","app.stopped","app.start.failed","app.unhandled","app.task.failed","ui.callback.failed","ui.command.failed",
   "ui.error","ui.promise.failed","ui.resource.failed","browser.load.failed","browser.ready","operation.failed",
   "game.requested","game.started","game.stopped","game.cancelled","game.duplicate.ignored","library.updated",
   "clients.hidden","clients.restored","history.warning","clients.warning","downloads.failed","install.failed",
   "journal.failed","minecraft.failed","report.created","report.failed","event.unknown"};
  static readonly HashSet<string> Components=new HashSet<string>{
   "journal","minecraft","journal.js","journal-model.js","minecraft.js","bootstrap","host","browser","home","prices","wallet","artwork","assistant","downloads","history","clients","support",
   "core.js","app.js","home.js","prices.js","sales.js","wallet.js","artwork.js","activity.js","downloads.js","support.js","ui-health.js","assistant.js"};
  readonly object gate=new object();readonly string file;readonly string session=Guid.NewGuid().ToString("N");
  readonly Dictionary<string,Tuple<DateTime,string>> recent=new Dictionary<string,Tuple<DateTime,string>>();
  public string DirectoryPath {get;}
  public bool Writable {get;private set;}=true;
  public DiagnosticsLog(string directory,string channel){DirectoryPath=directory;file=Path.Combine(directory,channel=="bootstrap"?"bootstrap.jsonl":"orbit.jsonl");}
  public string Record(string name,Exception error=null,string component="host",int number=0){
   lock(gate) {
    error=error?.GetBaseException();
    name=Names.Contains(name??"")?name:"event.unknown";component=Components.Contains(component??"")?component:"host";
    var key=name+"|"+component+"|"+error?.GetType().FullName+"|"+number;Tuple<DateTime,string> previous;
    if(recent.TryGetValue(key,out previous)&&DateTime.UtcNow-previous.Item1<TimeSpan.FromSeconds(10))return previous.Item2;
    var entry=new SupportLogEntry{timeUtc=DateTime.UtcNow.ToString("O"),id=Guid.NewGuid().ToString("N"),session=session,name=name,component=component,number=number};
    if(error!=null) {
     // Never record exception messages, Data, request bodies, URLs, account details or source-file paths.
     entry.exceptionType=error.GetType().FullName;entry.hresult="0x"+error.HResult.ToString("X8");
     try{entry.frames=(new StackTrace(error,false).GetFrames()??new StackFrame[0]).Take(8).Select(f=>f.GetMethod()).Where(m=>m!=null).Select(m=>m.DeclaringType?.FullName+"."+m.Name).ToArray();}catch{}
    }
    entry=Clean(entry);if(entry==null)return "";
    if(recent.Count>512)recent.Clear();recent[key]=Tuple.Create(DateTime.UtcNow,entry.id);
    try {
     Directory.CreateDirectory(DirectoryPath);var line=Encoding.UTF8.GetBytes(DiagnosticJson.Write(entry)+"\n");
     if(File.Exists(file)&&new FileInfo(file).Length+line.Length>MaxBytes){if(File.Exists(file+".2"))File.Delete(file+".2");if(File.Exists(file+".1"))File.Move(file+".1",file+".2");File.Move(file,file+".1");}
     using(var stream=new FileStream(file,FileMode.Append,FileAccess.Write,FileShare.Read)){stream.Write(line,0,line.Length);stream.Flush();}
     Writable=true;
    }catch{Writable=false;}
    return entry.id;
   }
  }
  static SupportLogEntry Clean(SupportLogEntry e) {
   DateTimeOffset at;Guid id,sessionId;
   if(e==null||!DateTimeOffset.TryParse(e.timeUtc,out at)||!Guid.TryParse(e.id,out id)||!Guid.TryParse(e.session,out sessionId)||!Names.Contains(e.name??""))return null;
   return new SupportLogEntry{timeUtc=at.ToUniversalTime().ToString("O"),id=id.ToString("N"),session=sessionId.ToString("N"),name=e.name,component=Components.Contains(e.component??"")?e.component:"host",number=Math.Max(-1000000,Math.Min(1000000,e.number)),
    exceptionType=Regex.IsMatch(e.exceptionType??"",@"\A[A-Za-z0-9_.+]{1,120}Exception\z")?e.exceptionType:null,
    hresult=Regex.IsMatch(e.hresult??"",@"\A0x[0-9A-Fa-f]{8}\z")?e.hresult:null,
    frames=(e.frames??new string[0]).Where(f=>f!=null&&f.Length<=220&&Regex.IsMatch(f,@"\A(?:Orbit|OrbitBootstrap|System|Microsoft|CefSharp|Playnite)[A-Za-z0-9_.+<>,\[\]`]{0,200}\z")).Take(8).ToArray()};
  }
  public static string Tail(string file,int max) {
   using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
    bool cut=stream.Length>max;if(cut)stream.Seek(-max,SeekOrigin.End);
    var bytes=new byte[Math.Min(max,(int)Math.Min(int.MaxValue,stream.Length))];int n=0,k;while(n<bytes.Length&&(k=stream.Read(bytes,n,bytes.Length-n))>0)n+=k;
    var value=Encoding.UTF8.GetString(bytes,0,n);if(cut){int i=value.IndexOf('\n');value=i<0?"":value.Substring(i+1);}return value;
   }
  }
  public static SupportLogEntry[] Read(string directory) {
   var entries=new List<SupportLogEntry>();
   foreach(var channel in new[]{"bootstrap.jsonl","orbit.jsonl"})foreach(var suffix in new[]{".2",".1",""})try {
    var path=Path.Combine(directory,channel+suffix);if(!File.Exists(path))continue;
    foreach(var line in Tail(path,MaxBytes).Split('\n'))if(line.Length>0&&line.Length<=8192)try{var e=Clean(DiagnosticJson.Read<SupportLogEntry>(line));if(e!=null)entries.Add(e);}catch{}
   }catch{}
   return entries.GroupBy(e=>e.id).Select(g=>g.First()).OrderBy(e=>e.timeUtc,StringComparer.Ordinal).TakeLastCompat(6000).ToArray();
  }
 }
 static class DiagnosticEnumerable {public static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> source,int count){var q=new Queue<T>();foreach(var item in source){q.Enqueue(item);if(q.Count>count)q.Dequeue();}return q;}}
}
