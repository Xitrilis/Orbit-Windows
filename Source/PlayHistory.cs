using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
namespace Orbit {
 public sealed class PlaySession {
  public string id,gameId,name,store,state;
  public DateTime startedAt;public DateTime? endedAt;public ulong? seconds;
 }
 public sealed class PlayHistory {
  readonly string path;readonly object gate=new object();List<PlaySession> rows=new List<PlaySession>();bool blocked,saveFailed,recoveredFromBackup;
  public string Warning {get;private set;}
  public PlayHistory(string directory) {
   Directory.CreateDirectory(directory);path=Path.Combine(directory,"play-history.json");
   if(File.Exists(path)||File.Exists(path+".bak")){try{rows=Read(path);}catch{try{rows=Read(path+".bak");recoveredFromBackup=true;Warning="История восстановлена из резервной копии.";}catch{blocked=true;Warning="Не удалось прочитать историю. Старые файлы сохранены; новые запуски записываются только в память.";} }}
   bool changed=false;foreach(var r in rows.Where(r=>r.state=="running")){r.state="interrupted";changed=true;}if(changed)Save();
  }
  static List<PlaySession> Read(string file) {
   if(new FileInfo(file).Length>5*1024*1024)throw new InvalidDataException();
   var list=JsonConvert.DeserializeObject<List<PlaySession>>(File.ReadAllText(file));
   if(list==null||list.Count>2000||list.Any(r=>r==null||!Guid.TryParse(r.id,out _)||!Guid.TryParse(r.gameId,out _)||string.IsNullOrEmpty(r.name)||r.name.Length>300||!new[]{"steam","epic","ea","gog","ubisoft","battlenet"}.Contains(r.store)||!new[]{"running","completed","interrupted"}.Contains(r.state)||r.startedAt.Year<2000||r.startedAt.Year>2100||r.endedAt<r.startedAt||r.seconds>31536000))throw new InvalidDataException();
   return list.GroupBy(r=>r.id).Select(g=>g.First()).OrderByDescending(r=>r.startedAt).ToList();
  }
  static DateTime Utc(DateTime d)=>d.Kind==DateTimeKind.Utc?d:d.ToUniversalTime();
  void Save() {
   if(blocked)return;
   try{var tmp=path+".tmp";File.WriteAllText(tmp,JsonConvert.SerializeObject(rows));if(File.Exists(path))File.Replace(tmp,path,recoveredFromBackup?null:path+".bak");else File.Move(tmp,path);recoveredFromBackup=false;if(saveFailed){saveFailed=false;Warning=null;}}
   catch{saveFailed=true;Warning="Не удалось сохранить историю на диск. Текущий сеанс виден до закрытия Orbit.";}
  }
  public void Started(string gameId,string name,string store,DateTime at) {
   if(!Guid.TryParse(gameId,out _)||string.IsNullOrWhiteSpace(name)||!new[]{"steam","epic","ea","gog","ubisoft","battlenet"}.Contains(store))return;
   lock(gate){if(rows.Any(r=>r.gameId==gameId&&r.state=="running"))return;rows.Insert(0,new PlaySession{id=Guid.NewGuid().ToString(),gameId=gameId,name=name.Substring(0,Math.Min(300,name.Length)),store=store,startedAt=Utc(at),state="running"});rows=rows.OrderByDescending(r=>r.startedAt).Take(2000).ToList();Save();}
  }
  public void Stopped(string gameId,DateTime at,ulong seconds,bool trackingStopped=false) {
   lock(gate){var r=rows.FirstOrDefault(x=>x.gameId==gameId&&x.state=="running");if(r==null)return;r.endedAt=trackingStopped?(DateTime?)null:(Utc(at)<r.startedAt?r.startedAt:Utc(at));r.seconds=!trackingStopped&&seconds<=31536000?(ulong?)seconds:null;r.state=trackingStopped?"interrupted":"completed";Save();}
  }
  public PlaySession[] Snapshot(){lock(gate)return rows.OrderByDescending(r=>r.startedAt).Select(r=>new PlaySession{id=r.id,gameId=r.gameId,name=r.name,store=r.store,state=r.state,startedAt=r.startedAt,endedAt=r.endedAt,seconds=r.seconds}).ToArray();}
  public void Shutdown(){lock(gate){foreach(var r in rows.Where(r=>r.state=="running"))r.state="interrupted";Save();}}
 }
}
