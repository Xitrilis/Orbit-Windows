using System;
using System.IO;
using System.Globalization;
namespace Orbit {
 // Per-launch ticket: no credentials, no executable paths, no shared success flag.
 internal sealed class StartupTicket {
  public string Token;public DateTime CreatedUtc;public int X,Y,Width,Height,ProcessId;public long LoaderHandle;
 }
 internal static class StartupHandoff {
  const string Request="startup.request";
  static bool Token(string token){Guid parsed;return token!=null&&Guid.TryParseExact(token,"N",out parsed);}
  static string Marker(string directory,string token,string extension){if(!Token(token))throw new ArgumentException("Invalid startup token.");return Path.Combine(directory,"startup-"+token+extension);}
  static bool Bounds(StartupTicket t)=>t.X>=-65536&&t.X<=65536&&t.Y>=-65536&&t.Y<=65536&&t.Width>=480&&t.Width<=32768&&t.Height>=360&&t.Height<=32768&&t.ProcessId>0&&t.LoaderHandle!=0;
  public static void Write(string directory,StartupTicket ticket){
   if(ticket==null||!Token(ticket.Token)||!Bounds(ticket))throw new ArgumentException("Invalid startup ticket.");Directory.CreateDirectory(directory);
   var text=string.Join("\n",ticket.Token,ticket.CreatedUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),ticket.X,ticket.Y,ticket.Width,ticket.Height,ticket.ProcessId,ticket.LoaderHandle);
   var file=Path.Combine(directory,Request);var temp=file+".tmp";File.WriteAllText(temp,text);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
  }
  public static StartupTicket Read(string directory,DateTime now){try{
   var file=Path.Combine(directory,Request);if(!File.Exists(file)||new FileInfo(file).Length>1024)return null;var p=File.ReadAllLines(file);if(p.Length!=8||!Token(p[0]))return null;
   var t=new StartupTicket{Token=p[0],CreatedUtc=new DateTime(long.Parse(p[1],CultureInfo.InvariantCulture),DateTimeKind.Utc),X=int.Parse(p[2]),Y=int.Parse(p[3]),Width=int.Parse(p[4]),Height=int.Parse(p[5]),ProcessId=int.Parse(p[6]),LoaderHandle=long.Parse(p[7])};
   var age=now.ToUniversalTime()-t.CreatedUtc;return Bounds(t)&&age>=TimeSpan.FromSeconds(-5)&&age<=TimeSpan.FromMinutes(2)?t:null;
  }catch{return null;}}
  public static void Ready(string directory,string token)=>File.WriteAllText(Marker(directory,token,".ready"),"ready");
  public static bool IsReady(string directory,string token)=>File.Exists(Marker(directory,token,".ready"));
  public static void Release(string directory,string token,string mode){if(mode!="activate"&&mode!="background"&&mode!="hide")throw new ArgumentException("Invalid release mode.");File.WriteAllText(Marker(directory,token,".released"),mode);}
  public static string Released(string directory,string token){try{var value=File.ReadAllText(Marker(directory,token,".released"));return value=="activate"||value=="background"||value=="hide"?value:null;}catch{return null;}}
  public static void Cleanup(string directory,string token){
   foreach(var ext in new[]{".ready",".released"})try{File.Delete(Marker(directory,token,ext));}catch{}
   try{var file=Path.Combine(directory,Request);if(File.Exists(file)&&new FileInfo(file).Length<=1024&&File.ReadAllLines(file)[0]==token)File.Delete(file);}catch{}
  }
 }
}
