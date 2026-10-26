using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace Orbit {
 public sealed partial class OrbitPlugin {
  GameJournal gameJournal;
  async void JournalCommand(JObject m){string requestId=(string)m["requestId"];try{var ids=((JArray)m["ids"])?.Values<string>().ToArray();if(ids==null||ids.Length>100)throw new JournalException("invalid");foreach(var id in ids)GetGame(id);
    string kind=(string)m["kind"],note=(string)m["note"];bool queued=(bool?)m["queued"]??false,first=(bool?)m["first"]??false;
    var expected=m["expected"]?.ToObject<Dictionary<string,long>>();
    var result=await Task.Run(()=>kind=="journalNote"?gameJournal.SetNote(ids,note,expected):kind=="journalQueue"?gameJournal.SetQueued(ids,queued,first,DateTime.UtcNow):throw new JournalException("invalid"));
    if(!exiting){Send(new{kind="journalSaved",requestId,journal=result});Snapshot(true);}
   }catch(Exception ex){diagnosticLog?.Record("journal.failed",ex,"journal");var code=(ex as JournalException)?.Code??"write";Send(new{kind="journalError",requestId,code,message=code=="conflict"?"Заметка уже изменилась. Сравни тексты перед повторным сохранением.":code=="readonly"?"Запись отключена: проверь состояние данных.":code=="invalid"?"Не удалось сохранить: данные игры изменились или текст слишком длинный.":"Не удалось сохранить изменения. Черновик остаётся в Orbit; проверь доступ к папке и свободное место.",journal=gameJournal?.Snapshot()});}
  }
 }
}
