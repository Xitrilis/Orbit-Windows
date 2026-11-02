using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;
namespace Orbit {
 // Separate, time-limited process: credentials arrive only via stdin; no global keys,
 // clipboard, coordinates, network config edits, or interaction with unrelated processes.
 static class RadminHelper {
  [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
  delegate bool EnumProc(IntPtr h,IntPtr p);
  [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr p);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  static int End(string code){Console.Out.WriteLine(new JObject{{"code",code}}.ToString(Newtonsoft.Json.Formatting.None));return code=="found"?0:3;}
  [STAThread]static int Main(){
   Console.InputEncoding=new System.Text.UTF8Encoding(false);Console.OutputEncoding=new System.Text.UTF8Encoding(false);
   AutomationElement main=null,dialog=null;
   try{
    var text=Console.In.ReadToEnd();if(text.Length>10000)return End("invalid");var j=JObject.Parse(text);text=null;
    string exe=(string)j["exe"],network=(string)j["network"]??"",password=(string)j["password"]??"",action=(string)j["action"];
    j.Remove("password");if(!new[]{"check","join","show"}.Contains(action)||!Path.IsPathRooted(exe)||!File.Exists(exe)||!string.Equals(Path.GetFileName(exe),"RvRvpnGui.exe",StringComparison.OrdinalIgnoreCase))return End("invalid");
    if(action!="show")network=RadminRules.Network(network);if(action=="join"&&password.Length>0)RadminRules.Password(password);
    var ids=FindProcesses(exe);if(ids.Count==0){Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Minimized,WorkingDirectory=Path.GetDirectoryName(exe)});for(int t=0;t<30&&ids.Count==0;t++){Thread.Sleep(300);ids=FindProcesses(exe);}}
    if(ids.Count!=1)return End("ambiguous");int pid=ids[0];
    for(int t=0;t<30&&main==null;t++){main=MainWindow(pid);if(main==null)Thread.Sleep(300);}
    if(main==null)return End("window");
    if(action=="show"){Show(main);return End("opened");}
    if(HasNetwork(main,network)){if(!OtherWindows(pid,main))Hide(main);else return End("dialog");return End("found");}
    if(action=="check"){Hide(main);return End("not_found");}if(password.Length==0){Show(main);return End("not_found");}
    if(OtherWindows(pid,main)){Show(main);return End("dialog");}
    // The exact button label and an inspectable, unique password form are both mandatory.
    var commands=Nodes(main).Where(e=>Type(e)==ControlType.Button&&RadminRules.JoinButton(Name(e))&&Enabled(e)).ToList();
    if(commands.Count!=1||!commands[0].TryGetCurrentPattern(InvokePattern.Pattern,out var invoke)){Show(main);return End("manual");}
    ((InvokePattern)invoke).Invoke();dialog=null;
    for(int t=0;t<20&&dialog==null;t++){dialog=JoinWindow(pid);if(dialog==null)Thread.Sleep(250);}
    if(dialog==null){Show(main);return End("manual");}
    var nodes=Nodes(dialog);var edits=nodes.Where(e=>Type(e)==ControlType.Edit&&Enabled(e)).ToList();
    var secret=edits.Where(IsPassword).ToList();var name=edits.Where(e=>!IsPassword(e)).ToList();
    var submits=nodes.Where(e=>Type(e)==ControlType.Button&&Enabled(e)&&RadminRules.SubmitButton(Name(e))).ToList();
    if(edits.Count!=2||secret.Count!=1||name.Count!=1||submits.Count!=1||!name[0].TryGetCurrentPattern(ValuePattern.Pattern,out var np)||!secret[0].TryGetCurrentPattern(ValuePattern.Pattern,out var sp)||!submits[0].TryGetCurrentPattern(InvokePattern.Pattern,out var ip)){Show(dialog);return End("manual");}
    ((ValuePattern)np).SetValue(network);if(!RadminRules.NetworkItem(((ValuePattern)np).Current.Value,network)){Show(dialog);return End("manual");}
    ((ValuePattern)sp).SetValue(password);password=null;((InvokePattern)ip).Invoke();
    for(int t=0;t<30;t++){Thread.Sleep(300);main=MainWindow(pid);if(main!=null&&HasNetwork(main,network)&&!OtherWindows(pid,main)){Hide(main);return End("found");}}
    var visible=JoinWindow(pid);if(visible!=null)Show(visible);else if(main!=null)Show(main);return End("unconfirmed");
   }catch{try{if(dialog!=null)Show(dialog);else if(main!=null)Show(main);}catch{}return End("manual");}
  }
  static List<int> FindProcesses(string exe){var list=new List<int>();foreach(var p in Process.GetProcessesByName("RvRvpnGui"))using(p)try{if(string.Equals(Path.GetFullPath(p.MainModule.FileName),Path.GetFullPath(exe),StringComparison.OrdinalIgnoreCase))list.Add(p.Id);}catch{}return list;}
  static IEnumerable<AutomationElement> Windows(int pid){var list=new List<AutomationElement>();EnumWindows((h,_)=>{try{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==(uint)pid){var e=AutomationElement.FromHandle(h);if(e!=null&&Type(e)==ControlType.Window)list.Add(e);}}catch{}return true;},IntPtr.Zero);return list;}
  static AutomationElement MainWindow(int pid){var w=Windows(pid).Where(e=>RadminRules.MainWindow(Name(e))).ToList();return w.Count==1?w[0]:null;}
  static AutomationElement JoinWindow(int pid){var w=Windows(pid).Where(e=>RadminRules.JoinDialog(Name(e))).ToList();return w.Count==1?w[0]:null;}
  static bool OtherWindows(int pid,AutomationElement main){int h=Handle(main);return Windows(pid).Any(e=>Handle(e)!=0&&Handle(e)!=h&&!Offscreen(e));}
  static bool HasNetwork(AutomationElement main,string name)=>Nodes(main).Any(e=>Type(e)==ControlType.TreeItem&&RadminRules.NetworkItem(Name(e),name));
  static List<AutomationElement> Nodes(AutomationElement root){var result=new List<AutomationElement>();var q=new Queue<AutomationElement>();q.Enqueue(root);var walk=TreeWalker.ControlViewWalker;while(q.Count>0&&result.Count<3000){var e=q.Dequeue();result.Add(e);try{var x=walk.GetFirstChild(e);while(x!=null&&q.Count<3000){q.Enqueue(x);x=walk.GetNextSibling(x);}}catch{}}return result;}
  static string Name(AutomationElement e){try{return e.Current.Name??"";}catch{return "";}}
  static ControlType Type(AutomationElement e){try{return e.Current.ControlType;}catch{return null;}}
  static bool Enabled(AutomationElement e){try{return e.Current.IsEnabled;}catch{return false;}}
  static bool Offscreen(AutomationElement e){try{return e.Current.IsOffscreen;}catch{return true;}}
  static bool IsPassword(AutomationElement e){try{return e.Current.IsPassword;}catch{return false;}}
  static int Handle(AutomationElement e){try{return e.Current.NativeWindowHandle;}catch{return 0;}}
  static void Show(AutomationElement e){var h=Handle(e);if(h!=0)ShowWindowAsync(new IntPtr(h),9);}
  static void Hide(AutomationElement e){var h=Handle(e);if(h!=0)ShowWindowAsync(new IntPtr(h),0);}
 }
}
