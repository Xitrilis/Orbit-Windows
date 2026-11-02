using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
namespace Orbit {
 internal sealed class StartupWindow:Form {
  public readonly CancellationTokenSource Cancellation=new CancellationTokenSource();
  readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer{Interval=33};readonly DateTime begun=DateTime.UtcNow;
  public string Status="Проверяем компоненты…";public int Progress=-1;public bool Finishing;
  public StartupWindow(string root){
   Text="Orbit";FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;BackColor=Color.FromArgb(16,18,23);DoubleBuffered=true;AutoScaleMode=AutoScaleMode.None;
   float scale;using(var g=Graphics.FromHwnd(IntPtr.Zero))scale=g.DpiX/96f;var area=Screen.FromPoint(Cursor.Position).WorkingArea;
   var width=Math.Max(480,Math.Min(area.Width-32,(int)(1360*scale)));var height=Math.Max(360,Math.Min(area.Height-32,(int)(880*scale)));
   Bounds=new Rectangle(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height);
   try{Icon=new Icon(Path.Combine(root,"orbit.ico"));}catch{}
   var close=new Button{Text="×",AccessibleName="Закрыть загрузку Orbit",FlatStyle=FlatStyle.Flat,ForeColor=Color.FromArgb(165,169,190),BackColor=BackColor,Size=new Size((int)(46*scale),(int)(36*scale)),Anchor=AnchorStyles.Top|AnchorStyles.Right,TabIndex=0};close.FlatAppearance.BorderSize=0;close.Location=new Point(ClientSize.Width-close.Width,0);close.Click+=(s,e)=>Close();Controls.Add(close);
   animation.Tick+=(s,e)=>Invalidate();animation.Start();
   MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};
   FormClosing+=(s,e)=>{if(!Finishing)Cancellation.Cancel();};
  }
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;var scale=g.DpiX/96f;
   g.TranslateTransform(ClientSize.Width/2f-240*scale,ClientSize.Height/2f-60*scale);g.ScaleTransform(scale,scale);
   var accent=Color.FromArgb(181,162,255);using(var pen=new Pen(accent,2.8f))using(var ink=new SolidBrush(accent)){
    var state=g.Save();g.TranslateTransform(40,40);g.RotateTransform(-42);g.DrawEllipse(pen,-35,-16.67f,70,33.34f);g.Restore(state);g.FillEllipse(ink,30,30,20,20);g.FillEllipse(ink,58.3f,13.3f,10,10);
   }
   using(var title=new Font("Segoe UI",26,FontStyle.Bold,GraphicsUnit.Pixel))using(var text=new Font("Segoe UI",14,FontStyle.Regular,GraphicsUnit.Pixel))using(var fg=new SolidBrush(Color.FromArgb(239,239,246)))using(var muted=new SolidBrush(Color.FromArgb(157,160,181))){
    g.DrawString("Загрузка Orbit",title,fg,104,9);g.DrawString(Status,text,muted,new RectangleF(106,49,380,44));
   }
   using(var track=new SolidBrush(Color.FromArgb(37,39,53)))using(var fill=new SolidBrush(accent)){
    g.FillRectangle(track,0,112,480,3);if(Progress>=0)g.FillRectangle(fill,0,112,480*Math.Min(100,Progress)/100f,3);
    else {float phase=(float)((DateTime.UtcNow-begun).TotalSeconds%1.6/1.6);var state=g.Save();g.SetClip(new RectangleF(0,112,480,3));g.FillRectangle(fill,-144+phase*624,112,144,3);g.Restore(state);}
   }
  }
  public StartupTicket Ticket()=>new StartupTicket{Token=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow,X=Bounds.X,Y=Bounds.Y,Width=Bounds.Width,Height=Bounds.Height,ProcessId=System.Diagnostics.Process.GetCurrentProcess().Id,LoaderHandle=Handle.ToInt64()};
  public bool HasForeground=>GetForegroundWindow()==Handle;
  protected override void Dispose(bool disposing){if(disposing){animation.Dispose();Cancellation.Dispose();}base.Dispose(disposing);}
  [DllImport("user32.dll")]static extern bool ReleaseCapture();
  [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
  [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 }
}
