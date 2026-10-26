using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace Orbit
{
    public sealed class GameFrame { public byte[] bytes; public string capturedAt; public int width, height; }
    public static class GameCapture
    {
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] struct Point { public int x, y; }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr handle, int flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        public static string Executable(IntPtr window)
        {
            if (window == IntPtr.Zero) return "";
            GetWindowThreadProcessId(window, out var pid);
            var p = OpenProcess(0x1000, false, pid); if (p == IntPtr.Zero) return "";
            try { var b = new StringBuilder(32768); var size = b.Capacity; return QueryFullProcessImageName(p, 0, b, ref size) ? b.ToString() : ""; }
            finally { CloseHandle(p); }
        }
        public static bool Matches(GameContext game, IntPtr window)
        {
            var exe = Executable(window);
            var name = Path.GetFileNameWithoutExtension(exe);
            if (new[] { "steam", "steamwebhelper", "EpicGamesLauncher", "EADesktop", "EALauncher", "GalaxyClient", "UbisoftConnect", "upc", "Battle.net", "explorer" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
            return !string.IsNullOrEmpty(exe) && (GameDiagnostics.IsInside(exe, game.installDirectory) || (!string.IsNullOrEmpty(game.executable) && InstallPaths.Same(exe, game.executable)));
        }
        public static GameFrame Capture(IntPtr window, GameContext game)
        {
            if (window == IntPtr.Zero || window != GetForegroundWindow() || IsIconic(window) || !Matches(game, window))
                throw new InvalidOperationException("Переключись в игру и нажми горячую клавишу. Снимок другого приложения не сделан.");
            if (!GetClientRect(window, out var r)) throw new InvalidOperationException("Игра не предоставила границы окна.");
            var point = new Point(); if (!ClientToScreen(window, ref point)) throw new InvalidOperationException("Не удалось определить область игры.");
            var rect = Rectangle.Intersect(new Rectangle(point.x, point.y, r.right - r.left, r.bottom - r.top), Forms.SystemInformation.VirtualScreen);
            if (rect.Width < 80 || rect.Height < 80 || rect.Width > 16000 || rect.Height > 10000 || (long)rect.Width * rect.Height > 32000000) throw new InvalidOperationException("Область игры недоступна для снимка или слишком велика.");
            using (var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(rect.Location, System.Drawing.Point.Empty, rect.Size, CopyPixelOperation.SourceCopy);
                // Foreground may have changed between checking the process and copying the pixels.
                if (GetForegroundWindow() != window) throw new InvalidOperationException("Активное окно изменилось. Этот кадр отброшен; повтори снимок из игры.");
                var ratio = Math.Min(1.0, 1600.0 / Math.Max(bitmap.Width, bitmap.Height));
                using (var resized = new Bitmap((int)(bitmap.Width * ratio), (int)(bitmap.Height * ratio)))
                {
                    using (var g = Graphics.FromImage(resized)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(bitmap, 0, 0, resized.Width, resized.Height); }
                    int light = 0;
                    for (var y = 0; y < resized.Height; y += Math.Max(1, resized.Height / 30))
                    for (var x = 0; x < resized.Width; x += Math.Max(1, resized.Width / 30)) { var pixel = resized.GetPixel(x, y); if (pixel.R + pixel.G + pixel.B > 18) light++; }
                    if (light < 3) throw new InvalidOperationException("Получился чёрный кадр. Попробуй оконный или безрамочный режим и повтори снимок.");
                    using (var memory = new MemoryStream())
                    using (var parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
                        resized.Save(memory, ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg"), parameters);
                        return new GameFrame { bytes = memory.ToArray(), width = resized.Width, height = resized.Height, capturedAt = DateTime.Now.ToString("HH:mm:ss") };
                    }
                }
            }
        }
    }
}
