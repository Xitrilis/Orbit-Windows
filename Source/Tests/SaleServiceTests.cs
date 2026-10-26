using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orbit;
class SaleServiceTests
{
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
 sealed class Http:HttpMessageHandler {public Func<HttpRequestMessage,HttpResponseMessage> respond;public List<string> urls=new List<string>();protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){ct.ThrowIfCancellationRequested();lock(urls)urls.Add(r.RequestUri.AbsoluteUri);return Task.FromResult(respond(r));}}
 static HttpResponseMessage Response(string s,HttpStatusCode status=HttpStatusCode.OK)=>new HttpResponseMessage(status){Content=new StringContent(s)};
 static string Fx()=>"<ValCurs Date='"+DateTime.UtcNow.ToString("dd.MM.yyyy")+"'><Valute><CharCode>KZT</CharCode><Nominal>100</Nominal><Value>20</Value></Valute><Valute><CharCode>USD</CharCode><Nominal>1</Nominal><Value>90</Value></Valute></ValCurs>";
 static string Calendar="<h2>Winter Sale 2026 | December 17, 2026 - January 4, 2027</h2><h2>Spring Sale 2027 | 18 March, 2027 - 25 March, 2027</h2><h2>2026 Fests</h2><table><tr><td>Oct 26<br>Nov 2</td><td>Steam Scream V</td></tr></table><h2>Next Fest | October 19 - October 26, 2026</h2><h2>Autumn Sale 2027 | TBD</h2>";
 static PriceOffer Offer(DateTime t,decimal amount=100,decimal? initial=200,string currency="KZT",string country="KZ",string key="steam::1")=>new PriceOffer{source="steam",url="https://store.steampowered.com/app/1/",productKey=key,amount=amount,originalAmount=initial,currency=currency,country=country,checkedAt=t,kind="Магазин",official=true};
 static async Task<int> Main(string[] args) {
  var dir=Path.Combine(Path.GetTempPath(),"orbit-sales-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  try {
   var now=DateTime.UtcNow;var calendar=SteamSaleCalendar.Parse(Calendar,now);Check(calendar.Count==3&&calendar.Single(e=>e.scope=="themed").end=="2026-11-02","official calendar: cross-year dates, BR-separated dates, ignore demos/TBD");
   bool rejected=false;try{SteamSaleCalendar.Parse("<h2>Winter Sale 2026 | tomorrow - later</h2>",now);}catch{rejected=true;}Check(rejected,"changed calendar fails closed");
   var store=new PriceHistoryStore(dir);var old=Offer(now.AddHours(-3));store.Observe(new[]{old},old.checkedAt);var current=Offer(now,200,200);store.Observe(new[]{current},now);
   Check(current.history.previousDiscount.amount==100&&current.history.lowest.amount==100,"previous discount is an actual observation");Check(current.history.previousLower.amount==100,"lower listing price separately tracked");
   var again=Offer(now.AddMinutes(1),190,200);new PriceHistoryStore(dir).Observe(new[]{again},now.AddMinutes(1));Check(again.history.previousDiscount.amount==100,"history survives restart");
   foreach(var changed in new[]{Offer(now,200,200,"RUB"),Offer(now,200,200,"KZT","RU"),Offer(now,200,200,"KZT","KZ","steam::2")}){store.Observe(new[]{changed},now);Check(changed.history.previousDiscount==null,"currency, region and SKU isolation");}
   var before=current.history.observations;store.Observe(new[]{current},now);Check(current.history.observations==before,"cached quote not duplicated");
   var tmp=Path.Combine(dir,"price-history.json.tmp");Directory.CreateDirectory(tmp);var fail=Offer(now.AddMinutes(2),50,200);store.Observe(new[]{fail},now.AddMinutes(2));Check(fail.history.message.Contains("Не удалось")&&fail.history.lowest.amount==100,"failed disk save retains prior observations");Directory.Delete(tmp);
   File.WriteAllText(Path.Combine(dir,"price-history.json"),"broken");var recovered=Offer(now.AddMinutes(3),200,200);new PriceHistoryStore(dir).Observe(new[]{recovered},now.AddMinutes(3));Check(recovered.history.lowest.amount==100,"backup recovers damaged primary");
   File.WriteAllText(Path.Combine(dir,"price-history.json"),"broken");File.WriteAllText(Path.Combine(dir,"price-history.json.bak"),"broken");var blocked=Offer(now);new PriceHistoryStore(dir).Observe(new[]{blocked},now);Check(blocked.history.message.Contains("повреждена")&&File.ReadAllText(Path.Combine(dir,"price-history.json"))=="broken","two corrupt files protected from overwrite");
   var feed=JObject.Parse("{data:{Catalog:{searchStore:{elements:[{id:'offer1',namespace:'ns',price:{totalPrice:{currencyCode:'KZT'}},promotions:{upcomingPromotionalOffers:[{promotionalOffers:[{startDate:'',endDate:'',discountSetting:{discountType:'PERCENTAGE',discountPercentage:0}}]}]}}]}}}}");
   var promo=feed.SelectToken("data.Catalog.searchStore.elements[0].promotions.upcomingPromotionalOffers[0].promotionalOffers[0]");promo["startDate"]=now.AddDays(2);promo["endDate"]=now.AddDays(9);
   Check(PriceService.MatchPromotions(feed.ToString(),"epic:ns:offer1",now).Single().amount==0,"explicitly free future Epic promotion");Check(PriceService.MatchPromotions(feed.ToString(),"epic:other:offer1",now).Count==0,"Epic namespace and SKU must both match");
   promo["discountSetting"]["discountPercentage"]=20;Check(PriceService.MatchPromotions(feed.ToString(),"epic:ns:offer1",now).Single().amount==null,"nonzero percentage not extrapolated into future money");promo["endDate"]=now.AddDays(-1);Check(PriceService.MatchPromotions(feed.ToString(),"epic:ns:offer1",now).Count==0,"expired/inverted promotions excluded");
   var specials="{specials:{items:[{id:1,type:0,name:'Fixture Game',discounted:true,windows_available:true,currency:'KZT',original_price:200000,final_price:100000},{id:2,type:1,name:'Bundle',discounted:true,windows_available:true,currency:'KZT',original_price:200000,final_price:100000},{id:3,type:0,name:'Mac only',discounted:true,windows_available:false,currency:'KZT',original_price:200000,final_price:100000}]}}";
   Check(PriceService.ParseSteamSpecials(specials,"KZ",now).Single().preview.amount==1000,"regional specials use cents and exclude packages/non-Windows");
   var h=new Http{respond=r=>{
    if(r.RequestUri.Host=="partner.steamgames.com")return Response(Calendar);
    if(r.RequestUri.Host=="www.cbr.ru")return Response(Fx());
    if(r.RequestUri.Host=="catalog.gog.com")return Response("{products:[{id:'g1',title:'Fixture Game',storeLink:'https://www.gog.com/en/game/fixture',price:{finalMoney:{currency:'USD',amount:'2'},baseMoney:{amount:'4'}}}]}");
    if(r.RequestUri.Host=="store.epicgames.com")return Response("{data:{Catalog:{searchStore:{elements:[{id:'e1',namespace:'en',title:'Fixture Game 2',offerType:'BASE_GAME',productSlug:'fixture2',price:{totalPrice:{originalPrice:500,discountPrice:100,currencyCode:'KZT',currencyInfo:{decimals:2}}}}]}}}}");
    if(r.RequestUri.AbsolutePath.Contains("featuredcategories"))return Response(specials);
    if(r.RequestUri.Host=="store.steampowered.com")return Response("{'1':{success:true,data:{name:'Fixture Game',is_free:false,price_overview:{currency:'KZT',initial:200000,final:150000}}}}");
    return Response("",HttpStatusCode.TooManyRequests);
   }};
   var settings=new PriceSettings{accounts=new Dictionary<string,PriceAccount>{{"steam",new PriceAccount{country="KZ",currency="KZT"}},{"epic",new PriceAccount{country="KZ",currency="KZT"}},{"gog",new PriceAccount{country="KZ",currency="USD"}}}};
   using(var service=new PriceService(h)){
    var c=await service.Compare(new PriceProduct{store="steam",id="1",name="Fixture Game"},settings,false,CancellationToken.None);
    Check(c.sources.Single(x=>x.source=="gog").offers.Single().rub==180,"exact title cross-store quote and FX");Check(c.sources.Single(x=>x.source=="epic").offers.Count==0,"sequel not substituted for same game");Check(c.sources.Single(x=>x.source=="steam").offers.Single().amount==1500&&c.sources.Single(x=>x.source=="funpay").status=="error","provider failure does not hide official price");Check(c.calendar.events.Any(x=>x.scope=="themed"),"calendar separate from game discount");
    var d=await service.Discover("steam",settings.Account("steam"),false,CancellationToken.None);Check(d.products.Single().preview.kzt==1000,"discovery contains actual regional quote");
   }
   using(var service=new PriceService(new Http{respond=r=>Response("",HttpStatusCode.ServiceUnavailable)})){
    var fallback=await service.Calendar(false,CancellationToken.None);Check(fallback.cached&&fallback.checkedAt.Year==2026,"calendar failure exposes dated fallback");var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;try{await service.Calendar(true,cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancellation not disguised as cached success");
   }
   Check(!PriceService.SafeUrl("https://partner.steamgames.com.evil.test/")&&PriceService.SafeUrl(SteamSaleCalendar.Url),"exact-host source URL allowlist");
   if(args.Contains("--live"))using(var service=new PriceService()){
    var d=await service.Discover("steam",settings.Account("steam"),true,CancellationToken.None);Console.WriteLine("LIVE calendar fresh="+!d.calendar.cached+" events="+d.calendar.events.Count+" featured="+d.products.Count);Check(!d.calendar.cached&&d.products.Count>0,"live Steam calendar and deals");
    var c=await service.Compare(new PriceProduct{store="steam",id="1091500",name="Cyberpunk 2077"},settings,true,CancellationToken.None);foreach(var x in c.sources)Console.WriteLine("LIVE "+x.source+" "+x.status+" "+string.Join(",",x.offers.Take(1).Select(o=>o.amount+" "+o.currency)));
    Directory.CreateDirectory("Orbit-Windows/qa");File.WriteAllText("Orbit-Windows/qa/sales-live.json",JsonConvert.SerializeObject(c));File.WriteAllText("Orbit-Windows/qa/sales-discovery-live.json",JsonConvert.SerializeObject(d));Check(c.sources.Single(x=>x.source=="steam").offers.Count==1,"live direct game quote");
   }
   Console.WriteLine(count+" sale checks passed.");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{Directory.Delete(dir,true);}
 }
}
