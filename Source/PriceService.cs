using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Parser.Html;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Orbit
{
    public sealed class PriceAccount { public string country = ""; public string currency = ""; }
    public sealed class PriceSettings
    {
        public Dictionary<string, PriceAccount> accounts = new Dictionary<string, PriceAccount>();
        public Dictionary<string, string> funpaySections = new Dictionary<string, string>();
        // A known Steam storefront region is the shared default, not proof of another account's country.
        // Currency is deliberately not inherited: each storefront returns its own currency.
        public PriceAccount Account(string store)
        {
            PriceAccount own, shared;
            accounts.TryGetValue(store, out own);
            accounts.TryGetValue("steam", out shared);
            return new PriceAccount { country = !string.IsNullOrEmpty(own?.country) ? own.country : shared?.country ?? "",
                currency = own?.currency ?? "" };
        }
    }
    public sealed class PriceProduct
    {
        public string token, store, id, name, image, url, productType, catalogNamespace;
        public PriceOffer preview;
    }
    public sealed class PriceOffer
    {
        public string source, name, url, currency, country, kind, seller, sellerInfo, region, platform, edition, note, productKey;
        public DateTime? saleEndsAt;
        public PriceHistoryView history;
        public decimal? amount, originalAmount, rub, kzt;
        public bool official, from, free;
        public DateTime checkedAt;
    }
    public sealed class PriceSourceResult
    {
        public string source, status, message, url, regionBasis;
        public List<PriceOffer> offers = new List<PriceOffer>();
        public int excluded;
        public string match, promotionMessage;
        public List<PricePromotion> promotions = new List<PricePromotion>();
    }
    public sealed class PriceSearchResult
    {
        public string store, country, message, fxDate, fxError;
        public List<PriceProduct> products = new List<PriceProduct>();
    }
    public sealed class PriceComparison
    {
        public string name, store, country, expectedCurrency;
        public List<PriceSourceResult> sources = new List<PriceSourceResult>();
        public SaleCalendar calendar;
        public string fxDate, fxError, fxUrl = "https://www.cbr.ru/currency_base/daily/";
    }
    public sealed class PriceFx
    {
        public DateTime date;
        public Dictionary<string, decimal> rubPerUnit = new Dictionary<string, decimal> { ["RUB"] = 1m };
        public decimal? Convert(decimal amount, string from, string to)
        {
            if (from == to) return amount;
            if (!rubPerUnit.ContainsKey(from ?? "") || !rubPerUnit.ContainsKey(to ?? "")) return null;
            return decimal.Round(amount * rubPerUnit[from] / rubPerUnit[to], 2, MidpointRounding.AwayFromZero);
        }
        public static PriceFx Parse(string xml, DateTime now)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 300000 };
            XDocument doc;
            using (var reader = XmlReader.Create(new StringReader(xml), settings)) doc = XDocument.Load(reader);
            DateTime date;
            if (!DateTime.TryParseExact((string)doc.Root.Attribute("Date"), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) || date < now.Date.AddDays(-14) || date > now.Date.AddDays(2))
                throw new InvalidDataException("Курс слишком старый или дата источника некорректна.");
            var fx = new PriceFx { date = date };
            foreach (var v in doc.Root.Elements("Valute"))
            {
                var code = (string)v.Element("CharCode");
                decimal nominal, value;
                if (Regex.IsMatch(code ?? "", "^[A-Z]{3}$") && PriceService.Number((string)v.Element("Nominal"), out nominal) && nominal > 0 && PriceService.Number((string)v.Element("Value"), out value) && value > 0)
                    fx.rubPerUnit[code] = value / nominal;
            }
            if (!fx.rubPerUnit.ContainsKey("KZT")) throw new InvalidDataException("В источнике курса нет KZT.");
            return fx;
        }
    }
    // Read-only public storefront data. No cookies, account credentials, purchases or redirects to third-party hosts.
    public sealed partial class PriceService : IDisposable
    {
        sealed class Cached { public string body; public DateTime at; }
        readonly HttpClient http;
        readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>();
        readonly object cacheLock = new object();
        public static readonly string[] Stores = { "steam", "epic", "gog", "ea", "ubisoft", "battlenet" };
        public static readonly string[] AutomaticStores = { "steam", "epic", "gog" };
        public PriceService(HttpMessageHandler handler = null, string dataDirectory = null)
        {
            history = new PriceHistoryStore(dataDirectory);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            http.Timeout = TimeSpan.FromSeconds(25); http.MaxResponseContentBufferSize = 8 * 1024 * 1024;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Orbit/0.7 (desktop price comparison)");
        }
        public static string Normalize(string s) => Regex.Replace((s ?? "").Replace("™", "").Replace("®", "").Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        public static bool Number(string s, out decimal value) => decimal.TryParse((s ?? "").Replace("\u00a0", "").Replace("\u202f", "").Replace(" ", "").Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        static decimal? Amount(JToken t) { decimal n; return t != null && Number(t.ToString(), out n) && n >= 0 && n < 1000000000m ? n : (decimal?)null; }
        static string Text(string s, int max = 800) { s = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(s ?? "", "<[^>]*>", " ")), @"\s+", " ").Trim(); return s.Substring(0, Math.Min(max, s.Length)); }
        static string Query(string s) => Uri.EscapeDataString(s ?? "");
        public static string Country(string value)
        {
            value = (value ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(value, "^[A-Z]{2}$")) throw new ArgumentException("Укажи двухбуквенный код страны аккаунта: например RU или KZ.");
            try { new RegionInfo(value); } catch { throw new ArgumentException("Неизвестный код страны аккаунта."); }
            return value;
        }
        public static string Currency(string value)
        {
            value = (value ?? "").Trim().ToUpperInvariant();
            if (value.Length > 0 && !Regex.IsMatch(value, "^[A-Z]{3}$")) throw new ArgumentException("Валюта задаётся кодом: RUB, KZT, USD, EUR…");
            return value;
        }
        public static bool SafeUrl(string value)
        {
            Uri u;
            if (!Uri.TryCreate(value, UriKind.Absolute, out u) || u.Scheme != "https" || !u.IsDefaultPort || !string.IsNullOrEmpty(u.UserInfo)) return false;
            return new[] { "store.steampowered.com", "store.epicgames.com", "www.gog.com", "www.ea.com", "store.ubisoft.com", "shop.battle.net", "eu.shop.battle.net", "us.shop.battle.net", "funpay.com", "plati.market", "www.cbr.ru", "partner.steamgames.com" }.Contains(u.DnsSafeHost.ToLowerInvariant());
        }
        public static string FunpaySection(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            Uri u;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out u) || u.Scheme != "https" || u.Host != "funpay.com" || !u.IsDefaultPort || u.UserInfo.Length > 0 || !Regex.IsMatch(u.AbsolutePath, @"^/(?:en/)?lots/[0-9]{1,10}/?$"))
                throw new ArgumentException("Нужна ссылка на раздел ключей FunPay: https://funpay.com/lots/номер/.");
            return "https://funpay.com" + u.AbsolutePath.Replace("/en/", "/").TrimEnd('/') + "/";
        }
        public static string StoreUrl(string store, string name = "")
        {
            switch (store)
            {
                case "steam": return "https://store.steampowered.com/search/?term=" + Query(name);
                case "epic": return "https://store.epicgames.com/browse?q=" + Query(name);
                case "gog": return "https://www.gog.com/en/games?query=" + Query(name);
                case "ea": return "https://www.ea.com/games/library";
                case "ubisoft": return "https://store.ubisoft.com/";
                case "battlenet": return "https://shop.battle.net/";
                default: throw new ArgumentException("Неизвестный магазин.");
            }
        }
        async Task<Cached> Get(string url, bool force, CancellationToken ct, int minutes = 5)
        {
            Cached saved;
            lock (cacheLock) if (!force && cache.TryGetValue(url, out saved) && DateTime.UtcNow - saved.at < TimeSpan.FromMinutes(minutes)) return saved;
            var uri = new Uri(url);
            if (uri.Scheme != "https" || !new[] { "store.steampowered.com", "store.epicgames.com", "catalog.gog.com", "plati.io", "api.digiseller.com", "funpay.com", "www.cbr.ru", "partner.steamgames.com", "store-site-backend-static.ak.epicgames.com" }.Contains(uri.Host)) throw new InvalidOperationException("Источник не разрешён.");
            using (var response = await http.GetAsync(uri, ct).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) throw new HttpRequestException((int)response.StatusCode == 429 ? "Источник ограничил частоту запросов. Попробуй позже." : "Источник не вернул данные (HTTP " + (int)response.StatusCode + ").");
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Слишком большой ответ источника.");
                // Structural JSON/HTML/XML fields are UTF-8/ASCII. CBR's Cyrillic currency names are not consumed.
                saved = new Cached { body = Encoding.UTF8.GetString(bytes), at = DateTime.UtcNow };
                ct.ThrowIfCancellationRequested();
                lock (cacheLock) { if (cache.Count > 120) cache.Clear(); cache[url] = saved; }
                return saved;
            }
        }
        static string Failure(Exception e) => e is TaskCanceledException || e is OperationCanceledException ? "Источник не ответил вовремя. Повтори запрос." : e is HttpRequestException ? e.Message : "Формат ответа изменился или данные отсутствуют. Открой источник.";
        public async Task<PriceSearchResult> Search(string store, string query, PriceAccount account, bool force, CancellationToken ct, bool convert = true)
        {
            if (!Stores.Contains(store)) throw new ArgumentException("Неизвестный магазин.");
            query = (query ?? "").Trim(); if (query.Length < 3 || query.Length > 150) throw new ArgumentException("Введи от 3 до 150 символов названия игры.");
            var result = new PriceSearchResult { store = store, country = account.country };
            if (!AutomaticStores.Contains(store)) { result.message = "Автоматические цены этого магазина пока не подключены. Открой его каталог; цены не подменяются предложениями Steam."; return result; }
            var country = Country(account.country);
            if (store == "steam")
            {
                var raw = await Get("https://store.steampowered.com/api/storesearch/?term=" + Query(query) + "&l=english&cc=" + country.ToLowerInvariant(), force, ct).ConfigureAwait(false);
                var data = JObject.Parse(raw.body); if (!(data["items"] is JArray)) throw new InvalidDataException();
                foreach (var p in data["items"].Take(20).Where(p => (string)p["type"] == "app"))
                {
                    var id = (string)p["id"]; if (!Regex.IsMatch(id ?? "", "^[0-9]+$")) continue;
                    var item = new PriceProduct { id = id, store = store, name = Text((string)p["name"]), image = (string)p["tiny_image"], url = "https://store.steampowered.com/app/" + id + "/?cc=" + country.ToLowerInvariant(), productType = "Игра / DLC — выбери нужное издание" };
                    item.preview = SteamOffer(p["price"], item.name, item.url, country, raw.at, false); result.products.Add(item);
                }
            }
            else if (store == "epic")
            {
                // Public storefront query, not Epic's authenticated Ecom API. No API stability guarantee.
                var q = "query { Catalog { searchStore(country: " + JsonConvert.SerializeObject(country) + ", locale: \"en-US\", keywords: " + JsonConvert.SerializeObject(query) + ", count: 20) { elements { id title namespace offerType productSlug urlSlug keyImages { type url } price(country: " + JsonConvert.SerializeObject(country) + ") { totalPrice { originalPrice discountPrice currencyCode currencyInfo { decimals } } } } } } }";
                var raw = await Get("https://store.epicgames.com/graphql?query=" + Query(q), force, ct).ConfigureAwait(false);
                var elements = JObject.Parse(raw.body).SelectToken("data.Catalog.searchStore.elements") as JArray; if (elements == null) throw new InvalidDataException();
                foreach (var p in elements.Where(p => new[] { "BASE_GAME", "EDITION", "DLC" }.Contains((string)p["offerType"])) .Take(20))
                {
                    var slug = ((string)p["productSlug"] ?? (string)p["urlSlug"] ?? "").Replace("/home", "");
                    if (!Regex.IsMatch(slug ?? "", "^[a-zA-Z0-9_-]+$")) continue;
                    var item = new PriceProduct { id = (string)p["id"], catalogNamespace = (string)p["namespace"], store = store, name = Text((string)p["title"]), url = "https://store.epicgames.com/p/" + slug, productType = (string)p["offerType"], image = (string)(p["keyImages"] as JArray)?.FirstOrDefault(i => (string)i["type"] == "OfferImageWide")?["url"] };
                    var price = p.SelectToken("price.totalPrice"); var decimals = (int?)price?.SelectToken("currencyInfo.decimals");
                    var amount = Amount(price?["discountPrice"]); var initial = Amount(price?["originalPrice"]);
                    if (amount.HasValue && decimals.HasValue && decimals >= 0 && decimals <= 4)
                    {
                        decimal divisor = 1; for (int i = 0; i < decimals; i++) divisor *= 10;
                        item.preview = Official(store, item.name, item.url, country, (string)price["currencyCode"], amount / divisor, initial / divisor, raw.at);
                    }
                    result.products.Add(item);
                }
            }
            else
            {
                var currency = Currency(account.currency); if (currency == "") currency = "USD";
                var raw = await Get("https://catalog.gog.com/v1/catalog?limit=20&query=" + Query("like:" + query) + "&countryCode=" + country + "&currencyCode=" + currency + "&locale=en-US", force, ct).ConfigureAwait(false);
                var products = JObject.Parse(raw.body)["products"] as JArray; if (products == null) throw new InvalidDataException();
                foreach (var p in products.Take(20))
                {
                    var url = (string)p["storeLink"]; if (!SafeUrl(url) || new Uri(url).Host != "www.gog.com") continue;
                    var item = new PriceProduct { id = (string)p["id"], store = store, name = Text((string)p["title"]), image = (string)p["coverHorizontal"], url = url, productType = (string)p["productType"] };
                    var price = p.SelectToken("price.finalMoney");
                    if (price != null) item.preview = Official(store, item.name, url, country, (string)price["currency"], Amount(price["amount"]), Amount(p.SelectToken("price.baseMoney.amount")), raw.at);
                    result.products.Add(item);
                }
            }
            foreach (var p in result.products) { CheckCurrency(p.preview, account); if (p.preview != null) p.preview.productKey = p.store + ":" + p.catalogNamespace + ":" + p.id; }
            if (convert && result.products.Count > 0)
            {
                PriceFx fx = null;
                try { fx = PriceFx.Parse((await Get("https://www.cbr.ru/scripts/XML_daily.asp", force, ct, 360).ConfigureAwait(false)).body, DateTime.UtcNow); result.fxDate = fx.date.ToString("yyyy-MM-dd"); }
                catch { ct.ThrowIfCancellationRequested(); result.fxError = "Курс недоступен. Пересчёт не показан."; }
                foreach (var p in result.products) ApplyFx(p.preview, fx);
            }
            result.message = result.products.Count == 0 ? "В выбранном регионе ничего не найдено. Попробуй оригинальное название." : "Выбери точную игру и издание. Цена относится к выбранной стране магазина.";
            return result;
        }
        static PriceOffer Official(string store, string name, string url, string country, string currency, decimal? amount, decimal? initial, DateTime at)
        {
            return new PriceOffer { source = store, official = true, name = name, url = url, country = country, region = country, kind = "Магазин", currency = Currency(currency), amount = amount, originalAmount = initial, checkedAt = at, note = "Цена витрины. Персональные скидки и условия оплаты проверяются в магазине." };
        }
        public static PriceOffer SteamOffer(JToken price, string name, string url, string country, DateTime at, bool free)
        {
            if (price == null && !free) return null;
            // Storefront API consistently uses cents, including JPY/KRW. Do not substitute ISO currency decimals.
            var amount = free ? 0m : Amount(price?["final"]) / 100m;
            var offer = Official("steam", name, url, country, (string)price?["currency"] ?? "", amount, Amount(price?["initial"]) / 100m, at); offer.free = free; return offer;
        }
        static void CheckCurrency(PriceOffer offer, PriceAccount account)
        {
            if (offer == null) return;
            if (!string.IsNullOrEmpty(account.currency) && offer.currency != account.currency && offer.amount != 0)
                offer.note = "Магазин ответил в " + offer.currency + ", а в настройках аккаунта указано " + account.currency + ". Проверь регион. " + offer.note;
        }
        async Task<PriceSourceResult> OfficialQuote(PriceProduct game, PriceAccount account, bool force, CancellationToken ct)
        {
            var result = new PriceSourceResult { source = game.store, url = StoreUrl(game.store, game.name), status = "unavailable" };
            if (!AutomaticStores.Contains(game.store)) { result.message = "Автоматическая цена ещё не подключена. Проверь её в соответствующем магазине."; return result; }
            if (string.IsNullOrEmpty(account.country)) { result.message = "Выбери страну аккаунта в настройках цен."; return result; }
            if (game.store == "steam" && Regex.IsMatch(game.id ?? "", "^[0-9]{1,12}$"))
            {
                var country = Country(account.country);
                result.url = "https://store.steampowered.com/app/" + game.id + "/?cc=" + country.ToLowerInvariant();
                var raw = await Get("https://store.steampowered.com/api/appdetails?appids=" + game.id + "&cc=" + country.ToLowerInvariant() + "&l=english", force, ct).ConfigureAwait(false);
                var root = JObject.Parse(raw.body)[game.id]; var d = root?["data"];
                if ((bool?)root?["success"] == true && d != null)
                {
                    var offer = SteamOffer(d["price_overview"], Text((string)d["name"]), result.url, country, raw.at, (bool?)d["is_free"] == true);
                    if (offer?.amount != null) { CheckCurrency(offer, account); offer.productKey = "steam::" + game.id; result.match = "id"; result.offers.Add(offer); }
                }
            }
            else
            {
                var search = await Search(game.store, game.name, account, force, ct, false).ConfigureAwait(false);
                var matches = search.products.Where(p => p.id == game.id && (string.IsNullOrEmpty(game.catalogNamespace) || p.catalogNamespace == game.catalogNamespace)).ToList();
                // Library IDs for Epic can be a namespace or launch app name. Never use a fuzzy title as an exact price.
                if (matches.Count == 0) matches = search.products.Where(p => Normalize(p.name) == Normalize(game.name)).ToList();
                if (matches.Count == 1 && matches[0].preview?.amount != null) { result.offers.Add(matches[0].preview); result.url = matches[0].url; result.match = matches[0].id == game.id ? "id" : "title"; }
            }
            result.status = result.offers.Count > 0 ? "ok" : "unavailable";
            result.message = result.offers.Count > 0 ? "Цена получена из витрины магазина." : "Нет подтверждённой цены для этой игры и страны. Игра может быть недоступна, иметь другое издание или не продаваться отдельно.";
            return result;
        }
        public static string OfferKind(string title, string description = "")
        {
            var s = (title + " " + description).ToLowerInvariant();
            if (Regex.IsMatch(s, @"offline|офф?лайн|общий аккаунт|готовый аккаунт|аккаунт с игрой|логин\s*[:/]\s*пароль|login\s*[:/]\s*password|аренд|\brental\b|\baccount for\b")) return "other";
            if (Regex.IsMatch(s, @"пополнени|баланс|\btop[ -]?up\b|wallet|случайн|\brandom\b")) return "other";
            if (Regex.IsMatch(s, @"подарком|steam gift|\bgift\b|подарок на")) return "gift";
            if (Regex.IsMatch(s, @"цифровой ключ|ключ|\bkey\b|\bkeys\b")) return "key";
            return "unknown";
        }
        static bool Related(string title, string name)
        {
            var t = " " + Normalize(title) + " "; var n = " " + Normalize(name) + " ";
            var at = t.IndexOf(n, StringComparison.Ordinal); if (at < 0) return false;
            var tail = t.Substring(at + n.Length).TrimStart().Split(' ')[0];
            // A base title must not match a numbered sequel (Hades II, Game 2).
            return !Regex.IsMatch(tail, @"^(?:[0-9]+|[ivx]+)$");
        }
        static bool DifferentEdition(string title, string name)
        {
            var t = Normalize(title); var n = Normalize(name);
            return new[] { "dlc", "дополнение", "ultimate", "deluxe", "gold edition", "complete edition", "season pass", "phantom liberty", "shadow of the erdtree" }.Any(x => t.Contains(x) && !n.Contains(x));
        }
        public static PriceSourceResult ParsePlati(string json, string name, DateTime at)
        {
            var result = new PriceSourceResult { source = "plati", status = "ok", url = "https://plati.market/", message = "До 50 результатов поиска. Базовая цена «от»: страна, издание и способ оплаты выбираются у продавца. Совместимость с аккаунтом не подтверждена." };
            var items = JObject.Parse(json)["items"] as JArray; if (items == null) throw new InvalidDataException();
            foreach (var p in items)
            {
                var title = Text((string)p["name"]); var desc = Text((string)p["description"], 16000); var kind = OfferKind(title, desc); var url = (string)p["url"];
                if (!Related(title, name) || DifferentEdition(title, name) || (kind != "gift" && kind != "key") || !SafeUrl(url) || new Uri(url).Host != "plati.market") { result.excluded++; continue; }
                var price = Amount(p["price_rur"]); if (price == null || price <= 0) { result.excluded++; continue; }
                result.offers.Add(new PriceOffer { source = "plati", name = title, url = url, amount = price, currency = "RUB", from = true, checkedAt = at, kind = kind, seller = Text((string)p["seller_name"] ?? "ID " + (string)p["seller_id"], 100), sellerInfo = "Продаж: " + ((string)p["numsold"] ?? "не указано"), region = "Выбор у продавца", platform = DetectPlatform(title + " " + desc), edition = "Проверь издание", note = "Тип определён по описанию продавца. Это базовая цена, не итог для выбранной страны. Возможна комиссия оплаты." });
            }
            result.offers = result.offers.OrderBy(x => x.amount).Take(8).ToList();
            if (result.offers.Count == 0) { result.status = "empty"; result.message = "В первой странице поиска нет подходящих ключей или подарков. Аккаунты, аренда, дополнения и неясные товары отфильтрованы."; }
            return result;
        }
        static string DetectPlatform(string text)
        {
            text = text.ToLowerInvariant();
            var names = new[] { "steam", "gog", "epic", "ubisoft", "battle.net", "ea app" }.Where(text.Contains).ToArray();
            return names.Length == 1 ? names[0] : "Проверь платформу";
        }
        public static string FindFunpaySection(string html, string name)
        {
            var doc = new HtmlParser().Parse(html);
            foreach (var group in doc.QuerySelectorAll(".promo-game-item"))
                if (Normalize(group.QuerySelector(".game-title")?.TextContent) == Normalize(name))
                    foreach (var link in group.QuerySelectorAll("li a"))
                        if (new[] { "ключи", "keys" }.Contains(Normalize(link.TextContent))) return FunpaySection(link.GetAttribute("href"));
            return null;
        }
        public static PriceSourceResult ParseFunpay(string html, string name, string country, string url, DateTime at)
        {
            var doc = new HtmlParser().Parse(html);
            var result = new PriceSourceResult { source = "funpay", status = "ok", url = url, message = "Публичные лоты раздела ключей. Регион и способ передачи указаны продавцами; условия активации проверь в лоте." };
            var items = doc.QuerySelectorAll("a.tc-item");
            if (items.Length == 0) { result.status = "empty"; result.message = "Нет доступных лотов либо страница изменилась. Открой раздел FunPay."; return result; }
            foreach (var p in items.Take(3000))
            {
                var title = Text(p.QuerySelector(".tc-desc-text")?.TextContent); var method = p.GetAttribute("data-f-method") ?? "";
                var platform = p.GetAttribute("data-f-platform") ?? ""; var edition = p.GetAttribute("data-f-type") ?? "";
                var region = p.GetAttribute("data-f-region2") ?? p.GetAttribute("data-f-region") ?? "";
                var kind = OfferKind(title + " " + method); var link = p.GetAttribute("href");
                if (!Related(title, name) || DifferentEdition(title + " " + edition, name) || (kind != "key" && kind != "gift") || (!string.IsNullOrEmpty(platform) && platform.ToLowerInvariant() != "pc") || !SafeUrl(link) || new Uri(link).Host != "funpay.com") { result.excluded++; continue; }
                // Only explicit country/global declarations enter region-specific results. CIS is intentionally ambiguous.
                var rn = Normalize(region); var compatible = rn == "global" || rn == "весь мир" || rn == "все регионы" || rn == "все страны" ||
                    (country == "RU" && new[] { "россия", "russia", "ru" }.Contains(rn)) || (country == "KZ" && new[] { "казахстан", "kazakhstan", "kz" }.Contains(rn));
                if (!compatible) { result.excluded++; continue; }
                var cell = p.QuerySelector(".tc-price"); var unit = cell?.QuerySelector(".unit")?.TextContent.Trim();
                var currency = unit == "₽" ? "RUB" : unit == "$" ? "USD" : unit == "€" ? "EUR" : unit == "₸" ? "KZT" : null;
                decimal price; var display = cell?.QuerySelector("div")?.TextContent ?? "";
                if (currency == null || !Number(display.Replace(unit, "").Trim(), out price) || price <= 0 || price > 100000000m) { result.excluded++; continue; }
                result.offers.Add(new PriceOffer { source = "funpay", name = title, amount = price, currency = currency, url = link, checkedAt = at, country = country, kind = kind, region = Text(region, 100), edition = Text(edition, 100), platform = DetectPlatform(title), seller = Text(p.QuerySelector(".media-user-name")?.TextContent, 100), sellerInfo = "Отзывов: " + Text(p.QuerySelector(".rating-mini-count")?.TextContent, 30), note = "Данные объявления, не проверка продавца. Цена из видимого текста лота; проверь издание и ограничения активации." });
            }
            // Do not sort incomparable currencies as if they were one currency.
            result.offers = result.offers.OrderBy(x => x.currency).ThenBy(x => x.amount).Take(8).ToList();
            if (result.offers.Count == 0) { result.status = "empty"; result.message = "Не найдены ключи/подарки для ПК с явно указанным регионом " + country + " или Global. Неясные регионы, другие издания и товары исключены."; }
            return result;
        }
        async Task<PriceSourceResult> Marketplace(string source, PriceProduct game, PriceAccount account, PriceSettings settings, bool force, CancellationToken ct)
        {
            if (source == "plati")
            {
                var raw = await Get("https://plati.io/api/search.ashx?query=" + Query(game.name) + "&pagesize=50&pagenum=1&response=json", force, ct).ConfigureAwait(false);
                return ParsePlati(raw.body, game.name, raw.at);
            }
            string section; settings.funpaySections.TryGetValue(Normalize(game.name), out section);
            if (string.IsNullOrEmpty(section))
            {
                var home = await Get("https://funpay.com/", force, ct, 360).ConfigureAwait(false);
                section = FindFunpaySection(home.body, game.name);
            }
            if (string.IsNullOrEmpty(section)) return new PriceSourceResult { source = source, status = "unavailable", url = "https://funpay.com/", message = "Точный раздел игры не найден. Можно указать ссылку на раздел ключей FunPay вручную." };
            section = FunpaySection(section);
            if (string.IsNullOrEmpty(account.country)) return new PriceSourceResult { source = source, status = "unavailable", url = section, message = "Сначала выбери страну аккаунта для проверки региона лотов." };
            var rawPage = await Get(section, force, ct).ConfigureAwait(false);
            return ParseFunpay(rawPage.body, game.name, account.country, section, rawPage.at);
        }
        public async Task<PriceComparison> Compare(PriceProduct game, PriceSettings settings, bool force, CancellationToken ct)
        {
            var account = settings.Account(game.store);
            var result = new PriceComparison { name = game.name, store = game.store, country = account.country, expectedCurrency = account.currency };
            var wanted = new[] { game.store }.Concat(AutomaticStores).Concat(new[] { "plati", "funpay" }).Distinct();
            var calendarTask = Calendar(force, ct);
            var tasks = wanted.Select(async source => {
                try {
                    if (source == "plati" || source == "funpay") return await Marketplace(source, game, account, settings, force, ct).ConfigureAwait(false);
                    var own = settings.Account(source);
                    var quote = source == game.store ? await OfficialQuote(game, own, force, ct).ConfigureAwait(false) : await MatchingQuote(source, game, own, force, ct).ConfigureAwait(false);
                    PriceAccount explicitAccount;
                    if (!settings.accounts.TryGetValue(source, out explicitAccount) || string.IsNullOrEmpty(explicitAccount.country))
                        quote.regionBasis = "общий регион из Steam";
                    if (source == "epic" && quote.offers.Count == 1 && !string.IsNullOrEmpty(own.country)) {
                        try { quote.promotions = MatchPromotions((await EpicPromotions(own.country, force, ct).ConfigureAwait(false)).body, quote.offers[0].productKey, DateTime.UtcNow); }
                        catch { ct.ThrowIfCancellationRequested(); quote.promotionMessage = "Календарь акций Epic сейчас недоступен."; }
                    }
                    return quote;
                } catch (Exception e) { ct.ThrowIfCancellationRequested(); return new PriceSourceResult { source = source, status = "error", message = Failure(e), url = source == "funpay" ? "https://funpay.com/" : source == "plati" ? "https://plati.market/" : StoreUrl(source, game.name) }; }
            }).ToArray();
            var fxTask = Get("https://www.cbr.ru/scripts/XML_daily.asp", force, ct, 360);
            result.sources.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));
            result.calendar = await calendarTask.ConfigureAwait(false);
            PriceFx fx = null;
            try { fx = PriceFx.Parse((await fxTask.ConfigureAwait(false)).body, DateTime.UtcNow); result.fxDate = fx.date.ToString("yyyy-MM-dd"); }
            catch (Exception) { ct.ThrowIfCancellationRequested(); result.fxError = "Курс недоступен. Показана только исходная цена, пересчёт не подставляется."; }
            foreach (var offer in result.sources.SelectMany(s => s.offers))
            {
                ApplyFx(offer, fx);
            }
            ct.ThrowIfCancellationRequested();
            history.Observe(result.sources.SelectMany(x => x.offers).ToList(), DateTime.UtcNow);
            return result;
        }
        public static void ApplyFx(PriceOffer offer, PriceFx fx)
        {
            if (offer?.amount == null) return;
            offer.rub = offer.currency == "RUB" ? offer.amount : fx?.Convert(offer.amount.Value, offer.currency, "RUB");
            offer.kzt = offer.currency == "KZT" ? offer.amount : fx?.Convert(offer.amount.Value, offer.currency, "KZT");
            if (offer.amount == 0) offer.rub = offer.kzt = 0;
        }
        public void Dispose() { http.Dispose(); }
    }
}
