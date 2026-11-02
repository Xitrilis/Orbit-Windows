using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Forms=System.Windows.Forms;
namespace Orbit {
 public sealed class HorizonSettings {public int schema=1;public string home="",launcher="",minecraft="",game="";}
 public sealed class HorizonController {
  readonly LocalDocument<HorizonSettings> doc; readonly MinecraftSettingsStore minecraftSettings; long settingsRevision; readonly string dataDir;readonly Func<Window> owner;readonly Action<object> send;readonly Action<Exception> fault;readonly Func<bool> exiting;
  readonly Dictionary<string,MinecraftPack> inbox=new Dictionary<string,MinecraftPack>();readonly Dictionary<string,string> hmods=new Dictionary<string,string>();bool busy;
  public HorizonController(string dataDir,Func<Window> owner,Action<object> send,Action<Exception> fault,Func<bool> exiting,MinecraftSettingsStore minecraftSettings=null){this.minecraftSettings=minecraftSettings??new MinecraftSettingsStore(dataDir);this.dataDir=dataDir;this.owner=owner;this.send=send;this.fault=fault;this.exiting=exiting;doc=new LocalDocument<HorizonSettings>(Path.Combine(dataDir,"horizon-settings.json"),65536,Validate);}
  static void Validate(HorizonSettings s){if(s==null||s.schema!=1)throw new InvalidOperationException();foreach(var v in new[]{s.home,s.launcher,s.minecraft,s.game})if(v==null||v.Length>1000||v.Any(char.IsControl)||v.Length>0&&!Path.IsPathRooted(v))throw new InvalidOperationException();}
  HorizonSettings Settings(){var s=JsonConvert.DeserializeObject<HorizonSettings>(JsonConvert.SerializeObject(doc.Value));if(s.home.Length==0)s.home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"horizon-launcher");if(s.game.Length==0)s.game=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"horizon");return s;}
  static bool Exe(string p)=>!string.IsNullOrEmpty(p)&&File.Exists(p)&&string.Equals(Path.GetFileName(p),"horizon-launcher.exe",StringComparison.OrdinalIgnoreCase);
  void Reply(object value){if(!exiting())send(value);}
  MinecraftSettings MC()=>minecraftSettings.Snapshot();
  async Task State(string requestId){
   HorizonSettings s;MinecraftSettings mc;Tuple<MinecraftScan,Dictionary<string,string>> found;long ownRevision,mcRevision;
   do{ownRevision=settingsRevision;mcRevision=minecraftSettings.Revision;s=Settings();mc=MC();found=await Task.Run(()=>{var scan=MinecraftFiles.Scan(mc);var mods=new Dictionary<string,string>();if(Directory.Exists(mc.packFolder))try{foreach(var f in Directory.EnumerateFiles(mc.packFolder,"*.hmod").Take(100)){try{HorizonBridge.CheckHmod(f);mods[MinecraftFiles.Key("hmod-",f)]=f;}catch{}}}catch{}return Tuple.Create(scan,mods);});}while(!exiting()&&(ownRevision!=settingsRevision||mcRevision!=minecraftSettings.Revision));
   if(exiting())return;inbox.Clear();foreach(var p in found.Item1.packs.Where(p=>p.format=="Modrinth"||p.format=="CurseForge"))inbox[p.id]=p;
   hmods.Clear();foreach(var mod in found.Item2)hmods[mod.Key]=mod.Value;
   Reply(new{kind="hzState",requestId,home=s.home,launcher=s.launcher,minecraft=s.minecraft,game=s.game,hmods=hmods.Select(x=>new{id=x.Key,name=Path.GetFileName(x.Value)}).ToArray(),available=Exe(s.launcher),warning=doc.Warning??minecraftSettings.Warning,readOnly=doc.ReadOnly,packReadOnly=minecraftSettings.ReadOnly,packFolder=mc.packFolder,packs=inbox.Values.Select(p=>new{id=p.id,name=p.name,format=p.format,version=p.version}).ToArray()});}
  public GameContext AssistantGame(){try{var s=Settings();var exe=GameCapture.Executable(GameCapture.GetForegroundWindow());if(!string.Equals(Path.GetFileName(exe),"horizon-client.exe",StringComparison.OrdinalIgnoreCase)||!GameDiagnostics.IsInside(exe,Path.Combine(s.home,"versions")))return null;return new GameContext{id="orbit-horizon",name="Minecraft · Horizon",store="Horizon",installDirectory=Path.GetDirectoryName(exe),executable=exe,installed=true,running=true};}catch{return null;}}
  public async Task<string> AssistantState(GameContext game){
   var s=Settings();if(game==null||game.id!="orbit-horizon"||!GameDiagnostics.IsInside(game.executable,Path.Combine(s.home,"versions")))throw new InvalidOperationException("Открой помощник поверх запущенного Horizon.");
   var ctl=Path.Combine(Path.GetDirectoryName(game.executable),"horizon-ctl.exe");if(!File.Exists(ctl))throw new InvalidOperationException("В этой версии Horizon не найден horizon-ctl.");
   var result=await HorizonBridge.Run(ctl,s.home,new[]{"state"},10000);
   if(result.timedOut||result.code!=0)throw new InvalidOperationException("Состояние недоступно. Проверь, что при запуске включён локальный режим ИИ и в Horizon не включён No AI.");
   return HorizonBridge.SafeState(result.output);
  }
  void Done(string id,string message,bool cancelled=false){Reply(new{kind="hzDone",requestId=id,message,cancelled});}
  static string Folder(Window owner,string title){using(var d=new Forms.FolderBrowserDialog{Description=title,ShowNewFolderButton=true})return d.ShowDialog(new WindowOwner(owner))==Forms.DialogResult.OK?d.SelectedPath:null;}
  static string FilePicker(Window owner,string title,string filter){using(var d=new Forms.OpenFileDialog{Title=title,Filter=filter,CheckFileExists=true,RestoreDirectory=true})return d.ShowDialog(new WindowOwner(owner))==Forms.DialogResult.OK?d.FileName:null;}
  static void Open(string url){Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
  static string MCExe(MinecraftSettings m){if(File.Exists(m.radminExe)&&string.Equals(Path.GetFileName(m.radminExe),"RvRvpnGui.exe",StringComparison.OrdinalIgnoreCase))return m.radminExe;return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Radmin VPN","RvRvpnGui.exe"),Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432")??Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Radmin VPN","RvRvpnGui.exe")}.FirstOrDefault(File.Exists);}
  public async void Handle(JObject m){string kind=(string)m["kind"],id=(string)m["requestId"];bool acquired=false;
   try{
    if(kind=="hzScan"){await State(id);return;}
    if(busy)throw new InvalidOperationException("Подожди завершения предыдущей команды Horizon.");busy=true;acquired=true;
    var s=Settings();
    if(kind=="hzChoose"){
     string field=(string)m["field"],path;
     if(field=="launcher"){path=FilePicker(owner(),"Выбери horizon-launcher.exe","Horizon Launcher|horizon-launcher.exe");if(path!=null&&!Exe(path))throw new InvalidOperationException("Выбери настоящий файл horizon-launcher.exe.");}
     else if(field=="home")path=Folder(owner(),"Папка данных Horizon: версии, настройки и установленные файлы");
     else if(field=="minecraft")path=Folder(owner(),"Папка установленной Minecraft Java Edition или Prism");
     else if(field=="game")path=Folder(owner(),"Папка игры Horizon, в которой лежат options.txt и horizon-mods");
     else if(field=="packFolder")path=Folder(owner(),"Папка, куда скачиваются модпаки и моды .hmod");
     else throw new InvalidOperationException("Неизвестная настройка.");
     if(path==null){Done(id,"",true);return;}
     if(field=="packFolder"){minecraftSettings.Update(mc=>mc.packFolder=path);}else{typeof(HorizonSettings).GetField(field).SetValue(s,path);doc.Save(s);settingsRevision++;}await State(id);return;
    }
    if(kind=="hzDownload"){
     Reply(new{kind="hzProgress",requestId=id,message="Поиск официального выпуска…",done=0L,total=0L});
     var exe=await HorizonInstaller.Install(s.home,(done,total)=>Reply(new{kind="hzProgress",requestId=id,message="Загрузка официального Horizon Launcher",done,total}));
     s.launcher=exe;doc.Save(s);settingsRevision++;await State(id);return;
    }
    if(kind=="hzLink"){string key=(string)m["link"],url=key=="horizon"?"https://playhorizon.dev/":key=="downloads"?"https://github.com/horizon-rust/horizon/releases":key=="radmin"?"https://www.radmin-vpn.com/":null;if(url==null)throw new InvalidOperationException("Неизвестная ссылка.");Open(url);Done(id,"Открыл официальный сайт.");return;}
    if(kind=="hzAddMod"){string path,key=(string)m["modId"];if(key!=null){if(!hmods.TryGetValue(key,out path))throw new InvalidOperationException("Мод больше недоступен. Обнови список.");}else path=FilePicker(owner(),"Добавить мод Horizon", "Мод Horizon|*.hmod");if(path==null){Done(id,"",true);return;}await Task.Run(()=>HorizonBridge.AddHmod(path,s.game));Done(id,"Мод скопирован. Включи его в игре: Настройки → Моды. Проверь его разрешения.");return;}
    if(kind=="hzRadmin"){string exe=MCExe(MC());if(exe==null)throw new InvalidOperationException("Radmin VPN не найден. Установи его или выбери файл в настройках обычного Minecraft.");Process.Start(new ProcessStartInfo(exe){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(exe)});Done(id,"Radmin открыт. Подключись к одной сети с другом и проверь соединение.");return;}
    if(!Exe(s.launcher))throw new InvalidOperationException("Сначала скачай Horizon Launcher или выбери его файл в настройках.");
    if(kind=="hzOpen"){var p=new ProcessStartInfo(s.launcher){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(s.launcher)};p.EnvironmentVariables["HORIZON_LAUNCHER_HOME"]=s.home;Process.Start(p);Done(id,"Horizon открыт. При необходимости войди в Microsoft и проверь настройки.");return;}
    if(kind!="hzRun")throw new InvalidOperationException("Неизвестная команда.");
    var action=(string)m["action"];var args=new List<string>();bool json=false;int timeout=180000;string label="Выполняется команда Horizon…";
    switch(action){
     case "versions":args.AddRange(new[]{"versions","--installed"});json=true;timeout=30000;break;
     case "install":args.AddRange(new[]{"install","latest"});timeout=1800000;label="Horizon скачивает и устанавливает версию…";break;
     case "prepare":args.Add("prepare");if(s.minecraft.Length>0)args.AddRange(new[]{"--minecraft",s.minecraft});timeout=1800000;label="Horizon готовит файлы твоей лицензии…";break;
     case "prepareDownload":args.AddRange(new[]{"prepare","--download"});timeout=1800000;label="Horizon загружает и готовит файлы Minecraft…";break;
     case "play":args.Add("play");timeout=30000;label="Запуск игры…";break;
     case "join":args.AddRange(new[]{"play","--join",HorizonBridge.Invite((string)m["value"])});timeout=30000;label="Подключение по приглашению…";break;
     case "server":args.AddRange(new[]{"play","--server",HorizonBridge.Address((string)m["value"])});timeout=30000;label="Подключение к серверу…";break;
     case "modsSearch":args.AddRange(new[]{"mods","search",HorizonBridge.Query((string)m["value"]),"--kind",new[]{"mod","resource","modpack"}.Contains((string)m["filter"])?(string)m["filter"]:"mod"});json=true;break;
     case "modsList":args.AddRange(new[]{"mods","list"});json=true;break;
     case "modsCheck":case "modsInstall":case "modsEnable":case "modsDisable":args.AddRange(new[]{"mods",action.Substring(4).ToLowerInvariant(),HorizonBridge.Id((string)m["value"])});json=action=="modsCheck";timeout=600000;break;
     case "packsList":args.AddRange(new[]{"modpack","list"});json=true;break;
     case "packsPort":case "packsPlay":args.AddRange(new[]{"modpack",action=="packsPort"?"port":"play",HorizonBridge.Id((string)m["value"])});timeout=600000;break;
     case "instances":args.AddRange(new[]{"instance","list"});json=true;break;
     case "packsImport":{string path;MinecraftPack pack;var key=(string)m["packId"];if(key!=null){if(!inbox.TryGetValue(key,out pack))throw new InvalidOperationException("Сборка больше недоступна. Обнови список.");path=pack.path;}else path=FilePicker(owner(),"Импорт сборки в Horizon","Сборки Modrinth и CurseForge|*.mrpack;*.zip");if(path==null){Done(id,"",true);return;}var info=await Task.Run(()=>MinecraftFiles.Pack(path));if(info.format!="Modrinth"&&info.format!="CurseForge")throw new InvalidOperationException("Выбери .mrpack или экспорт CurseForge ZIP.");args.AddRange(new[]{"modpack","import",path});timeout=600000;break;}
     default:throw new InvalidOperationException("Эта команда Horizon не поддерживается.");
    }
    if((action=="modsSearch"||action=="modsCheck"||action=="modsInstall")&&new[]{"horizon","modrinth","curseforge"}.Contains((string)m["source"]))args.AddRange(new[]{"--source",(string)m["source"]});
    if((action=="play"||action=="join"||action=="server")&&(bool?)m["control"]==true)args.AddRange(new[]{"--arg","--control"});
    if(json)args.Add("--json");Reply(new{kind="hzProgress",requestId=id,message=label,done=0L,total=0L});var result=await HorizonBridge.Run(s.launcher,s.home,args,timeout);
    if(result.timedOut)throw new InvalidOperationException(result.error);
    if(result.code!=0){var text=HorizonBridge.CleanText(result.error,1800);throw new InvalidOperationException("Horizon сообщил об ошибке"+(text.Length>0?": "+text:". Проверь версию, подключение и вход Microsoft."));}
    string output=HorizonBridge.Printable(result.output);var msg=action=="play"||action=="join"||action=="server"||action=="packsPlay"?"Команда запуска передана Horizon. Проверь появление игры.":"Команда Horizon завершена. Проверь результат ниже.";
    Reply(new{kind="hzResult",requestId=id,action,message=msg,output});
   }catch(Exception ex){fault(ex);Reply(new{kind="hzError",requestId=id,message=HorizonBridge.CleanText(ex is InvalidOperationException||ex is IOException?ex.Message:"Не удалось выполнить команду. Проверь сеть, права доступа и свободное место.",2000)});}finally{if(acquired)busy=false;}
  }
 }
}
