using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace Orbit {
 public sealed class WinClientWindows:IClientWindows {
  delegate bool EnumProc(IntPtr window,IntPtr data);
  [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr data);
  [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr window);
  [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder text,int length);
  [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
  [DllImport("kernel32.dll")]static extern IntPtr OpenProcess(uint access,bool inherit,uint process);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref uint size);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  static string Image(uint pid){var h=OpenProcess(0x1000,false,pid);if(h==IntPtr.Zero)return null;try{var b=new StringBuilder(32768);uint n=(uint)b.Capacity;return QueryFullProcessImageName(h,0,b,ref n)?b.ToString():null;}finally{CloseHandle(h);}}
  public List<ClientWindow> List(){
   var result=new List<ClientWindow>();var processes=new Dictionary<uint,ClientWindow>();
   EnumWindows((h,_)=>{try{
    if(!IsWindowVisible(h))return true;uint pid;GetWindowThreadProcessId(h,out pid);if(pid==0)return true;
    ClientWindow process;if(!processes.TryGetValue(pid,out process)){
     var path=Image(pid);var store=ClientWindowRules.Store(Path.GetFileName(path));
     if(store!=null)using(var p=Process.GetProcessById((int)pid))process=new ClientWindow{pid=(int)pid,path=path,store=store,startedTicks=p.StartTime.ToUniversalTime().Ticks};
     processes[pid]=process;
    }
    if(process==null)return true;var title=new StringBuilder(512);GetWindowText(h,title,title.Capacity);var cls=new StringBuilder(256);GetClassName(h,cls,cls.Capacity);
    result.Add(new ClientWindow{handle=h.ToInt64(),pid=process.pid,path=process.path,store=process.store,startedTicks=process.startedTicks,title=title.ToString(),windowClass=cls.ToString(),visible=true,owned=GetWindow(h,4)!=IntPtr.Zero,dialog=cls.ToString()=="#32770"||!IsWindowEnabled(h)});
   }catch{}return true;},IntPtr.Zero);return result;
  }
  public bool Same(ClientWindow w){try{var h=new IntPtr(w.handle);if(!IsWindow(h))return false;uint pid;GetWindowThreadProcessId(h,out pid);if(pid!=w.pid)return false;using(var p=Process.GetProcessById(w.pid)){if(p.StartTime.ToUniversalTime().Ticks!=w.startedTicks)return false;}return string.Equals(Image(pid),w.path,StringComparison.OrdinalIgnoreCase);}catch{return false;}}
  public bool Visible(ClientWindow w)=>Same(w)&&IsWindowVisible(new IntPtr(w.handle));
  public bool Hide(ClientWindow w)=>Same(w)&&ShowWindowAsync(new IntPtr(w.handle),0);
  public bool Restore(ClientWindow w)=>Same(w)&&ShowWindowAsync(new IntPtr(w.handle),4); // SW_SHOWNOACTIVATE
 }
}
