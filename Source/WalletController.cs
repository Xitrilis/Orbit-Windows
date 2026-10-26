using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Orbit
{
    public sealed class WalletSettings
    {
        public string login = "", region = "RU", method = "sbp";
        public bool rememberLogin;
    }
    public sealed class WalletController : IDisposable
    {
        readonly WalletService service = new WalletService();
        readonly Action<object> send;
        readonly string path;
        WalletSettings settings = new WalletSettings();
        CancellationTokenSource cancellation;
        int generation;
        bool disposed;
        public WalletController(string dataDirectory, Action<object> send)
        {
            this.send = send; path = Path.Combine(dataDirectory, "wallet-settings.json");
            try { if (File.Exists(path)) settings = Validate(JObject.Parse(File.ReadAllText(path))); } catch { }
        }
        public static string Login(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length != 0 && !Regex.IsMatch(value, @"\A[A-Za-z0-9_]{1,64}\z")) throw new ArgumentException("Логин Steam: латинские буквы, цифры и _. Не имя профиля, ссылка или пароль.");
            return value;
        }
        static WalletSettings Validate(JObject j)
        {
            if (j == null) throw new ArgumentException("Нет настроек пополнения.");
            var method = (string)j["method"];
            if (method != "sbp" && method != "card") throw new ArgumentException("Выбери СБП или карту.");
            var remember = (bool?)j["rememberLogin"] == true;
            return new WalletSettings { region = WalletService.Region((string)j["region"]), method = method, rememberLogin = remember, login = remember ? Login((string)j["login"]) : "" };
        }
        void Cancel() { generation++; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; }
        public void Handle(JObject m)
        {
            if (disposed) return;
            try
            {
                switch ((string)m["kind"])
                {
                    case "walletSettings": send(new { kind = "walletSettings", settings }); break;
                    case "walletSave":
                        var next = Validate(m["settings"] as JObject);
                        var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonConvert.SerializeObject(next, Formatting.Indented));
                        // No backup containing a login after the user turns off remembering it.
                        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
                        settings = next; send(new { kind = "walletSaved", settings }); break;
                    case "walletCancel": Cancel(); break;
                    case "walletQuote": Load(m); break;
                    case "walletOpen":
                        var key = (string)m["target"] ?? ""; string url;
                        if (!WalletService.Links.TryGetValue(key, out url)) throw new ArgumentException("Неизвестная ссылка пополнения.");
                        // No login, card data, amount or arbitrary return URL in the browser address.
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        send(new { kind = "walletOpened", target = key }); break;
                    case "walletCopyLogin":
                        var login = Login((string)m["login"]);
                        if (login.Length == 0) throw new ArgumentException("Сначала введи логин Steam.");
                        System.Windows.Clipboard.SetText(login);
                        send(new { kind = "toast", message = "Логин скопирован. Вставь его на странице сервиса." }); break;
                }
            }
            catch (Exception e) { send(new { kind = "walletError", message = e is ArgumentException ? e.Message : "Не удалось выполнить действие. Проверь доступ к папке Orbit и браузер по умолчанию." }); }
        }
        async void Load(JObject m)
        {
            var requestId = (string)m["requestId"];
            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 80) return;
            Cancel(); var epoch = generation; cancellation = new CancellationTokenSource(); var ct = cancellation.Token;
            try
            {
                decimal amount;
                if (!PriceService.Number((string)m["amount"], out amount)) throw new ArgumentException("Укажи сумму цифрами.");
                var result = await service.Load(amount, (string)m["region"], ct);
                if (!disposed && epoch == generation) send(new { kind = "walletQuote", requestId, data = result });
            }
            catch (Exception e)
            {
                if (!disposed && epoch == generation && !ct.IsCancellationRequested) send(new { kind = "walletError", requestId, message = e is ArgumentException ? e.Message : "Не удалось получить расчёт. Повтори запрос или открой сервис." });
            }
        }
        public void Dispose() { disposed = true; Cancel(); service.Dispose(); }
    }
}
