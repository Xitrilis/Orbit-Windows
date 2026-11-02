using System;using System.Collections.Generic;using System.Linq;using System.Net;using System.Net.Http;using System.Threading;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using Orbit;
class PublisherPriceTests {
 static int count;const string Id="5afda5f888a7e34d25b50149",EaUrl="https://store.ea.com/commerce/app/products/fixture-game",UbiUrl="https://store.ubisoft.com/de/fixture-game/"+Id+".html";
 static DateTime Now=new DateTime(2026,11,1,12,0,0,DateTimeKind.Utc);
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+(++count)+" "+name);}
 static void Reject(Action a,string name){try{a();}catch{Check(true,name);return;}throw new Exception(name);}
 static string E(string s)=>WebUtility.HtmlEncode(s);
 static string Label(string id="Origin.OFR.full",string name="Standard Edition",string price="39.99",string currency="USD",string extra="")=>"<label class='pdp-edition'><input name='pdp-edition' value='"+E(id)+"' data-price='"+E(price)+"' data-currency='"+E(currency)+"' data-list='59.99' "+extra+"><span class='pdp-edition-name'>"+E(name)+"</span></label>";
 static string Ea(string country="us",string labels=null,bool windows=true,string name="Fixture Game",string single="")=>"<script id='eax-sal-config'>"+new JObject{["country"]=country}+"</script><h1>"+name+"</h1><a id='pdp-buy-btn' data-game-type='BASE_GAME' href='/commerce/app/products/fixture-game/buy?offerId=Origin.OFR.full'>Buy</a>"+(windows?"<dd class='pdp-sysreq-value'>64-bit Windows 10</dd>":"")+(labels??Label())+single;
 static string Catalog(int total=1)=>"<script id='eax-sal-config'>{}</script>"+string.Join("",Enumerable.Range(0,total).Select(i=>"<article class='tile'><a class='tile-link' href='/commerce/app/products/fixture-game"+(total==1?"":"-"+i)+"'><h3>Fixture Game"+(total==1?"":" "+i)+"</h3></a><span class='price'>USD 0.00</span></article>"));
 static JObject Context(string c="DE")=>JObject.Parse("{country:'"+c+"',currency:'EUR',product:{productID:'"+Id+"',gameName:'Fixture Game',editionName:'Standard Edition',platform:'pcdl',productType:'game',productDemo:'no',productFreeWeekEnd:'no',availability:'available',salesType:'order',unitSalePrice:12.34,originalUnitSalePrice:59.99}}");
 static JObject Schema()=>JObject.Parse("{'@type':'Product',name:'Fixture Game',url:'"+UbiUrl+"?lang=de_DE',image:'/image.jpg',offers:[{'@type':'Offer',price:12.34,priceCurrency:'EUR',seller:'Ubisoft',availability:'https://schema.org/InStock',priceValidUntil:'2026-11-02'}]}");
 static JObject Model()=>JObject.Parse("{details:{ID:'"+Id+"',isDigital:true,isMasterProduct:false,isOnlineProduct:true},availability:{addToCartPossible:true,isOutOfStock:false},priceObject:{price:{sales:{value:12.34,decimalPrice:'12.34',currency:'EUR'}},priceOptions:{isRangePrice:false,showFromLabel:false}},dlc:{isDlcType:false},offer:{isDemo:false,isOpenBeta:false,isFreeWeekend:false,isFreeWithPurchase:false,isFreeToPlay:false,isGiveaway:false}}");
 static string Ubi(JObject context=null,JObject schema=null,JObject model=null)=>"<script>var wa_data = "+(context??Context())+";</script><script type='application/ld+json'>"+(schema??Schema())+"</script>"+(model==null?"":"<script>window.extendedProductModel = "+model+";</script>");
 static string UbiCatalog(string id=Id,string href=UbiUrl,string dlc="")=>"<script>var wa_data = {country:'DE'};</script><div class='product-tile'><a class='thumb-link' href='"+E(href)+"'></a><div class='prod-title'>Fixture Game</div><div class='card-subtitle'>Standard Edition</div><button data-pcdl-productid='"+id+"'></button><div class='product-availability-label' data-dlc='"+dlc+"' data-ismobileproduct='false'></div></div>";
 static JObject EaRegional(string currency="RUB",string amount="2 499,00 руб") {
  var price=new JObject{["displayTotal"]=amount,["displayTotalWithDiscount"]=amount,["currency"]=currency};
  var edition=new JObject{["slug"]="standard",["editionName"]="Standard Edition",["checkoutId"]="Origin.OFR.regional",["acquirable"]=true,["isFree"]=false,["price"]=price};
  return new JObject{["slug"]="fixture-game",["name"]="Fixture Game",["franchise"]=new JObject{["slug"]="fixture"},["isPurchasableGame"]=true,["isCMSDataOnly"]=false,
   ["supportedPlatforms"]=new JArray(new JObject{["slug"]="pc"}),["editions"]=new JArray(edition),
   ["platformDetails"]=new JArray(new JObject{["slug"]="EA-APP",["isThirdParty"]=false,["editions"]=new JArray(new JObject{["slug"]="standard",["checkoutId"]="Origin.OFR.regional",["price"]=price.DeepClone()})})};
 }
 sealed class Http:HttpMessageHandler {
  public List<string> urls=new List<string>();public Func<HttpRequestMessage,CancellationToken,Task<string>> body;public int active,max;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){lock(urls){urls.Add(r.RequestUri.ToString());if(++active>max)max=active;}try{if(r.Headers.Authorization!=null||r.Headers.Contains("Cookie"))throw new Exception("credentials sent");return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(await body(r,ct))};}finally{lock(urls)active--;}}
 }
 static async Task Main(string[] args){
  var full=PriceService.ParseEaProduct(Ea(labels:Label("Origin.OFR.trial","Trial","0")+Label()),EaUrl,"US",Now);
  var regional=PriceService.ParseEaRegionalProduct(EaRegional().ToString(),"fixture-game","Fixture Game","KZ",Now).Single();
  Check(regional.preview.amount==2499m&&regional.preview.currency=="RUB"&&regional.preview.country=="KZ","EA public regional API preserves requested KZ and actual RUB currency");
  Check(regional.id=="Origin.OFR.regional"&&regional.catalogNamespace=="drop:fixture-game"&&regional.url=="https://www.ea.com/games/fixture/fixture-game/buy","EA regional exact offer identity and official game page");
  Check(PriceService.ParseEaRegionalProduct(EaRegional("USD","$1,234.56").ToString(),"fixture-game","Fixture Game","US",Now).Single().preview.amount==1234.56m,"EA localized grouped amount parsed");
  Check(PriceService.ParseEaRegionalProduct(EaRegional().ToString(),"different-game","Fixture Game","KZ",Now).Count==0,"EA response slug cannot substitute another game");
  Check(PriceService.ParseEaRegionalProduct(EaRegional().ToString(),"fixture-game","Other Game","KZ",Now).Count==0,"EA response title must match catalogue game");
  foreach(var path in new[]{"editions[0].acquirable","isPurchasableGame","platformDetails[0].isThirdParty"}) {var j=EaRegional();j.SelectToken(path).Replace(path.EndsWith("isThirdParty")?true:false);Check(PriceService.ParseEaRegionalProduct(j.ToString(),"fixture-game","Fixture Game","KZ",Now).Count==0,"EA regional rejects unavailable or third party "+path);}
  var rj=EaRegional();rj["platformDetails"][0]["editions"][0]["checkoutId"]="Origin.OFR.console";Check(PriceService.ParseEaRegionalProduct(rj.ToString(),"fixture-game","Fixture Game","KZ",Now).Count==0,"EA regional exact PC offer ID must agree");
  rj=EaRegional();rj["platformDetails"][0]["editions"][0]["price"]["currency"]="USD";Check(PriceService.ParseEaRegionalProduct(rj.ToString(),"fixture-game","Fixture Game","KZ",Now).Count==0,"EA regional conflicting currency rejected");
  rj=EaRegional();rj["editions"][0]["editionName"]="Friend's Pass";Check(PriceService.ParseEaRegionalProduct(rj.ToString(),"fixture-game","Fixture Game","KZ",Now).Count==0,"EA regional friend pass is not the full game");
  Check(PriceService.ParseEaRegionalProduct(EaRegional("RUB","0 руб").ToString(),"fixture-game","Fixture Game","KZ",Now).Count==0,"EA regional zero requires explicit free edition");
  Check(full.Count==1&&full[0].id=="Origin.OFR.full"&&full[0].preview.amount==39.99m,"EA trial excluded from full-game price");
  Check(full[0].preview.originalAmount==59.99m&&full[0].preview.platform=="PC"&&full[0].preview.edition=="Standard Edition","EA original amount, PC and edition retained");
  Check(full[0].name=="Fixture Game"&&full[0].url.Contains("offerId=Origin.OFR.full"),"EA exact offer link and standard name");
  Check(PriceService.ParseEaProduct(Ea(labels:Label("Origin.OFR.pass","Friend’s Pass","0")),EaUrl,"US",Now).Count==0,"Friend's Pass is not a full game");
  Check(PriceService.ParseEaProduct(Ea(windows:false),EaUrl,"US",Now).Count==0,"EA console or unverified platform excluded");
  Check(PriceService.ParseEaProduct(Ea(labels:Label(extra:"disabled")),EaUrl,"US",Now).Count==0,"disabled EA edition excluded");
  Check(PriceService.ParseEaProduct(Ea(labels:Label("Origin.OFR.beta","Open Beta","0")),EaUrl,"US",Now).Count==0,"EA beta is not the full game");
  Check(PriceService.ParseEaProduct(Ea(labels:Label("Origin.OFR.preview","Preview Access","0")),EaUrl,"US",Now).Single().preview==null,"unknown zero-price edition needs full-game confirmation");
  var wrong=PriceService.ParseEaProduct(Ea(),EaUrl,"KZ",Now).Single();Check(wrong.preview==null&&wrong.priceMessage.Contains("US")&&wrong.priceMessage.Contains("KZ"),"EA response country must equal requested country");
  Check(PriceService.ParseEaProduct(Ea("us-extra"),EaUrl,"US",Now).Single().preview==null,"malformed country not truncated to US");
  foreach(var price in new[]{"-1","1000000000","NaN",""})Check(PriceService.ParseEaProduct(Ea(labels:Label(price:price)),EaUrl,"US",Now).Single().preview==null,"EA rejects invalid amount "+price);
  Check(PriceService.ParseEaProduct(Ea(labels:Label(currency:"$")),EaUrl,"US",Now).Single().preview==null,"EA unknown currency rejected");
  Check(PriceService.ParseEaProduct(Ea(labels:Label(price:"0")),EaUrl,"US",Now).Single().preview.free,"confirmed permanent free offer stays zero");
  Check(PriceService.ParseEaProduct(Ea(labels:Label()+Label("Origin.OFR.deluxe","Deluxe Edition","49.99")),EaUrl,"US",Now).Count==2,"paid EA editions remain separate");
  var single=new JObject{["@graph"]=new JArray(new JObject{["@type"]="VideoGame",["name"]="Fixture Game",["offers"]=new JObject{["@type"]="Offer",["name"]="Standard Edition",["price"]=39.99m,["priceCurrency"]="USD",["availability"]="https://schema.org/InStock"}})};
  string ld="<script type='application/ld+json'>"+single+"</script>";
  Check(PriceService.ParseEaProduct(Ea(labels:"",single:ld),EaUrl,"US",Now).Single().id=="Origin.OFR.full","single-edition EA page reads exact typed offer");
  Check(PriceService.ParseEaProduct(Ea(labels:Label(extra:"disabled"),single:ld),EaUrl,"US",Now).Count==0,"disabled radio cannot fall back to unrelated schema availability");
  Reject(()=>PriceService.ParseEaProduct(Ea(),"https://store.ea.com.evil.example/x","US",Now),"EA exact host enforced");
  Reject(()=>PriceService.ParsePublisherCatalog("<html>captcha</html>","ea"),"EA challenge is not empty catalogue");
  Check(PriceService.ParsePublisherCatalog(Catalog(),"ea").Single().preview==null,"EA catalogue minimum never becomes the quote");
  Check(PriceService.StoreUrl("ea","Game & more").Contains("search=Game%20%26%20more"),"EA search URL preserves encoded game name");
  Check(PriceService.StoreUrl("ubisoft","Game","DE").Contains("/de/search?q=Game"),"Ubisoft search URL retains region");
  Check(!PriceService.SafeUrl("https://store.ea.com@evil.example/")&&PriceService.SafeUrl(EaUrl),"exact safe publisher host");
  var ubi=PriceService.ParseUbisoftProduct(Ubi(),UbiUrl,"DE",Id,Now);Check(ubi.preview.amount==12.34m&&ubi.preview.currency=="EUR"&&ubi.preview.originalAmount==59.99m,"Ubisoft validates currency and amount against exact variant");
  Check(ubi.preview.country=="DE"&&ubi.preview.platform=="PC","Ubisoft country and PC preserved");
  var reference=PriceService.ParseUbisoftProduct(Ubi(),UbiUrl,"KZ",Id,Now);
  Check(reference.preview.reference&&reference.preview.country=="DE"&&reference.preview.amount==12.34m&&reference.priceMessage.Contains("KZ"),"Ubisoft foreign quote remains explicit DE reference instead of KZ price");
  var invalidReference=Context();invalidReference["product"]["unitSalePrice"]=1;
  Check(PriceService.ParseUbisoftProduct(Ubi(invalidReference),UbiUrl,"KZ",Id,Now).preview==null,"Ubisoft reference must still validate price against exact variant");
  Check(PriceService.ParseUbisoftProduct(Ubi(Context("ZZ")),UbiUrl,"KZ",Id,Now).preview==null,"Ubisoft unknown response region cannot become a reference price");
  foreach(var key in new[]{"platform","productID","productType","productDemo","productFreeWeekEnd"}){var c=Context();c["product"][key]=key=="platform"?"ps5":key=="productID"?new string('a',24):key=="productType"?"dlc":"yes";Check(PriceService.ParseUbisoftProduct(Ubi(c),UbiUrl,"DE",Id,Now)==null,"Ubisoft rejects incompatible "+key);}
  var ctx=Context();ctx["product"]["availability"]="unavailable";Check(PriceService.ParseUbisoftProduct(Ubi(ctx),UbiUrl,"DE",Id,Now).preview==null,"unavailable Ubisoft variant cannot quote");
  foreach(var key in new[]{"price","priceCurrency","priceValidUntil","availability"}){var sc=Schema();sc["offers"][0][key]=key=="price"?new JValue(1):new JValue(key=="priceCurrency"?"USD":key=="priceValidUntil"?"2026-10-30":"https://schema.org/OutOfStock");Check(PriceService.ParseUbisoftProduct(Ubi(schema:sc),UbiUrl,"DE",Id,Now).preview==null,"Ubisoft schema rejects contradictory "+key);}
  var other=Schema();other["url"]="https://store.ubisoft.com/de/other/"+new string('b',24)+".html";Check(PriceService.ParseUbisoftProduct(Ubi(schema:other),UbiUrl,"DE",Id,Now).preview==null,"other product schema not used");
  other=Schema();((JArray)other["offers"]).Add(other["offers"][0].DeepClone());Check(PriceService.ParseUbisoftProduct(Ubi(schema:other),UbiUrl,"DE",Id,Now).preview==null,"ambiguous offers not minimized");
  Check(PriceService.ParsePublisherCatalog(UbiCatalog(),"ubisoft").Single().id==Id,"Ubisoft search chooses PC variant");
  Check(PriceService.ParsePublisherCatalog(UbiCatalog(id:"bad"),"ubisoft").Count==0,"invalid variant rejected");
  Check(PriceService.ParsePublisherCatalog(UbiCatalog(href:"https://evil.example/x"),"ubisoft").Count==0,"foreign publisher link rejected");
  Check(PriceService.ParsePublisherCatalog(UbiCatalog(dlc:"true"),"ubisoft").Count==0,"currency and DLC tiles excluded");
  Reject(()=>PriceService.ParsePublisherCatalog("<html>challenge</html>","ubisoft"),"Ubisoft challenge is an error");
  var expired=Schema();expired["offers"][0]["priceValidUntil"]="2026-10-30";Check(PriceService.ParseUbisoftProduct(Ubi(schema:expired,model:Model()),UbiUrl,"DE",Id,Now).preview.amount==12.34m,"current exact buy model works independently of expired search-engine schema");
  foreach(var key in new[]{"details.ID","availability.addToCartPossible","availability.isOutOfStock","priceObject.priceOptions.isRangePrice","priceObject.priceOptions.showFromLabel","dlc.isDlcType","offer.isDemo","offer.isOpenBeta","offer.isFreeWeekend","offer.isFreeWithPurchase"}){var m=Model();m.SelectToken(key).Replace(key=="details.ID"?new JValue(new string('a',24)):new JValue(key=="availability.addToCartPossible"?false:true));Check(PriceService.ParseUbisoftProduct(Ubi(model:m),UbiUrl,"DE",Id,Now).preview==null,"buy model rejects "+key);}
  foreach(var key in new[]{"value","decimalPrice","currency"}){var m=Model();m.SelectToken("priceObject.price.sales."+key).Replace(key=="currency"?new JValue("USD"):new JValue(1));Check(PriceService.ParseUbisoftProduct(Ubi(model:m),UbiUrl,"DE",Id,Now).preview==null,"buy model rejects contradictory "+key);}
  var h=new Http{body=(r,c)=>Task.FromResult(r.RequestUri.AbsolutePath.EndsWith("/products")?Catalog():Ea())};
  using(var service=new PriceService(h)){
   var found=await service.Search("ea","Fixture Game",new PriceAccount{country="US"},false,CancellationToken.None,false);Check(found.products.Single().preview.amount==39.99m&&h.urls.Count==2,"EA live path verifies the exact product page");
   await service.Search("ea","Fixture Game",new PriceAccount{country="US"},false,CancellationToken.None,false);Check(h.urls.Count==2,"publisher cache reused");
   var kz=await service.Search("ea","Fixture Game",new PriceAccount{country="KZ"},false,CancellationToken.None,false);Check(kz.products.Single().preview==null&&kz.message.Contains("KZ"),"cache cannot leak a US quote into KZ");
   await service.Search("ea","Fixture Game",new PriceAccount{country="US"},true,CancellationToken.None,false);Check(h.urls.Count==5,"force refresh bypasses both publisher pages after regional fallback attempt");
   var q=await service.Compare(found.products.Single(),new PriceSettings{accounts=new Dictionary<string,PriceAccount>{{"ea",new PriceAccount{country="US"}}}},false,CancellationToken.None);Check(q.sources.First().offers.Single().amount==39.99m&&q.sources.First().match=="id","selected EA offer rechecked by exact ID");
   Check(!h.urls.Any(u=>new Uri(u).AbsolutePath.EndsWith("/buy")),"no checkout/buy HTTP calls");
   var unknown=new PriceProduct{store="ea",id="Origin.OFR.unknown",name="Fixture Game"};q=await service.Compare(unknown,new PriceSettings{accounts=new Dictionary<string,PriceAccount>{{"ea",new PriceAccount{country="US"}}}},false,CancellationToken.None);Check(q.sources.First().offers.Count==0&&q.sources.First().message.Contains("ID"),"unknown library ID cannot silently choose standard edition");
  }
  h=new Http{body=(r,c)=>Task.FromResult(r.RequestUri.Host=="drop-api.ea.com"?EaRegional().ToString():r.RequestUri.AbsolutePath.EndsWith("/products")?Catalog():Ea())};
  using(var service=new PriceService(h)) {
   var kz=await service.Search("ea","Fixture Game",new PriceAccount{country="KZ"},false,CancellationToken.None,false);
   Check(kz.products.Single().preview.amount==2499m&&h.urls.Single(u=>u.Contains("drop-api.ea.com")).EndsWith("?locale=en&override-country-code=KZ"),"EA region mismatch uses official public KZ price request");
   var q=await service.Compare(kz.products.Single(),new PriceSettings{accounts=new Dictionary<string,PriceAccount>{{"ea",new PriceAccount{country="KZ"}}}},false,CancellationToken.None);
   Check(q.sources.First().offers.Single().productKey=="ea:drop:fixture-game:Origin.OFR.regional","selected regional EA edition rechecked by exact offer ID");
   Check(!h.urls.Any(u=>new Uri(u).AbsolutePath.EndsWith("/buy")),"EA regional lookup makes no checkout or public buy-page request");
  }
  h=new Http{body=(r,c)=>{if(r.RequestUri.Host=="drop-api.ea.com")throw new HttpRequestException("regional unavailable");return Task.FromResult(r.RequestUri.AbsolutePath.EndsWith("/products")?Catalog():Ea());}};
  using(var service=new PriceService(h)) {
   var kz=await service.Search("ea","Fixture Game",new PriceAccount{country="KZ"},false,CancellationToken.None,false);
   Check(kz.products.Single().preview==null&&kz.products.Single().priceStatus=="region_mismatch","failed EA regional lookup cannot leak a US price");
   var q=await service.Compare(new PriceProduct{store="steam",id="123",name="Fixture Game"},new PriceSettings{accounts=new Dictionary<string,PriceAccount>{{"ea",new PriceAccount{country="KZ"}}}},false,CancellationToken.None);
   var source=q.sources.Single(s=>s.source=="ea");Check(source.status=="region_mismatch"&&source.message.Contains("KZ")&&source.message.Contains("US"),"cross-store EA comparison preserves the region mismatch reason");
  }
  h=new Http{body=(r,c)=>Task.FromResult(r.RequestUri.AbsolutePath.EndsWith("/search")?UbiCatalog():Ubi())};using(var service=new PriceService(h)){var found=await service.Search("ubisoft","Fixture Game",new PriceAccount{country="DE"},false,CancellationToken.None,false);Check(found.products.Single().preview.amount==12.34m&&h.urls.All(u=>new Uri(u).Host=="store.ubisoft.com"),"Ubisoft complete search stays on official host");}
  var fx=PriceFx.Parse("<ValCurs Date='"+DateTime.UtcNow.ToString("dd.MM.yyyy")+"'><Valute><CharCode>EUR</CharCode><Nominal>1</Nominal><Value>100</Value></Valute><Valute><CharCode>KZT</CharCode><Nominal>100</Nominal><Value>20</Value></Valute></ValCurs>",DateTime.UtcNow);PriceService.ApplyFx(ubi.preview,fx);Check(ubi.preview.rub==1234m&&ubi.preview.kzt==6170m,"publisher EUR converts to RUB and KZT without changing original");
  h=new Http{body=async(r,c)=>{if(r.RequestUri.AbsolutePath.EndsWith("/products"))return Catalog(8);await Task.Delay(40,c);var n=r.RequestUri.Segments.Last().Split('-').Last();return Ea(name:"Fixture Game "+n,labels:Label("Origin.OFR."+n));}};using(var service=new PriceService(h)){var found=await service.Search("ea","Fixture Game",new PriceAccount{country="US"},false,CancellationToken.None,false);Check(found.products.Count==6&&h.max<=3&&h.urls.Count==7,"six details maximum with three concurrent workers");}
  h=new Http{body=async(r,c)=>{if(r.RequestUri.AbsolutePath.EndsWith("/products"))return Catalog(8);await Task.Delay(1000,c);return Ea();}};using(var service=new PriceService(h))using(var cancel=new CancellationTokenSource(40)){try{await service.Search("ea","Fixture Game",new PriceAccount{country="US"},false,cancel.Token,false);throw new Exception("cancel ignored");}catch(OperationCanceledException){Check(true,"cancelled requests never become completed empty results");}}
  if(args.Contains("--live")){using(var service=new PriceService())foreach(var store in new[]{"ea","ubisoft"}){var country=store=="ea"?"US":"DE";var query=store=="ea"?"It Takes Two":"Odyssey";var found=await service.Search(store,query,new PriceAccount{country=country},true,CancellationToken.None,false);Check(found.products.Any(p=>p.preview?.amount>0),"public online quote "+store);foreach(var p in found.products)Console.WriteLine("LIVE "+store+" "+country+" | "+p.name+" | "+p.id+" | "+p.preview?.amount+" "+p.preview?.currency+" | "+p.priceMessage);}}
  Console.WriteLine("Publisher prices: "+count+" checks passed.");
 }
}
