using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
namespace Orbit {
 public sealed class JournalEntry {public string gameId;public string note="";public long noteRevision;public long position;public DateTime? queuedAt;public JournalEntry Copy()=> (JournalEntry)MemberwiseClone();}
 public sealed class JournalDocument {[JsonProperty(Required=Required.Always)]public int schema=1;[JsonProperty(Required=Required.Always)]public long revision;[JsonProperty(Required=Required.Always)]public List<JournalEntry> entries=new List<JournalEntry>();}
 public sealed class JournalSnapshot {public long revision;public JournalEntry[] entries;public bool readOnly;public string warning;}
 public sealed class JournalException:Exception {public string Code{get;}public JournalException(string code):base("Journal operation failed"){Code=code;}}
 public sealed class GameJournal {
  public const int MaxNote=4000,MaxEntries=5000;const long MaxNumber=9007199254740000;
  readonly object gate=new object();readonly LocalDocument<JournalDocument> doc;
  public GameJournal(string path){doc=new LocalDocument<JournalDocument>(path,64*1024*1024,Validate);}
  static void Validate(JournalDocument d){if(d==null||d.schema!=1||d.revision<0||d.revision>MaxNumber||d.entries==null||d.entries.Count>MaxEntries)throw new JournalException("invalid");var seen=new HashSet<Guid>();
   foreach(var e in d.entries){Guid id;if(e==null||!Guid.TryParse(e.gameId,out id)||!seen.Add(id)||e.note==null||e.note.Length>MaxNote||e.note.Any(c=>char.IsControl(c)&&c!='\n'&&c!='\r'&&c!='\t')||e.noteRevision<0||e.noteRevision>d.revision||Math.Abs(e.position)>MaxNumber||e.queuedAt.HasValue&&(e.queuedAt.Value.Year<2000||e.queuedAt.Value.Year>2100))throw new JournalException("invalid");}
  }
  public JournalSnapshot Snapshot(){lock(gate)return new JournalSnapshot{revision=doc.Value.revision,entries=doc.Value.entries.Select(e=>e.Copy()).ToArray(),readOnly=doc.ReadOnly,warning=doc.Warning};}
  static string[] Ids(IEnumerable<string> ids){var a=(ids??new string[0]).Take(101).ToArray();if(a.Length==0||a.Length>100||a.Any(s=>!Guid.TryParse(s,out var id)))throw new JournalException("invalid");return a.Select(s=>Guid.Parse(s).ToString()).Distinct().ToArray();}
  JournalDocument Next(){if(doc.ReadOnly)throw new JournalException("readonly");if(doc.Value.revision>=MaxNumber)throw new JournalException("limit");return new JournalDocument{revision=doc.Value.revision+1,entries=doc.Value.entries.Select(e=>e.Copy()).ToList()};}
  static JournalEntry Entry(JournalDocument d,string id){var e=d.entries.FirstOrDefault(x=>x.gameId==id);if(e==null){e=new JournalEntry{gameId=id};d.entries.Add(e);}return e;}
  JournalSnapshot Commit(JournalDocument d){d.entries.RemoveAll(e=>e.note.Length==0&&!e.queuedAt.HasValue);if(d.entries.Count>MaxEntries)throw new JournalException("limit");doc.Save(d);return Snapshot();}
  public JournalSnapshot SetNote(IEnumerable<string> gameIds,string text,IDictionary<string,long> expected){lock(gate){var ids=Ids(gameIds);text=(text??"").Replace("\r\n","\n").Replace("\r","\n");if(string.IsNullOrWhiteSpace(text))text="";
   if(text.Length>MaxNote||text.Any(c=>char.IsControl(c)&&c!='\n'&&c!='\t'))throw new JournalException("invalid");var d=Next();
   foreach(var id in ids){long rev;var e=d.entries.FirstOrDefault(x=>x.gameId==id);if(expected==null||!expected.TryGetValue(id,out rev)||rev!=(e?.noteRevision??0))throw new JournalException("conflict");}
   foreach(var id in ids){var e=Entry(d,id);e.note=text;e.noteRevision=d.revision;}return Commit(d);
  }}
  public JournalSnapshot SetQueued(IEnumerable<string> gameIds,bool queued,bool first,DateTime now){lock(gate){var ids=Ids(gameIds);var d=Next();var old=d.entries.Where(e=>ids.Contains(e.gameId)&&e.queuedAt.HasValue).ToArray();long pos=first?(d.entries.Select(e=>e.position).DefaultIfEmpty(0).Min()-1):old.Length>0?old.Min(e=>e.position):d.entries.Select(e=>e.position).DefaultIfEmpty(0).Max()+1;DateTime date=old.Length>0?old.Min(e=>e.queuedAt.Value):now.ToUniversalTime();foreach(var id in ids){var e=Entry(d,id);e.queuedAt=queued?(DateTime?)date:null;e.position=queued?pos:0;}return Commit(d);}}
 }
}
