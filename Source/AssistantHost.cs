using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using CefSharp;
using CefSharp.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Forms = System.Windows.Forms;

namespace Orbit
{
    public sealed class AssistantHost : IDisposable
    {
        readonly Window window;
        readonly ChromiumWebBrowser browser;
        bool ready, disposed;
        object latest;
        string currentTab = "chat";
        public event Action<JObject> Command;
        public event Action Hidden;
        public bool Visible => window.IsVisible;
        public IntPtr Handle => new WindowInteropHelper(window).Handle;
        public AssistantHost(string folder)
        {
            var file = Path.Combine(folder, "ui", "assistant.html");
            window = new Window { Title = "Orbit · помощник", Width = 500, Height = 820, MinWidth = 400, MinHeight = 580, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResize, Topmost = true, ShowInTaskbar = false, Background = new SolidColorBrush(Color.FromRgb(15, 19, 24)), WindowStartupLocation = WindowStartupLocation.Manual };
            WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(16) });
            browser = new ChromiumWebBrowser { Address = new Uri(file).AbsoluteUri, RequestHandler = new LocalUiRequestHandler(file) };
            browser.JavascriptMessageReceived += (s, e) =>
            {
                var text = e.Message as string; if (text == null || text.Length > 48000) return;
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (disposed || !string.Equals(new Uri(browser.Address).LocalPath, file, StringComparison.OrdinalIgnoreCase)) return;
                        var message = JObject.Parse(text); var kind = (string)message["kind"];
                        if (kind == "ready") { ready = true; Send(latest); Send(new { kind = "tab", tab = currentTab }); }
                        else if (kind == "hide") Hide();
                        else if (kind == "drag") { if (Mouse.LeftButton == MouseButtonState.Pressed) try { window.DragMove(); } catch { } }
                        else Command?.Invoke(message);
                    }
                    catch { Send(new { kind = "notice", message = "Не удалось обработать действие. Закрой и снова открой помощника." }); }
                }));
            };
            browser.FrameLoadEnd += (s, e) => { if (e.Frame.IsMain) window.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) { ready = true; Send(latest); Send(new { kind = "tab", tab = currentTab }); } })); };
            window.Content = browser;
            window.Closing += (s, e) => { if (!disposed) { e.Cancel = true; Hide(); } };
            window.SourceInitialized += (s, e) => { try { int rounded = 2; DwmSetWindowAttribute(Handle, 33, ref rounded, 4); } catch { } };
        }
        public void Show(IntPtr gameWindow)
        {
            if (disposed) return;
            var screen = gameWindow != IntPtr.Zero ? Forms.Screen.FromHandle(gameWindow) : Forms.Screen.PrimaryScreen;
            window.Height = Math.Min(820, SystemParameters.WorkArea.Height - 32);
            window.Show();
            // Native positioning uses device pixels, including negative coordinates on a left monitor.
            GetWindowRect(Handle, out var size);
            var area = screen.WorkingArea;
            var width = Math.Min(size.right - size.left, area.Width - 40);
            var height = Math.Min(size.bottom - size.top, area.Height - 40);
            SetWindowPos(Handle, new IntPtr(-1), area.Right - width - 20, area.Top + 20, width, height, 0x0040);
            window.Activate(); browser.Focus();
        }
        public void Hide() { if (disposed) return; window.Hide(); Hidden?.Invoke(); }
        public void Send(object value)
        {
            if (value == null || disposed) return;
            var obj = value as JObject ?? JObject.FromObject(value);
            var kind = (string)obj["kind"];
            if (kind == "state") latest = value;
            if (kind == "tab") currentTab = (string)obj["tab"];
            if (ready && !browser.IsDisposed) browser.ExecuteScriptAsync("window.OrbitAssistant&&window.OrbitAssistant.receive(" + JsonConvert.SerializeObject(value) + ");");
        }
        public void Dispose() { if (disposed) return; disposed = true; latest = null; browser.Dispose(); window.Close(); }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int left, top, right, bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    }
}
