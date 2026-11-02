using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Orbit {
 // Horizon's command line interface is a preview. Keep the command surface narrow
 // and never infer game state from a successful process launch alone.
 public static class HorizonBridge {
  public static void CheckHmod(string file){
   if(!File.Exists(file)||!file.EndsWith(".hmod",StringComparison.OrdinalIgnoreCase)||new FileInfo(file).Length>64L*1024*1024||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Выбери готовый файл мода Horizon .hmod размером до 64 МБ.");
   try{using(var z=ZipFile.OpenRead(file)){var manifest=z.GetEntry("horizon-mod.toml");if(z.Entries.Count>10000||manifest==null||manifest.Length>262144||z.Entries.Sum(x=>x.Length)>256L*1024*1024)throw new InvalidOperationException();using(var reader=new StreamReader(manifest.Open()))if(string.IsNullOrWhiteSpace(reader.ReadToEnd()))throw new InvalidOperationException();}}
   catch{throw new InvalidOperationException("Файл .hmod ещё не загружен или не содержит манифест Horizon.");}
  }
  public static void AddHmod(string file,string game){
   CheckHmod(file);if(!Directory.Exists(game))throw new InvalidOperationException("Выбери существующую папку игры Horizon в настройках.");
   var dest=Path.Combine(game,"horizon-mods");Directory.CreateDirectory(dest);var name=Path.GetFileName(file);var target=Path.Combine(dest,name);if(File.Exists(target))throw new InvalidOperationException("Мод с таким именем уже есть. Orbit не заменяет его.");var tmp=Path.Combine(dest,".orbit-"+Guid.NewGuid().ToString("N")+".hmod");
   try{File.Copy(file,tmp,false);CheckHmod(tmp);File.Move(tmp,target);}finally{try{if(File.Exists(tmp))File.Delete(tmp);}catch{}}
  }
  public static string Id(string value) {
   value=(value??"").Trim();
   if(value.Length<1||value.Length>100||value[0]=='-'||value=="."||value==".."||value.IndexOf("..",StringComparison.Ordinal)>=0||!Regex.IsMatch(value,@"\A[\p{L}\p{N} _.-]+\z"))throw new InvalidOperationException("Укажи название или идентификатор без путей и специальных символов.");
   return value;
  }
  public static string Query(string value) {
   value=(value??"").Trim();
   if(value.Length<1||value.Length>100||value[0]=='-'||!Regex.IsMatch(value,@"\A[\p{L}\p{N} _.-]+\z"))throw new InvalidOperationException("Введи название для поиска без специальных символов.");
   return value;
  }
  public static string Invite(string value) {
   value=(value??"").Trim();if(value.Length<1||value.Length>4096||value[0]=='-'||!Regex.IsMatch(value,@"\A[A-Za-z0-9._:/+=-]+\z"))throw new InvalidOperationException("Проверь код приглашения Horizon.");return value;
  }
  public static string Address(string value) {
   value=(value??"").Trim();
   if(value.Length<1||value.Length>180||value[0]=='-'||!Regex.IsMatch(value,@"\A[A-Za-z0-9.:[\]_-]+\z"))throw new InvalidOperationException("Проверь адрес сервера или код приглашения.");
   return value;
  }
  public static string Arguments(IEnumerable<string> args)=>string.Join(" ",args.Select(MinecraftFiles.Quote));
  static readonly string[] Sensitive={"token","secret","password","authorization","credential","email","uuid","access_key","invite","address","server_ip","ip_address","url","path","directory"};
  public static JToken Redact(JToken t,int depth=0) {
   if(t==null||depth>9)return JValue.CreateNull();
   if(t is JObject obj){var result=new JObject();foreach(var p in obj.Properties().Take(200))if(!Sensitive.Any(x=>p.Name.IndexOf(x,StringComparison.OrdinalIgnoreCase)>=0))result[p.Name]=Redact(p.Value,depth+1);return result;}
   if(t is JArray arr)return new JArray(arr.Take(200).Select(x=>Redact(x,depth+1)));
   if(t.Type==JTokenType.String){var s=(string)t??"";return new JValue(CleanText(s,500));}
   return t.DeepClone();
  }
  public static string CleanText(string value,int max=3000){
   var s=value??"";
   s=Regex.Replace(s,@"(?i)\b(token|secret|password|authorization|credential|invite|access[_ -]?key)\s*[:=]\s*\S+","$1: [скрыто]");
   s=Regex.Replace(s,@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b","[скрыто]");
   s=Regex.Replace(s,@"(?i)\b[A-Z]:\\(?:[^\s\""<>|]+\\)*[^\s\""<>|]*","[локальный путь]");
   s=Regex.Replace(s,@"https?://\S+","[ссылка скрыта]");
   return s.Length>max?s.Substring(0,max)+"…":s;
  }
  // Only a small allow-list may leave the game; ignore chat, account and server data.
  public static string SafeState(string raw){
   JToken root;try{root=JToken.Parse(raw);}catch{throw new InvalidOperationException("Формат состояния Horizon не распознан.");}
   var allowed=new HashSet<string>(new[]{"health","food","hunger","position","dimension"},StringComparer.OrdinalIgnoreCase);var lines=new List<string>();
   Action<JToken,int> visit=null;visit=(node,depth)=>{if(depth>6||lines.Count>=8)return;if(node is JObject o)foreach(var p in o.Properties().Take(100)){
    if(allowed.Contains(p.Name)){
     if(p.Value.Type==JTokenType.Integer||p.Value.Type==JTokenType.Float)lines.Add(p.Name+": "+p.Value.ToString(Newtonsoft.Json.Formatting.None));
     else if(p.Name.Equals("dimension",StringComparison.OrdinalIgnoreCase)&&p.Value.Type==JTokenType.String){var v=(string)p.Value;if(Regex.IsMatch(v??"",@"\A[a-zA-Z0-9_:.-]{1,80}\z"))lines.Add("dimension: "+v);}
     else if(p.Name.Equals("position",StringComparison.OrdinalIgnoreCase)&&p.Value is JArray a&&a.Count==3&&a.All(x=>x.Type==JTokenType.Integer||x.Type==JTokenType.Float))lines.Add("position: "+a.ToString(Newtonsoft.Json.Formatting.None));
     else if(p.Name.Equals("position",StringComparison.OrdinalIgnoreCase)&&p.Value is JObject q&&new[]{"x","y","z"}.All(k=>q[k]!=null&&(q[k].Type==JTokenType.Integer||q[k].Type==JTokenType.Float)))lines.Add("position: "+string.Join(", ",new[]{"x","y","z"}.Select(k=>q[k].ToString(Newtonsoft.Json.Formatting.None))));
    }else if(p.Value is JObject||p.Value is JArray)visit(p.Value,depth+1);
   }else if(node is JArray a)foreach(var v in a.Take(30))visit(v,depth+1);};visit(root,0);
   if(lines.Count==0)throw new InvalidOperationException("Horizon не вернул известные поля состояния. Возможно, формат этой версии изменился.");
   return string.Join("\n",lines.Distinct());
  }
  public static string Printable(string raw){try{return Redact(JToken.Parse(raw)).ToString(Newtonsoft.Json.Formatting.Indented);}catch{return CleanText(raw,6000);}}
  static async Task<string> ReadBounded(StreamReader reader){var b=new StringBuilder();var chars=new char[4096];int n;while((n=await reader.ReadAsync(chars,0,chars.Length).ConfigureAwait(false))>0)if(b.Length<65536)b.Append(chars,0,Math.Min(n,65536-b.Length));return b.ToString();}
  public sealed class Result {public int code;public string output;public string error;public bool timedOut;}
  public static async Task<Result> Run(string exe,string home,IEnumerable<string> args,int timeoutMs){
   var start=new ProcessStartInfo(exe,Arguments(args)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.GetDirectoryName(exe)};
   start.EnvironmentVariables["HORIZON_LAUNCHER_HOME"]=home;
   start.EnvironmentVariables["HORIZON_LAUNCHER_NO_SELF_UPDATE"]="1";
   var p=Process.Start(start);if(p==null)throw new IOException("Не удалось запустить Horizon.");
   var stdout=ReadBounded(p.StandardOutput);var stderr=ReadBounded(p.StandardError);
   var exited=await Task.Run(()=>p.WaitForExit(timeoutMs)).ConfigureAwait(false);
   if(!exited){ // Long operations are not killed: a timeout is not evidence of failure or success.
    return new Result{timedOut=true,error="Horizon ещё выполняет команду. Проверь результат позже; Orbit не прерывал процесс."};
   }
   await Task.WhenAll(stdout,stderr).ConfigureAwait(false);
   var result=new Result{code=p.ExitCode,output=stdout.Result,error=stderr.Result};p.Dispose();return result;
  }
 }
}
