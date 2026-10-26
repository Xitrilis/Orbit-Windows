using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Text.RegularExpressions;using Newtonsoft.Json;
namespace Orbit {
 public sealed class ArtworkAsset {public string file;public double x=50,y=50,zoom=1;public ArtworkAsset Copy()=>new ArtworkAsset{file=file,x=x,y=y,zoom=zoom};}
 public sealed class ArtworkChange {public string mode="keep",token;public double x=50,y=50,zoom=1;}
 public sealed class ArtworkView {public string url,original;public bool custom;public double x=50,y=50,zoom=1;}
 public sealed class ArtworkStore:IDisposable {
  public static readonly string[] Slots={"icon","cover","background"};
  readonly string folder,manifest;Dictionary<string,Dictionary<string,ArtworkAsset>> saved=new Dictionary<string,Dictionary<string,ArtworkAsset>>();
  sealed class Pending{public string slot,file;}readonly Dictionary<string,Pending> pending=new Dictionary<string,Pending>();
  string session,game;public string LoadError{get;private set;}
  public ArtworkStore(string directory){
   folder=Path.Combine(directory,"images");manifest=Path.Combine(directory,"artwork.json");Directory.CreateDirectory(folder);
   if(File.Exists(manifest)){try{saved=Load(manifest);}catch{try{saved=Load(manifest+".bak");}catch{LoadError="Не удалось прочитать оформление. Сохранение отключено, чтобы не перезаписать прежние настройки.";}}}
   if(LoadError==null)Collect();
  }
  static Dictionary<string,Dictionary<string,ArtworkAsset>> Load(string path){
   var value=JsonConvert.DeserializeObject<Dictionary<string,Dictionary<string,ArtworkAsset>>>(File.ReadAllText(path));if(value==null)throw new InvalidDataException();
   foreach(var pair in value){GameId(pair.Key);if(pair.Value==null)throw new InvalidDataException();foreach(var a in pair.Value){Slot(a.Key);Validate(a.Value);}}return value;
  }
  static string GameId(string value){Guid id;if(!Guid.TryParse(value,out id))throw new InvalidDataException("Игра не найдена.");return id.ToString();}
  static void Slot(string value){if(!Slots.Contains(value))throw new InvalidDataException("Неизвестный вид изображения.");}
  static void Validate(ArtworkAsset a){
   if(a==null||double.IsNaN(a.x)||double.IsInfinity(a.x)||a.x<0||a.x>100||double.IsNaN(a.y)||double.IsInfinity(a.y)||a.y<0||a.y>100||double.IsNaN(a.zoom)||double.IsInfinity(a.zoom)||a.zoom<1||a.zoom>2)throw new InvalidDataException("Проверь положение и масштаб изображения.");
   if(a.file!=null&&!Regex.IsMatch(a.file,@"\A[a-f0-9]{32}\.png\z"))throw new InvalidDataException("Неверный файл оформления.");
  }
  public string Begin(string id){Cancel(session);game=GameId(id);session=Guid.NewGuid().ToString("N");return session;}
  public bool Active(string s)=>s!=null&&s==session;
  void Require(string s){if(!Active(s))throw new InvalidOperationException("Окно оформления уже закрыто. Открой его снова.");if(LoadError!=null)throw new IOException(LoadError);}
  public string Stage(string s,string slot,byte[] png){
   Require(s);Slot(slot);byte[] signature={137,80,78,71,13,10,26,10};
   if(png==null||png.Length<8||png.Length>25*1024*1024||!png.Take(8).SequenceEqual(signature))throw new InvalidDataException("Не удалось подготовить изображение.");
   var token=Guid.NewGuid().ToString("N");var file=token+".png";File.WriteAllBytes(Path.Combine(folder,file),png);pending.Add(token,new Pending{slot=slot,file=file});return token;
  }
  public string Preview(string s,string token){Require(s);Pending p;if(!pending.TryGetValue(token,out p))throw new InvalidDataException("Изображение уже недоступно.");return new Uri(Path.GetFullPath(Path.Combine(folder,p.file))).AbsoluteUri;}
  public Dictionary<string,ArtworkView> View(string id,Dictionary<string,string> originals){
   Dictionary<string,ArtworkAsset> entry;saved.TryGetValue(id,out entry);
   return Slots.ToDictionary(slot=>slot,slot=>{ArtworkAsset a=null;entry?.TryGetValue(slot,out a);var exists=a?.file!=null&&File.Exists(Path.Combine(folder,a.file));string original;originals.TryGetValue(slot,out original);return new ArtworkView{original=original??"",url=exists?new Uri(Path.GetFullPath(Path.Combine(folder,a.file))).AbsoluteUri:original??"",custom=exists,x=a?.x??50,y=a?.y??50,zoom=a?.zoom??1};});
  }
  public void Save(string s,IEnumerable<string> ids,Dictionary<string,ArtworkChange> changes){
   Require(s);var targets=ids?.Select(GameId).Distinct().ToArray();
   if(targets==null||targets.Length==0||targets.Length>100||!targets.Contains(game))throw new InvalidDataException("Не удалось определить копии игры.");
   if(changes==null||changes.Count!=3||changes.Keys.Any(k=>!Slots.Contains(k)))throw new InvalidDataException("Неполные настройки оформления.");
   var next=saved.ToDictionary(p=>p.Key,p=>p.Value.ToDictionary(a=>a.Key,a=>a.Value.Copy()));
   foreach(var id in targets){
    Dictionary<string,ArtworkAsset> entry;if(!next.TryGetValue(id,out entry))entry=new Dictionary<string,ArtworkAsset>();
    foreach(var slot in Slots){
     var change=changes[slot];if(change==null)throw new InvalidDataException();if(change.mode=="keep")continue;if(change.mode=="reset"){entry.Remove(slot);continue;}
     var asset=new ArtworkAsset{x=change.x,y=change.y,zoom=change.zoom};
     if(change.mode=="adjust"){ArtworkAsset old;asset.file=entry.TryGetValue(slot,out old)?old.file:null;}
     else if(change.mode=="original"){asset.file=null;}
     else if(change.mode=="set"){Pending p;if(change.token==null||!pending.TryGetValue(change.token,out p)||p.slot!=slot||!File.Exists(Path.Combine(folder,p.file)))throw new InvalidDataException("Выбери изображение ещё раз.");asset.file=p.file;}
     else throw new InvalidDataException("Неизвестное действие оформления.");
     Validate(asset);if(asset.file!=null||asset.x!=50||asset.y!=50||asset.zoom!=1)entry[slot]=asset;else entry.Remove(slot);
    }if(entry.Count==0)next.Remove(id);else next[id]=entry;
   }
   var tmp=manifest+".tmp";try{File.WriteAllText(tmp,JsonConvert.SerializeObject(next,Formatting.Indented));if(File.Exists(manifest))File.Replace(tmp,manifest,manifest+".bak");else File.Move(tmp,manifest);}finally{try{if(File.Exists(tmp))File.Delete(tmp);}catch{}}
   saved=next;pending.Clear();session=null;game=null;Collect();
  }
  void Collect(){
   if(LoadError!=null)return;var used=new HashSet<string>(saved.Values.SelectMany(v=>v.Values).Where(a=>a.file!=null).Select(a=>a.file));
   try{if(File.Exists(manifest+".bak"))foreach(var a in Load(manifest+".bak").Values.SelectMany(v=>v.Values).Where(a=>a.file!=null))used.Add(a.file);}catch{}
   foreach(var a in pending.Values)used.Add(a.file);foreach(var file in Directory.EnumerateFiles(folder,"*.png"))if(Regex.IsMatch(Path.GetFileName(file),@"\A[a-f0-9]{32}\.png\z")&&!used.Contains(Path.GetFileName(file)))try{File.Delete(file);}catch{}
  }
  public void Cancel(string s){if(!Active(s))return;pending.Clear();session=null;game=null;Collect();}
  public void Dispose(){Cancel(session);}
 }
}
