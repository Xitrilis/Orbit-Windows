using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Forms = System.Windows.Forms;

namespace Orbit
{
    public sealed class AssistantController : IDisposable
    {
        readonly string folder, dataDir;
        readonly Func<List<GameContext>> games;
        readonly Action<string> openClient;
        readonly Action<object> notifyMain;
        readonly GeminiClient gemini = new GeminiClient();
        readonly List<AiTurn> history = new List<AiTurn>();
        readonly List<AiModel> models = new List<AiModel>();
        AssistantHost host;
        GameContext selected;
        GameFrame frame;
        DiagnosticReport diagnostic;
        CancellationTokenSource request;
        CancellationTokenSource connection;
        HwndSource source;
        IntPtr hotkeyWindow, gameWindow;
        ushort hotkeyId;
        string key = "", model = "", shortcut = "ctrlShiftSpace", notice = "", hotkeyStatus = "";
        string pendingQuestion = "";
        bool noSpoilers = true, busy, scanning, connecting, disposed;
        int epoch;
        public AssistantController(string folder, string dataDir, Func<List<GameContext>> games, Action<string> openClient, Action<object> notifyMain)
        {
            this.folder = folder; this.dataDir = dataDir; this.games = games; this.openClient = openClient; this.notifyMain = notifyMain;
            try { var c = JObject.Parse(File.ReadAllText(Path.Combine(dataDir, "ai-settings.json"))); model = (string)c["model"] ?? ""; shortcut = (string)c["shortcut"] ?? shortcut; noSpoilers = (bool?)c["noSpoilers"] ?? true; } catch { }
            var file = Path.Combine(dataDir, "ai-key.bin");
            if (File.Exists(file)) try { key = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser)); }
                catch { notice = "Сохранённый ключ недоступен этому пользователю Windows. Введи его заново в настройках."; }
        }
        public bool RecommendationReady => !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(model);
        public Task<string> Recommend(JObject body, CancellationToken ct)
        {
            if (connecting) throw new InvalidOperationException("Дождись проверки подключения Gemini.");
            return gemini.Ask(key, model, body, ct);
        }
        public void Register(IntPtr hwnd)
        {
            if (source != null) source.RemoveHook(Hook);
            if (hotkeyWindow != IntPtr.Zero && hotkeyId != 0) UnregisterHotKey(hotkeyWindow, hotkeyId);
            hotkeyWindow = hwnd;
            if (hotkeyId == 0) hotkeyId = GlobalAddAtom("OrbitAssistant." + Guid.NewGuid().ToString("N"));
            source = HwndSource.FromHwnd(hwnd); source?.AddHook(Hook);
            if (shortcut == "off") { hotkeyStatus = "Горячая клавиша отключена."; return; }
            uint modifiers = shortcut == "altShiftSpace" ? 0x0001u | 0x0004u : shortcut == "ctrlAltSpace" ? 0x0002u | 0x0001u : 0x0002u | 0x0004u;
            hotkeyStatus = RegisterHotKey(hwnd, hotkeyId, modifiers | 0x4000u, 0x20) ? "Горячая клавиша активна." : "Сочетание занято или недоступно. Выбери другое в настройках помощника.";
        }
        IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0312 && w.ToInt32() == hotkeyId) { handled = true; Application.Current.Dispatcher.BeginInvoke(new Action(ToggleOverlay)); }
            return IntPtr.Zero;
        }
        void EnsureHost()
        {
            if (host != null) return;
            host = new AssistantHost(folder); host.Command += Handle;
            host.Hidden += () => { frame = null; Push(); if (gameWindow != IntPtr.Zero) GameCapture.SetForegroundWindow(gameWindow); };
        }
        void Select(GameContext game)
        {
            if (game?.id == selected?.id) { selected = game; return; }
            Cancel(); selected = game; frame = null; diagnostic = null; history.Clear(); pendingQuestion = ""; gameWindow = IntPtr.Zero; notice = "";
        }
        public void Open(string id = null, bool diagnostics = false, bool settings = false)
        {
            EnsureHost();
            if (!string.IsNullOrWhiteSpace(id))
            {
                var found = games().FirstOrDefault(g => g.id == id);
                if (found == null) { notice = "Игра больше не найдена в библиотеке."; }
                else Select(found);
            }
            host.Show(IntPtr.Zero); Push();
            host.Send(new { kind = "tab", tab = settings ? "settings" : diagnostics ? "diagnostics" : "chat" });
            if (diagnostics && selected != null) Scan();
        }
        void ToggleOverlay()
        {
            if (disposed) return;
            if (host?.Visible == true) { host.Hide(); return; }
            try
            {
                var target = GameCapture.GetForegroundWindow();
                var matches = games().Where(g => g.installed && GameCapture.Matches(g, target)).ToList();
                var game = matches.OrderByDescending(g => g.running).ThenByDescending(g => (g.installDirectory ?? "").Length).FirstOrDefault();
                EnsureHost();
                if (game == null)
                {
                    frame = null; gameWindow = IntPtr.Zero;
                    notice = "Не удалось связать активное окно с установленной игрой. Кадр не сделан. Запусти игру через Orbit и повтори горячую клавишу.";
                    host.Show(IntPtr.Zero); Push(); return;
                }
                Select(game); gameWindow = target;
                frame = GameCapture.Capture(target, game);
                notice = "Кадр готов. Посмотри его и задай вопрос — до отправки он остаётся на этом ПК.";
                host.Show(target); Push(); host.Send(new { kind = "tab", tab = "chat" });
            }
            catch (Exception e) { frame = null; EnsureHost(); notice = e is InvalidOperationException ? e.Message : "Не удалось получить кадр игры. Попробуй оконный или безрамочный режим."; host.Show(IntPtr.Zero); Push(); }
        }
        void Push()
        {
            if (disposed || host == null) return;
            host.Send(new { kind = "state", selected, games = games().Select(g => new { g.id, g.name, g.store, g.installed }).ToArray(),
                settings = new { hasKey = !string.IsNullOrWhiteSpace(key), model, shortcut, noSpoilers, hotkeyStatus }, models = models.ToArray(),
                history = history.ToArray(), busy, scanning, connecting, pendingQuestion, diagnostic, notice,
                frame = frame == null ? null : new { url = "data:image/jpeg;base64," + Convert.ToBase64String(frame.bytes), frame.width, frame.height, frame.capturedAt } });
        }
        void Handle(JObject message)
        {
            try
            {
                switch ((string)message["kind"])
                {
                    case "selectGame": Select(games().FirstOrDefault(g => g.id == (string)message["id"])); Push(); break;
                    case "ask": Ask((string)message["question"], (bool?)message["includeFrame"] == true, (bool?)message["includeDiagnostics"] == true); break;
                    case "cancel": Cancel(); notice = "Запрос остановлен. Уже отправленные данные не отзываются у Gemini."; Push(); break;
                    case "clearChat": Cancel(); history.Clear(); frame = null; notice = "Диалог и кадр очищены из памяти Orbit."; Push(); break;
                    case "removeFrame": frame = null; notice = "Кадр убран. Следующий вопрос отправится без него."; Push(); break;
                    case "recapture": notice = "Вернись в игру и снова нажми горячую клавишу — так в кадр не попадёт панель помощника."; host.Hide(); break;
                    case "saveSettings": SaveSettings(message); break;
                    case "forgetKey": Cancel(); connection?.Cancel(); key = ""; models.Clear(); var f = Path.Combine(dataDir, "ai-key.bin"); if (File.Exists(f)) File.Delete(f); notice = "Ключ удалён с этого ПК."; Push(); break;
                    case "scan": Scan(); break;
                    case "verify": Verify(); break;
                    case "exportDiagnostics": Export(); break;
                    case "apiKeys": Process.Start(new ProcessStartInfo("https://aistudio.google.com/api-keys") { UseShellExecute = true }); break;
                }
            }
            catch (Exception e) { notice = e is ArgumentException || e is InvalidOperationException ? e.Message : "Действие не выполнено. Проверь доступность папки Orbit и повтори."; Push(); }
        }
        static void Write(string file, string value)
        {
            var temporary = file + ".tmp"; File.WriteAllText(temporary, value, Encoding.UTF8);
            if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
        }
        async void SaveSettings(JObject m)
        {
            if (connecting) return;
            try
            {
                if (busy) throw new InvalidOperationException("Сначала дождись ответа или останови запрос.");
                var entered = ((string)m["key"] ?? "").Trim();
                var chosen = ((string)m["model"] ?? "").Trim(); if (chosen.Length > 0) chosen = GeminiClient.ModelId(chosen);
                var hotkey = (string)m["shortcut"];
                if (!new[] { "ctrlShiftSpace", "altShiftSpace", "ctrlAltSpace", "off" }.Contains(hotkey)) throw new ArgumentException("Выбери горячую клавишу из списка.");
                if (entered.Length > 0)
                {
                    if (entered.Length > 512 || entered.Any(char.IsWhiteSpace)) throw new ArgumentException("Проверь формат ключа.");
                    var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(entered), null, DataProtectionScope.CurrentUser);
                    var file = Path.Combine(dataDir, "ai-key.bin"); File.WriteAllBytes(file + ".tmp", encrypted);
                    if (File.Exists(file)) File.Replace(file + ".tmp", file, null); else File.Move(file + ".tmp", file);
                    key = entered;
                }
                model = chosen; shortcut = hotkey; noSpoilers = (bool?)m["noSpoilers"] ?? true;
                SaveConfig(); if (hotkeyWindow != IntPtr.Zero) Register(hotkeyWindow);
                if ((bool?)m["test"] == true)
                {
                    connecting = true; notice = "Проверяем ключ и получаем доступные модели…"; Push();
                    connection?.Dispose(); connection = new CancellationTokenSource();
                    var result = await gemini.Models(key, connection.Token);
                    if (disposed || string.IsNullOrWhiteSpace(key)) return;
                    models.Clear(); models.AddRange(result);
                    if (model.Length == 0 || model.Contains("omni")) model = models.Where(mo => !mo.id.Contains("omni")).OrderBy(mo => mo.id.Contains("preview") || mo.id.Contains("exp")).ThenByDescending(mo => mo.id.Contains("flash")).ThenByDescending(mo => mo.id, StringComparer.Ordinal).FirstOrDefault()?.id ?? "gemini-2.5-flash";
                    SaveConfig(); notice = models.Count > 0 ? "Ключ принят. Выбери модель для вопросов и снимков. Генерация ответа расходует квоту Gemini API." : "Ключ принят, но подходящих моделей не найдено.";
                }
                else notice = "Настройки сохранены. Ключ хранится с защитой твоей учётной записи Windows.";
            }
            catch (OperationCanceledException) { if (!string.IsNullOrWhiteSpace(key)) notice = "Gemini не ответил вовремя. Настройки сохранены; проверку можно повторить."; }
            catch (HttpRequestException) { notice = "Не удалось подключиться к Gemini. Проверь сеть и доступность API."; }
            catch (Exception e) { notice = e is ArgumentException || e is InvalidOperationException ? e.Message : "Не удалось сохранить или проверить настройки Gemini."; }
            finally { connecting = false; Push(); }
        }
        void SaveConfig() { Write(Path.Combine(dataDir, "ai-settings.json"), JsonConvert.SerializeObject(new { model, shortcut, noSpoilers })); }
        async void Ask(string question, bool includeFrame, bool includeDiagnostics)
        {
            if (busy) return;
            var turn = ++epoch; var requestGame = selected;
            try
            {
                if (connecting) throw new InvalidOperationException("Дождись проверки подключения.");
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Подключи Gemini в настройках помощника.");
                GeminiClient.ModelId(model);
                var image = includeFrame ? frame?.bytes : null;
                var report = includeDiagnostics ? diagnostic?.ToText() : null;
                if (includeFrame && image == null) throw new InvalidOperationException("Кадр уже убран. Сделай новый снимок или отправь только вопрос.");
                var body = GeminiClient.BuildRequest(requestGame?.name, requestGame?.store, question, history, image, report, noSpoilers);
                request?.Dispose(); request = new CancellationTokenSource(); busy = true; pendingQuestion = question.Trim(); notice = ""; Push();
                var answer = await gemini.Ask(key, model, body, request.Token);
                if (disposed || turn != epoch) return;
                history.Add(new AiTurn { role = "user", text = question.Trim(), image = image != null });
                history.Add(new AiTurn { role = "model", text = answer });
                if (history.Count > 40) history.RemoveRange(0, history.Count - 40);
            }
            catch (OperationCanceledException) { if (turn == epoch) notice = "Время ожидания Gemini истекло или запрос отменён. Можно повторить."; }
            catch (HttpRequestException) { if (turn == epoch) notice = "Не удалось связаться с Gemini. Проверь сеть и доступность API."; }
            catch (Exception e) { if (turn == epoch) notice = e is ArgumentException || e is InvalidOperationException ? e.Message : "Ответ не получен. Проверь настройки Gemini и повтори запрос."; }
            finally { if (turn == epoch) { busy = false; pendingQuestion = ""; } Push(); }
        }
        void Cancel() { epoch++; request?.Cancel(); busy = false; pendingQuestion = ""; }
        async void Scan()
        {
            if (scanning || selected == null) return;
            var game = selected; scanning = true; Push();
            try { var report = await Task.Run(() => GameDiagnostics.Check(game)); if (selected?.id == game.id) diagnostic = report; }
            catch { notice = "Локальная проверка не завершена. Повтори её после закрытия игры."; }
            finally { scanning = false; Push(); }
        }
        void Verify()
        {
            if (selected == null || !selected.installed) throw new InvalidOperationException("Выбери установленную копию игры.");
            var game = games().FirstOrDefault(g => g.id == selected.id);
            if (game == null || game.running) throw new InvalidOperationException("Закрой игру перед проверкой файлов.");
            if (game.store == "steam" && Regex.IsMatch(game.appId ?? "", @"^\d+$"))
            {
                Process.Start(new ProcessStartInfo("steam://validate/" + game.appId) { UseShellExecute = true });
                notice = "Запрос проверки передан Steam. Подтверждение и результат доступны в окне Steam; Orbit не получает итог этой проверки.";
            }
            else { openClient(game.store); notice = "Открыт клиент магазина. Запусти «Проверить» / «Восстановить» в меню этой игры. Результат проверки файлов Orbit пока не получает."; }
            Push();
        }
        void Export()
        {
            if (diagnostic == null) return;
            using (var dialog = new Forms.SaveFileDialog { Filter = "Текстовый отчёт|*.txt", FileName = "Orbit-" + InstallPaths.SafeName(selected?.name ?? "diagnostics") + ".txt" })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) { File.WriteAllText(dialog.FileName, diagnostic.ToText(), Encoding.UTF8); notice = "Отчёт сохранён."; Push(); }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; Cancel(); request?.Dispose(); connection?.Cancel(); connection?.Dispose(); frame = null; history.Clear(); key = "";
            if (hotkeyId != 0) { UnregisterHotKey(hotkeyWindow, hotkeyId); GlobalDeleteAtom(hotkeyId); }
            source?.RemoveHook(Hook); host?.Dispose(); gemini.Dispose();
        }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern ushort GlobalAddAtom(string name);
        [DllImport("kernel32.dll")] static extern ushort GlobalDeleteAtom(ushort atom);
    }
}
