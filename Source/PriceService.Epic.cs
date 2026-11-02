using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Orbit
{
    public sealed partial class PriceService
    {
        // Public storefront, independent of the local launcher. Schema failure must not become a zero price.
        static string EpicFields(string country) => "id title namespace offerType productSlug urlSlug " +
            "catalogNs { mappings(pageType: \"productHome\") { pageSlug pageType } } offerMappings { pageSlug pageType } " +
            "keyImages { type url } price(country: " + JsonConvert.SerializeObject(country) + ") { " +
            "totalPrice { originalPrice discountPrice currencyCode currencyInfo { decimals } } lineOffers { appliedRules { endDate } } }";

        Task<Cached> EpicCatalog(string country, string query, bool sale, bool force, CancellationToken ct)
        {
            var q = "query { Catalog { searchStore(country: " + JsonConvert.SerializeObject(country) + ", locale: \"en-US\", " +
                (sale ? "onSale: true" : "keywords: " + JsonConvert.SerializeObject(query)) + ", count: 20) { elements { " + EpicFields(country) + " } } } }";
            return Get("https://store.epicgames.com/graphql?query=" + Query(q), force, ct);
        }

        Task<Cached> EpicOffer(PriceProduct game, string country, bool force, CancellationToken ct)
        {
            var q = "query { Catalog { catalogOffer(namespace: " + JsonConvert.SerializeObject(game.catalogNamespace) +
                ", id: " + JsonConvert.SerializeObject(game.id) + ", locale: \"en-US\") { " + EpicFields(country) + " } } }";
            return Get("https://store.epicgames.com/graphql?query=" + Query(q), force, ct);
        }

        static bool EpicUnavailable(Exception e, CancellationToken ct)
        {
            // HttpClient uses cancellation exceptions for its own timeout too. User cancellation must still stop both sources.
            if (ct.IsCancellationRequested) return false;
            if (e is OperationCanceledException) return true;
            if (!(e is HttpRequestException)) return false;
            var status = Regex.Match(e.Message ?? "", @"HTTP (\d{3})");
            if (!status.Success) return true; // Transport failure or the shared rate-limit message.
            var code = int.Parse(status.Groups[1].Value, CultureInfo.InvariantCulture);
            return code == 403 || code == 408 || code == 429 || code >= 500 && code <= 599;
        }

        async Task<Cached> EpicCatalogAvailable(string country, string query, bool sale, bool force, CancellationToken ct)
        {
            try { return await EpicCatalog(country, query, sale, force, ct).ConfigureAwait(false); }
            catch (Exception e) when (EpicUnavailable(e, ct))
            {
                ct.ThrowIfCancellationRequested();
                var url = "https://api.egdata.app/search/v2/search?country=" + country + "&locale=en-US";
                var body = sale ? JsonConvert.SerializeObject(new { onSale = true, page = 1, limit = 40, sortBy = "discountPercent", sortDir = "desc" })
                    : JsonConvert.SerializeObject(new { title = query, page = 1, limit = 40 });
                var key = url + "\n" + body;
                Cached saved;
                lock (cacheLock) if (!force && cache.TryGetValue(key, out saved) && DateTime.UtcNow - saved.at < TimeSpan.FromMinutes(5)) return saved;
                using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
                using (var response = await http.PostAsync(url, content, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("Запасной каталог Epic не ответил (HTTP " + (int)response.StatusCode + ").");
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Слишком большой ответ каталога Epic.");
                    saved = new Cached { body = Encoding.UTF8.GetString(bytes), at = DateTime.UtcNow };
                    ct.ThrowIfCancellationRequested();
                    lock (cacheLock) { if (cache.Count > 120) cache.Clear(); cache[key] = saved; }
                    return saved;
                }
            }
        }

        async Task<Cached> EpicOfferAvailable(PriceProduct game, string country, bool force, CancellationToken ct)
        {
            try { return await EpicOffer(game, country, force, ct).ConfigureAwait(false); }
            catch (Exception e) when (EpicUnavailable(e, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (!Regex.IsMatch(game.id ?? "", "^[a-fA-F0-9]{32}$")) throw;
                var baseUrl = "https://api.egdata.app/offers/" + game.id;
                var offer = JObject.Parse((await Get(baseUrl + "?country=" + country + "&locale=en-US", force, ct).ConfigureAwait(false)).body);
                var price = JObject.Parse((await Get(baseUrl + "/price?country=" + country, force, ct).ConfigureAwait(false)).body);
                offer["price"] = price;
                return new Cached { body = offer.ToString(Formatting.None), at = DateTime.UtcNow };
            }
        }

        static JObject EpicResponse(string json)
        {
            var root = JObject.Parse(json);
            // Epic can return valid products alongside a 404 for one product's page mapping.
            // Isolate errors with an exact product path; unscoped/schema errors still fail the request.
            foreach (var error in (root["errors"] as JArray) ?? new JArray())
            {
                var path = error["path"] as JArray;
                JToken product = null; string field = null;
                if (path?.Count >= 5 && (string)path[0] == "Catalog" && (string)path[1] == "searchStore" && (string)path[2] == "elements" && path[3].Type == JTokenType.Integer)
                {
                    var items = root.SelectToken("data.Catalog.searchStore.elements") as JArray;
                    var index = (int)path[3];
                    if (items != null && index >= 0 && index < items.Count) { product = items[index]; field = (string)path[4]; }
                }
                else if (path?.Count >= 3 && (string)path[0] == "Catalog" && (string)path[1] == "catalogOffer")
                { product = root.SelectToken("data.Catalog.catalogOffer"); field = (string)path[2]; }
                if (product == null || !new[] { "offerMappings", "catalogNs", "keyImages", "productSlug", "urlSlug", "price" }.Contains(field))
                    throw new InvalidDataException("Epic не подтвердил данные витрины.");
                if (product is JObject) product[field] = null;
            }
            return root;
        }

        static bool EpicTitleMatches(string title, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            var words = Normalize(title).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var terms = Normalize(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            // EGData searches descriptions as well as names. Require every typed word in the title;
            // allow word prefixes for unfinished input, but never match "Hades" inside "Shades".
            return terms.Length > 0 && terms.All(term => words.Any(word => word == term ||
                term.Length >= 3 && word.StartsWith(term, StringComparison.Ordinal)));
        }

        public static List<PriceProduct> ParseEpicProducts(string json, string country, DateTime at, bool discountsOnly = false, string query = null)
        {
            var root = JObject.Parse(json);
            var mirror = root["offers"] is JArray;
            var elements = mirror ? root["offers"] as JArray : EpicResponse(json).SelectToken("data.Catalog.searchStore.elements") as JArray;
            if (elements == null) throw new InvalidDataException();
            if (mirror) elements = new JArray(elements.OfType<JObject>().GroupBy(p => Normalize((string)p["title"]) + ":" + (string)p["offerType"])
                .Select(g => g.OrderByDescending(p => ((p["offerMappings"] as JArray) ?? new JArray()).Any(m => new[] { "productHome", "offer" }.Contains((string)m["pageType"])))
                    .ThenByDescending(p => p.SelectToken("price.price.discountPrice") != null)
                    .ThenByDescending(p => (string)p["lastModifiedDate"]).First()));
            var products = elements.OfType<JObject>().Where(p => EpicTitleMatches((string)p["title"], query)).Select(p => EpicProduct(p, country, at)).Where(p => p != null && (!discountsOnly ||
                (p.preview?.amount != null && p.preview.originalAmount > p.preview.amount && (!p.preview.saleEndsAt.HasValue || p.preview.saleEndsAt > at))))
                .GroupBy(p => p.catalogNamespace + ":" + p.id).Select(g => g.First());
            return products.Take(20).ToList();
        }

        public static PriceProduct ParseEpicOffer(string json, string country, DateTime at) =>
            EpicProduct(JObject.Parse(json)["id"] != null ? JObject.Parse(json) : EpicResponse(json).SelectToken("data.Catalog.catalogOffer"), country, at);

        const string EpicPlatform = "https://egs-platform-service.store.epicgames.com/api/v1/egs/";

        // Use Windows' HTTP stack for the current Epic storefront. Its TLS transport works
        // with this public API on .NET Framework hosts where legacy HttpClient is rejected.
        // Each request is anonymous and isolated; redirects and Windows credential login are disabled.
        async Task<Cached> EpicPlatformGet(string url, bool force, CancellationToken ct)
        {
            if (!nativeEpicTransport) return await Get(url, force, ct, 1).ConfigureAwait(false);
            if (!url.StartsWith(EpicPlatform, StringComparison.Ordinal)) throw new InvalidOperationException("Источник не разрешён.");
            Cached saved;
            lock (cacheLock) if (!force && cache.TryGetValue(url, out saved) && DateTime.UtcNow - saved.at < TimeSpan.FromMinutes(1)) return saved;
            return await Task.Run(() => {
                ct.ThrowIfCancellationRequested();
                object instance = null;
                try
                {
                    instance = Activator.CreateInstance(Type.GetTypeFromProgID("WinHttp.WinHttpRequest.5.1", true));
                    dynamic request = instance;
                    request.Option[6] = false; // EnableRedirects
                    request.SetAutoLogonPolicy(2); // Never send Windows credentials.
                    request.SetTimeouts(2000, 3000, 3000, 6000);
                    request.Open("GET", url, true);
                    request.SetRequestHeader("User-Agent", "Orbit/0.19 (desktop price comparison)");
                    request.Send();
                    while (!(bool)request.WaitForResponse(1)) ct.ThrowIfCancellationRequested();
                    ct.ThrowIfCancellationRequested();
                    int status = (int)request.Status;
                    if (status < 200 || status >= 300) throw new HttpRequestException("Источник Epic не вернул данные (HTTP " + status + ").");
                    byte[] bytes = (byte[])request.ResponseBody;
                    if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Слишком большой ответ Epic.");
                    var value = new Cached { body = Encoding.UTF8.GetString(bytes), at = DateTime.UtcNow };
                    ct.ThrowIfCancellationRequested();
                    lock (cacheLock) { if (cache.Count > 120) cache.Clear(); cache[url] = value; }
                    return value;
                }
                catch (COMException e) { ct.ThrowIfCancellationRequested(); throw new HttpRequestException("Источник Epic не ответил.", e); }
                finally
                {
                    if (instance != null)
                    {
                        try { ((dynamic)instance).Abort(); } catch { }
                        Marshal.FinalReleaseComObject(instance);
                    }
                }
            }, ct).ConfigureAwait(false);
        }

        static PriceProduct EpicCopy(PriceProduct game) => new PriceProduct {
            token = game.token, store = game.store, id = game.id, name = game.name, image = game.image,
            url = game.url, productType = game.productType, catalogNamespace = game.catalogNamespace
        };

        static PriceProduct EpicUnconfirmed(PriceProduct game, string message, string status = "price_unavailable")
        {
            var result = EpicCopy(game); result.priceStatus = status; result.priceMessage = message; return result;
        }

        // The public platform API used by Epic's product pages returns current purchase states,
        // unlike a historical mirror record whose discount can already have ended.
        // A same-object return means the caller may still use its explicitly labelled fallback.
        public async Task<PriceProduct> EpicCurrentProduct(PriceProduct game, string country, bool force, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Uri page;
            if (game == null || game.store != "epic" || string.IsNullOrEmpty(game.id) || string.IsNullOrEmpty(game.catalogNamespace) ||
                !Uri.TryCreate(game.url, UriKind.Absolute, out page) || page.Scheme != "https" || page.Host != "store.epicgames.com" ||
                !page.IsDefaultPort || page.UserInfo.Length != 0) return game;
            var slug = Regex.Match(page.AbsolutePath, @"^/(?:[a-z]{2}-[A-Z]{2}/)?p/([a-zA-Z0-9_-]{1,240})/?$");
            if (!slug.Success || !Regex.IsMatch(country ?? "", "^[A-Z]{2}$")) return game;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    var suffix = "?country=" + country + "&locale=en-US";
                    var mapping = JObject.Parse((await EpicPlatformGet(EpicPlatform + "mappings/" + slug.Groups[1].Value + suffix, force, timeout.Token).ConfigureAwait(false)).body);
                    var productId = (string)mapping["productId"];
                    if (!Regex.IsMatch(productId ?? "", "^[a-zA-Z0-9_-]{1,100}$") || !(mapping.SelectToken("modules.pageOffers") is JArray)) return game;
                    if ((string)mapping["slug"] != slug.Groups[1].Value || (string)mapping["sandboxId"] != game.catalogNamespace ||
                        !((JArray)mapping.SelectToken("modules.pageOffers")).Any(o => (string)o["offerId"] == game.id &&
                            (string)o["sandboxId"] == game.catalogNamespace && (string)o["store"] == "EGS"))
                        return EpicUnconfirmed(game, "Страница Epic относится к другому предложению. Цена не подставлена.");
                    var raw = await EpicPlatformGet(EpicPlatform + "products/" + productId + "/offers/" + Query(game.id) + suffix + "&store=EGS", force, timeout.Token).ConfigureAwait(false);
                    return ParseEpicPlatformOffer(raw.body, game, country, raw.at, productId);
                }
                catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is JsonException || e is InvalidDataException)
                { ct.ThrowIfCancellationRequested(); return game; }
            }
        }

        public async Task<List<PriceProduct>> EpicCurrentProducts(List<PriceProduct> games, string country, bool force, CancellationToken ct)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            using (var slots = new SemaphoreSlim(3))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                var tasks = games.Select(async game => {
                    var entered = false;
                    try {
                        await slots.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
                        return await EpicCurrentProduct(game, country, force, deadline.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); return game; }
                    finally { if (entered) slots.Release(); }
                }).ToArray();
                return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
            }
        }

        static bool EpicDisplayedAmount(string display, out string currency, out decimal amount)
        {
            currency = null; amount = 0;
            // locale=en-US: accept an explicit ISO code and correctly grouped decimal number.
            // An unqualified "$" is ambiguous (USD/CAD/AUD/etc.) and must not become a guessed currency.
            var match = Regex.Match(display ?? "", @"^([A-Z]{3})[\s\u00a0]+((?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?:\.[0-9]{1,4})?)$");
            if (!match.Success || !decimal.TryParse(match.Groups[2].Value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out amount) || amount < 0 || amount >= 1000000000m) return false;
            currency = match.Groups[1].Value;
            var decimals = EpicMirrorDecimals(currency);
            return decimals.HasValue && decimal.Round(amount, decimals.Value) == amount;
        }

        public static PriceProduct ParseEpicPlatformOffer(string json, PriceProduct game, string country, DateTime at, string productId = null)
        {
            var p = JObject.Parse(json);
            if ((string)p["__typename"] != "Offer" || !(p["purchase"] is JArray)) throw new InvalidDataException("Epic не подтвердил формат предложения.");
            if (string.IsNullOrWhiteSpace((string)p["title"])) throw new InvalidDataException("Epic не подтвердил название предложения.");
            if ((string)p["id"] != game.id || (string)p["store"] != "EGS" || productId != null && (string)p["productId"] != productId)
                return EpicUnconfirmed(game, "Epic вернул другое издание. Цена не подставлена.");
            if (!((p.SelectToken("tags.platforms") as JArray) ?? new JArray()).Any(t => (string)t["id"] == "9547" || (string)t["name"] == "Windows"))
                return EpicUnconfirmed(game, "Это предложение Epic не подтверждено для Windows.");
            var slug = (string)p.SelectToken("mapping.slug");
            if ((string)p.SelectToken("mapping.__typename") != "InternalMapping" || !Regex.IsMatch(slug ?? "", "^[a-zA-Z0-9_-]{1,240}$"))
                throw new InvalidDataException("Epic не подтвердил страницу предложения.");
            var now = (long)(at.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var purchases = ((JArray)p["purchase"]).OfType<JObject>().Where(o =>
                (string)o.SelectToken("purchasePayload.offerId") == game.id && (string)o.SelectToken("purchasePayload.sandboxId") == game.catalogNamespace).ToList();
            if (purchases.Count == 0) return EpicUnconfirmed(game, "Epic не подтвердил цену нужного предложения.");
            if (purchases.Any(o => o["purchaseStateEffectiveDate"]?.Type != JTokenType.Integer || (long)o["purchaseStateEffectiveDate"] <= 0))
                throw new InvalidDataException("Epic не подтвердил время действия предложения.");
            var current = purchases.Where(o => o["purchaseStateEffectiveDate"]?.Type == JTokenType.Integer &&
                (long)o["purchaseStateEffectiveDate"] <= now).OrderByDescending(o => (long)o["purchaseStateEffectiveDate"]).FirstOrDefault();
            if (current == null) return EpicUnconfirmed(game, "Epic пока не открыл покупку этого предложения.");
            var result = EpicCopy(game); result.name = Text((string)p["title"]); result.url = "https://store.epicgames.com/p/" + slug;
            if ((string)current["status"] == "Unavailable")
                return EpicUnconfirmed(result, "Epic сейчас не предлагает покупку этого издания в выбранной стране.", "unavailable");
            // Free games use AvailableToClaim and omit the currency. Preserve the verified fallback
            // until that state can be represented without inventing a currency from the country.
            if ((string)current["status"] != "AvailableToPurchase") throw new InvalidDataException("Epic вернул другой тип получения игры.");
            string currency; decimal amount;
            if (!EpicDisplayedAmount((string)current["priceDisplay"], out currency, out amount))
                throw new InvalidDataException("Epic не подтвердил валюту цены.");
            decimal? original = null; DateTime? ends = null;
            if (current["discount"] is JObject)
            {
                string originalCurrency; decimal originalValue; DateTime end;
                if (!EpicDisplayedAmount((string)current.SelectToken("discount.originalPriceDisplay"), out originalCurrency, out originalValue) ||
                    originalCurrency != currency || originalValue < amount || !DateTime.TryParse((string)current.SelectToken("discount.discountEndDate"),
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out end))
                    throw new InvalidDataException("Epic не подтвердил условия скидки.");
                if (end <= at) return EpicUnconfirmed(result, "Скидка Epic уже закончилась. Новая цена ещё не подтверждена.", "expired");
                original = originalValue; ends = end;
            }
            result.preview = Official("epic", result.name, result.url, country, currency, amount, original, at);
            result.preview.provider = "Epic"; result.preview.free = amount == 0; result.preview.saleEndsAt = ends;
            result.preview.productKey = "epic:" + game.catalogNamespace + ":" + game.id;
            result.preview.note = "Текущая цена официальной витрины Epic Games для выбранной страны.";
            if ((string)current["offerLifecycleState"] == "PrePurchase") result.preview.note += " Предзаказ.";
            return result;
        }

        static int? EpicMirrorDecimals(string currency)
        {
            if (new[] { "JPY", "KRW", "CLP", "VND" }.Contains(currency)) return 0;
            if (new[] { "BHD", "KWD", "OMR", "JOD" }.Contains(currency)) return 3;
            if (new[] { "KZT", "RUB", "USD", "EUR", "GBP", "UAH", "TRY", "PLN", "CAD", "AUD", "BRL", "CNY", "INR", "MXN", "NZD", "ZAR", "AED" }.Contains(currency)) return 2;
            return null;
        }

        static PriceProduct EpicProduct(JToken p, string country, DateTime at)
        {
            if (!(p is JObject) || !new[] { "BASE_GAME", "EDITION", "DLC", "BUNDLE" }.Contains((string)p["offerType"]) || (bool?)p["isCodeRedemptionOnly"] == true) return null;
            var id = (string)p["id"]; var ns = (string)p["namespace"]; var name = Text((string)p["title"]);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(ns) || name.Length == 0) return null;
            // Offer mappings precede namespace mappings: editions can have different pages in the same namespace.
            var mapped = ((p["offerMappings"] as JArray) ?? new JArray()).Where(m => new[] { "productHome", "offer" }.Contains((string)m["pageType"]))
                .Concat(((p.SelectToken("catalogNs.mappings") as JArray) ?? new JArray()).Where(m => (string)m["pageType"] == "productHome"))
                .Select(m => (string)m["pageSlug"]);
            var slug = mapped.Concat(new[] { (string)p["productSlug"], (string)p["urlSlug"] })
                .Select(s => Regex.Replace(s ?? "", @"/home/?$", "")).FirstOrDefault(s => Regex.IsMatch(s, "^[a-zA-Z0-9_-]{1,240}$"));
            var url = slug == null ? StoreUrl("epic", name) : "https://store.epicgames.com/p/" + slug;
            var images = (p["keyImages"] as JArray) ?? new JArray();
            var art = images.FirstOrDefault(i => (string)i["type"] == "OfferImageWide") ?? images.FirstOrDefault(i => (string)i["type"] == "DieselStoreFrontWide") ?? images.FirstOrDefault();
            var type = (string)p["offerType"];
            var item = new PriceProduct { store = "epic", id = id, catalogNamespace = ns, name = name, image = (string)art?["url"], url = url,
                productType = type == "DLC" ? "Дополнение · DLC" : type == "EDITION" ? "Издание" : type == "BUNDLE" ? "Комплект" : "Игра" };
            var mirror = p.SelectToken("price.price") != null;
            var price = mirror ? p.SelectToken("price.price") : p.SelectToken("price.totalPrice");
            int decimals;
            var currency = (string)price?["currencyCode"];
            var amount = Amount(price?["discountPrice"]); var initial = Amount(price?["originalPrice"]);
            decimals = -1;
            if (mirror) decimals = EpicMirrorDecimals(currency) ?? -1;
            else if (!int.TryParse((string)price?.SelectToken("currencyInfo.decimals"), out decimals)) decimals = -1;
            if (!amount.HasValue)
            { item.priceStatus = "price_unavailable"; item.priceMessage = "Epic не вернул цену для этой страны. Проверь доступность на странице игры."; return item; }
            if (decimals < 0 || decimals > 4 || !Regex.IsMatch(currency ?? "", "^[A-Z]{3}$"))
            { item.priceStatus = "price_unavailable"; item.priceMessage = "Не удалось проверить валюту и формат цены Epic. Проверь цену на странице игры."; return item; }
            decimal divisor = 1; for (int i = 0; i < decimals; i++) divisor *= 10;
            item.preview = Official("epic", name, url, country, currency, amount / divisor, initial / divisor, at);
            if (mirror)
            {
                var recordCountry = (string)p.SelectToken("price.country");
                var recordRegion = (string)p.SelectToken("price.region");
                if (recordCountry != country && recordRegion != country)
                { item.preview = null; item.priceStatus = "region_mismatch"; item.priceMessage = "Каталог Epic не подтвердил цену для страны " + country + ". Проверь регион на странице игры."; return item; }
                if ((string)p.SelectToken("price.offerId") != id || (string)p.SelectToken("price.namespace") != ns)
                { item.preview = null; item.priceStatus = "price_unavailable"; item.priceMessage = "Цена каталога Epic относится к другому предложению. Проверь нужное издание на странице игры."; return item; }
                item.preview.provider = "EGData";
                item.preview.note = "Данные каталога EGData для Epic Games; перед покупкой проверь цену в Epic. Обновление записи: " + Text((string)p.SelectToken("price.updatedAt"), 30) + ".";
            }
            item.preview.free = amount == 0;
            item.preview.productKey = "epic:" + ns + ":" + id;
            var ends = new List<DateTime>();
            var rules = mirror ? (p.SelectToken("price.appliedRules") as JArray) ?? new JArray()
                : new JArray(((p.SelectToken("price.lineOffers") as JArray) ?? new JArray()).SelectMany(l => (l["appliedRules"] as JArray) ?? new JArray()));
            foreach (var rule in rules)
            {
                DateTime end;
                if (DateTime.TryParse((string)rule["endDate"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out end)) ends.Add(end);
            }
            if (initial > amount && ends.Count > 0) item.preview.saleEndsAt = ends.Min();
            // Cached expired discounts are not valid current quotes. Keep the product discoverable without a price.
            if (item.preview.saleEndsAt <= at)
            { item.preview = null; item.priceStatus = "expired"; item.priceMessage = "Скидка в данных Epic уже закончилась. Новая цена ещё не подтверждена — обнови её или открой страницу игры."; }
            return item;
        }
    }
}
