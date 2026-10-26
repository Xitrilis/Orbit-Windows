using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
namespace Orbit {
 public static class StartupIntegrity {
  // Manifest is compiled into Orbit.exe; user data is never in this manifest.
  public static List<string> Check(string root,string manifest,CancellationToken cancel,Action<int,int> progress=null) {
   var issues=new List<string>();var lines=(manifest??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
   if(lines.Length==0||lines.Length>5000)return new List<string>{"Отсутствует или повреждён список компонентов сборки."};
   root=Path.GetFullPath(root);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int done=0;var buffer=new byte[1024*1024];
   foreach(var line in lines) {
    cancel.ThrowIfCancellationRequested();var fields=line.Split('\t');long size;
    if(fields.Length!=3||fields[0].Length!=64||!fields[0].All(Uri.IsHexDigit)||!long.TryParse(fields[1],out size)||size<0||size>2L*1024*1024*1024){issues.Add("Неверная запись в списке компонентов.");break;}
    var rel=fields[2];var segments=rel.Replace('\\','/').Split('/');
    if(string.IsNullOrWhiteSpace(rel)||rel.StartsWith("/")||rel.StartsWith("\\")||rel.Contains(":")||segments.Any(x=>x==".."||x=="."||x=="")||!seen.Add(rel)){issues.Add("Недопустимый путь в списке компонентов.");break;}
    var path=Path.Combine(root,Path.Combine(segments));
    try {
     if(!File.Exists(path))issues.Add(rel+" — файл отсутствует.");
     else using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
      if(stream.Length!=size)issues.Add(rel+" — неверный размер ("+stream.Length+" вместо "+size+" байт).");
      else using(var hash=SHA256.Create()) {
       int n;
       while((n=stream.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();hash.TransformBlock(buffer,0,n,buffer,0);}
       hash.TransformFinalBlock(new byte[0],0,0);
       if(!string.Equals(BitConverter.ToString(hash.Hash).Replace("-",""),fields[0],StringComparison.OrdinalIgnoreCase))issues.Add(rel+" — содержимое отличается от этой сборки.");
      }
     }
    }catch(OperationCanceledException){throw;}catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){issues.Add(rel+" — не удалось прочитать файл.");}
    progress?.Invoke(++done,lines.Length);if(issues.Count>=30){issues.Add("Проверка остановлена после 30 ошибок.");break;}
   }
   return issues;
  }
 }
}
