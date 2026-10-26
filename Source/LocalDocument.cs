using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Orbit {
 // Atomic local documents. A backup is never used to downgrade a newer schema.
 public sealed class LocalDocument<T> where T:new() {
  readonly string file; readonly int maxBytes; readonly Action<T> validate; bool recovered;
  public bool ReadOnly {get;private set;} public string Warning {get;private set;}
  public T Value {get;private set;}
  sealed class FutureSchema:Exception {}
  public LocalDocument(string file,int maxBytes,Action<T> validate){this.file=file;this.maxBytes=maxBytes;this.validate=validate;Value=new T();
   if(!File.Exists(file)&&!File.Exists(file+".bak"))return;
   try{Value=Read(file);return;}catch(FutureSchema){ReadOnly=true;Warning="Данные созданы более новой версией Orbit. Запись отключена.";return;}catch{}
   try{Value=Read(file+".bak");recovered=true;Warning="Восстановлена резервная копия данных.";}catch{ReadOnly=true;Warning="Не удалось прочитать данные и резервную копию. Запись отключена; файлы сохранены.";}
  }
  T Read(string path){if(new FileInfo(path).Length>maxBytes)throw new IOException();JObject root;
   using(var reader=new JsonTextReader(new StreamReader(path)) {MaxDepth=20}){root=JObject.Load(reader);if(reader.Read())throw new IOException();}
   if(root["schema"]?.Type!=JTokenType.Integer)throw new IOException();if((int)root["schema"]!=1)throw new FutureSchema();
   var result=root.ToObject<T>(JsonSerializer.Create(new JsonSerializerSettings{MissingMemberHandling=MissingMemberHandling.Error}));validate(result);return result;
  }
  public void Save(T value){if(ReadOnly)throw new InvalidOperationException("readonly");validate(value);var bytes=System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));if(bytes.Length>maxBytes)throw new IOException();
   string tmp=file+".tmp-"+Guid.NewGuid().ToString("N");try{Directory.CreateDirectory(Path.GetDirectoryName(file));
    using(var stream=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
    if(File.Exists(file))File.Replace(tmp,file,recovered?null:file+".bak");else File.Move(tmp,file);
    Value=value;Warning=null;recovered=false;
   }catch{Warning="Не удалось сохранить изменения. Проверь доступ к папке Orbit и свободное место.";throw;}finally{try{if(File.Exists(tmp))File.Delete(tmp);}catch{}}
  }
 }
}
