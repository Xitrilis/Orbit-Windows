using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
namespace Orbit
{
 public sealed class PriceDiscovery { public string store,country,message,fxDate,fxError; public SaleCalendar calendar; public List<PriceProduct> products=new List<PriceProduct>(); }
 public sealed partial class PriceService
 {
  readonly PriceHistoryStore history; SaleCalendar lastCalendar;
  public async Task<SaleCalendar> Calendar(bool force,CancellationToken ct) {
   SaleCalendar value;
   try{var raw=await Get(SteamSaleCalendar.Url,force,ct,360).ConfigureAwait(false);value=new SaleCalendar{events=SteamSaleCalendar.Parse(raw.body,raw.at),checkedAt=raw.at,message="Даты из Steamworks. Участие конкретной игры и размер скидки определяет издатель."};lock(cacheLock)lastCalendar=value;}
   catch{ct.ThrowIfCancellationRequested();lock(cacheLock)value=lastCalendar==null?SteamSaleCalendar.Bundled():new SaleCalendar{events=lastCalendar.events.ToList(),checkedAt=lastCalendar.checkedAt,cached=true,message="Не удалось обновить календарь. Показаны ранее полученные даты; проверь анонс магазина."};}
   var epic=new SaleEvent{source="epic",name="Halloween Sale 2026",start="2026-10-19",end="2026-11-02",scope="seasonal",checkedAt=new DateTime(2026,10,24,0,0,0,DateTimeKind.Utc),url="https://store.epicgames.com/news/epic-games-store-halloween-sale-buyers-guide-2026"};
   return new SaleCalendar{checkedAt=value.checkedAt,cached=value.cached,message=value.message,events=value.events.Concat(SteamSaleCalendar.Bundled().events.Where(x=>string.CompareOrdinal(x.end,DateTime.UtcNow.ToString("yyyy-MM-dd"))<0)).Concat(new[]{epic}).GroupBy(x=>x.source+x.name+x.start).Select(x=>x.First()).Where(x=>string.CompareOrdinal(x.end,DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd"))>=0).OrderBy(x=>x.start).ToList()};
  }
  async Task<PriceSourceResult> MatchingQuote(string store,PriceProduct game,PriceAccount account,bool force,CancellationToken ct) {
   var result=new PriceSourceResult{source=store,status="unavailable",url=StoreUrl(store,game.name)};
   if(string.IsNullOrEmpty(account.country)){result.message="Укажи страну аккаунта этого магазина в настройках цен.";return result;}
   var found=await Search(store,game.name,account,force,ct,false).ConfigureAwait(false);var matches=found.products.Where(p=>Normalize(p.name)==Normalize(game.name)).ToList();
   if(matches.Count!=1){result.message=matches.Count==0?"Точное название и издание не найдены. Похожие игры не подставляются.":"Несколько изданий с таким названием. Выбери нужное в поиске магазина.";return result;}
   if(store=="steam") result=await OfficialQuote(matches[0],account,force,ct).ConfigureAwait(false);
   else if(matches[0].preview?.amount!=null){result.offers.Add(matches[0].preview);result.url=matches[0].url;result.status="ok";}
   result.match="title";
   if(result.offers.Count>0)result.message="Совпало название. Состав издания проверь в магазине.";return result;
  }
  Task<Cached> EpicPromotions(string country,bool force,CancellationToken ct)=>Get("https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=en-US&country="+Country(country)+"&allowCountries="+Country(country),force,ct,15);
  public static List<PricePromotion> MatchPromotions(string json,string key,DateTime now) {
   var elements=JObject.Parse(json).SelectToken("data.Catalog.searchStore.elements") as JArray;if(elements==null)throw new System.IO.InvalidDataException();var result=new List<PricePromotion>();
   foreach(var p in elements.Where(p=>"epic:"+(string)p["namespace"]+":"+(string)p["id"]==key))
    foreach(var group in new[]{"promotionalOffers","upcomingPromotionalOffers"})
     foreach(var o in ((p.SelectToken("promotions."+group) as JArray)??new JArray()).SelectMany(g=>(g["promotionalOffers"] as JArray)??new JArray())) {
      DateTime from,to;if(!DateTime.TryParse((string)o["startDate"],CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out from)||!DateTime.TryParse((string)o["endDate"],CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out to)||to<=from||to<=now||from>now.AddYears(1))continue;
      // Epic uses a price multiplier here. Only explicitly free promotions get a future amount.
      bool free=(string)o.SelectToken("discountSetting.discountType")=="PERCENTAGE"&&(decimal?)o.SelectToken("discountSetting.discountPercentage")==0;
      result.Add(new PricePromotion{start=from,end=to,amount=free?0m:(decimal?)null,currency=(string)p.SelectToken("price.totalPrice.currencyCode"),url="https://store.epicgames.com/free-games",note=free?"Объявлена бесплатная раздача для выбранной страны. Условия получения — в Epic.":"Магазин объявил период акции. Будущая цена в денежном выражении не опубликована."});
     }
   return result.OrderBy(p=>p.start).Take(10).ToList();
  }
  public static List<PriceProduct> ParseSteamSpecials(string json,string country,DateTime at) {
   var items=JObject.Parse(json).SelectToken("specials.items") as JArray;if(items==null)throw new System.IO.InvalidDataException();var products=new List<PriceProduct>();
   foreach(var p in items.Where(p=>(int?)p["type"]==0&&(bool?)p["discounted"]==true&&(bool?)p["windows_available"]==true).Take(12)) {
    var id=(string)p["id"];if(!Regex.IsMatch(id??"","^[0-9]{1,12}$"))continue;var url="https://store.steampowered.com/app/"+id+"/?cc="+country.ToLowerInvariant();
    var o=SteamOffer(new JObject{["currency"]=p["currency"],["initial"]=p["original_price"],["final"]=p["final_price"]},Text((string)p["name"]),url,country,at,false);if(o?.amount==null||!(o.originalAmount>o.amount))continue;
    var expiry=(long?)p["discount_expiration"];if(expiry>0&&expiry<4102444800)o.saleEndsAt=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(expiry.Value);if(o.saleEndsAt<=at)continue;o.productKey="steam::"+id;
    products.Add(new PriceProduct{store="steam",id=id,name=o.name,image=(string)p["large_capsule_image"]??(string)p["header_image"],url=url,productType="Акция Steam · проверь издание",preview=o});
   }return products;
  }
  public async Task<PriceDiscovery> Discover(string store,PriceAccount account,bool force,CancellationToken ct) {
   if(!Stores.Contains(store))throw new ArgumentException("Неизвестный магазин.");var result=new PriceDiscovery{store=store,country=account.country};var calendar=Calendar(force,ct);
   if(store=="steam"&&!string.IsNullOrEmpty(account.country))try {
    var raw=await Get("https://store.steampowered.com/api/featuredcategories?cc="+Country(account.country).ToLowerInvariant()+"&l=english",force,ct).ConfigureAwait(false);result.products=ParseSteamSpecials(raw.body,account.country,raw.at);result.message="Текущие скидки с витрины Steam для "+account.country+". Перед покупкой обнови цену игры.";
    PriceFx fx=null;try{fx=PriceFx.Parse((await Get("https://www.cbr.ru/scripts/XML_daily.asp",force,ct,360).ConfigureAwait(false)).body,DateTime.UtcNow);result.fxDate=fx.date.ToString("yyyy-MM-dd");}catch{ct.ThrowIfCancellationRequested();result.fxError="Курс недоступен. Показана исходная валюта магазина.";}
    foreach(var p in result.products){CheckCurrency(p.preview,account);ApplyFx(p.preview,fx);}ct.ThrowIfCancellationRequested();history.Observe(result.products.Select(p=>p.preview),DateTime.UtcNow);
   }catch(Exception e){ct.ThrowIfCancellationRequested();result.message="Подборка скидок недоступна. "+Failure(e);}
   else result.message=store=="steam"?"Выбери страну Steam, чтобы увидеть текущие скидки.":"Найди конкретную игру: её цены и скидки появятся в сравнении. Подборка популярных акций пока доступна для Steam.";
   result.calendar=await calendar.ConfigureAwait(false);return result;
  }
 }
}
