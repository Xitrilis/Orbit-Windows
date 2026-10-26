using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Orbit;

class PriceServiceTests
{
    sealed class Http : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> respond;
        public readonly List<string> urls = new List<string>();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken token) { lock(urls) urls.Add(r.RequestUri.AbsoluteUri); token.ThrowIfCancellationRequested(); return Task.FromResult(respond(r)); }
    }
    static HttpResponseMessage Json(string s, HttpStatusCode status = HttpStatusCode.OK) => new HttpResponseMessage(status) { Content = new StringContent(s) };
    static string Fx(string date = null) => "<ValCurs Date=\"" + (date ?? DateTime.UtcNow.ToString("dd.MM.yyyy")) + "\"><Valute><CharCode>KZT</CharCode><Nominal>100</Nominal><Value>20,0000</Value></Valute><Valute><CharCode>USD</CharCode><Nominal>1</Nominal><Value>90,0000</Value></Valute></ValCurs>";
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Throws(Action f) { try { f(); } catch { return; } throw new Exception("Expected rejection"); }
    static int count;
    static void Test(string name, Action run) { run(); Console.WriteLine("PASS " + name); count++; }
    static async Task TestAsync(string name, Func<Task> run) { await run(); Console.WriteLine("PASS " + name); count++; }
    static string Lot(string id, string name, string region = "казахстан", string platform = "pc", string unit = "₽", string method = "цифровой ключ", string price = "1 250,50") =>
        "<a class='tc-item' href='https://funpay.com/lots/offer?id=" + id + "' data-f-platform='" + platform + "' data-f-region='" + region + "' data-f-method='" + method + "'><div class='tc-desc-text'>" + name + " STEAM</div><div class='media-user-name'>FixtureSeller</div><span class='rating-mini-count'>15</span><div class='tc-price' data-s='1'><div>" + price + " <span class='unit'>" + unit + "</span></div></div></a>";
    static async Task<int> Main(string[] args)
    {
        try
        {
            Test("CBR nominal and two-way decimal conversion", () => { var fx = PriceFx.Parse(Fx(), DateTime.UtcNow); Check(fx.Convert(1000m,"KZT","RUB")==200m,"nominal 100"); Check(fx.Convert(100m,"RUB","KZT")==500m,"inverse"); Check(fx.Convert(1m,"USD","KZT")==450m,"cross"); Check(fx.Convert(1m,"XXX","RUB")==null,"unknown not guessed"); });
            Test("FX rejects stale, malformed and unsafe XML", () => { Throws(()=>PriceFx.Parse(Fx("01.01.2000"),DateTime.UtcNow)); Throws(()=>PriceFx.Parse("<!DOCTYPE ValCurs [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><ValCurs Date='01.01.2000'>&x;</ValCurs>",DateTime.UtcNow)); Throws(()=>PriceFx.Parse("<ValCurs Date='"+DateTime.UtcNow.ToString("dd.MM.yyyy")+"'/>",DateTime.UtcNow)); });
            Test("No price is not zero; free remains zero", () => { Check(PriceService.SteamOffer(null,"X","u","KZ",DateTime.UtcNow,false)==null,"missing"); var free=PriceService.SteamOffer(null,"X","u","KZ",DateTime.UtcNow,true);PriceService.ApplyFx(free,null);Check(free.rub==0&&free.kzt==0,"free");var paid=PriceService.SteamOffer(JObject.Parse("{currency:'KZT',final:125050,initial:200000}"),"X","u","KZ",DateTime.UtcNow,false);PriceService.ApplyFx(paid,null);Check(paid.amount==1250.50m&&paid.kzt==1250.50m&&paid.rub==null,"failed FX preserves original"); });
            Test("URLs, countries and currencies validated", () => { Check(PriceService.Country("kz")=="KZ","country"); Check(PriceService.Currency(" rub ")=="RUB","currency");Throws(()=>PriceService.Country("ZZ"));Throws(()=>PriceService.Currency("RUB&cc=US"));Check(!PriceService.SafeUrl("https://funpay.com.evil.org/"),"suffix");Check(!PriceService.SafeUrl("file:///C:/Windows/system32/cmd.exe"),"file");Check(!PriceService.SafeUrl("https://user@funpay.com/"),"userInfo");Check(!PriceService.SafeUrl("https://funpay.com:1234/"),"port");Throws(()=>PriceService.FunpaySection("https://funpay.com/lots/offer?id=1"));Check(PriceService.FunpaySection("https://funpay.com/en/lots/976/?x=y")=="https://funpay.com/lots/976/","canonical section"); });
            Test("Titles keep sequels and editions distinct", () => { Check(PriceService.Normalize("Game™: Two")=="game two","normalization");Check(PriceService.Normalize("Game 2")!=PriceService.Normalize("Game 20"),"sequel"); });
            Test("Marketplace type detection avoids account and balance traps", () => { Check(PriceService.OfferKind("Fixture Game Steam Gift","Подарком на ваш аккаунт")=="gift","gift");Check(PriceService.OfferKind("Fixture Game ключ","общий аккаунт для оффлайн активации")=="other","account");Check(PriceService.OfferKind("Steam Wallet key")=="other","wallet");Check(PriceService.OfferKind("Fixture Game key")=="key","key"); });
            Test("Plati is a base price, not an account-region final price", () => {
                var items=new JArray();
                foreach(var n in new[]{"Fixture Game STEAM GIFT","Fixture Game Ultimate Edition KEY","Fixture Game account","Fixture Game 2 KEY","Fixture Game DLC KEY"})items.Add(new JObject{["name"]=n,["description"]=n.Contains("account")?"общий аккаунт":"",["price_rur"]=123.45m,["url"]="https://plati.market/itm/123",["seller_name"]="Fixture"});
                var r=PriceService.ParsePlati(new JObject{["items"]=items}.ToString(),"Fixture Game",DateTime.UtcNow);
                Check(r.offers.Count==1,"sequels and editions excluded");
                Check(r.offers.Any(x=>x.kind=="gift"&&x.from&&x.currency=="RUB"),"base data");Check(!r.offers.Any(x=>x.name.Contains("Ultimate")||x.name.Contains("account")||x.name.Contains("DLC")),"types excluded");
            });
            Test("FunPay keeps stated region, visible price and PC-only keys", () => {
                var html=Lot("1","Fixture Game")+Lot("2","Fixture Game","россия")+Lot("3","Fixture Game","снг")+Lot("4","Fixture Game DLC")+Lot("5","Fixture Game","казахстан","xbox")+Lot("6","Fixture Game","казахстан","pc","?")+Lot("7","Fixture Game","global","pc","$");
                var r=PriceService.ParseFunpay(html,"Fixture Game","KZ","https://funpay.com/lots/1/",DateTime.UtcNow);
                Check(r.offers.Count==2&&r.excluded==5,"filters");Check(r.offers.All(x=>x.amount==1250.50m),"visible not sorting data");Check(r.offers.Any(x=>x.currency=="USD"),"source currency");
            });
            Test("FunPay exact category lookup does not choose a sequel", () => {var html="<div class='promo-game-item'><div class='game-title'>Fixture Game 2</div><ul><li><a href='https://funpay.com/lots/2/'>Ключи</a></li></ul></div><div class='promo-game-item'><div class='game-title'>Fixture Game</div><ul><li><a href='https://funpay.com/lots/1/'>Ключи</a></li></ul></div>";Check(PriceService.FindFunpaySection(html,"Fixture Game")=="https://funpay.com/lots/1/","exact");Check(PriceService.FindFunpaySection(html,"Unknown")==null,"not guessed"); });
            await TestAsync("Steam country, currency mismatch, cache and force refresh", async () => {
                var h=new Http{respond=r=>r.RequestUri.Host=="www.cbr.ru"?Json(Fx()):Json("{items:[{type:'app',id:10,name:'Fixture Game',price:{currency:'KZT',final:150000,initial:200000}}]}")};
                using(var s=new PriceService(h)) {var a=new PriceAccount{country="KZ",currency="RUB"};var r=await s.Search("steam","Fixture Game",a,false,CancellationToken.None);Check(r.products[0].preview.rub==300m&&r.products[0].preview.kzt==1500m,"converted");Check(r.products[0].preview.note.Contains("KZT"),"currency mismatch");Check(h.urls[0].Contains("cc=kz"),"country");await s.Search("steam","Fixture Game",a,false,CancellationToken.None);Check(h.urls.Count==2,"cache");await s.Search("steam","Fixture Game",a,true,CancellationToken.None);Check(h.urls.Count==4,"force");}
            });
            await TestAsync("A failed provider does not hide the official quote", async () => {
                var h=new Http{respond=r=>r.RequestUri.Host=="www.cbr.ru"?Json(Fx()):r.RequestUri.Host=="store.steampowered.com"?Json("{'10':{success:true,data:{name:'Fixture Game',is_free:false,price_overview:{currency:'RUB',initial:120000,final:90000}}}}") :Json("",HttpStatusCode.TooManyRequests)};
                using(var s=new PriceService(h)) {var c=await s.Compare(new PriceProduct{id="10",name="Fixture Game",store="steam"},new PriceSettings{accounts=new Dictionary<string,PriceAccount>{{"steam",new PriceAccount{country="RU"}}}},false,CancellationToken.None);Check(c.sources[0].offers.Single().rub==900m,"official");Check(c.sources.Where(x=>x.source=="plati"||x.source=="funpay").All(x=>x.status=="error"&&x.offers.Count==0)&&c.sources.Where(x=>x.source=="epic"||x.source=="gog").All(x=>x.status=="unavailable"&&x.offers.Count==0),"isolated errors");}
            });
            await TestAsync("Unconfigured country and unsupported store never become Steam prices", async () => {
                var h=new Http{respond=r=>r.RequestUri.Host=="www.cbr.ru"?Json(Fx()):r.RequestUri.Host=="plati.io"?Json("{items:[]}"):Json("<html></html>")};
                using(var s=new PriceService(h)){var c=await s.Compare(new PriceProduct{id="x",name="Fixture Game",store="ea"},new PriceSettings(),false,CancellationToken.None);Check(c.sources[0].source=="ea"&&c.sources[0].offers.Count==0,"unsupported");Check(!h.urls.Any(x=>x.Contains("steampowered")),"no substitute");}
            });
            if(args.Contains("--live"))
            {
                using(var s=new PriceService())
                {
                    var settings=new PriceSettings{accounts=new Dictionary<string,PriceAccount>{{"steam",new PriceAccount{country="KZ",currency="KZT"}},{"epic",new PriceAccount{country="KZ",currency="KZT"}},{"gog",new PriceAccount{country="KZ",currency="USD"}}}};
                    foreach(var store in new[]{"steam","epic","gog"})
                    {
                        var found=await s.Search(store,"Cyberpunk 2077",settings.Account(store),true,CancellationToken.None);
                        var game=found.products.Single(x=>x.name=="Cyberpunk 2077");Check(game.preview.amount.HasValue,"live quote");
                        Console.WriteLine("LIVE "+store+": "+game.preview.amount+" "+game.preview.currency+", RUB="+game.preview.rub+", KZT="+game.preview.kzt+", FX="+found.fxDate);
                        if(store=="steam"){var result=await s.Compare(game,settings,true,CancellationToken.None);foreach(var r in result.sources)Console.WriteLine("LIVE source "+r.source+": "+r.status+", offers="+r.offers.Count+", filtered="+r.excluded);Directory.CreateDirectory("qa");File.WriteAllText("qa/price-live.json",Newtonsoft.Json.JsonConvert.SerializeObject(result));Check(result.sources[0].offers.Count==1,"live exact");}
                    }
                }
            }
            Console.WriteLine(count+" pricing checks passed.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
