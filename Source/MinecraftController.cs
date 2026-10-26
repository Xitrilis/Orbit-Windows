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
 public sealed class MinecraftController {
  readonly LocalDocument<MinecraftSettings> doc;readonly Func<Window> owner;readonly Action<object> send;readonly Action<Exception> fault;readonly Func<bool> exiting;
  readonly Dictionary<string,MinecraftProfile> profiles=new Dictionary<string,MinecraftProfile>();readonly Dictionary<string,MinecraftPack> packs=new Dictionary<string,MinecraftPack>();
  readonly Dictionary<string,DateTime> launches=new Dictionary<string,DateTime>();bool busy;int generation;
  public MinecraftController(string dataDir,Func<Window> owner,Action<object> send,Action<Exception> fault,Func<bool> exiting){doc=new LocalDocument<MinecraftSettings>(Path.Combine(dataDir,"minecraft-settings.json"),65536,MinecraftFiles.ValidateSettings);this.owner=owner;this.send=send;this.fault=fault;this.exiting=exiting;}
  static string Existing(params string[] values)=>values.FirstOrDefault(p=>File.Exists(p)||Directory.Exists(p))??"";
  static string Env(Environment.SpecialFolder f)=>Environment.GetFolderPath(f);
  static string Program64()=>Environment.GetEnvironmentVariable("ProgramW6432")??Env(Environment.SpecialFolder.ProgramFiles);
  MinecraftSettings Settings(){var s=JsonConvert.DeserializeObject<MinecraftSettings>(JsonConvert.SerializeObject(doc.Value));
   if(s.prismRoot.Length==0)s.prismRoot=Path.Combine(Env(Environment.SpecialFolder.ApplicationData),"PrismLauncher");
   if(s.curseRoot.Length==0)s.curseRoot=Existing(Path.Combine(Env(Environment.SpecialFolder.UserProfile),"curseforge","minecraft"),Path.Combine(Env(Environment.SpecialFolder.MyDocuments),"curseforge","minecraft"));
   if(s.prismExe.Length==0)s.prismExe=Existing(Path.Combine(Env(Environment.SpecialFolder.LocalApplicationData),"Programs","PrismLauncher","prismlauncher.exe"),Path.Combine(Program64(),"PrismLauncher","prismlauncher.exe"));
   if(s.radminExe.Length==0)s.radminExe=Existing(Path.Combine(Env(Environment.SpecialFolder.ProgramFilesX86),"Radmin VPN","RvRvpnGui.exe"),Path.Combine(Program64(),"Radmin VPN","RvRvpnGui.exe"));return s;
  }
  static bool Exe(string path,string name)=>!string.IsNullOrEmpty(path)&&Path.GetFileName(path).Equals(name,StringComparison.OrdinalIgnoreCase)&&File.Exists(path);
  async Task State(string requestId){MinecraftSettings s;MinecraftScan scan;int gen;do{gen=generation;s=Settings();scan=await Task.Run(()=>MinecraftFiles.Scan(s));}while(gen!=generation&&!exiting());if(exiting())return;profiles.Clear();packs.Clear();foreach(var p in scan.profiles)profiles[p.id]=p;foreach(var p in scan.packs)packs[p.id]=p;
   send(new{kind="mcState",requestId,settings=s,profiles=scan.profiles,packs=scan.packs,warnings=scan.warnings,warning=doc.Warning,readOnly=doc.ReadOnly,prismAvailable=Exe(s.prismExe,"prismlauncher.exe")&&!string.IsNullOrEmpty(s.prismRoot),radminAvailable=Exe(s.radminExe,"RvRvpnGui.exe")});
  }
  MinecraftProfile Profile(string id){MinecraftProfile p;if(id==null||!profiles.TryGetValue(id,out p))throw new MinecraftException("missing");return p.source=="prism"?MinecraftFiles.PrismProfile(p.path):MinecraftFiles.CurseProfile(p.path);}
  void Prism(string action=null,string value=null){var s=Settings();if(!Exe(s.prismExe,"prismlauncher.exe"))throw new MinecraftException("prism");var args=MinecraftFiles.PrismArguments(s.prismRoot,action,value);Process.Start(new ProcessStartInfo(s.prismExe,args){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(s.prismExe)});}
  void Done(string id,string message=null,bool cancelled=false){if(!exiting())send(new{kind="mcDone",requestId=id,message,cancelled});}
  public async void Handle(JObject m){string kind=(string)m["kind"],requestId=(string)m["requestId"];bool acquired=false;
   try{if(kind=="mcScan"){await State(requestId);return;}if(kind=="mcMods"){var p=Profile((string)m["id"]);var mods=await Task.Run(()=>MinecraftFiles.Mods(p));if(!exiting())send(new{kind="mcMods",requestId,id=p.id,mods});return;}
    if(busy)throw new MinecraftException("busy");busy=true;acquired=true;
    switch(kind){
     case "mcChoose":{
      var field=(string)m["field"];if(!new[]{"prismExe","prismRoot","curseRoot","packFolder","radminExe"}.Contains(field))throw new MinecraftException("settings");string path;
      if(field.EndsWith("Exe")){string name=field=="prismExe"?"prismlauncher.exe":"RvRvpnGui.exe";using(var d=new Forms.OpenFileDialog{Title="Выбери "+name,Filter=name+"|"+name,CheckFileExists=true,RestoreDirectory=true}){if(d.ShowDialog(new WindowOwner(owner()))!=Forms.DialogResult.OK){Done(requestId,null,true);return;}path=d.FileName;}if(!Exe(path,name))throw new MinecraftException("settings");}
      else using(var d=new Forms.FolderBrowserDialog{Description=field=="prismRoot"?"Папка данных Prism с prismlauncher.cfg и instances":"Выбери папку",ShowNewFolderButton=true}){if(d.ShowDialog(new WindowOwner(owner()))!=Forms.DialogResult.OK){Done(requestId,null,true);return;}path=d.SelectedPath;}
      var s=Settings();typeof(MinecraftSettings).GetField(field).SetValue(s,path);if(field=="prismExe"&&File.Exists(Path.Combine(Path.GetDirectoryName(path),"prismlauncher.cfg")))s.prismRoot=Path.GetDirectoryName(path);
      if(field=="prismExe"&&s.prismRoot.Length==0)s.prismRoot=Path.Combine(Env(Environment.SpecialFolder.ApplicationData),"PrismLauncher");doc.Save(s);generation++;await State(requestId);break;
     }
     case "mcPreferences":{var s=Settings();if(m["askRadmin"]?.Type!=JTokenType.Boolean)throw new MinecraftException("settings");s.askRadmin=(bool)m["askRadmin"];doc.Save(s);generation++;await State(requestId);break;}
     case "mcOpenPrism":Prism();Done(requestId,"Prism открыт. Войди в Microsoft и проверь Java в настройках.");break;
     case "mcShow":{var p=Profile((string)m["id"]);if(p.source!="prism")throw new MinecraftException("prism");Prism("show",p.instanceId);Done(requestId);break;}
     case "mcLaunch":{var p=Profile((string)m["id"]);if(!p.launchable||p.source!="prism")throw new MinecraftException("prism");DateTime last;if(launches.TryGetValue(p.id,out last)&&DateTime.UtcNow-last<TimeSpan.FromSeconds(8))throw new MinecraftException("busy");Prism("launch",p.instanceId);launches[p.id]=DateTime.UtcNow;Done(requestId,"Команда запуска передана Prism.");break;}
     case "mcRadmin":{var s=Settings();if(!Exe(s.radminExe,"RvRvpnGui.exe"))throw new MinecraftException("radmin");Process.Start(new ProcessStartInfo(s.radminExe){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(s.radminExe)});Done(requestId,"Radmin открыт. Проверь подключение к общей сети.");break;}
     case "mcImport":{string file;MinecraftPack pack;if((string)m["id"]!=null){if(!packs.TryGetValue((string)m["id"],out pack))throw new MinecraftException("missing");file=pack.path;}else using(var d=new Forms.OpenFileDialog{Title="Импортировать сборку в Prism",Filter="Модпаки|*.zip;*.mrpack",CheckFileExists=true,RestoreDirectory=true}){if(d.ShowDialog(new WindowOwner(owner()))!=Forms.DialogResult.OK){Done(requestId,null,true);return;}file=d.FileName;}await Task.Run(()=>MinecraftFiles.Pack(file));Prism("import",file);Done(requestId,"Подтверди импорт в Prism. После завершения сборка появится здесь.");break;}
     case "mcAddMods":{var p=Profile((string)m["id"]);string[] files;using(var d=new Forms.OpenFileDialog{Title="Добавить моды: "+p.name,Filter="Моды Java|*.jar",Multiselect=true,CheckFileExists=true,RestoreDirectory=true}){if(d.ShowDialog(new WindowOwner(owner()))!=Forms.DialogResult.OK){Done(requestId,null,true);return;}files=d.FileNames;}await Task.Run(()=>MinecraftFiles.AddMods(p,files));Done(requestId,"Моды добавлены. Проверь версии и зависимости перед запуском.");send(new{kind="mcMods",requestId=(string)null,id=p.id,mods=MinecraftFiles.Mods(p)});break;}
     case "mcToggleMod":{var p=Profile((string)m["id"]);string file=(string)m["file"];await Task.Run(()=>MinecraftFiles.ToggleMod(p,file));Done(requestId);send(new{kind="mcMods",requestId=(string)null,id=p.id,mods=MinecraftFiles.Mods(p)});break;}
     case "mcFolder":{var p=Profile((string)m["id"]);var folder=(bool?)m["mods"]==true?MinecraftFiles.ModsRoot(p):p.path;if((bool?)m["mods"]==true)Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});Done(requestId);break;}
     case "mcLink":{var key=(string)m["link"];string url=key=="prism"?"https://prismlauncher.org/download/windows/":key=="curseforge"?"https://www.curseforge.com/minecraft/modpacks":key=="radmin"?"https://www.radmin-vpn.com/":key=="export"?"https://support.curseforge.com/support/solutions/articles/9000197912":null;if(url==null)throw new MinecraftException("invalid");Process.Start(new ProcessStartInfo(url){UseShellExecute=true});Done(requestId);break;}
     default:throw new MinecraftException("invalid");
    }
   }catch(Exception ex){fault(ex);var code=(ex as MinecraftException)?.Code??"io";string message=code=="busy"?"Предыдущая команда ещё выполняется. Подожди несколько секунд.":code=="prism"?"Выбери установленный Prism Launcher и его папку данных в настройках Minecraft.":code=="radmin"?"Установи Radmin VPN и выбери RvRvpnGui.exe в настройках Minecraft.":code=="exists"?"Файл с таким именем уже есть. Orbit не заменяет существующие моды.":code=="loader"?"У этой сборки нет загрузчика модов. Сначала установи Forge, Fabric, NeoForge или Quilt в Prism.":code=="mod"?"Выбери JAR модов Java с манифестом; до 100 файлов и 2 ГБ за раз.":code=="pack"?"Архив не распознан или ещё скачивается. Нужен экспорт CurseForge, Prism или файл .mrpack.":code=="missing"?"Сборка или файл больше не доступны. Обнови список и проверь папки.":"Не удалось выполнить действие. Закрой Minecraft, проверь файлы, права доступа и свободное место.";if(!exiting())send(new{kind="mcError",requestId,code,message});}finally{if(acquired)busy=false;}
  }
 }
}
