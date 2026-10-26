using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Orbit
{
    public sealed class PriceController : IDisposable
    {
        readonly PriceService service;
        readonly string settingsPath;
        readonly Action<object> send;
        readonly Func<string, PriceProduct> libraryGame;
        readonly Dictionary<string, PriceProduct> products = new Dictionary<string, PriceProduct>();
        readonly HashSet<string> openable = new HashSet<string>();
        readonly object gate = new object();
        PriceSettings settings = new PriceSettings();
        PriceProduct current;
        CancellationTokenSource cancellation;
        int generation;
        bool disposed;
        public PriceController(string dataDirectory, Func<string, PriceProduct> libraryGame, Action<object> send)
        {
            this.libraryGame = libraryGame; this.send = send; service = new PriceService(null, dataDirectory);
            settingsPath = Path.Combine(dataDirectory, "price-settings.json");
            try
            {
                if (File.Exists(settingsPath))
                {
                    var loaded = JsonConvert.DeserializeObject<PriceSettings>(File.ReadAllText(settingsPath));
                    if (loaded != null) { settings.accounts = ValidateAccounts(JObject.FromObject(loaded.accounts ?? new Dictionary<string, PriceAccount>()));
                        foreach (var p in (loaded.funpaySections ?? new Dictionary<string, string>()).Take(100))
                            if (p.Key.Length <= 180) settings.funpaySections[p.Key] = PriceService.FunpaySection(p.Value);
                    }
                }
            }
            catch { settings = new PriceSettings(); }
            foreach (var s in PriceService.Stores) openable.Add(PriceService.StoreUrl(s));
            openable.Add("https://funpay.com/"); openable.Add("https://plati.market/"); openable.Add("https://www.cbr.ru/currency_base/daily/");
        }
        static Dictionary<string, PriceAccount> ValidateAccounts(JObject json)
        {
            var result = new Dictionary<string, PriceAccount>();
            if (json == null) return result;
            foreach (var s in PriceService.Stores)
                if (json[s] is JObject a)
                {
                    var country = ((string)a["country"] ?? "").Trim();
                    result[s] = new PriceAccount { country = country.Length == 0 ? "" : PriceService.Country(country), currency = PriceService.Currency((string)a["currency"]) };
                }
            return result;
        }
        PriceSettings SettingsCopy() => JsonConvert.DeserializeObject<PriceSettings>(JsonConvert.SerializeObject(settings));
        void State() => send(new { kind = "pricesSettings", settings, automaticStores = PriceService.AutomaticStores });
        void Cancel() { lock (gate) { generation++; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; } }
        void Save()
        {
            var tmp = settingsPath + ".tmp"; File.WriteAllText(tmp, JsonConvert.SerializeObject(settings, Formatting.Indented));
            if (File.Exists(settingsPath)) File.Replace(tmp, settingsPath, settingsPath + ".bak"); else File.Move(tmp, settingsPath);
        }
        void Remember(string url) { if (PriceService.SafeUrl(url)) openable.Add(url); }
        public string RegisterProduct(PriceProduct product)
        {
            lock (gate) { if (products.Count > 800) products.Clear(); product.token = Guid.NewGuid().ToString("N"); products[product.token] = product; Remember(product.url); return product.token; }
        }
        public void Handle(JObject message)
        {
            if (disposed) return;
            var kind = (string)message["kind"];
            switch (kind)
            {
                case "pricesSettings": State(); break;
                case "pricesSaveSettings":
                    try
                    {
                        var next = ValidateAccounts(message["accounts"] as JObject); var previous = settings.accounts;
                        Cancel(); settings.accounts = next;
                        try { Save(); } catch { settings.accounts = previous; throw; }
                        State(); send(new { kind = "toast", message = "Страны и валюты сохранены. Настройки аккаунтов в магазинах не изменяются." });
                    }
                    catch (Exception e) { send(new { kind = "pricesSettingsError", message = e is ArgumentException ? e.Message : "Не удалось сохранить настройки цен. Проверь доступ к папке Orbit." }); }
                    break;
                case "pricesSaveFunpay":
                    if (current == null) throw new InvalidOperationException("Сначала выбери игру.");
                    var section = PriceService.FunpaySection((string)message["url"]); var key = PriceService.Normalize(current.name);
                    if (section == "") settings.funpaySections.Remove(key); else settings.funpaySections[key] = section;
                    Save(); State(); send(new { kind = "toast", message = "Раздел сохранён. Обнови цены игры." }); break;
                case "pricesCancel": Cancel(); break;
                case "pricesDiscover":
                    Start(message, false); break;
                case "pricesSearch":
                    Start(message, false); break;
                case "pricesCompare":
                    Start(message, true); break;
                case "pricesOpen":
                    var url = (string)message["url"];
                    lock (gate) if (!PriceService.SafeUrl(url) || !openable.Contains(url)) throw new InvalidOperationException("Ссылка не принадлежит полученному предложению.");
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); break;
            }
        }
        async void Start(JObject message, bool compare)
        {
            var requestId = (string)message["requestId"];
            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 80) return;
            int epoch; CancellationToken token; PriceSettings config;
            lock (gate)
            {
                Cancel(); cancellation = new CancellationTokenSource(); token = cancellation.Token; epoch = generation; config = SettingsCopy();
            }
            try
            {
                var force = (bool?)message["force"] == true;
                if ((string)message["kind"] == "pricesDiscover") {
                    var store = (string)message["store"];
                    var result = await service.Discover(store, config.Account(store), force, token).ConfigureAwait(false);
                    lock (gate) {
                        if (disposed || epoch != generation) return;
                        foreach (var e in result.calendar.events) Remember(e.url);
                        if (products.Count > 800) products.Clear();
                        foreach (var p in result.products) { p.token = Guid.NewGuid().ToString("N"); products[p.token] = p; Remember(p.url); }
                        send(new { kind = "pricesDiscovery", requestId, data = result });
                    }
                }
                else if (compare)
                {
                    PriceProduct game;
                    if (!string.IsNullOrEmpty((string)message["id"])) game = libraryGame((string)message["id"]);
                    else
                    {
                        var productToken = (string)message["token"] ?? "";
                        lock (gate) if (!products.TryGetValue(productToken, out game)) throw new InvalidOperationException("Результат поиска устарел. Найди игру ещё раз.");
                    }
                    current = game;
                    var result = await service.Compare(game, config, force, token).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (disposed || epoch != generation) return;
                        foreach (var source in result.sources) { Remember(source.url); foreach (var offer in source.offers) Remember(offer.url); foreach (var p in source.promotions) Remember(p.url); }
                        foreach (var e in result.calendar.events) Remember(e.url);
                        send(new { kind = "pricesResult", requestId, data = result });
                    }
                }
                else
                {
                    var store = (string)message["store"]; var query = (string)message["query"];
                    var result = await service.Search(store, query, config.Account(store), force, token).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (disposed || epoch != generation) return;
                        if (products.Count > 800) products.Clear();
                        foreach (var p in result.products) { p.token = Guid.NewGuid().ToString("N"); products[p.token] = p; Remember(p.url); }
                        Remember(PriceService.StoreUrl(store, query));
                        send(new { kind = "pricesSearchResult", requestId, data = result, url = PriceService.StoreUrl(store, query) });
                    }
                }
            }
            catch (Exception e)
            {
                lock (gate)
                {
                    if (disposed || epoch != generation || token.IsCancellationRequested) return;
                    var error = e is ArgumentException || e is InvalidOperationException ? e.Message : e is TaskCanceledException ? "Источник не ответил вовремя. Повтори запрос." : "Не удалось загрузить данные. Проверь соединение или открой магазин.";
                    send(new { kind = "pricesError", requestId, message = error });
                }
            }
        }
        public void Dispose() { lock (gate) { disposed = true; Cancel(); service.Dispose(); } }
    }
}
