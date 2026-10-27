using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CefSharp;
using CefSharp.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Forms = System.Windows.Forms;

namespace Orbit
{
    [LoadPlugin]
    public sealed partial class OrbitPlugin : GenericPlugin
    {
        public override Guid Id { get; } = new Guid("3e643d36-877c-4906-9b27-b193df91fc37");
        readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        readonly string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string dataDir;
        Window window;
        ChromiumWebBrowser browser;
        AssistantController assistant;
        PriceController prices;
        HomeController home;
        PlayHistory playHistory;
        BackgroundClients backgroundClients;
        WalletController wallet;
        ArtworkStore artworkStore;
        ArtworkController artwork;
        Forms.NotifyIcon tray;
        DispatcherTimer timer;
        JObject prefs = new JObject();
        readonly List<InstallJob> jobs = new List<InstallJob>();
        readonly Dictionary<string, Process> helpers = new Dictionary<string, Process>();
        bool ready, exiting, modalOpen;
        MinecraftController minecraft;
        string lastSnapshot = "";
        bool pollingDownloads;
        readonly Dictionary<string, DownloadTracker> downloadTrackers = new Dictionary<string, DownloadTracker>();
        readonly Dictionary<string, DownloadMetrics> downloadMetrics = new Dictionary<string, DownloadMetrics>();
        readonly HashSet<string> stores = new HashSet<string> { "steam", "epic", "ea", "battlenet", "ubisoft", "gog" };
        public OrbitPlugin(IPlayniteAPI api) : base(api) { Properties = new GenericPluginProperties { HasSettings = false }; }
        void Ui(Action a) { if (!exiting) PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() => Safe(a))); }
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Desktop) return;
            if (!PlayniteApi.MainView.UIDispatcher.CheckAccess()) { PlayniteApi.MainView.UIDispatcher.Invoke(new Action(() => OnApplicationStarted(args))); return; }
            diagnosticLog = new DiagnosticsLog(Path.Combine(root,"OrbitData","Logs"),"app");
            AppDomain.CurrentDomain.UnhandledException += Unhandled;
            Application.Current.DispatcherUnhandledException += DispatcherFailed;
            TaskScheduler.UnobservedTaskException += TaskFailed;
            try
            {
                dataDir = Path.Combine(root, "OrbitData"); Directory.CreateDirectory(dataDir);
                var p = Path.Combine(dataDir, "preferences.json");
                if (File.Exists(p)) { try { prefs = JObject.Parse(File.ReadAllText(p)); } catch { File.Copy(p, p + ".invalid-" + DateTime.UtcNow.Ticks); } }
                var jf = Path.Combine(dataDir, "downloads.json");
                if (File.Exists(jf)) { try { jobs.AddRange(JsonConvert.DeserializeObject<List<InstallJob>>(File.ReadAllText(jf)) ?? new List<InstallJob>()); } catch { } }
                foreach (var j in jobs.Where(j => j.state == "preparing")) { j.state = "attention"; j.message = "Автодействия прерваны перезапуском. Проверь клиент."; }
                assistant = new AssistantController(folder, dataDir, AssistantGames, OpenClient, Send);
                prices = new PriceController(dataDir, id => { var g = GetGame(id); return new PriceProduct { id = g.GameId, name = g.Name, store = StoreOf(g), image = Art(g.CoverImage) }; }, message => Ui(() => Send(message)));
                wallet = new WalletController(dataDir, message => Ui(() => Send(message)));
                artworkStore = new ArtworkStore(Path.Combine(dataDir, "Artwork"));
                artwork = new ArtworkController(artworkStore, GetGame, Art, () => window, Send, () => Snapshot(true));
                home = new HomeController(dataDir, HomeLibrary, () => assistant.RecommendationReady, assistant.Recommend, prices.RegisterProduct, message => Ui(() => Send(message)));
                playHistory = new PlayHistory(dataDir);
                gameJournal = new GameJournal(Path.Combine(dataDir,"game-journal.json"));
                minecraft = new MinecraftController(dataDir, () => window, Send, ex => diagnosticLog?.Record("minecraft.failed",ex,"minecraft"), () => exiting);
                backgroundClients = new BackgroundClients(dataDir, new WinClientWindows(), () => (bool?)prefs["backgroundClients"] != false, () => modalOpen || jobs.Any(j => j.state == "preparing"), () => Snapshot(true));
                CreateWindow(); CreateTray();
                Application.Current.MainWindow?.Hide();
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (s, e) => Safe(() => { var showFile=Path.Combine(dataDir,"show.request"); if(File.Exists(showFile)){try{File.Delete(showFile);}catch{} Show();} PollJobs(); if (window.IsVisible && !modalOpen) Snapshot(); });
                timer.Start(); Show();diagnosticLog.Record("app.started");
                PlayniteApi.Database.Games.ItemUpdated += (s, e) => Ui(() => Snapshot());
                PlayniteApi.Database.Games.ItemCollectionChanged += (s, e) => Ui(() => Snapshot());
            }
            catch (Exception e) { diagnosticLog.Record("app.start.failed",e);try{Application.Current.MainWindow?.Show();}catch{}PlayniteApi.Dialogs.ShowErrorMessage("Не удалось открыть Orbit: " + e.Message + "\nДля отчёта запусти Orbit-Diagnostics.cmd из папки Orbit.", "Orbit"); }
        }
        void CreateWindow()
        {
            window = new Window { Title = "Orbit", Width = 1360, Height = 880, MinWidth = 880, MinHeight = 640,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.CanResize, Background = new SolidColorBrush(Color.FromRgb(16,18,23)), ShowInTaskbar = true };
            WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(12) });
            try { window.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.Combine(folder, "orbit.ico"))); } catch { }
            var uiFile = Path.Combine(folder, "ui", "index.html");
            browser = new ChromiumWebBrowser { Address = new Uri(uiFile).AbsoluteUri, BrowserSettings = new BrowserSettings { WindowlessFrameRate = 60 } };
            browser.RequestHandler = new LocalUiRequestHandler(uiFile);
            browser.JavascriptMessageReceived += (s, e) =>
            {
                var text = e.Message as string;
                if (text == null || text.Length > 256000) return;
                Ui(() => { try { if (new Uri(browser.Address).LocalPath == uiFile) Handle(JObject.Parse(text)); } catch (Exception ex) { var code=diagnosticLog?.Record("ui.command.failed",ex);Send(new{kind="error",message=ex.Message,diagnosticCode=code}); } });
            };
            browser.FrameLoadEnd += (s, e) => { if (e.Frame.IsMain) Ui(() => { ready = true;diagnosticLog?.Record("browser.ready",component:"browser"); Snapshot(true); }); };
            browser.LoadError += (s,e) => { if(e.Frame.IsMain && (int)e.ErrorCode != -3) diagnosticLog?.Record("browser.load.failed",component:"browser",number:(int)e.ErrorCode); };
            // Keep HTML scrollbar hit targets inside the native six-pixel resize frame.
            browser.Margin = new Thickness(6);
            window.Content = browser;
            window.Closing += (s, e) => { if (!exiting) { e.Cancel = true; window.Hide(); } };
            window.SourceInitialized += (s, e) => { try { var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle; int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, 4); assistant?.Register(hwnd); } catch { } };
        }
        void CreateTray()
        {
            tray = new Forms.NotifyIcon { Text = "Orbit", Visible = true };
            try { tray.Icon = new System.Drawing.Icon(Path.Combine(folder, "orbit.ico")); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Открыть Orbit", null, (s,e) => Ui(Show));
            menu.Items.Add("ИИ-помощник", null, (s,e) => Ui(() => assistant?.Open()));
            menu.Items.Add("Обновить библиотеку", null, (s,e) => Ui(Refresh));
            menu.Items.Add("Показать окна клиентов", null, (s,e) => Ui(() => backgroundClients?.RestoreAll()));
            menu.Items.Add("Сохранить отчёт об ошибке", null, (s,e) => Ui(() => ExportSupport()));
            menu.Items.Add("Открыть Playnite", null, (s,e) => Ui(ShowEngine));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Выйти", null, (s,e) => Ui(() => Application.Current.Shutdown()));
            tray.ContextMenuStrip = menu; tray.MouseClick += (s,e) => { if (e.Button == Forms.MouseButtons.Left) Ui(Show); };
        }
        void Show() { window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); Snapshot(true); }
        void ShowEngine() { backgroundClients?.Cancel();var w = Application.Current.MainWindow; if (w == null) return; w.Show(); w.WindowState = WindowState.Normal; w.Activate(); }
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args) { yield return new MainMenuItem { Description = "Открыть Orbit", Action = _ => Ui(Show) }; }
        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) { Ui(() => { diagnosticLog?.Record("library.updated");Snapshot(true); Send(new { kind = "syncDone" }); backgroundClients?.Schedule(); }); }
        public override void OnGameStarted(OnGameStartedEventArgs args) { Ui(() => { diagnosticLog?.Record("game.started");var g=args.Game;playHistory?.Started(g.Id.ToString(),g.Name,StoreOf(g),DateTime.UtcNow);Snapshot(true);backgroundClients?.Schedule(); }); }
        public override void OnGameStopped(OnGameStoppedEventArgs args) { Ui(() => { diagnosticLog?.Record("game.stopped");playHistory?.Stopped(args.Game.Id.ToString(),DateTime.UtcNow,args.ElapsedSeconds,args.ManuallyStopped);Snapshot(true); }); }
        public override void OnGameStartupCancelled(OnGameStartupCancelledEventArgs args) { Ui(() => { diagnosticLog?.Record("game.cancelled");backgroundClients?.Cancel();Snapshot(true); }); }
        public override void OnGameInstalled(OnGameInstalledEventArgs args) { Ui(() => { PollJobs(); Snapshot(true); }); }
        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            exiting = true; timer?.Stop();diagnosticLog?.Record("app.stopped");
            AppDomain.CurrentDomain.UnhandledException -= Unhandled;Application.Current.DispatcherUnhandledException -= DispatcherFailed;TaskScheduler.UnobservedTaskException -= TaskFailed;
            foreach (var p in helpers.Values.ToArray()) { try { if (!p.HasExited) p.Kill(); } catch { } }
            try { SavePrefs(); SaveJobs(); } catch { }
            playHistory?.Shutdown(); backgroundClients?.Dispose(); home?.Dispose(); wallet?.Dispose(); artwork?.Dispose(); prices?.Dispose(); assistant?.Dispose(); tray?.Dispose(); browser?.Dispose(); window?.Close();
        }
        static string StoreName(string name)
        {
            name = (name ?? "").ToLowerInvariant();
            if (name.Contains("steam")) return "steam";
            if (name.Contains("epic")) return "epic";
            if (name.Contains("gog")) return "gog";
            if (name.Contains("ubisoft") || name.Contains("uplay")) return "ubisoft";
            if (name.Contains("battle")) return "battlenet";
            if (name == "ea" || name.Contains("ea app") || name.Contains("ea library") || name.Contains("origin")) return "ea";
            return null;
        }
        string StoreOf(Game game) => StoreName(PlayniteApi.Addons.Plugins.OfType<LibraryPlugin>().FirstOrDefault(p => p.Id == game.PluginId)?.Name) ?? StoreName(game.Source?.Name);
        LibraryPlugin Plugin(string store) => PlayniteApi.Addons.Plugins.OfType<LibraryPlugin>().FirstOrDefault(p => StoreName(p.Name) == store);
        List<GameContext> AssistantGames()
        {
            return PlayniteApi.Database.Games.Where(g => StoreOf(g) != null).Select(g =>
            {
                string directory = "", executable = "";
                try
                {
                    if (!string.IsNullOrWhiteSpace(g.InstallDirectory)) directory = PlayniteApi.ExpandGameVariables(g, g.InstallDirectory);
                    var action = g.GameActions?.FirstOrDefault(a => a.IsPlayAction && a.Type == GameActionType.File && !string.IsNullOrWhiteSpace(a.Path));
                    if (action != null)
                    {
                        var path = PlayniteApi.ExpandGameVariables(g, action.Path);
                        if (!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(directory)) path = Path.Combine(directory, path);
                        if (Path.IsPathRooted(path) && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) executable = path;
                    }
                }
                catch { }
                return new GameContext { id = g.Id.ToString(), name = g.Name, store = StoreOf(g), appId = g.GameId, installDirectory = directory, executable = executable, installed = g.IsInstalled, running = g.IsRunning };
            }).OrderBy(g => g.name).ToList();
        }
        string Art(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            if (Uri.TryCreate(path, UriKind.Absolute, out var u) && u.Scheme == "https") return u.AbsoluteUri;
            try { var full = PlayniteApi.Database.GetFullFilePath(path); return File.Exists(full) ? new Uri(full).AbsoluteUri : ""; } catch { return ""; }
        }
        List<HomeGame> HomeLibrary() => PlayniteApi.Database.Games.Where(g => StoreOf(g) != null).Select(g => {
            var art = artwork?.View(g);
            return new HomeGame { id = g.Id.ToString(), libraryId = g.Id.ToString(), name = g.Name, appId = g.GameId, store = StoreOf(g), owned = true, installed = g.IsInstalled, favorite = g.Favorite,
                image = art?["background"].url ?? Art(g.BackgroundImage), cover = art?["cover"].url ?? Art(g.CoverImage),
                description = RecommendationRules.Clean(System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(g.Description ?? "", "<[^>]*>", " ")),2200),
                genres = g.Genres?.Select(x => x.Name).ToArray() ?? new string[0], features = g.Features?.Select(x => x.Name).ToArray() ?? new string[0] };
        }).ToList();
        void Snapshot(bool force = false)
        {
            if (!ready || exiting) return;
            var gs = PlayniteApi.Database.Games.Where(g => StoreOf(g) != null).Select(g => {
                var visual = artwork?.View(g);
                return new {
                    id = g.Id.ToString(), appId = g.GameId, name = g.Name, store = StoreOf(g), installed = g.IsInstalled, installing = g.IsInstalling,
                    launching = g.IsLaunching, running = g.IsRunning, favorite = g.Favorite, playtime = g.Playtime,
                    lastPlayed = g.LastActivity, icon = visual?["icon"].url ?? Art(g.Icon), cover = visual?["cover"].url ?? Art(g.CoverImage),
                    background = visual?["background"].url ?? Art(g.BackgroundImage), artwork = visual,
                    description = RecommendationRules.Clean(System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(g.Description ?? "", "<[^>]*>", " ")),2200),
                    features = g.Features?.Select(x => x.Name).ToArray() ?? new string[0],
                    genres = g.Genres?.Select(x => x.Name).ToArray() ?? new string[0]
                };
            }).ToArray();
            if(playHistory?.Warning!=null)diagnosticLog?.Record("history.warning",component:"history");if(backgroundClients?.Warning!=null)diagnosticLog?.Record("clients.warning",component:"clients");
            var snapshot = new { kind = "snapshot", data = new { games = gs, prefs, stores = stores.Select(s => new { id = s, available = Plugin(s) != null }), downloads = DownloadRows(), journal = gameJournal?.Snapshot(), activity = playHistory?.Snapshot() ?? new PlaySession[0], activityWarning = playHistory?.Warning, backgroundClients = new { enabled = (bool?)prefs["backgroundClients"] != false, hiddenStores = backgroundClients?.HiddenStores ?? new string[0], warning = backgroundClients?.Warning }, notifications = PlayniteApi.Notifications.Messages.Where(n => n.Type == NotificationType.Error).Select(n => n.Text).Take(6).ToArray() } };
            var key = JsonConvert.SerializeObject(new { snapshot.data.games, snapshot.data.prefs, snapshot.data.stores, snapshot.data.notifications, snapshot.data.journal, snapshot.data.activity, snapshot.data.activityWarning, snapshot.data.backgroundClients });
            var json = JsonConvert.SerializeObject(snapshot);
            if (!force && lastSnapshot == key) return;
            lastSnapshot = key; ExecuteJson(json);
        }
        void ExecuteJson(string json) { if (ready && browser != null && !browser.IsDisposed) browser.ExecuteScriptAsync("window.Orbit&&window.Orbit.receive(" + json + ");"); }
        void Send(object message) { var payload=JObject.FromObject(message);LogResponse(payload);ExecuteJson(payload.ToString(Formatting.None)); }
        void Error(string message) => Send(new { kind = "error", message });
        static void AtomicWrite(string file, string text) { var tmp = file + ".tmp"; File.WriteAllText(tmp,text); if (File.Exists(file)) File.Replace(tmp,file,file+".bak"); else File.Move(tmp,file); }
        void SavePrefs() { if (dataDir != null) AtomicWrite(Path.Combine(dataDir,"preferences.json"),prefs.ToString()); }
        void SaveJobs() { if (dataDir != null) AtomicWrite(Path.Combine(dataDir,"downloads.json"),JsonConvert.SerializeObject(jobs)); }
        Game GetGame(string id)
        {
            if (!Guid.TryParse(id,out var g)) throw new InvalidOperationException("Неверный идентификатор игры.");
            var game = PlayniteApi.Database.Games.Get(g);
            if (game == null || StoreOf(game) == null) throw new InvalidOperationException("Игра больше не найдена в библиотеке.");
            return game;
        }
        void Handle(JObject m)
        {
            var kind = (string)m["kind"];
            if(kind!=null&&kind.StartsWith("journal",StringComparison.Ordinal)){JournalCommand(m);return;}
            if(kind!=null&&kind.StartsWith("mc",StringComparison.Ordinal)){minecraft?.Handle(m);return;}
            if(kind=="uiFault") { var type=(string)m["type"];diagnosticLog?.Record(type=="promise"?"ui.promise.failed":type=="resource"?"ui.resource.failed":"ui.error",component:(string)m["component"],number:Math.Max(0,Math.Min(1000000,(int?)m["line"]??0)));return; }
            if(kind=="supportStatus"){SupportStatus((string)m["requestId"]);return;}
            if(kind=="supportExport"){ExportSupport((string)m["requestId"]);return;}
            if(kind=="supportOpenLogs"){OpenLogs();return;}
            if (kind != null && kind.StartsWith("home", StringComparison.Ordinal)) { home?.Handle(m); return; }
            if (kind != null && kind.StartsWith("wallet", StringComparison.Ordinal)) { wallet?.Handle(m); return; }
            if (kind != null && kind.StartsWith("prices", StringComparison.Ordinal)) { prices?.Handle(m); return; }
            if (kind != null && kind.StartsWith("artwork", StringComparison.Ordinal)) { artwork?.Handle(m); return; }
            switch(kind)
            {
                case "ready": ready=true; Snapshot(true); break;
                case "minimize": window.WindowState=WindowState.Minimized; break;
                case "maximize": window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized; break;
                case "close": window.Hide(); break;
                case "drag": if (Mouse.LeftButton==MouseButtonState.Pressed) try { window.DragMove(); } catch { } break;
                case "prefs":
                    var patch=m["patch"] as JObject;
                    if(patch!=null) { foreach(var p in patch.Properties().Where(p=>new[]{"preferred","groups","separate","view","reducedMotion","backgroundClients"}.Contains(p.Name))) { if(p.Name=="backgroundClients" && p.Value.Type!=JTokenType.Boolean)throw new InvalidOperationException("Неверное значение режима клиентов.");prefs[p.Name]=p.Value.DeepClone(); }SavePrefs();if ((bool?)prefs["backgroundClients"] == false) backgroundClients?.RestoreAll();Snapshot(true); }
                    break;
                case "showClients": backgroundClients?.RestoreAll();diagnosticLog?.Record("clients.restored",component:"clients");Send(new{kind="toast",message="Скрытые окна клиентов возвращены. Автоскрытие приостановлено на 10 минут."});break;
                case "hideClients": var hiddenCount=backgroundClients?.HideNow()??0;diagnosticLog?.Record("clients.hidden",component:"clients",number:hiddenCount);Send(new{kind="toast",message=hiddenCount>0?"Убираем в фон окон: "+hiddenCount+". Клиенты продолжают работать.":"Сейчас нет окон, которые можно убрать в фон. Во время подготовки установки скрытие приостановлено."});break;
                case "favorite":
                    foreach(var id in ((JArray)m["ids"]).Values<string>()) { var game=GetGame(id); game.Favorite=(bool)m["value"];PlayniteApi.Database.Games.Update(game); } Snapshot(true);break;
                case "play":
                    var g=GetGame((string)m["id"]);if(!g.IsInstalled)throw new InvalidOperationException("Выбранная копия ещё не установлена.");if(g.IsRunning||g.IsLaunching){diagnosticLog?.Record("game.duplicate.ignored");break;}diagnosticLog?.Record("game.requested");PlayniteApi.StartGame(g.Id);break;
                case "friendsOpen":
                    var steamUrl=(string)m["url"];
                    if(!ValidFriendsUrl(steamUrl))throw new InvalidOperationException("Неверная ссылка Steam.");
                    try { backgroundClients?.Allow("steam");Process.Start(new ProcessStartInfo(steamUrl){UseShellExecute=true});Send(new{kind="friendsResult",message="Запрос передан Steam. Подключение и доступ к игре проверяет Steam."}); }
                    catch { Send(new{kind="friendsResult",message="Не удалось открыть Steam. Проверь, установлен ли клиент."}); }
                    break;
                case "connect": Connect((string)m["store"]);break;
                case "client": OpenClient((string)m["store"]);break;
                case "advanced": ShowEngine(); break;
                case "assistant": assistant?.Open((string)m["id"]); break;
                case "assistantSettings": assistant?.Open(settings:true); break;
                case "diagnostics": assistant?.Open(GetGame((string)m["id"]).Id.ToString(), diagnostics:true); break;
                case "sync": Refresh();break;
                case "installOptions": InstallOptions(GetGame((string)m["id"]));break;
                case "browseFolder":
                    using(var d=new Forms.FolderBrowserDialog { Description="Выбери папку для установки игры",SelectedPath=(string)m["path"]??"",ShowNewFolderButton=true })
                    { if(d.ShowDialog(new WindowOwner(window))==Forms.DialogResult.OK)Send(new {kind="folder",path=d.SelectedPath,free=InstallPaths.Free(d.SelectedPath)}); } break;
                case "install": StartInstall(GetGame((string)m["id"]),(string)m["path"]);break;
                case "cancelInstall": CancelInstall((string)m["id"]);break;
                case "dismissJob": jobs.RemoveAll(j=>j.id==(string)m["id"]&&j.state=="complete");SaveJobs();Snapshot(true);break;
            }
        }
        internal static bool ValidFriendsUrl(string url)
        {
            if(url=="steam://open/friends")return true;
            if(url==null||url.Length>200)return false;
            if(System.Text.RegularExpressions.Regex.IsMatch(url,@"\Asteam://joinlobby/[1-9]\d{0,9}/[1-9]\d{0,19}/[1-9]\d{0,19}\z"))return true;
            var match=System.Text.RegularExpressions.Regex.Match(url,@"\Asteam://connect/(\d{1,3}(?:\.\d{1,3}){3}):(\d{1,5})\z");
            int port;
            return match.Success&&match.Groups[1].Value.Split('.').All(x=>int.Parse(x)<=255)&&int.TryParse(match.Groups[2].Value,out port)&&port>=1&&port<=65535;
        }
        void Connect(string store)
        {
            var plugin=Plugin(store);if(plugin==null)throw new InvalidOperationException("Интеграция не загружена. Открой Playnite → Дополнения и проверь модуль магазина.");
            backgroundClients?.Allow(store);modalOpen=true; window.Hide(); ShowEngine();
            try { PlayniteApi.MainView.OpenPluginSettings(plugin.Id); }
            finally { modalOpen=false;Application.Current.MainWindow?.Hide();Show(); }
        }
        void OpenClient(string store) { backgroundClients?.Allow(store);var p=Plugin(store);if(p?.Client==null)throw new InvalidOperationException("Клиент магазина не найден.");p.Client.Open(); }
        void Refresh()
        {
            // Playnite 10.63 has no public SDK import command. Use the pinned engine's checked ICommand.
            var vm=Application.Current.MainWindow?.DataContext;
            var prop=vm?.GetType().GetProperty("UpdateGamesCommand");
            var command=prop?.GetValue(vm) as ICommand;
            if(command==null) { Error("Не удалось вызвать обновление. В Playnite нажми F5.");Send(new{kind="syncDone",message="Открой настройки основы и нажми F5."});return; }
            if(command.CanExecute(null))command.Execute(null);else Send(new {kind="syncDone",message="Обновление уже выполняется."});
        }
        void InstallOptions(Game g)
        {
            var steam=StoreOf(g)=="steam";
            var libs=steam?InstallPaths.SteamLibraries():new List<string>();
            var drives=DriveInfo.GetDrives().Where(d=>d.IsReady&&(d.DriveType==DriveType.Fixed||d.DriveType==DriveType.Removable)).Select(d=>new {path=d.RootDirectory.FullName,free=d.AvailableFreeSpace}).ToArray();
            Send(new{kind="installOptions",id=g.Id.ToString(),libraries=libs.Select(p=>new{path=p,free=InstallPaths.Free(p)}),drives,suggested=steam?libs.FirstOrDefault():Path.Combine(drives.FirstOrDefault()?.path??@"C:\", "Games", InstallPaths.SafeName(g.Name))});
        }
        async void StartInstall(Game game,string path)
        {
            try
            {
                if(jobs.Any(j=>j.state=="preparing"))throw new InvalidOperationException("Дождись настройки предыдущей установки.");
                if(game.IsInstalled||game.IsInstalling)throw new InvalidOperationException("Эта копия уже установлена или устанавливается.");
                path=InstallPaths.Validate(path);var store=StoreOf(game);
                if(store=="steam"&&!InstallPaths.SteamLibraries().Any(p=>InstallPaths.Same(p,path)))throw new InvalidOperationException("Сначала добавь эту папку в Steam → Настройки → Хранилище, затем снова выбери её в Orbit. Путь Steam не будет подменён молча.");
                var helper=Path.Combine(folder,"Orbit.InstallHelper.exe");if(!File.Exists(helper))throw new FileNotFoundException("Не найден помощник установки.");
                var job=new InstallJob{id=Guid.NewGuid().ToString(),gameId=game.Id.ToString(),appId=game.GameId,name=game.Name,store=store,path=path,state="preparing",message="Ожидаем окно установки и проверяем выбранную папку…",createdAt=DateTime.UtcNow};
                jobs.Insert(0,job);SaveJobs();Snapshot(true);Send(new{kind="installStarted"});
                var request=Path.Combine(dataDir,"install-"+job.id+".json");File.WriteAllText(request,JsonConvert.SerializeObject(job));
                var p=new Process{StartInfo=new ProcessStartInfo(helper,"\""+request+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8,WorkingDirectory=folder}};
                p.Start();helpers[job.id]=p;
                PlayniteApi.InstallGame(game.Id);
                var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
                var exited=Task.Run(()=>p.WaitForExit(55000));
                if(!await exited) { try{p.Kill();}catch{} job.state="attention";job.message="Клиент не ответил. Автодействия остановлены; выбранный путь не подтверждён."; }
                else if(job.state=="preparing")
                {
                    var text=await output;await errors;
                    JObject result=null;try{result=JObject.Parse(text);}catch{}
                    job.state=p.ExitCode==0?"handed":"attention";
                    job.message=(string)result?["message"]??"Автовыбор папки не подтверждён. Открой клиент и проверь установку.";
                }
                helpers.Remove(job.id);p.Dispose();try{File.Delete(request);}catch{} SaveJobs();Snapshot(true);Show();
            }
            catch(Exception e){ diagnosticLog?.Record("install.failed",e);foreach(var j in jobs.Where(j=>j.gameId==game.Id.ToString()&&j.state=="preparing")){CancelInstall(j.id);j.message="Ошибка подготовки: "+e.Message;}Error(e.Message);SaveJobs();Snapshot(true); }
        }
        void CancelInstall(string id)
        {
            if(helpers.TryGetValue(id,out var p)){try{if(!p.HasExited)p.Kill();}catch{}}
            var j=jobs.FirstOrDefault(x=>x.id==id);if(j!=null&&j.state=="preparing"){j.state="attention";j.message="Автодействия остановлены. Если загрузка уже началась, её состояние проверь в клиенте.";} SaveJobs();Snapshot(true);
        }
        object[] DownloadRows()
        {
            return jobs.Take(100).Select(j => {
                DownloadMetrics metric; downloadMetrics.TryGetValue(j.id, out metric);
                Game game = null; Guid id; if (Guid.TryParse(j.gameId, out id)) game = PlayniteApi.Database.Games.Get(id);
                return (object)new { j.id, j.gameId, j.name, j.store, j.path, j.state, j.message, j.createdAt,
                    percent = metric?.percent ?? (j.state == "complete" ? 100 : (double?)null), metrics = metric,
                    coverCrop = game != null ? artwork?.View(game)["cover"] : null, cover = game != null ? artwork?.View(game)["cover"].url ?? Art(game.CoverImage) : "", background = Art(game?.BackgroundImage) };
            }).ToArray();
        }
        async void PollJobs()
        {
            if (pollingDownloads || exiting) return; pollingDownloads = true;
            try
            {
                bool changed = false;
                foreach (var j in jobs.Where(j => j.state != "complete"))
                {
                    Guid id; if (!Guid.TryParse(j.gameId, out id)) continue;
                    var g = PlayniteApi.Database.Games.Get(id);
                    if (g?.IsInstalled == true && !g.IsInstalling) { j.state = "complete"; j.completedAt = DateTime.UtcNow; j.message = "Установлена · можно играть"; j.percent = 100; changed = true; }
                }
                if (changed) SaveJobs();
                var tracked = jobs.Take(100).Select(j => new { j.id, j.store, j.path, j.appId, j.state, j.createdAt, j.completedAt }).ToArray();
                var samples = await Task.Run(() => {
                    var values = new Dictionary<string, DownloadMetrics>(); var now = DateTime.UtcNow;
                    foreach (var j in tracked)
                    {
                        if (j.store != "steam") { values[j.id] = new DownloadMetrics { phase = j.state == "complete" ? "complete" : j.state == "preparing" ? "preparing" : j.state == "attention" ? "attention" : "external", sampledAt = now, elapsedSeconds = j.createdAt.HasValue ? Math.Max(0, ((j.completedAt ?? now) - j.createdAt.Value).TotalSeconds) : 0, note = "Этот клиент пока не передаёт Orbit скорость, объём и время. Открой его окно загрузок." }; continue; }
                        DownloadTracker tracker;
                        if (!downloadTrackers.TryGetValue(j.id, out tracker)) { tracker = new DownloadTracker(); downloadTrackers[j.id] = tracker; }
                        values[j.id] = tracker.Observe(SteamTransfer.Read(j.path, j.appId), j.state, j.createdAt, now, j.completedAt);
                    }
                    foreach (var id in downloadTrackers.Keys.Except(tracked.Select(j => j.id)).ToArray()) downloadTrackers.Remove(id);
                    return values;
                });
                if (exiting) return;
                downloadMetrics.Clear(); foreach (var m in samples) downloadMetrics[m.Key] = m.Value;
                if (window.IsVisible) Send(new { kind = "downloads", jobs = DownloadRows() });
            }
            catch (Exception ex) { diagnosticLog?.Record("downloads.failed",ex,"downloads");if (!exiting && window.IsVisible) Send(new { kind = "downloadError", message = "Не удалось обновить показатели. Проверь состояние загрузки в клиенте магазина." }); }
            finally { pollingDownloads = false; }
        }
        [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr h,int a,ref int v,int s);
    }
    sealed class WindowOwner:Forms.IWin32Window {public IntPtr Handle{get;}public WindowOwner(Window w){Handle=new System.Windows.Interop.WindowInteropHelper(w).Handle;}}
    public sealed class InstallJob {public string id,gameId,appId,name,store,path,state,message;public double? percent;public DateTime? createdAt,completedAt;}
    sealed class LocalUiRequestHandler:CefSharp.Handler.RequestHandler
    {
        readonly string file;public LocalUiRequestHandler(string path){file=Path.GetFullPath(path);}
        protected override bool OnBeforeBrowse(IWebBrowser browser,IBrowser native,IFrame frame,IRequest request,bool userGesture,bool redirect)
        { return !Uri.TryCreate(request.Url,UriKind.Absolute,out var u)||!u.IsFile||!string.Equals(Path.GetFullPath(u.LocalPath),file,StringComparison.OrdinalIgnoreCase); }
    }
}
