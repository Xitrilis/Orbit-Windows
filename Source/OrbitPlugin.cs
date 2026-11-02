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
        LauncherStateFile<JObject> preferencesFile;
        LauncherStateFile<List<InstallJob>> downloadsFile;
        readonly List<string> startupWarnings = new List<string>();
        readonly List<InstallJob> jobs = new List<InstallJob>();
        readonly Dictionary<string, Process> helpers = new Dictionary<string, Process>();
        bool ready, exiting, modalOpen, engineRequested;
        Window engineWindow;
        MinecraftController minecraft;
        RadminController radmin;
        HorizonController horizon;
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
            GuardEngineWindow();
            diagnosticLog = new DiagnosticsLog(Path.Combine(root,"OrbitData","Logs"),"app");
            AppDomain.CurrentDomain.UnhandledException += Unhandled;
            Application.Current.DispatcherUnhandledException += DispatcherFailed;
            TaskScheduler.UnobservedTaskException += TaskFailed;
            try
            {
                dataDir = Path.Combine(root, "OrbitData"); Directory.CreateDirectory(dataDir);
                preferencesFile = new LauncherStateFile<JObject>(Path.Combine(dataDir,"preferences.json"),2*1024*1024,LauncherState.Preferences,()=>new JObject());
                downloadsFile = new LauncherStateFile<List<InstallJob>>(Path.Combine(dataDir,"downloads.json"),8*1024*1024,LauncherState.Downloads,()=>new List<InstallJob>());
                prefs=preferencesFile.Value;jobs.AddRange(downloadsFile.Value);
                if(preferencesFile.Warning!=null){startupWarnings.Add("Настройки: "+preferencesFile.Warning);diagnosticLog.Record(preferencesFile.ReadOnly?"settings.warning":"settings.recovered",component:"settings");}
                if(downloadsFile.Warning!=null){startupWarnings.Add("История загрузок: "+downloadsFile.Warning);diagnosticLog.Record(downloadsFile.ReadOnly?"downloads.warning":"downloads.recovered",component:"downloads");}
                foreach (var j in jobs.Where(j => j.state == "preparing")) { j.state = "attention"; j.message = "Автодействия прерваны перезапуском. Проверь клиент."; }
                assistant = new AssistantController(folder, dataDir, AssistantGames, OpenClient, Send, g => horizon.AssistantState(g));
                prices = new PriceController(dataDir, id => { var g = GetGame(id); return new PriceProduct { id = g.GameId, name = g.Name, store = StoreOf(g), image = Art(g.CoverImage) }; }, message => Ui(() => Send(message)));
                wallet = new WalletController(dataDir, message => Ui(() => Send(message)));
                artworkStore = new ArtworkStore(Path.Combine(dataDir, "Artwork"));
                artwork = new ArtworkController(artworkStore, GetGame, Art, () => window, Send, () => Snapshot(true));
                home = new HomeController(dataDir, HomeLibrary, () => assistant.RecommendationReady, assistant.Recommend, prices.RegisterProduct, message => Ui(() => Send(message)));
                playHistory = new PlayHistory(dataDir);
                gameJournal = new GameJournal(Path.Combine(dataDir,"game-journal.json"));
                var sharedMinecraftSettings = new MinecraftSettingsStore(dataDir);
                minecraft = new MinecraftController(dataDir, () => window, Send, ex => diagnosticLog?.Record("minecraft.failed",ex,"minecraft"), () => exiting, sharedMinecraftSettings);
                radmin = new RadminController(folder, dataDir, () => window, message => Ui(() => Send(message)), ex => diagnosticLog?.Record("radmin.failed",ex,"radmin"), () => exiting);
                horizon = new HorizonController(dataDir, () => window, message => Ui(() => Send(message)), ex => diagnosticLog?.Record("horizon.failed",ex,"horizon"), () => exiting, sharedMinecraftSettings);
                backgroundClients = new BackgroundClients(dataDir, new WinClientWindows(), () => (bool?)prefs["backgroundClients"] != false, () => modalOpen || jobs.Any(j => j.state == "preparing"), () => Snapshot(true));
                CreateWindow(); CreateTray(); radmin.StartAuto();
                GuardEngineWindow();
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
            browser = new ChromiumWebBrowser { Address = new Uri(uiFile).AbsoluteUri };
            browser.RequestHandler = new LocalUiRequestHandler(uiFile);
            browser.JavascriptMessageReceived += (s, e) =>
            {
                var text = e.Message as string;
                if (text == null || text.Length > 256000) return;
                Ui(() => { JObject command=null;try { if (new Uri(browser.Address).LocalPath == uiFile) { command=JObject.Parse(text);Handle(command); } } catch (Exception ex) { var code=diagnosticLog?.Record("ui.command.failed",ex);var kind=(string)command?["kind"];Send(new{kind=new[]{"installOptions","browseFolder","install"}.Contains(kind)?"installError":"error",id=(string)command?["id"],requestId=(string)command?["requestId"],message=ex.Message,diagnosticCode=code}); } });
            };
            browser.FrameLoadEnd += (s, e) => { if (e.Frame.IsMain) Ui(() => { ready = true;diagnosticLog?.Record("browser.ready",component:"browser"); Snapshot(true); }); };
            browser.LoadError += (s,e) => { if(e.Frame.IsMain && (int)e.ErrorCode != -3) diagnosticLog?.Record("browser.load.failed",component:"browser",number:(int)e.ErrorCode); };
            window.Content = browser;PrepareStartup();
            window.Closing += (s, e) => { if (!exiting) { e.Cancel = true;if(startupTicket!=null&&!startupFinished)try{StartupHandoff.Release(dataDir,startupTicket.Token,"hide");}catch{} window.Hide(); } };
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
        void GuardEngineWindow()
        {
            var host = Application.Current.MainWindow;
            if (host == null || host == window) return;
            if (engineWindow != host)
            {
                engineWindow = host;
                host.IsVisibleChanged += (s, e) => HideEngineLater(host);
                host.Activated += (s, e) => HideEngineLater(host);
            }
            HideEngineLater(host);
        }
        void HideEngineLater(Window host)
        {
            if (engineRequested || exiting) return;
            host.Dispatcher.BeginInvoke(new Action(() => {
                if (!engineRequested && !exiting && host.IsVisible) host.Hide();
            }), DispatcherPriority.Background);
        }
        void Show() { window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; if(startupTicket==null||startupFinished)window.Activate(); Snapshot(true); }
        void ShowEngine() { backgroundClients?.Cancel();engineRequested = true;var w = Application.Current.MainWindow; if (w == null) return; w.Show(); w.WindowState = WindowState.Normal; w.Activate(); }
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args) { yield return new MainMenuItem { Description = "Открыть Orbit", Action = _ => Ui(Show) }; }
        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) { Ui(() => { diagnosticLog?.Record("library.updated");Snapshot(true); Send(new { kind = "syncDone" }); backgroundClients?.Schedule(); }); }
        public override void OnGameStarted(OnGameStartedEventArgs args) { Ui(() => { diagnosticLog?.Record("game.started");var g=args.Game;playHistory?.Started(g.Id.ToString(),g.Name,StoreOf(g),DateTime.UtcNow);Snapshot(true);backgroundClients?.Schedule(); }); }
        public override void OnGameStopped(OnGameStoppedEventArgs args) { Ui(() => { diagnosticLog?.Record("game.stopped");playHistory?.Stopped(args.Game.Id.ToString(),DateTime.UtcNow,args.ElapsedSeconds,args.ManuallyStopped);Snapshot(true); }); }
        public override void OnGameStartupCancelled(OnGameStartupCancelledEventArgs args) { Ui(() => { diagnosticLog?.Record("game.cancelled");backgroundClients?.Cancel();Snapshot(true); }); }
        public override void OnGameInstalled(OnGameInstalledEventArgs args) { Ui(() => { PollJobs(); Snapshot(true); }); }
        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            exiting = true; timer?.Stop();startupTimer?.Stop();diagnosticLog?.Record("app.stopped");
            AppDomain.CurrentDomain.UnhandledException -= Unhandled;Application.Current.DispatcherUnhandledException -= DispatcherFailed;TaskScheduler.UnobservedTaskException -= TaskFailed;
            foreach (var p in helpers.Values.ToArray()) { try { if (!p.HasExited) p.Kill(); } catch { } }
            try { SavePrefs(); } catch { }
            try { SaveJobs(); } catch { }
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
            var result = PlayniteApi.Database.Games.Where(g => StoreOf(g) != null).Select(g =>
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
            var activeHorizon = horizon?.AssistantGame(); if (activeHorizon != null) result.Add(activeHorizon);
            return result;
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
            var snapshot = new { kind = "snapshot", data = new { games = gs, prefs, startup=StartupState(), stores = stores.Select(s => new { id = s, available = Plugin(s) != null }), downloads = DownloadRows(), journal = gameJournal?.Snapshot(), activity = playHistory?.Snapshot() ?? new PlaySession[0], activityWarning = playHistory?.Warning, backgroundClients = new { enabled = (bool?)prefs["backgroundClients"] != false, hiddenStores = backgroundClients?.HiddenStores ?? new string[0], warning = backgroundClients?.Warning }, notifications = PlayniteApi.Notifications.Messages.Where(n => n.Type == NotificationType.Error).Select(n => n.Text).Take(6).ToArray() } };
            var key = JsonConvert.SerializeObject(new { snapshot.data.games, snapshot.data.prefs, snapshot.data.stores, snapshot.data.notifications, snapshot.data.journal, snapshot.data.activity, snapshot.data.activityWarning, snapshot.data.backgroundClients });
            var json = JsonConvert.SerializeObject(snapshot);
            if (!force && lastSnapshot == key) return;
            lastSnapshot = key; ExecuteJson(json);
        }
        void ExecuteJson(string json) { if (ready && browser != null && !browser.IsDisposed) browser.ExecuteScriptAsync("window.Orbit&&window.Orbit.receive(" + json.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029") + ");"); }
        void Send(object message) { var payload=JObject.FromObject(message);LogResponse(payload);ExecuteJson(payload.ToString(Formatting.None)); }
        void Error(string message) => Send(new { kind = "error", message });
        void SavePrefs() { preferencesFile?.Save(prefs); }
        void SaveJobs() { downloadsFile?.Save(jobs); }
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
            if(kind!=null&&kind.StartsWith("hz",StringComparison.Ordinal)){horizon?.Handle(m);return;}
            if(kind!=null&&kind.StartsWith("rv",StringComparison.Ordinal)){radmin?.Handle(m);return;}
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
                case "friendsOpen":
                    var steamUrl=(string)m["url"];
                    if(!SteamLinks.Allowed(steamUrl)){Send(new{kind="friendsResult",message="Проверь ссылку приглашения или адрес сервера."});break;}
                    try{Process.Start(new ProcessStartInfo(steamUrl){UseShellExecute=true});Send(new{kind="friendsResult",message="Команда передана Steam. Подключение проверь в самом Steam."});}
                    catch(Exception ex){diagnosticLog?.Record("friends.failed",ex,"friends");Send(new{kind="friendsResult",message="Не удалось открыть Steam. Проверь, что он установлен и запущен."});}break;
                case "startupRendered": StartupRendered((string)m["token"]);break;
                case "ready": ready=true; Snapshot(true);if(startupWarnings.Count>0){Send(new{kind="toast",message=string.Join(" ",startupWarnings)});startupWarnings.Clear();}break;
                case "minimize": window.WindowState=WindowState.Minimized; break;
                case "maximize": window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized; break;
                case "close": window.Hide(); break;
                case "drag": if (Mouse.LeftButton==MouseButtonState.Pressed) { var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;ReleaseCapture();SendMessage(handle,0xA1,new IntPtr(2),IntPtr.Zero); } break;
                case "prefs":
                    var patch=m["patch"] as JObject;
                    if(patch!=null){JObject next;try{next=LauncherState.Patch(prefs,patch);preferencesFile.Save(next);}catch{Snapshot(true);throw;}prefs=next;if((bool?)prefs["backgroundClients"]==false)backgroundClients?.RestoreAll();Snapshot(true);}
                    break;
                case "showClients": backgroundClients?.RestoreAll();diagnosticLog?.Record("clients.restored",component:"clients");Send(new{kind="toast",message="Скрытые окна клиентов возвращены. Автоскрытие приостановлено на 10 минут."});break;
                case "hideClients": var hiddenCount=backgroundClients?.HideNow()??0;diagnosticLog?.Record("clients.hidden",component:"clients",number:hiddenCount);Send(new{kind="toast",message=hiddenCount>0?"Убираем в фон окон: "+hiddenCount+". Клиенты продолжают работать.":"Сейчас нет окон, которые можно убрать в фон. Во время подготовки установки скрытие приостановлено."});break;
                case "favorite":
                    foreach(var id in ((JArray)m["ids"]).Values<string>()) { var game=GetGame(id); game.Favorite=(bool)m["value"];PlayniteApi.Database.Games.Update(game); } Snapshot(true);break;
                case "play":
                    var g=GetGame((string)m["id"]);if(!g.IsInstalled)throw new InvalidOperationException("Выбранная копия ещё не установлена.");if(g.IsRunning||g.IsLaunching){diagnosticLog?.Record("game.duplicate.ignored");break;}diagnosticLog?.Record("game.requested");PlayniteApi.StartGame(g.Id);break;
                case "connect": Connect((string)m["store"]);break;
                case "client": OpenClient((string)m["store"]);break;
                case "advanced": ShowEngine(); break;
                case "assistant": assistant?.Open((string)m["id"]); break;
                case "assistantSettings": assistant?.Open(settings:true); break;
                case "diagnostics": assistant?.Open(GetGame((string)m["id"]).Id.ToString(), diagnostics:true); break;
                case "sync": Refresh();break;
                case "installOptions": InstallOptions(GetGame((string)m["id"]),(string)m["requestId"]);break;
                case "browseFolder":
                    using(var d=new Forms.FolderBrowserDialog { Description="Выбери папку для установки игры",SelectedPath=(string)m["path"]??"",ShowNewFolderButton=true })
                    { var accepted=d.ShowDialog(new WindowOwner(window))==Forms.DialogResult.OK;Send(new {kind="folder",id=(string)m["id"],requestId=(string)m["requestId"],cancelled=!accepted,path=accepted?d.SelectedPath:null,free=accepted?InstallPaths.Free(d.SelectedPath):0}); } break;
                case "install": StartInstall(GetGame((string)m["id"]),(string)m["path"],(string)m["requestId"]);break;
                case "cancelInstall": CancelInstall((string)m["id"]);break;
                case "dismissJob": jobs.RemoveAll(j=>j.id==(string)m["id"]&&new[]{"complete","attention","handed"}.Contains(j.state));SaveJobs();Snapshot(true);break;
            }
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
        void InstallOptions(Game g,string requestId)
        {
            var steam=StoreOf(g)=="steam";
            var libs=steam?InstallPaths.SteamLibraries():new List<string>();
            var drives=DriveInfo.GetDrives().Where(d=>d.IsReady&&(d.DriveType==DriveType.Fixed||d.DriveType==DriveType.Removable)).Select(d=>new {path=d.RootDirectory.FullName,free=d.AvailableFreeSpace}).ToArray();
            Send(new{kind="installOptions",id=g.Id.ToString(),requestId,libraries=libs.Select(p=>new{path=p,free=InstallPaths.Free(p)}),drives,suggested=steam?libs.FirstOrDefault():Path.Combine(drives.FirstOrDefault()?.path??@"C:\", "Games", InstallPaths.SafeName(g.Name))});
        }
        async void StartInstall(Game game,string path,string requestId)
        {
            InstallJob job=null;Process process=null;string requestFile=null;
            try
            {
                if(jobs.Any(j=>j.state=="preparing"))throw new InvalidOperationException("Дождись настройки предыдущей установки.");
                if(game.IsInstalled||game.IsInstalling)throw new InvalidOperationException("Эта копия уже установлена или устанавливается.");
                path=InstallPaths.Validate(path);var store=StoreOf(game);
                if(store=="steam"&&!InstallPaths.SteamLibraries().Any(p=>InstallPaths.Same(p,path)))throw new InvalidOperationException("Сначала добавь эту папку в Steam → Настройки → Хранилище, затем снова выбери её в Orbit.");
                var helper=Path.Combine(folder,"Orbit.InstallHelper.exe");if(!File.Exists(helper))throw new FileNotFoundException("Не найден помощник установки.");
                job=new InstallJob{id=Guid.NewGuid().ToString(),gameId=game.Id.ToString(),appId=game.GameId,name=game.Name,store=store,path=path,state="preparing",message="Ожидаем окно установки и проверяем выбранную папку…",createdAt=DateTime.UtcNow};
                jobs.Insert(0,job);SaveJobs();Snapshot(true);
                requestFile=Path.Combine(dataDir,"install-"+job.id+".json");File.WriteAllText(requestFile,JsonConvert.SerializeObject(job));
                process=new Process{StartInfo=new ProcessStartInfo(helper,"\""+requestFile+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8,WorkingDirectory=folder}};
                process.Start();helpers[job.id]=process;
                // Drain both pipes before InstallGame, which can open a modal client dialog.
                var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                PlayniteApi.InstallGame(game.Id);Send(new{kind="installStarted",id=game.Id.ToString(),requestId});
                if(!await Task.Run(()=>process.WaitForExit(55000))) { try{process.Kill();}catch{} if(job.state=="preparing"){job.state="attention";job.message="Клиент не ответил. Автодействия остановлены; выбранный путь не подтверждён.";} }
                else if(job.state=="preparing")
                {
                    var text=await output;await errors;JObject result=null;try{result=JObject.Parse(text);}catch{}
                    job.state=process.ExitCode==0?"handed":"attention";
                    job.message=(string)result?["message"]??"Автовыбор папки не подтверждён. Открой клиент и проверь установку.";
                }
            }
            catch(Exception e)
            {
                diagnosticLog?.Record("install.failed",e);
                // A rejected duplicate owns no job and must not cancel the first request.
                if(job!=null&&job.state=="preparing"){job.state="attention";job.message="Ошибка подготовки: "+e.Message;}
                Send(new{kind="installError",id=game.Id.ToString(),requestId,message=e.Message});
            }
            finally
            {
                if(process!=null){try{if(!process.HasExited)process.Kill();}catch{}process.Dispose();}
                if(job!=null)helpers.Remove(job.id);if(requestFile!=null)try{File.Delete(requestFile);}catch{}
                try{SaveJobs();Snapshot(true);}catch(Exception e){diagnosticLog?.Record("install.save.failed",e);}
            }
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
        [DllImport("user32.dll")]static extern bool ReleaseCapture();
        [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    }
    sealed class WindowOwner:Forms.IWin32Window {public IntPtr Handle{get;}public WindowOwner(Window w){Handle=new System.Windows.Interop.WindowInteropHelper(w).Handle;}}
    sealed class LocalUiRequestHandler:CefSharp.Handler.RequestHandler
    {
        readonly string file;public LocalUiRequestHandler(string path){file=Path.GetFullPath(path);}
        protected override bool OnBeforeBrowse(IWebBrowser browser,IBrowser native,IFrame frame,IRequest request,bool userGesture,bool redirect)
        { return !Uri.TryCreate(request.Url,UriKind.Absolute,out var u)||!u.IsFile||!string.Equals(Path.GetFullPath(u.LocalPath),file,StringComparison.OrdinalIgnoreCase); }
    }
}
