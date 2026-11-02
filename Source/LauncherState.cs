using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Orbit
{
    public sealed class InstallJob { public string id,gameId,appId,name,store,path,state,message; public double? percent; public DateTime? createdAt,completedAt; }
    public sealed class StateRead<T> { public T value; public bool repaired; }

    // Reads legacy documents without changing their schema. Preserve damaged originals before repair.
    public sealed class LauncherStateFile<T>
    {
        readonly string path; readonly int maxBytes; readonly Func<string,StateRead<T>> parse;
        bool recovered;
        public T Value { get; private set; }
        public bool ReadOnly { get; private set; }
        public string Warning { get; private set; }
        public LauncherStateFile(string path,int maxBytes,Func<string,StateRead<T>> parse,Func<T> empty)
        {
            this.path=path;this.maxBytes=maxBytes;this.parse=parse;Value=empty();
            if(!File.Exists(path)&&!Directory.Exists(path)&&!File.Exists(path+".bak"))return;
            StateRead<T> result=null;
            try{result=Read(path);}catch{}
            if(result==null){try{result=Read(path+".bak");recovered=true;}catch{}}
            if(result==null){ReadOnly=true;Warning="Не удалось прочитать данные и резервную копию. Файлы сохранены, запись отключена.";return;}
            Value=result.value;
            if(!recovered&&!result.repaired)return;
            recovered=true;
            try
            {
                if(Directory.Exists(path))throw new IOException();
                if(File.Exists(path))
                {
                    if(new FileInfo(path).Length>maxBytes)throw new IOException();
                    File.Copy(path,path+".invalid-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
                }
                Warning=File.Exists(path)?"Данные восстановлены; повреждённый исходный файл сохранён отдельно.":"Данные восстановлены из резервной копии.";
            }
            catch{ReadOnly=true;Warning="Данные прочитаны с восстановлением. Не удалось сохранить копию исходного файла, поэтому запись отключена.";}
        }
        StateRead<T> Read(string file)
        {
            using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length>maxBytes)throw new InvalidDataException();
                using(var reader=new StreamReader(stream))return parse(reader.ReadToEnd());
            }
        }
        public void Save(T value)
        {
            if(ReadOnly)throw new InvalidOperationException("Запись отключена: сохранённые данные требуют восстановления. Исходные файлы оставлены в папке OrbitData.");
            var text=JsonConvert.SerializeObject(value);var checkedValue=parse(text);
            if(checkedValue.repaired)throw new InvalidDataException("Некорректные данные не сохранены.");
            var bytes=Encoding.UTF8.GetBytes(text);if(bytes.Length>maxBytes)throw new InvalidDataException("Слишком большой файл настроек.");
            var temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                if(File.Exists(path))File.Replace(temporary,path,recovered?null:path+".bak");else File.Move(temporary,path);
                Value=checkedValue.value;recovered=false;Warning=null;
            }
            finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
        }
    }

    public static class LauncherState
    {
        static readonly string[] Stores={"steam","epic","ea","battlenet","ubisoft","gog"};
        static readonly string[] Flags={"backgroundClients","reducedMotion","lowEffects"};
        static JToken Json(string text)
        {
            using(var reader=new JsonTextReader(new StringReader(text)){MaxDepth=20,DateParseHandling=DateParseHandling.None})
            {var root=JToken.ReadFrom(reader);if(reader.Read())throw new InvalidDataException();return root;}
        }
        static bool Id(string s)=>Guid.TryParse(s,out var id)&&id!=Guid.Empty;
        static bool Text(string s,int length)=>s!=null&&s.Length<=length&&!s.Any(char.IsControl);
        public static StateRead<JObject> Preferences(string json)
        {
            var value=Json(json) as JObject;if(value==null)throw new InvalidDataException();bool repaired=false;
            foreach(var key in Flags)if(value[key]!=null&&value[key].Type!=JTokenType.Boolean){value.Remove(key);repaired=true;}
            if(value["view"]!=null&&(value["view"].Type!=JTokenType.String||((string)value["view"]!="grid"&&(string)value["view"]!="list"))){value.Remove("view");repaired=true;}
            foreach(var key in new[]{"preferred","groups","separate"})
            {
                if(value[key]==null)continue;
                var map=value[key] as JObject;if(map==null){value.Remove(key);repaired=true;continue;}
                foreach(var property in map.Properties().ToArray())
                {
                    bool ok=key=="preferred"?Text(property.Name,1024)&&property.Name.Length>0&&property.Value.Type==JTokenType.String&&Id((string)property.Value):
                        Id(property.Name)&&(key=="separate"?property.Value.Type==JTokenType.Boolean:property.Value.Type==JTokenType.String&&Text((string)property.Value,1024)&&((string)property.Value).Length>0);
                    if(!ok){property.Remove();repaired=true;}
                }
            }
            return new StateRead<JObject>{value=value,repaired=repaired};
        }
        public static JObject Patch(JObject current,JObject patch)
        {
            var next=(JObject)current.DeepClone();
            foreach(var property in patch.Properties().Where(p=>Flags.Concat(new[]{"preferred","groups","separate","view"}).Contains(p.Name)))next[property.Name]=property.Value.DeepClone();
            var checkedValue=Preferences(next.ToString());if(checkedValue.repaired)throw new InvalidOperationException("Неверные настройки. Изменения не сохранены.");return checkedValue.value;
        }
        public static StateRead<List<InstallJob>> Downloads(string json)
        {
            var rows=Json(json) as JArray;if(rows==null)throw new InvalidDataException();var jobs=new List<InstallJob>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);bool repaired=false;
            foreach(var row in rows)
            {
                InstallJob job;try{job=(row as JObject)?.ToObject<InstallJob>();}catch{repaired=true;continue;}
                if(job==null||!Id(job.id)||!Id(job.gameId)||!Stores.Contains(job.store)||!Text(job.path,2000)||!Text(job.appId,200)||!Text(job.name,500)||!seen.Add(job.id)){repaired=true;continue;}
                if(job.name.Length==0){job.name="Игра";repaired=true;}
                if(!new[]{"preparing","handed","attention","complete"}.Contains(job.state)){job.state="attention";job.message="Состояние установки неизвестно. Проверь клиент магазина.";repaired=true;}
                if(job.percent.HasValue&&(double.IsNaN(job.percent.Value)||double.IsInfinity(job.percent.Value)||job.percent<0||job.percent>100)){job.percent=null;repaired=true;}
                if(job.createdAt.HasValue&&(job.createdAt.Value.Year<2000||job.createdAt.Value.Year>2100)){job.createdAt=null;repaired=true;}
                if(job.completedAt.HasValue&&(job.completedAt.Value.Year<2000||job.completedAt.Value.Year>2100||job.completedAt<job.createdAt)){job.completedAt=null;repaired=true;}
                if(job.message!=null&&job.message.Length>4000){job.message=job.message.Substring(0,4000);repaired=true;}
                jobs.Add(job);
            }
            return new StateRead<List<InstallJob>>{value=jobs,repaired=repaired};
        }
    }
}
