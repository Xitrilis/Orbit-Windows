using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Orbit;
class HomeTests {
 static int count;
 static void Check(bool v,string message="Assertion failed"){if(!v)throw new Exception(message);}
 static void Test(string name,Action a){a();count++;Console.WriteLine("PASS "+name);}
 static void Throws(Action a){try{a();}catch{return;}throw new Exception("Expected rejection");}
 sealed class Http:HttpMessageHandler {
  public readonly List<string> urls=new List<string>();public Func<string,string> respond;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){ct.ThrowIfCancellationRequested();var u=r.RequestUri.AbsoluteUri;urls.Add(u);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(respond(u))});}
 }
 static HomeGame Owned()=>new HomeGame{id="owned",libraryId="owned",name="Portal 2",appId="620",owned=true,description="SECRET LOCAL DESCRIPTION",image="file:///private/picture.jpg",features=new[]{"Online Co-op","Shared/Split Screen Co-op","Single-player"},genres=new[]{"Puzzle"}};
 static string Details(string type="game",bool windows=true,int[] features=null)=>new JObject{["620"]=new JObject{["success"]=true,["data"]=new JObject{["type"]=type,["name"]="Portal 2",["platforms"]=new JObject{["windows"]=windows},["short_description"]="<b>Puzzles</b> &amp; portals",["header_image"]="https://example.invalid/cover.jpg",["categories"]=new JArray((features??new[]{2,1,9,38,39}).Select(x=>new JObject{["id"]=x,["description"]="label"})),["genres"]=new JArray(new JObject{["description"]="Puzzle"}),["release_date"]=new JObject{["date"]="19 Apr, 2011"},["price_overview"]=new JObject{["initial"]=10000,["final"]=5000,["currency"]="KZT"}}}}.ToString();
 static string Feed()=>"{'specials':{'items':[{'id':620,'type':0,'name':'Portal 2','windows_available':true,'discounted':true,'original_price':10000,'final_price':5000,'currency':'KZT'}]},'top_sellers':{'items':[{'id':620,'type':0,'name':'Portal 2','windows_available':true},{'id':9,'type':1,'name':'DLC','windows_available':true}]},'new_releases':{'items':[{'id':620,'type':0,'name':'Portal 2','windows_available':true}]}}";
 static Http Transport()=>new Http{respond=u=>u.Contains("featuredcategories")?Feed():u.Contains("appdetails")?Details():u.Contains("storesearch")?"{'items':[{'id':620,'type':'app','name':'Portal 2','tiny_image':'https://example.invalid/tiny.jpg'}]}":"invalid calendar"};
 static string Advice(string id="owned",string name="Portal 2")=>new JObject{["reply"]="Подойдут головоломки.",["games"]=new JArray(new JObject{["name"]=name,["libraryId"]=id,["reason"]="Головоломки для совместной игры."})}.ToString();
 static async Task<JObject> Run(HomeController c,List<JObject> sent,JObject m){int n;lock(sent)n=sent.Count;c.Handle(m);for(int i=0;i<200;i++){lock(sent)if(sent.Count>n)return sent.Last();await Task.Delay(10);}throw new Exception("Controller did not respond");}
 static async Task Main(string[] args) {
  var owned=Owned();var library=new[]{owned};var kz=new PriceAccount{country="KZ",currency="KZT"};
  Test("query and mode bounds",()=>{Throws(()=>RecommendationRules.Query("hi"));Throws(()=>RecommendationRules.Query(new string('x',1201)));Throws(()=>RecommendationRules.Mode("pvp"));Check(RecommendationRules.Query("  Portal 2  ")=="Portal 2");});
  Test("Steam single player category is 2, not multiplayer 1",()=>{var solo=PriceService.ParseCatalogDetails(Details(features:new[]{2}),"620",kz);var multi=PriceService.ParseCatalogDetails(Details(features:new[]{1}),"620",kz);Check(RecommendationRules.Matches(solo,"solo",false));Check(!RecommendationRules.Matches(multi,"solo",false));});
  Test("cooperative modes are checked separately",()=>{var online=new HomeGame{features=new[]{"Online Co-op"}};Check(RecommendationRules.Matches(online,"online",true));Check(!RecommendationRules.Matches(online,"local",true));Check(!RecommendationRules.Matches(new HomeGame{features=new[]{"Multi-player"}},"any",true));Check(!RecommendationRules.Matches(new HomeGame(),"online",true));});
  Test("query intent",()=>{Check(RecommendationRules.WantsCoop("вдвоём с девушкой"));Check(RecommendationRules.WantsNew("Новинки игр"));Check(!RecommendationRules.WantsNew("Portal 2"));});
  Test("structured JSON and private metadata excluded",()=>{var body=RecommendationRules.Request("Головоломки на двоих","local",true,library,new string[0],new HomeGame[0]);var json=body.ToString();Check((string)body.SelectToken("generationConfig.responseMimeType")=="application/json");Check(!json.Contains("SECRET")&&!json.Contains("private/picture"));var u=JObject.Parse((string)body.SelectToken("contents[0].parts[0].text"));Check(u["library"].Count()==1&&(string)u["library"][0]["id"]=="owned");});
  Test("context bounded to 300 and 4 messages",()=>{var b=RecommendationRules.Request("Puzzle games","any",false,Enumerable.Range(0,500).Select(i=>new HomeGame{id=i.ToString(),name="Game",genres=null,features=null}),Enumerable.Range(0,8).Select(i=>i.ToString()),new HomeGame[0]);var u=JObject.Parse((string)b.SelectToken("contents[0].parts[0].text"));Check(u["library"].Count()==300&&u["previousRequests"].Count()==4);});
  Test("library opt-out has no titles or IDs",()=>{var b=RecommendationRules.Request("Хочу играть","any",false,new HomeGame[0],new string[0],new HomeGame[0]);var u=JObject.Parse((string)b.SelectToken("contents[0].parts[0].text"));Check(u["library"].Count()==0&&!b.ToString().Contains("Portal 2"));});
  Test("unknown and swapped ownership IDs rejected",()=>{var p=RecommendationRules.Parse(Advice("fabricated"),library,false);Check(p.games.Count==0&&p.discarded==1);Check(RecommendationRules.Parse(Advice("owned","Portal 2 Deluxe"),library,false).games.Count==0);Check(RecommendationRules.Parse(Advice(),library,true).games.Count==1);});
  Test("owned-only rejects model suggestions outside library",()=>Check(RecommendationRules.Parse(Advice(""),library,true).games.Count==0));
  Test("malformed and oversized output rejected",()=>{Throws(()=>RecommendationRules.Parse("oops",library,false));Throws(()=>RecommendationRules.Parse("{}",library,false));Throws(()=>RecommendationRules.Parse(new string('x',30001),library,false));});
  Test("duplicate suggestions removed",()=>{var j=JObject.Parse(Advice());((JArray)j["games"]).Add(j["games"][0].DeepClone());Check(RecommendationRules.Parse(j.ToString(),library,false).games.Count==1);});
  Test("DLC, non-Windows and missing products excluded",()=>{Check(PriceService.ParseCatalogDetails(Details("dlc"),"620",kz)==null);Check(PriceService.ParseCatalogDetails(Details(windows:false),"620",kz)==null);Check(PriceService.ParseCatalogDetails("{'620':{'success':false}}","620",kz)==null);});
  Test("description strips HTML, regional currency kept",()=>{var g=PriceService.ParseCatalogDetails(Details(),"620",kz);Check(g.description=="Puzzles & portals"&&g.price==50&&g.currency=="KZT");});
  Test("unconfigured region and currency mismatch hide price",()=>{Check(PriceService.ParseCatalogDetails(Details(),"620",new PriceAccount()).price==null);Check(PriceService.ParseCatalogDetails(Details(),"620",new PriceAccount{country="KZ",currency="USD"}).price==null);});
  using(var service=new PriceService(Transport())){var f=await service.Home(kz,false,CancellationToken.None);Test("feed drops non-game entries, keeps deals and real prices",()=>Check(f.popular.Count==1&&f.deals.Single().price==50&&f.deals.Single().originalPrice==100));var noRegion=await service.Home(new PriceAccount(),false,CancellationToken.None);Test("catalogue fallback explicit and no assumed account prices",()=>Check(!noRegion.regionSet&&noRegion.deals.Single().price==null&&noRegion.message.Contains("US")));Throws(()=>service.CatalogDetails("bad/url",kz,CancellationToken.None).GetAwaiter().GetResult());}
  var dir=Path.Combine(Path.GetTempPath(),"orbit-home-"+Guid.NewGuid());Directory.CreateDirectory(dir);
  try {
   var sent=new List<JObject>();JObject body=null;var all=new List<HomeGame>{owned};int asks=0;
   using(var c=new HomeController(dir,()=>all,()=>true,(b,ct)=>{body=b;asks++;return Task.FromResult(Advice((bool)JObject.Parse((string)b.SelectToken("contents[0].parts[0].text"))["ownedOnly"]?"owned":""));},p=>"safe-token",o=>{lock(sent)sent.Add(JObject.FromObject(o));},new PriceService(Transport()))) {
    var m=await Run(c,sent,new JObject{["kind"]="homeLoad",["requestId"]="feed"});Test("load does not call Gemini and marks owned catalogue game",()=>Check(asks==0&&(bool)m.SelectToken("data.deals[0].owned")&&(string)m.SelectToken("data.deals[0].libraryId")=="owned"));
    all.Clear();m=await Run(c,sent,new JObject{["kind"]="homeLoad",["requestId"]="feed2"});Test("ownership recalculated after library change",()=>Check(!(bool)m.SelectToken("data.deals[0].owned")));all.Add(Owned());
    m=await Run(c,sent,new JObject{["kind"]="homeRecommend",["requestId"]="q1",["query"]="Головоломки вдвоём",["mode"]="local",["shareLibrary"]=true,["ownedOnly"]=true});Test("verified recommendation preserves library ID",()=>Check((string)m["kind"]=="homeResults"&&m["games"].Count()==1&&(string)m["games"][0]["libraryId"]=="owned"));
    m=await Run(c,sent,new JObject{["kind"]="homeRecommend",["requestId"]="q2",["query"]="Головоломки вдвоём",["mode"]="local",["shareLibrary"]=false,["ownedOnly"]=false});var prompt=JObject.Parse((string)body.SelectToken("contents[0].parts[0].text"));Test("opt-out clears prior context and sends no library",()=>Check(prompt["library"].Count()==0&&prompt["previousRequests"].Count()==0&&m["games"].Count()==1));
    var before=asks;m=await Run(c,sent,new JObject{["kind"]="homeRecommend",["requestId"]="q3",["query"]="Только мои игры",["mode"]="any",["shareLibrary"]=false,["ownedOnly"]=true});Test("conflicting privacy setting stops before API",()=>Check((string)m["kind"]=="homeError"&&asks==before));
    m=await Run(c,sent,new JObject{["kind"]="homeDetails",["requestId"]="detail",["id"]="https://evil.invalid"});Test("details cannot request arbitrary URL",()=>Check((string)m["kind"]=="homeError"));
    m=await Run(c,sent,new JObject{["kind"]="homeSearch",["requestId"]="search",["query"]="Portal",["mode"]="solo",["ownedOnly"]=true});Test("title search needs no Gemini and includes owned descriptions",()=>Check(asks==before&&m["games"].Count()==1&&(string)m["games"][0]["description"]=="Puzzles & portals"));
   }
   sent.Clear();var slow=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);using(var c=new HomeController(dir,()=>new List<HomeGame>{Owned()},()=>true,(b,ct)=>slow.Task,p=>"token",o=>{lock(sent)sent.Add(JObject.FromObject(o));},new PriceService(Transport()))){
    c.Handle(new JObject{["kind"]="homeRecommend",["requestId"]="slow",["query"]="Найди игру",["mode"]="any",["shareLibrary"]=true});c.Handle(new JObject{["kind"]="homeCancel",["lane"]="query"});slow.SetResult(Advice());await Task.Delay(100);Test("cancelled AI result suppressed",()=>{lock(sent)Check(sent.Count==0);});
   }
  }finally{Directory.Delete(dir,true);}
  if(args.Contains("--live"))using(var service=new PriceService()){var f=await service.Home(kz,true,CancellationToken.None);var g=await service.CatalogDetails("620",kz,CancellationToken.None);var search=await service.CatalogSearch("Batman Arkham Knight",kz,CancellationToken.None);Check(f.deals.Count>0&&g.name=="Portal 2"&&search.Any(x=>x.appId=="208650"));Console.WriteLine("LIVE PASS: Steam deals "+f.deals.Count+", popular "+f.popular.Count+", releases "+f.releases.Count+", Portal 2 details and Batman search. No Gemini/account/purchase request.");}
  Console.WriteLine(count+" HOME TESTS PASSED");
 }
}
