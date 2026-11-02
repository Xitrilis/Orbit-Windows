using System;
using System.IO;
using Orbit;
class StartupHandoffTests {
 static int n;static void Check(bool condition,string label){n++;if(!condition)throw new Exception(label);}
 static void Main(){var dir=Path.Combine(Path.GetTempPath(),"orbit-handoff-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{
  var now=DateTime.UtcNow;var t=new StartupTicket{Token=Guid.NewGuid().ToString("N"),CreatedUtc=now,X=-1360,Y=80,Width=1360,Height=880,ProcessId=42,LoaderHandle=99};
  Check(StartupHandoff.Read(dir,now)==null,"missing ticket");StartupHandoff.Write(dir,t);var read=StartupHandoff.Read(dir,now);Check(read!=null&&read.Token==t.Token&&read.X==-1360&&read.Width==1360,"negative monitor coordinates survive");
  Check(StartupHandoff.Read(dir,now.AddMinutes(3))==null,"stale ticket ignored");Check(StartupHandoff.Read(dir,now.AddSeconds(-6))==null,"future ticket ignored");Check(StartupHandoff.Read(dir,now.AddSeconds(-3))!=null,"small clock drift allowed");
  Check(!StartupHandoff.IsReady(dir,t.Token),"not ready initially");StartupHandoff.Ready(dir,t.Token);Check(StartupHandoff.IsReady(dir,t.Token),"matching launch ready");var next=Guid.NewGuid().ToString("N");Check(!StartupHandoff.IsReady(dir,next),"prior ready never releases new launch");
  foreach(var mode in new[]{"activate","background","hide"}){StartupHandoff.Release(dir,t.Token,mode);Check(StartupHandoff.Released(dir,t.Token)==mode,mode);}
  File.WriteAllText(Path.Combine(dir,"startup-"+t.Token+".released"),"unknown");Check(StartupHandoff.Released(dir,t.Token)==null,"invalid release ignored");
  bool rejected=false;try{StartupHandoff.Ready(dir,"../unsafe");}catch(ArgumentException){rejected=true;}Check(rejected,"path traversal rejected");
  rejected=false;try{StartupHandoff.Release(dir,t.Token,"invalid");}catch(ArgumentException){rejected=true;}Check(rejected,"invalid mode rejected at writer");
  t.Width=0;rejected=false;try{StartupHandoff.Write(dir,t);}catch(ArgumentException){rejected=true;}Check(rejected,"zero bounds rejected");t.Width=1360;
  File.WriteAllText(Path.Combine(dir,"startup.request"),"bad\nfields");Check(StartupHandoff.Read(dir,now)==null,"truncated ticket ignored");
  File.WriteAllText(Path.Combine(dir,"startup.request"),new string('x',1025));Check(StartupHandoff.Read(dir,now)==null,"oversized ticket ignored");
  StartupHandoff.Write(dir,t);var old=t.Token;t.Token=next;StartupHandoff.Write(dir,t);StartupHandoff.Cleanup(dir,old);Check(StartupHandoff.Read(dir,now)?.Token==next,"cleanup cannot delete newer request");Check(!StartupHandoff.IsReady(dir,old),"old marker removed");StartupHandoff.Cleanup(dir,next);Check(StartupHandoff.Read(dir,now)==null,"current request removed");
  Console.WriteLine("Startup handoff: "+n+" checks passed.");
 }finally{Directory.Delete(dir,true);}}
}
