using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
namespace Orbit {
 public sealed partial class OrbitPlugin {
  StartupTicket startupTicket;DispatcherTimer startupTimer;DateTime? startupRenderedAt;bool startupFinished;
  object StartupState()=>new{token=startupTicket?.Token,waiting=startupTicket!=null&&!startupFinished};
  void PrepareStartup(){
   startupTicket=StartupHandoff.Read(dataDir,DateTime.UtcNow);
   if(startupTicket==null)return;
   window.ShowActivated=false;window.ShowInTaskbar=false;window.SourceInitialized+=(s,e)=>{
    try{
     var hwnd=new WindowInteropHelper(window).Handle;var source=HwndSource.FromHwnd(hwnd);var scale=source.CompositionTarget.TransformFromDevice;
     window.MinWidth=Math.Min(880,startupTicket.Width*scale.M11);window.MinHeight=Math.Min(640,startupTicket.Height*scale.M22);
     window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=startupTicket.X*scale.M11;window.Top=startupTicket.Y*scale.M22;window.Width=startupTicket.Width*scale.M11;window.Height=startupTicket.Height*scale.M22;
     var loader=new IntPtr(unchecked((int)startupTicket.LoaderHandle));uint pid;StartupGetWindowThreadProcessId(loader,out pid);
     // Keep the host behind the loader without making either window always-on-top.
     bool alive=pid==startupTicket.ProcessId;
     StartupSetWindowPos(hwnd,alive?loader:IntPtr.Zero,startupTicket.X,startupTicket.Y,startupTicket.Width,startupTicket.Height,0x0010u|(alive?0u:0x0004u));
    }catch(Exception ex){diagnosticLog?.Record("startup.bounds.failed",ex);}
   };
   startupTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
   startupTimer.Tick+=(s,e)=>Safe(()=>{
    var mode=StartupHandoff.Released(dataDir,startupTicket.Token);
    if(mode=="hide"&&!startupFinished)window.Hide();
    if(!startupRenderedAt.HasValue)return;
    if(mode!=null||DateTime.UtcNow-startupRenderedAt.Value>TimeSpan.FromSeconds(5))FinishStartup(mode??"background");
   });startupTimer.Start();
  }
  void StartupRendered(string token){
   if(startupFinished||startupTicket==null||token!=startupTicket.Token)return;
   if(!startupRenderedAt.HasValue)startupRenderedAt=DateTime.UtcNow;
   StartupHandoff.Ready(dataDir,token);diagnosticLog?.Record("startup.ui.rendered",component:"browser");
  }
  async void FinishStartup(string mode){
   if(startupFinished)return;startupFinished=true;startupTimer?.Stop();
   Send(new{kind="startupComplete",token=startupTicket.Token});
   window.ShowInTaskbar=true;window.ShowActivated=true;if(mode=="activate"&&window.IsVisible)window.Activate();else if(mode=="hide")window.Hide();
   diagnosticLog?.Record("startup.ui.released",component:"browser");
   // Keep cancellation markers alive while the loader is closing its own window.
   await System.Threading.Tasks.Task.Delay(2000);StartupHandoff.Cleanup(dataDir,startupTicket.Token);
  }
  [DllImport("user32.dll",EntryPoint="SetWindowPos")]static extern bool StartupSetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
  [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]static extern uint StartupGetWindowThreadProcessId(IntPtr h,out uint pid);
 }
}
