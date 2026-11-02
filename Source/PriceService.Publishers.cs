using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Parser.Html;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Orbit
{
    public sealed partial class PriceService
    {
        static bool Publisher(string store) => store == "ea" || store == "ubisoft";
        static bool PublisherId(string id, string store) => Regex.IsMatch(id ?? "", store == "ea"
            ? @"\A[A-Za-z0-9][A-Za-z0-9._:-]{1,149}\z" : @"\A[a-f0-9]{24}\z");
        static bool Trial(string title) => Regex.IsMatch(Normalize(title),
            @"\b(trial|demo|beta|friends? pass|friend s pass|free weekend|пробная|пробный|демоверсия)\b");
        static string EditionName(string name, string edition) =>
            new[] { "standard edition", "standard", "base game", "base games" }.Contains(Normalize(edition))
            || Normalize(name).EndsWith(Normalize(edition), StringComparison.Ordinal) ? name : name + " — " + edition;

        static string PublisherLink(string value, string store)
        {
            Uri uri;
            var host = store == "ea" ? "store.ea.com" : "store.ubisoft.com";
            if (!Uri.TryCreate(new Uri("https://" + host), value, out uri) || !SafeUrl(uri.AbsoluteUri) || uri.Host != host) return null;
            if (store == "ea" && !Regex.IsMatch(uri.AbsolutePath, @"\A/commerce/app/products/[a-z0-9-]{1,120}\z")) return null;
            if (store == "ubisoft" && !Regex.IsMatch(uri.AbsolutePath, @"\A/[a-z]{2}/[a-z0-9%_./-]+/[a-f0-9]{24}\.html\z", RegexOptions.IgnoreCase)) return null;
            return uri.GetLeftPart(UriPartial.Path);
        }
        static string PublisherSearchUrl(string store, string query, string country)
        {
            if (store == "ea") return "https://store.ea.com/commerce/app/products?search=" + Query(query);
            var routes = new Dictionary<string, string> {
                ["US"]="us", ["CA"]="ca", ["GB"]="uk", ["DE"]="de", ["FR"]="fr", ["PL"]="pl",
                ["TR"]="tr", ["JP"]="jp", ["KR"]="kr", ["AU"]="au", ["IT"]="it", ["ES"]="es"
            };
            string route; if (!routes.TryGetValue(country ?? "", out route)) route = "eu";
            // A route is only a catalogue hint, never proof of the returned price's country.
            return "https://store.ubisoft.com/" + route + "/search?q=" + Query(query);
        }
        static JObject PublisherJson(string text)
        {
            using (var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 40, DateParseHandling = DateParseHandling.None })
                return JObject.Load(reader);
        }
        static JObject UbisoftAssignment(AngleSharp.Dom.IDocument doc, string pattern, bool required)
        {
            foreach (var script in doc.QuerySelectorAll("script:not([src])"))
            {
                var match = Regex.Match(script.TextContent, pattern);
                if (match.Success) return PublisherJson(script.TextContent.Substring(match.Index + match.Length));
            }
            if (required) throw new InvalidDataException("Не подтверждён регион витрины Ubisoft.");
            return null;
        }
        static JObject UbisoftContext(AngleSharp.Dom.IDocument doc) => UbisoftAssignment(doc, @"\bvar\s+wa_data\s*=\s*(?=\{)", true);
        static string PublisherCountry(JToken value) => value?.Type == JTokenType.String
            && Regex.IsMatch((string)value, @"\A[A-Za-z]{2}\z") ? ((string)value).ToUpperInvariant() : "";
        static string RegionReason(string actual, string requested) =>
            "Магазин ответил для " + (string.IsNullOrEmpty(actual) ? "неизвестной страны" : actual) + ", запрошено " + requested
            + ". Публичная витрина не подтвердила цену для выбранной страны.";

        public static List<PriceProduct> ParsePublisherCatalog(string html, string store)
        {
            if (!Publisher(store)) throw new ArgumentException("Неизвестная витрина.");
            var doc = new HtmlParser().Parse(html); var result = new List<PriceProduct>();
            if (store == "ea")
            {
                if (doc.QuerySelector("#eax-sal-config") == null) throw new InvalidDataException();
                foreach (var tile in doc.QuerySelectorAll("article.tile"))
                {
                    var url = PublisherLink(tile.QuerySelector("a.tile-link")?.GetAttribute("href"), store);
                    var name = Text(tile.QuerySelector("h3, h2, .tile-title")?.TextContent, 180);
                    if (url == null || name.Length == 0 || Trial(name)) continue;
                    result.Add(new PriceProduct { store=store, id=new Uri(url).Segments.Last(), name=name,
                        image=tile.QuerySelector("img.tile-poster")?.GetAttribute("src"), url=url,
                        productType="Каталог EA · издание проверяется" });
                }
            }
            else
            {
                UbisoftContext(doc);
                foreach (var tile in doc.QuerySelectorAll(".product-tile"))
                {
                    var id = tile.QuerySelector("[data-pcdl-productid]")?.GetAttribute("data-pcdl-productid");
                    var url = PublisherLink(tile.QuerySelector("a.thumb-link")?.GetAttribute("href"), store);
                    var availability = tile.QuerySelector(".product-availability-label");
                    if (!PublisherId(id, store) || url == null || availability == null
                        || availability.GetAttribute("data-dlc") == "true" || availability.GetAttribute("data-ismobileproduct") == "true") continue;
                    var name = Text(tile.QuerySelector(".prod-title")?.TextContent, 180);
                    var edition = Text(tile.QuerySelector(".card-subtitle")?.TextContent, 100);
                    if (name.Length == 0 || edition.Length == 0 || Trial(name + " " + edition)) continue;
                    url = Regex.Replace(url, @"[a-f0-9]{24}\.html$", id + ".html", RegexOptions.IgnoreCase);
                    var img = tile.QuerySelector(".product-image img");
                    result.Add(new PriceProduct { store=store, id=id, name=EditionName(name, edition),
                        image=img?.GetAttribute("data-src") ?? img?.GetAttribute("src"), url=url, productType="PC · " + edition });
                }
            }
            return result.GroupBy(p => p.id).Select(g => g.First()).Take(30).ToList();
        }

        public static List<PriceProduct> ParseEaProduct(string html, string url, string country, DateTime at)
        {
            country = Country(country);
            var canonical = PublisherLink(url, "ea"); if (canonical == null) throw new InvalidDataException();
            var doc = new HtmlParser().Parse(html);
            var config = doc.QuerySelector("#eax-sal-config"); if (config == null) throw new InvalidDataException();
            var actual = PublisherCountry(PublisherJson(config.TextContent)["country"]);
            var name = Text(doc.QuerySelector("h1")?.TextContent, 180);
            var buy = doc.QuerySelector("#pdp-buy-btn[data-game-type='BASE_GAME']");
            if (name.Length == 0 || buy == null || !doc.QuerySelectorAll(".pdp-sysreq-value")
                .Any(e => Regex.IsMatch(e.TextContent, @"\bWindows\b", RegexOptions.IgnoreCase))) return new List<PriceProduct>();
            var offers = new List<Tuple<string,string,string,string,string>>();
            foreach (var label in doc.QuerySelectorAll("label.pdp-edition"))
            {
                var input = label.QuerySelector("input[name='pdp-edition']"); if (input == null || input.HasAttribute("disabled")) continue;
                offers.Add(Tuple.Create(input.GetAttribute("value"), Text(label.QuerySelector(".pdp-edition-name")?.TextContent, 100),
                    input.GetAttribute("data-price"), input.GetAttribute("data-currency"), input.GetAttribute("data-list")));
            }
            if (offers.Count == 0 && doc.QuerySelector("label.pdp-edition") == null)
            {
                // Extract the ID only. Never request a buy/checkout endpoint.
                var id = Regex.Match(buy.GetAttribute("href") ?? "", @"[?&]offerId=([^&#]+)");
                foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
                {
                    JObject json; try { json = PublisherJson(script.TextContent); } catch { continue; }
                    var nodes = json["@graph"] as JArray ?? new JArray(json);
                    foreach (var game in nodes.Where(n => (string)n["@type"] == "VideoGame" && Normalize((string)n["name"]) == Normalize(name)))
                    {
                        var offer = game["offers"] as JObject;
                        if (id.Success && offer != null && (string)offer["@type"] == "Offer"
                            && ((string)offer["availability"] ?? "").EndsWith("/InStock", StringComparison.Ordinal))
                            offers.Add(Tuple.Create(Uri.UnescapeDataString(id.Groups[1].Value),
                                Text((string)offer["name"] ?? doc.QuerySelector(".pdp-edition-single")?.TextContent, 100),
                                (string)offer["price"], (string)offer["priceCurrency"], ""));
                    }
                }
            }
            var result = new List<PriceProduct>();
            foreach (var offer in offers)
            {
                if (!PublisherId(offer.Item1, "ea") || offer.Item2.Length == 0 || Trial(offer.Item2)) continue;
                var item = new PriceProduct { store="ea", id=offer.Item1, catalogNamespace=new Uri(canonical).Segments.Last(),
                    name=EditionName(name, offer.Item2), url=canonical + "?offerId=" + Query(offer.Item1) + "#pdp-editions",
                    image=doc.QuerySelector("meta[property='og:image']")?.GetAttribute("content"), productType="PC · " + offer.Item2 };
                decimal amount, initial;
                if (actual != country) { item.priceMessage = RegionReason(actual, country); item.priceStatus = "region_mismatch"; }
                else if (!Number(offer.Item3, out amount) || amount < 0 || amount >= 1000000000m
                    || !Regex.IsMatch(offer.Item4 ?? "", @"\A[A-Z]{3}\z")) item.priceMessage = "Цена издания не подтверждена.";
                else if (amount == 0 && !new[] { "standard edition", "standard", "base game", "base games", "full game" }.Contains(Normalize(offer.Item2)))
                    item.priceMessage = "Бесплатное предложение не подтвердило полную версию игры. Проверь тип издания в EA.";
                else
                {
                    item.preview = Official("ea", item.name, item.url, actual, offer.Item4, amount,
                        Number(offer.Item5, out initial) && initial >= amount && initial < 1000000000m ? initial : (decimal?)null, at);
                    item.preview.edition=offer.Item2; item.preview.platform="PC"; item.preview.free=amount==0;
                    item.preview.note="Публичная витрина EA · " + actual + ". Проверь выбранное издание в EA. Налоги и персональные условия могут изменить итог.";
                }
                result.Add(item);
            }
            return result.GroupBy(p => p.id).Select(g => g.First()).ToList();
        }

        public static PriceProduct ParseUbisoftProduct(string html, string url, string country, string id, DateTime at)
        {
            country = Country(country);
            var canonical = PublisherLink(url, "ubisoft");
            if (canonical == null || !PublisherId(id, "ubisoft") || !canonical.EndsWith("/" + id + ".html", StringComparison.Ordinal)) throw new InvalidDataException();
            var doc = new HtmlParser().Parse(html); var context = UbisoftContext(doc); var p = context["product"];
            if (p == null || (string)p["productID"] != id || (string)p["platform"] != "pcdl"
                || (string)p["productType"] != "game" || (string)p["productDemo"] != "no" || (string)p["productFreeWeekEnd"] != "no") return null;
            var name = Text((string)p["gameName"], 180); var edition = Text((string)p["editionName"], 100);
            if (name.Length == 0 || edition.Length == 0 || Trial(name + " " + edition)) return null;
            var item = new PriceProduct { store="ubisoft", id=id, catalogNamespace="pcdl", name=EditionName(name, edition), url=canonical, productType="PC · " + edition };
            var actual = PublisherCountry(context["country"]);
            if (actual != country) {
                item.priceMessage=RegionReason(actual,country); item.priceStatus="region_mismatch";
                // Keep a real foreign storefront quote visible, with its original region.
                // The second parse runs every edition, availability and price consistency check.
                if (!string.IsNullOrEmpty(actual)) {
                    try {
                        var reference=ParseUbisoftProduct(html,canonical,Country(actual),id,at);
                        if (reference?.preview?.amount!=null) {
                            reference.preview.reference=true;
                            reference.preview.note="Справочная цена витрины "+actual+". Цена и доступность для "+country+" не подтверждены; проверь регион в Ubisoft.";
                            reference.priceMessage=reference.preview.note; reference.priceStatus="reference";
                            return reference;
                        }
                    } catch (ArgumentException) { }
                }
                return item;
            }
            if ((string)p["availability"] != "available" || (string)p["salesType"] != "order")
            { item.priceMessage="Это издание сейчас не продаётся отдельно в выбранной витрине."; return item; }
            // Prefer the exact variant's live buy-selector model. Search-engine JSON-LD
            // may carry yesterday's validity date even when the visible store price is current.
            var model = UbisoftAssignment(doc, @"\bwindow\.extendedProductModel\s*=\s*(?=\{)", false);
            if (model != null)
            {
                var sales = model.SelectToken("priceObject.price.sales");
                var amount = Amount(sales?["value"]); var currency = (string)sales?["currency"];
                bool flags = (string)model.SelectToken("details.ID") == id
                    && (bool?)model.SelectToken("details.isDigital") == true
                    && (bool?)model.SelectToken("details.isMasterProduct") == false
                    && (bool?)model.SelectToken("details.isOnlineProduct") == true
                    && (bool?)model.SelectToken("availability.addToCartPossible") == true
                    && (bool?)model.SelectToken("availability.isOutOfStock") == false
                    && (bool?)model.SelectToken("priceObject.priceOptions.isRangePrice") == false
                    && (bool?)model.SelectToken("priceObject.priceOptions.showFromLabel") == false
                    && (bool?)model.SelectToken("dlc.isDlcType") == false
                    && new[] { "isDemo", "isOpenBeta", "isFreeWeekend", "isFreeWithPurchase" }.All(key => (bool?)model.SelectToken("offer." + key) == false);
                if (!flags || amount == null || amount != Amount(p["unitSalePrice"]) || amount != Amount(sales?["decimalPrice"])
                    || currency != (string)context["currency"] || !Regex.IsMatch(currency ?? "", @"\A[A-Z]{3}\z")
                    || amount == 0 && (bool?)model.SelectToken("offer.isFreeToPlay") != true && (bool?)model.SelectToken("offer.isGiveaway") != true)
                { item.priceMessage="Точное PC-издание, доступность или цена в карточке Ubisoft не подтверждены."; return item; }
                item.preview=Official("ubisoft", item.name, canonical, actual, currency, amount,
                    Amount(p["originalUnitSalePrice"]) >= amount ? Amount(p["originalUnitSalePrice"]) : null, at);
                item.preview.edition=edition; item.preview.platform="PC"; item.preview.free=amount==0;
                item.image=(string)model.SelectToken("media.edition_packshot[0].url"); return item;
            }
            foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
            {
                JObject json; try { json=PublisherJson(script.TextContent); } catch { continue; }
                if ((string)json["@type"] != "Product" || Normalize((string)json["name"]) != Normalize(name)
                    || PublisherLink((string)json["url"], "ubisoft") != canonical) continue;
                var list=json["offers"] as JArray ?? (json["offers"] is JObject ? new JArray(json["offers"]) : new JArray());
                if (list.Count != 1) continue;
                var offer=list[0]; DateTime validUntil;
                if (offer["priceValidUntil"] != null && (!DateTime.TryParseExact((string)offer["priceValidUntil"], "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out validUntil) || validUntil.Date < at.Date)) continue;
                var currency=(string)offer["priceCurrency"]; var amount=Amount(offer["price"]);
                if ((string)offer["@type"] != "Offer" || (string)offer["seller"] != "Ubisoft"
                    || !((string)offer["availability"] ?? "").EndsWith("/InStock", StringComparison.Ordinal)
                    || !Regex.IsMatch(currency ?? "", @"\A[A-Z]{3}\z") || currency != (string)context["currency"]
                    || amount == null || amount != Amount(p["unitSalePrice"])) continue;
                item.image=(string)json["image"];
                item.preview=Official("ubisoft", item.name, canonical, actual, currency, amount,
                    Amount(p["originalUnitSalePrice"]) >= amount ? Amount(p["originalUnitSalePrice"]) : null, at);
                item.preview.edition=edition; item.preview.platform="PC"; item.preview.free=amount==0; break;
            }
            if (item.preview == null) item.priceMessage="Цена или валюта издания не подтверждена ответом Ubisoft.";
            return item;
        }

        static bool EaSlug(string value) => Regex.IsMatch(value ?? "", @"\A[a-z0-9][a-z0-9-]{0,119}\z");

        // This is the regional-pricing parameter used by EA's public website itself.
        // It is a price lookup only; no session, authentication or checkout request is made.
        static string EaRegionalUrl(string slug, string country) => "https://drop-api.ea.com/game/" + slug
            + "?locale=en&override-country-code=" + Country(country);

        static decimal? EaDisplayAmount(JToken value)
        {
            if (value?.Type != JTokenType.String) return null;
            var text=(string)value;
            if (text.Length>80 || Regex.IsMatch(text, @"[-−]")) return null;
            var number=Regex.Replace(text, @"[^0-9.,]", "");
            if (number.Length==0) return null;
            int comma=number.LastIndexOf(','), dot=number.LastIndexOf('.');
            if (comma>=0 && dot>=0) {
                char dec=comma>dot?',':'.'; number=number.Replace(dec==','?".":",", "");
                if (number.Count(c=>c==dec)!=1) return null;
                number=number.Replace(dec,'.');
            } else if (comma>=0 || dot>=0) {
                char separator=comma>=0?',':'.'; int last=number.LastIndexOf(separator);
                int decimals=number.Length-last-1;
                if (decimals==1 || decimals==2) number=number.Substring(0,last).Replace(separator.ToString(),"")+"."+number.Substring(last+1);
                else if (decimals==3) number=number.Replace(separator.ToString(),"");
                else return null;
            }
            decimal amount; return Number(number,out amount) && amount>=0 && amount<1000000000m ? amount : (decimal?)null;
        }

        public static List<PriceProduct> ParseEaRegionalProduct(string json, string slug, string expectedName, string country, DateTime at)
        {
            country=Country(country);
            if (!EaSlug(slug)) throw new InvalidDataException();
            var data=PublisherJson(json); var name=Text((string)data["name"],180);
            var franchise=(string)data.SelectToken("franchise.slug");
            if ((string)data["slug"]!=slug || !EaSlug(franchise) || Normalize(name)!=Normalize(expectedName)
                || Trial(name) || (bool?)data["isPurchasableGame"]!=true || (bool?)data["isCMSDataOnly"]!=false
                || !((data["supportedPlatforms"] as JArray)??new JArray()).Any(p=>(string)p["slug"]=="pc")) return new List<PriceProduct>();
            var platforms=((data["platformDetails"] as JArray)??new JArray())
                .Where(p=>(string)p["slug"]=="EA-APP" && (bool?)p["isThirdParty"]==false).ToList();
            if (platforms.Count!=1) return new List<PriceProduct>();
            var pcEditions=(platforms[0]["editions"] as JArray)??new JArray();
            var url="https://www.ea.com/games/"+franchise+"/"+slug+"/buy";
            var result=new List<PriceProduct>();
            foreach(var edition in (data["editions"] as JArray)??new JArray()) {
                var id=(string)edition["checkoutId"]; var editionSlug=(string)edition["slug"];
                var editionName=Text((string)edition["editionName"],100);
                if (!PublisherId(id,"ea") || !EaSlug(editionSlug) || editionName.Length==0 || Trial(editionName)
                    || (bool?)edition["acquirable"]!=true) continue;
                var same=pcEditions.Where(p=>(string)p["checkoutId"]==id && (string)p["slug"]==editionSlug).ToList();
                if (same.Count!=1) continue;
                var price=edition["price"]; var pcPrice=same[0]["price"];
                var currency=(string)price?["currency"]; var amount=EaDisplayAmount(price?["displayTotalWithDiscount"]);
                var initial=EaDisplayAmount(price?["displayTotal"]);
                if (!Regex.IsMatch(currency??"",@"\A[A-Z]{3}\z") || currency!=(string)pcPrice?["currency"]
                    || amount==null || initial==null || initial<amount || amount!=EaDisplayAmount(pcPrice?["displayTotalWithDiscount"])
                    || initial!=EaDisplayAmount(pcPrice?["displayTotal"]) || amount==0 && (bool?)edition["isFree"]!=true) continue;
                var item=new PriceProduct { store="ea",id=id,catalogNamespace="drop:"+slug,name=EditionName(name,editionName),
                    url=url,image=(string)edition.SelectToken("packArt.ar2X1")??(string)data.SelectToken("packArt.ar2X1"),productType="PC · "+editionName };
                item.preview=Official("ea",item.name,url,country,currency,amount,initial,at);
                item.preview.edition=editionName; item.preview.platform="PC"; item.preview.free=amount==0;
                item.preview.productKey="ea:"+item.catalogNamespace+":"+id;
                item.preview.note="Официальная витрина EA, запрос цен для "+country+". На странице игры выбери «"+editionName+"»; итоговую цену и доступность проверь в EA.";
                result.Add(item);
            }
            return result.GroupBy(p=>p.id).Select(g=>g.First()).ToList();
        }

        async Task<List<PriceProduct>> EaRegionalProducts(string slug, string name, string country, bool force, CancellationToken ct)
        {
            if (!EaSlug(slug)) return new List<PriceProduct>();
            var raw=await Get(EaRegionalUrl(slug,country),force,ct).ConfigureAwait(false);
            return ParseEaRegionalProduct(raw.body,slug,name,country,raw.at);
        }

        async Task<PriceSearchResult> PublisherSearch(string store, string query, string country, bool force, CancellationToken ct)
        {
            var raw=await Get(PublisherSearchUrl(store, query, country), force, ct).ConfigureAwait(false);
            var candidates=ParsePublisherCatalog(raw.body, store).Where(p => Normalize(p.name).Contains(Normalize(query))
                || Normalize(query).Contains(Normalize(p.name))).Take(6).ToList();
            using (var limit=new SemaphoreSlim(3))
            using (var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(40));
                var tasks=candidates.Select(async candidate => {
                    bool entered=false;
                    try
                    {
                        await limit.WaitAsync(deadline.Token).ConfigureAwait(false); entered=true;
                        var page=await Get(candidate.url, force, deadline.Token).ConfigureAwait(false);
                        if (store=="ea") {
                            var editions=ParseEaProduct(page.body,candidate.url,country,page.at);
                            if (editions.Any(p=>p.priceStatus=="region_mismatch")) {
                                try {
                                    var regional=await EaRegionalProducts(candidate.id,candidate.name,country,force,deadline.Token).ConfigureAwait(false);
                                    if (regional.Count>0) return regional;
                                } catch { deadline.Token.ThrowIfCancellationRequested(); }
                            }
                            return editions;
                        }
                        var p=ParseUbisoftProduct(page.body, candidate.url, country, candidate.id, page.at);
                        if (p != null && string.IsNullOrEmpty(p.image)) p.image=candidate.image;
                        return p==null ? new List<PriceProduct>() : new List<PriceProduct> {p};
                    }
                    catch (Exception e)
                    {
                        ct.ThrowIfCancellationRequested(); candidate.priceMessage="Не удалось проверить издание. " + Failure(e); candidate.priceStatus="error";
                        return new List<PriceProduct> {candidate};
                    }
                    finally { if (entered) limit.Release(); }
                }).ToArray();
                var rows=await Task.WhenAll(tasks).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
                var result=new PriceSearchResult {store=store, country=country,
                    products=rows.SelectMany(x=>x).GroupBy(p=>p.id).Select(g=>g.First()).Take(20).ToList()};
                foreach (var p in result.products)
                    if (p.image != null && Uri.TryCreate(new Uri("https://" + (store=="ea" ? "store.ea.com" : "store.ubisoft.com")), p.image, out var image)
                        && image.Scheme=="https") p.image=image.AbsoluteUri; else p.image=null;
                result.message=(result.products.Any(p=>p.preview?.amount!=null) ? null : result.products.Select(p=>p.priceMessage).FirstOrDefault(x=>!string.IsNullOrEmpty(x)))
                    ?? (result.products.Count==0 ? "Подтверждённых PC-изданий не найдено. Попробуй оригинальное название."
                    : "Первые совпадения официального каталога. Выбери точное PC-издание; при необходимости уточни запрос.");
                return result;
            }
        }

        async Task<PriceSourceResult> PublisherQuote(PriceProduct game, PriceAccount account, bool force, CancellationToken ct)
        {
            var country=Country(account.country);
            var result=new PriceSourceResult {source=game.store, status="unavailable", url=PublisherSearchUrl(game.store, game.name, country)};
            List<PriceProduct> found; var canonical=PublisherLink(game.url, game.store);
            if (game.store=="ea" && (game.catalogNamespace??"").StartsWith("drop:",StringComparison.Ordinal))
            {
                var slug=game.catalogNamespace.Substring(5);
                // The regional response carries the base game name as well as each exact edition.
                var raw=await Get(EaRegionalUrl(slug,country),force,ct).ConfigureAwait(false);
                var title=(string)PublisherJson(raw.body)["name"];
                found=ParseEaRegionalProduct(raw.body,slug,title,country,raw.at);
            }
            else if (canonical != null && !string.IsNullOrEmpty(game.catalogNamespace))
            {
                var page=await Get(canonical, force, ct).ConfigureAwait(false);
                if (game.store=="ea") found=ParseEaProduct(page.body, canonical, country, page.at);
                else { var p=ParseUbisoftProduct(page.body, canonical, country, game.id, page.at); found=p==null ? new List<PriceProduct>() : new List<PriceProduct> {p}; }
            }
            else found=(await PublisherSearch(game.store, game.name, country, force, ct).ConfigureAwait(false)).products;
            var exact=found.Where(p=>p.id==game.id).ToList();
            if (exact.Count != 1)
            { result.message="ID библиотеки не подтвердил точное PC-издание. Выбери издание в поиске этой витрины; похожая игра не подставляется."; return result; }
            result.url=exact[0].url; result.match="id";
            if (exact[0].preview?.amount==null) { result.status=exact[0].priceStatus??"price_unavailable"; result.message=exact[0].priceMessage ?? "Нет подтверждённой цены этого издания."; return result; }
            var offer=exact[0].preview; CheckCurrency(offer, account); offer.productKey=game.store+":"+exact[0].catalogNamespace+":"+game.id;
            result.offers.Add(offer); result.status="ok"; result.match="id";
            result.message=offer.reference?offer.note:"Подтверждены PC-издание и страна публичной витрины. Итог покупки проверь в магазине.";
            return result;
        }
    }
}
