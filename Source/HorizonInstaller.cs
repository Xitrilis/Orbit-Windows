using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Orbit {
 public static class HorizonInstaller {
  const string Api="https://api.github.com/repos/horizon-rust/horizon/releases?per_page=100";
  const long MaxZip=160L*1024*1024,MaxExpanded=400L*1024*1024;
  static bool Allowed(Uri u)=>u.Scheme==Uri.UriSchemeHttps && (u.Host=="api.github.com"||u.Host=="github.com"||u.Host=="release-assets.githubusercontent.com"||u.Host=="objects.githubusercontent.com");
  static async Task<HttpWebResponse> Get(Uri uri){
   for(int i=0;i<5;i++){
    if(!Allowed(uri))throw new IOException("Неизвестный адрес загрузки Horizon.");
    var req=(HttpWebRequest)WebRequest.Create(uri);req.UserAgent="Orbit-Horizon/1";req.Accept="application/vnd.github+json";req.AllowAutoRedirect=false;req.Timeout=30000;req.ReadWriteTimeout=60000;
    HttpWebResponse response;try{response=(HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false);}catch(WebException e){response=e.Response as HttpWebResponse;if(response==null)throw;}
    if((int)response.StatusCode>=300&&(int)response.StatusCode<400){var next=response.Headers["Location"];response.Dispose();if(string.IsNullOrEmpty(next))throw new IOException("Пустой адрес загрузки.");uri=new Uri(uri,next);continue;}
    if(response.StatusCode!=HttpStatusCode.OK){var status=(int)response.StatusCode;response.Dispose();throw new IOException("GitHub вернул ошибку "+status+". Попробуй позже или выбери официальный файл вручную.");}
    return response;
   }
   throw new IOException("Слишком много перенаправлений при загрузке Horizon.");
  }
  public static JObject SelectRelease(JArray releases){
   foreach(var release in releases.OfType<JObject>()){
    string tag=(string)release["tag_name"]??"";
    if(!Regex.IsMatch(tag,@"\Alauncher(?:-v|@)[0-9][A-Za-z0-9.\-]*\z")||(bool?)release["draft"]==true||(bool?)release["prerelease"]==true)continue;
    var asset=(release["assets"] as JArray)?.OfType<JObject>().FirstOrDefault(a=>(string)a["name"]=="horizon-launcher-windows-x64.zip");
    string digest=(string)asset?["digest"]??"",url=(string)asset?["browser_download_url"]??"";long size=(long?)asset?["size"]??0;
    if(asset==null)continue;Uri u;if(!Regex.IsMatch(digest,@"\Asha256:[a-fA-F0-9]{64}\z")||size<1||size>MaxZip||!Uri.TryCreate(url,UriKind.Absolute,out u)||u.Host!="github.com"||u.Scheme!="https"||Uri.UnescapeDataString(u.AbsolutePath)!="/horizon-rust/horizon/releases/download/"+tag+"/horizon-launcher-windows-x64.zip")throw new IOException("У последнего выпуска Horizon нет проверенного архива. Скачай его на официальном сайте и выбери файл лаунчера вручную.");
    return new JObject{{"tag",tag},{"url",url},{"digest",digest.Substring(7).ToLowerInvariant()},{"size",size}};
   }
   throw new IOException("Не найден официальный архив Horizon с опубликованной SHA-256. Скачай его на сайте и выбери файл лаунчера вручную.");
  }
  public static async Task<string> Install(string home,Action<long,long> progress){
   ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   JObject release;
   using(var response=await Get(new Uri(Api)).ConfigureAwait(false))using(var s=response.GetResponseStream())using(var reader=new StreamReader(s,Encoding.UTF8)){
    var data=await reader.ReadToEndAsync().ConfigureAwait(false);if(data.Length>4*1024*1024)throw new IOException("Ответ GitHub слишком велик.");release=SelectRelease(JArray.Parse(data));
   }
   Directory.CreateDirectory(home);var stage=Path.Combine(home,".orbit-horizon-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   try{
    var archive=Path.Combine(stage,"launcher.zip");long total=(long)release["size"],done=0;
    using(var response=await Get(new Uri((string)release["url"])).ConfigureAwait(false))using(var s=response.GetResponseStream())using(var f=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
     var b=new byte[65536];int n;while((n=await s.ReadAsync(b,0,b.Length).ConfigureAwait(false))>0){done+=n;if(done>total||done>MaxZip)throw new IOException("Размер загрузки не совпадает с опубликованным.");await f.WriteAsync(b,0,n).ConfigureAwait(false);progress?.Invoke(done,total);}if(done!=total)throw new IOException("Загрузка Horizon прервалась.");
    }
    string hash;using(var s=File.OpenRead(archive))using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(s)).Replace("-","").ToLowerInvariant();
    if(hash!=(string)release["digest"])throw new IOException("Контрольная сумма архива Horizon не совпала. Установка отменена.");
    var extract=Path.Combine(stage,"files");Directory.CreateDirectory(extract);long expanded=0;int count=0;string found=null;
    using(var zip=ZipFile.OpenRead(archive))foreach(var e in zip.Entries){
     if(++count>2000||(expanded+=e.Length)>MaxExpanded)throw new IOException("Архив Horizon слишком велик.");
     var relative=e.FullName.Replace('/',Path.DirectorySeparatorChar);if(relative.StartsWith("\\")||relative.Contains(":"))throw new IOException("Некорректный путь внутри архива Horizon.");
     var target=Path.GetFullPath(Path.Combine(extract,relative));if(!target.StartsWith(Path.GetFullPath(extract)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Некорректный путь внутри архива Horizon.");
     if(string.IsNullOrEmpty(e.Name)){Directory.CreateDirectory(target);continue;}
     Directory.CreateDirectory(Path.GetDirectoryName(target));using(var input=e.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var b=new byte[65536];long written=0;int n;while((n=input.Read(b,0,b.Length))>0){written+=n;if(written>e.Length)throw new IOException("Некорректный размер файла внутри архива Horizon.");output.Write(b,0,n);}if(written!=e.Length)throw new IOException("Архив Horizon распакован не полностью.");}
     if(e.Name.Equals("horizon-launcher.exe",StringComparison.OrdinalIgnoreCase)){if(found!=null)throw new IOException("В архиве несколько файлов лаунчера.");found=target;}
    }
    if(found==null)throw new IOException("В архиве не найден horizon-launcher.exe.");
    var suffix=found.Substring(extract.Length+1);var dest=Path.Combine(home,"orbit-launcher-"+((string)release["tag"]).Replace('@','-')+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
    Directory.Move(extract,dest);return Path.Combine(dest,suffix);
   }finally{try{Directory.Delete(stage,true);}catch{}}
  }
 }
}
