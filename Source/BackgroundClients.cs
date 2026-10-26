using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
namespace Orbit {
 public sealed class ClientWindow {
  public long handle,startedTicks;public int pid;public string store,path,title,windowClass;public bool visible,owned,dialog;
 }
 public interface IClientWindows {
  List<ClientWindow> List();bool Same(ClientWindow w);bool Visible(ClientWindow w);bool Hide(ClientWindow w);bool Restore(ClientWindow w);
 }
 public static class ClientWindowRules {
  public static string Store(string executable) {
   switch((executable??"").ToLowerInvariant()){
    case "steam.exe":case "steamwebhelper.exe":return "steam";
    case "epicgameslauncher.exe":return "epic";
    case "eadesktop.exe":case "ealauncher.exe":return "ea";
    case "galaxyclient.exe":return "gog";
    case "upc.exe":case "ubisoftconnect.exe":case "uplay.exe":return "ubisoft";
    case "battle.net.exe":return "battlenet";default:return null;
   }
  }
  public static bool IsMain(ClientWindow w) {
   if(w==null||!w.visible||w.owned||w.dialog||w.handle==0||w.startedTicks<=0||w.pid<=0||w.store!=Store(Path.GetFileName(w.path)))return false;
   var t=(w.title??"").Trim().ToLowerInvariant();
   switch(w.store){case "steam":return t=="steam";case "epic":return t=="epic games launcher";case "ea":return t=="ea"||t=="ea app"||t=="ea desktop";case "gog":return t=="gog galaxy"||t=="gog galaxy 2.0";case "ubisoft":return t=="ubisoft connect"||t=="uplay";case "battlenet":return t=="battle.net";default:return false;}
  }
 }
 // Only known main client windows are hidden, never processes or arbitrary game windows.
 public sealed class BackgroundClients:IDisposable {
  readonly IClientWindows windows;readonly string journal;readonly Func<bool> enabled,blocked;readonly Action changed;
  List<ClientWindow> hidden=new List<ClientWindow>();readonly Dictionary<string,DateTime> allowedUntil=new Dictionary<string,DateTime>();int generation;bool disposed,saveFailed;
  public string Warning {get;private set;}
  public BackgroundClients(string directory,IClientWindows windows,Func<bool> enabled,Func<bool> blocked,Action changed){
   this.windows=windows;this.enabled=enabled;this.blocked=blocked;this.changed=changed;journal=Path.Combine(directory,"background-windows.json");
   Directory.CreateDirectory(directory);
   try{if(File.Exists(journal)&&new FileInfo(journal).Length<=262144){var saved=JsonConvert.DeserializeObject<List<ClientWindow>>(File.ReadAllText(journal));if(saved!=null)hidden=saved.Where(w=>w!=null&&w.handle!=0&&w.pid>0&&w.startedTicks>0&&w.store!=null&&ClientWindowRules.Store(Path.GetFileName(w.path))==w.store).Take(60).ToList();}}catch{Warning="Не удалось прочитать список скрытых окон.";}
   Restore(null);
  }
  bool Save(){try{var tmp=journal+".tmp";File.WriteAllText(tmp,JsonConvert.SerializeObject(hidden));if(File.Exists(journal))File.Replace(tmp,journal,null);else File.Move(tmp,journal);if(saveFailed){saveFailed=false;Warning=null;}return true;}catch{saveFailed=true;Warning="Не удалось сохранить список окон. Автоматическое скрытие приостановлено.";return false;}}
  void Prune(){hidden.RemoveAll(w=>!windows.Same(w));}
  public string[] HiddenStores {get {Prune();return hidden.Where(w=>!windows.Visible(w)).Select(w=>w.store).Distinct().ToArray();}}
  public void Allow(string store){generation++;if(store!=null){allowedUntil[store]=DateTime.UtcNow.AddMinutes(10);Restore(store);}else RestoreAll();}
  public void Restore(string store) {
   generation++;foreach(var w in hidden.Where(w=>store==null||w.store==store).ToArray()){if(!windows.Same(w)||windows.Restore(w))hidden.Remove(w);}
   Save();changed?.Invoke();
  }
  public void RestoreAll(){generation++;foreach(var s in new[]{"steam","epic","ea","gog","ubisoft","battlenet"})allowedUntil[s]=DateTime.UtcNow.AddMinutes(10);Restore(null);}
  public async void Schedule() {
   if(!enabled()||disposed)return;var epoch=++generation;
   // A short, bounded sweep after success, not a permanent window-hiding loop.
   foreach(var delay in new[]{1800,3200}){await Task.Delay(delay);if(disposed||epoch!=generation||!enabled()||blocked())return;HideNow(false);}
  }
  public int HideNow(bool explicitRequest=true) {
   if(disposed||blocked()||(!explicitRequest&&!enabled()))return 0;if(explicitRequest){generation++;allowedUntil.Clear();hidden.RemoveAll(w=>windows.Visible(w));}
   Prune();var all=windows.List();int count=0;
   foreach(var w in all.Where(ClientWindowRules.IsMain)){
    if(all.Any(x=>x.pid==w.pid&&x.visible&&x.dialog))continue;
    DateTime until;if(!explicitRequest&&allowedUntil.TryGetValue(w.store,out until)&&until>DateTime.UtcNow)continue;
    if(hidden.Any(x=>x.handle==w.handle))continue;if(hidden.Count>=60)break;hidden.Add(w);
    // Persist restoration identities before issuing SW_HIDE; fail closed if not writable.
    if(!Save()){hidden.Remove(w);break;}
    if(windows.Hide(w))count++;else hidden.Remove(w);
   }
   Save();changed?.Invoke();return count;
  }
  public void Cancel(){generation++;}
  public void Dispose(){if(disposed)return;disposed=true;generation++;RestoreAll();}
 }
}
