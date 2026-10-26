using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Orbit;
class ActivityTests {
 static int checks;static readonly string root=Path.Combine(Path.GetTempPath(),"orbit-activity-"+Guid.NewGuid());static string A=Guid.NewGuid().ToString(),B=Guid.NewGuid().ToString();static DateTime t=new DateTime(2026,10,25,10,0,0,DateTimeKind.Utc);
 static string Dir(){var p=Path.Combine(root,Guid.NewGuid().ToString());Directory.CreateDirectory(p);return p;}
 static void Check(string name,Action test){test();checks++;Console.WriteLine("PASS "+name);}
 static void Eq<T>(T a,T b){if(!EqualityComparer<T>.Default.Equals(a,b))throw new Exception("Expected "+b+", got "+a);}
 static void Yes(bool v){if(!v)throw new Exception("Assertion failed");}
 static ClientWindow Win(string store="steam",int n=1){return new ClientWindow{handle=n,pid=n,startedTicks=100+n,path=store=="steam"?"steam.exe":store=="gog"?"GalaxyClient.exe":"game.exe",store=store,title=store=="steam"?"Steam":"GOG Galaxy",visible=true};}
 sealed class Windows:IClientWindows {
  public List<ClientWindow> rows=new List<ClientWindow>();public bool queued;public int hides,restores;public Action beforeHide;
  public List<ClientWindow> List()=>rows.ToList();
  public bool Same(ClientWindow w)=>rows.Any(x=>x.handle==w.handle&&x.pid==w.pid&&x.startedTicks==w.startedTicks&&x.path==w.path);
  public bool Visible(ClientWindow w)=>rows.Any(x=>x.handle==w.handle&&x.visible);
  public bool Hide(ClientWindow w){beforeHide?.Invoke();hides++;if(!queued)rows.First(x=>x.handle==w.handle).visible=false;return true;}
  public bool Restore(ClientWindow w){restores++;rows.First(x=>x.handle==w.handle).visible=true;return true;}
 }
 static void Main(){try {
  Check("empty history",()=>Eq(new PlayHistory(Dir()).Snapshot().Length,0));
  Check("only real game/store identities",()=>{var h=new PlayHistory(Dir());h.Started("fake","Test","steam",t);h.Started(A,"Test","other",t);Eq(h.Snapshot().Length,0);});
  Check("confirmed start retains timestamp and store",()=>{var h=new PlayHistory(Dir());h.Started(A,"Test","steam",t);var r=h.Snapshot().Single();Eq(r.startedAt,t);Eq(r.store,"steam");Eq(r.state,"running");});
  Check("duplicate start ignored",()=>{var h=new PlayHistory(Dir());h.Started(A,"Test","steam",t);h.Started(A,"Test","steam",t.AddMinutes(1));Eq(h.Snapshot().Length,1);});
  Check("stop without start ignored",()=>{var h=new PlayHistory(Dir());h.Stopped(A,t,2);Eq(h.Snapshot().Length,0);});
  Check("stop and relaunch are separate sessions",()=>{var h=new PlayHistory(Dir());h.Started(A,"Test","steam",t);h.Stopped(A,t.AddHours(1),3600);h.Started(A,"Test","steam",t.AddDays(1));var r=h.Snapshot();Eq(r.Length,2);Eq(r[1].seconds,(ulong?)3600);Eq(r[1].endedAt,(DateTime?)t.AddHours(1));});
  Check("tracking stop does not invent game end",()=>{var h=new PlayHistory(Dir());h.Started(A,"Test","steam",t);h.Stopped(A,t.AddHours(1),3600,true);var r=h.Snapshot().Single();Eq(r.state,"interrupted");Yes(r.endedAt==null&&r.seconds==null);});
  Check("restart marks open session interrupted",()=>{var d=Dir();var h=new PlayHistory(d);h.Started(A,"Test","steam",t);var r=new PlayHistory(d).Snapshot().Single();Eq(r.state,"interrupted");Yes(r.endedAt==null);});
  Check("shutdown keeps unknown end unknown",()=>{var h=new PlayHistory(Dir());h.Started(A,"Test","steam",t);h.Shutdown();Eq(h.Snapshot().Single().state,"interrupted");Yes(h.Snapshot().Single().endedAt==null);});
  Check("snapshot cannot mutate records",()=>{var h=new PlayHistory(Dir());h.Started(A,"Test","steam",t);h.Snapshot()[0].name="changed";Eq(h.Snapshot()[0].name,"Test");});
  Check("backup recovery",()=>{var d=Dir();var h=new PlayHistory(d);h.Started(A,"Test","steam",t);h.Stopped(A,t.AddHours(1),3600);File.WriteAllText(Path.Combine(d,"play-history.json"),"broken");var recovered=new PlayHistory(d);Eq(recovered.Snapshot().Length,1);Yes(recovered.Warning!=null);});
  Check("corrupt originals preserved",()=>{var d=Dir();var p=Path.Combine(d,"play-history.json");File.WriteAllText(p,"broken1");File.WriteAllText(p+".bak","broken2");var h=new PlayHistory(d);h.Started(A,"Test","steam",t);Eq(File.ReadAllText(p),"broken1");Eq(File.ReadAllText(p+".bak"),"broken2");Yes(h.Warning!=null);});
  Check("save failure retains in-memory history",()=>{var d=Dir();Directory.CreateDirectory(Path.Combine(d,"play-history.json.tmp"));var h=new PlayHistory(d);h.Started(A,"Test","steam",t);Eq(h.Snapshot().Length,1);Yes(h.Warning!=null);});
  Check("six known clients only",()=>{foreach(var pair in new[]{("Steam.exe","steam"),("EpicGamesLauncher.exe","epic"),("EADesktop.exe","ea"),("upc.exe","ubisoft"),("GalaxyClient.exe","gog"),("Battle.net.exe","battlenet")})Eq(ClientWindowRules.Store(pair.Item1),pair.Item2);Yes(ClientWindowRules.Store("game.exe")==null);});
  Check("title owner dialog exclusions",()=>{var w=Win();Yes(ClientWindowRules.IsMain(w));w.title="Steam Sign In";Yes(!ClientWindowRules.IsMain(w));w.title="Steam";w.owned=true;Yes(!ClientWindowRules.IsMain(w));w.owned=false;w.dialog=true;Yes(!ClientWindowRules.IsMain(w));});
  Check("journal exists before hide and exit restores",()=>{var d=Dir();var w=Win();var win=new Windows{rows={w}};using(var c=new BackgroundClients(d,win,()=>true,()=>false,null)){win.beforeHide=()=>Yes(File.ReadAllText(Path.Combine(d,"background-windows.json")).Contains("steam"));Eq(c.HideNow(),1);Eq(c.HiddenStores.Single(),"steam");Eq(w.pid,1);}Eq(win.restores,1);Yes(w.visible);});
  Check("pending async hide retains restoration identity",()=>{var win=new Windows{queued=true,rows={Win()}};using(var c=new BackgroundClients(Dir(),win,()=>true,()=>false,null)){c.HideNow();Eq(c.HiddenStores.Length,0);win.rows[0].visible=false;Eq(c.HiddenStores.Single(),"steam");}Eq(win.restores,1);});
  Check("restore queued after pending hide",()=>{var win=new Windows{queued=true,rows={Win()}};using(var c=new BackgroundClients(Dir(),win,()=>true,()=>false,null)){c.HideNow();c.RestoreAll();Eq(win.restores,1);}});
  Check("auto disabled and install lock respected",()=>{var win=new Windows{rows={Win()}};bool blocked=false;using(var c=new BackgroundClients(Dir(),win,()=>false,()=>blocked,null)){Eq(c.HideNow(false),0);blocked=true;Eq(c.HideNow(),0);blocked=false;Eq(c.HideNow(),1);}});
  Check("client with active dialog is untouched",()=>{var main=Win();var dialog=Win();dialog.handle=2;dialog.dialog=true;var win=new Windows{rows={main,dialog}};using(var c=new BackgroundClients(Dir(),win,()=>true,()=>false,null)){Eq(c.HideNow(),0);}});
  Check("manual reopen suspends auto, explicit hide allowed",()=>{var win=new Windows{rows={Win()}};using(var c=new BackgroundClients(Dir(),win,()=>true,()=>false,null)){c.HideNow();c.Allow("steam");Eq(c.HideNow(false),0);Eq(c.HideNow(),1);}});
  Check("unwritable journal prevents hiding",()=>{var d=Dir();Directory.CreateDirectory(Path.Combine(d,"background-windows.json.tmp"));var win=new Windows{rows={Win()}};using(var c=new BackgroundClients(d,win,()=>true,()=>false,null)){Eq(c.HideNow(),0);Eq(win.hides,0);Yes(c.Warning!=null);}});
  Check("reused HWND/PID not restored",()=>{var win=new Windows{rows={Win()}};using(var c=new BackgroundClients(Dir(),win,()=>true,()=>false,null)){c.HideNow();win.rows=new List<ClientWindow>{new ClientWindow{handle=1,pid=1,startedTicks=999,path="steam.exe",visible=true}};c.RestoreAll();Eq(win.restores,0);}});
  Check("crash journal restored and future auto remains enabled",()=>{var d=Dir();var w=Win();w.visible=false;File.WriteAllText(Path.Combine(d,"background-windows.json"),JsonConvert.SerializeObject(new[]{w}));var win=new Windows{rows={w}};using(var c=new BackgroundClients(d,win,()=>true,()=>false,null)){Eq(win.restores,1);Eq(c.HideNow(false),1);}});
  Check("unknown process cannot enter restore ledger",()=>{var d=Dir();var w=Win(null);w.path="game.exe";File.WriteAllText(Path.Combine(d,"background-windows.json"),JsonConvert.SerializeObject(new[]{w}));var win=new Windows{rows={w}};using(var c=new BackgroundClients(d,win,()=>true,()=>false,null)){Eq(win.restores,0);}});
  Check("history warning clears when disk recovers",()=>{var d=Dir();var block=Path.Combine(d,"play-history.json.tmp");Directory.CreateDirectory(block);var h=new PlayHistory(d);h.Started(A,"Test","steam",t);Yes(h.Warning!=null);Directory.Delete(block);h.Stopped(A,t.AddMinutes(1),60);Yes(h.Warning==null);Eq(new PlayHistory(d).Snapshot().Single().state,"completed");});
  Check("window warning clears when journal recovers",()=>{var d=Dir();var block=Path.Combine(d,"background-windows.json.tmp");Directory.CreateDirectory(block);var win=new Windows{rows={Win()}};using(var c=new BackgroundClients(d,win,()=>true,()=>false,null)){Yes(c.Warning!=null);Directory.Delete(block);Eq(c.HideNow(),1);Yes(c.Warning==null);}});
  Check("backup recovery does not replace good backup with corrupt main",()=>{var d=Dir();var p=Path.Combine(d,"play-history.json");var h=new PlayHistory(d);h.Started(A,"Test","steam",t);h.Stopped(A,t.AddMinutes(1),60);var good=File.ReadAllText(p+".bak");File.WriteAllText(p,"broken-main");var restored=new PlayHistory(d);Eq(restored.Snapshot().Length,1);Eq(File.ReadAllText(p+".bak"),good);});
  Console.WriteLine(checks+" ACTIVITY/BACKGROUND CHECKS PASSED");
 }finally{try{Directory.Delete(root,true);}catch{}}}
}
