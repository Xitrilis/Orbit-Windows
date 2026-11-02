using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using Forms=System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Orbit {
 public sealed class RadminController {
  readonly string folder,dataDir;readonly Func<Window> owner;readonly LocalDocument<RadminSettings> doc;readonly Action<object> send;readonly Action<Exception> fault;readonly Func<bool> exiting;
  bool busy;string status="Состояние сети ещё не проверено.";bool networkFound;
  public RadminController(string folder,string dataDir,Func<Window> owner,Action<object> send,Action<Exception> fault,Func<bool> exiting){this.folder=folder;this.dataDir=dataDir;this.owner=owner;this.send=send;this.fault=fault;this.exiting=exiting;doc=new LocalDocument<RadminSettings>(Path.Combine(dataDir,"radmin-settings.json"),65536,RadminRules.Validate);}
  void Reply(object v){if(!exiting())send(v);}
  string Exe(){string custom="";try{custom=new LocalDocument<MinecraftSettings>(Path.Combine(dataDir,"minecraft-settings.json"),65536,MinecraftFiles.ValidateSettings).Value.radminExe;}catch{}
   return new[]{doc.Value.exe,custom,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Radmin VPN","RvRvpnGui.exe"),Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432")??Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Radmin VPN","RvRvpnGui.exe")}.FirstOrDefault(p=>!string.IsNullOrEmpty(p)&&File.Exists(p)&&string.Equals(Path.GetFileName(p),"RvRvpnGui.exe",StringComparison.OrdinalIgnoreCase));}
  void State(string id){Reply(new{kind="rvState",requestId=id,network=doc.Value.network,autoStart=doc.Value.autoStart,available=Exe()!=null,exe=Exe()??"",status,networkFound,busy,warning=doc.Warning});}
  public void StartAuto(){if(doc.Value.autoStart&&!string.IsNullOrWhiteSpace(doc.Value.network))Handle(new JObject{{"kind","rvAuto"},{"requestId","auto"}});}
  async Task<string> Run(string action,string name,string password){var exe=Exe();if(exe==null)return "missing";var helper=Path.Combine(folder,"Orbit.RadminHelper.exe");if(!File.Exists(helper))return "helper";
   return await Task.Run(()=>{using(var p=new Process{StartInfo=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=folder}}){
    if(!p.Start())return "helper";var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
    var request=new JObject{{"action",action},{"network",name??""},{"password",password??""},{"exe",exe}}.ToString(Formatting.None);var bytes=Encoding.UTF8.GetBytes(request);p.StandardInput.BaseStream.Write(bytes,0,bytes.Length);p.StandardInput.Close();Array.Clear(bytes,0,bytes.Length);request=null;password=null;
    if(!p.WaitForExit(35000)){try{p.Kill();}catch{}return "timeout";}Task.WaitAll(output,errors);try{var s=output.Result;if(s.Length>2048)return "helper";return (string)JObject.Parse(s)["code"]??"helper";}catch{return "helper";}
   }});
  }
  public async void Handle(JObject m){string kind=(string)m["kind"],id=(string)m["requestId"];bool acquired=false;try{
    if(kind=="rvState"){State(id);return;}if(busy)throw new InvalidOperationException("Подожди завершения предыдущего подключения.");busy=true;acquired=true;
    if(kind=="rvDownload"){Process.Start(new ProcessStartInfo("https://www.radmin-vpn.com/"){UseShellExecute=true});status="Открыл официальный сайт Radmin VPN.";Reply(new{kind="rvResult",requestId=id,code="opened",message=status,networkFound,network=doc.Value.network,autoStart=doc.Value.autoStart});return;}
    if(kind=="rvChoose"){string file;using(var dialog=new Forms.OpenFileDialog{Title="Выбери RvRvpnGui.exe",Filter="Radmin VPN|RvRvpnGui.exe",CheckFileExists=true,RestoreDirectory=true}){if(dialog.ShowDialog(new WindowOwner(owner()))!=Forms.DialogResult.OK){State(id);return;}file=dialog.FileName;}if(!string.Equals(Path.GetFileName(file),"RvRvpnGui.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Выбери файл RvRvpnGui.exe.");var old=doc.Value;doc.Save(new RadminSettings{network=old.network,autoStart=old.autoStart,exe=file});status="Путь к Radmin сохранён.";State(id);return;}
    if(kind=="rvDisableAuto"){var old=doc.Value;doc.Save(new RadminSettings{network=old.network,autoStart=false,exe=old.exe});status="Автопроверка сети отключена.";State(id);return;}
    string action=kind=="rvJoin"?"join":kind=="rvAuto"?"check":kind=="rvShow"?"show":null;if(action==null)throw new InvalidOperationException("Неизвестное действие.");
    string name=kind=="rvJoin"?RadminRules.Network((string)m["network"]):doc.Value.network;
    string pass=(string)m["password"]??"";m.Remove("password");if(pass.Length>0)RadminRules.Password(pass);
    bool wantAuto=kind=="rvJoin"&&(bool?)m["autoStart"]==true;
    status=action=="show"?"Открываем Radmin…":"Проверяем сеть в Radmin…";Reply(new{kind="rvProgress",requestId=id,message=status});
    var code=await Run(action,name,pass);pass=null;
    networkFound=code=="found";
    status=code=="found"?"Сеть найдена в Radmin. Проверьте, что оба компьютера в сети и доступны друг другу.":code=="opened"?"Radmin открыт.":code=="missing"?"Radmin VPN не найден. Установи его или выбери RvRvpnGui.exe в настройках сети.":code=="not_found"?"Эта сеть не найдена. Для первого подключения укажи пароль или открой Radmin.":code=="dialog"?"В Radmin открыто другое окно. Заверши действие вручную и повтори.":code=="unconfirmed"?"Запрос передан Radmin, но Orbit не смог подтвердить сеть. Проверь сообщение в Radmin.":code=="timeout"?"Radmin не ответил вовремя. Открой его и проверь подключение.":"Автоподключение недоступно в этом окне Radmin. Открой Radmin и присоединись вручную, затем повтори проверку.";
    if(kind=="rvJoin"&&code=="found")doc.Save(new RadminSettings{network=name,autoStart=wantAuto,exe=doc.Value.exe});
    Reply(new{kind="rvResult",requestId=id,code,message=status,networkFound,network=code=="found"?name:doc.Value.network,autoStart=doc.Value.autoStart});
   }catch(Exception ex){fault(ex);status=ex is InvalidOperationException?ex.Message:"Не удалось выполнить действие Radmin. Открой его и проверь настройки.";Reply(new{kind="rvError",requestId=id,message=status});}finally{if(acquired)busy=false;}
  }
 }
}
