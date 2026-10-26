using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
namespace Orbit {
 public sealed class HomeFeed {
  public SaleCalendar calendar;public List<HomeGame> deals=new List<HomeGame>(),popular=new List<HomeGame>(),releases=new List<HomeGame>();
  public string country,currency,message;public bool regionSet;
 }
 public sealed partial class PriceService {
  static PriceAccount CatalogAccount(PriceAccount a)=>new PriceAccount{country=string.IsNullOrEmpty(a.country)?"US":a.country,currency=a.currency};
  static bool HomePrice(PriceAccount a,PriceOffer o)=>!string.IsNullOrEmpty(a.country)&&(string.IsNullOrEmpty(a.currency)||o?.currency==a.currency||o?.amount==0);
  public static HomeGame CatalogProduct(PriceProduct p,bool price)=>new HomeGame{id="steam:"+p.id,appId=p.id,name=p.name,image=p.image,cover=p.image,url=p.url,price=price?p.preview?.amount:null,currency=price?p.preview?.currency:null,originalPrice=price?p.preview?.originalAmount:null};
  public async Task<HomeFeed> Home(PriceAccount a,bool force,CancellationToken ct) {
   var result=new HomeFeed{country=a.country,currency=a.currency,regionSet=!string.IsNullOrEmpty(a.country)};var ca=CatalogAccount(a);var calendar=Calendar(force,ct);
   try {
    var raw=await Get("https://store.steampowered.com/api/featuredcategories?cc="+Country(ca.country).ToLowerInvariant()+"&l=english",force,ct).ConfigureAwait(false);
    var json=JObject.Parse(raw.body);var specials=ParseSteamSpecials(raw.body,ca.country,raw.at).Take(8).ToList();foreach(var p in specials)CheckCurrency(p.preview,a);result.deals=specials.Select(p=>CatalogProduct(p,HomePrice(a,p.preview))).ToList();
    Func<string,List<HomeGame>> section=key=>((json.SelectToken(key+".items") as JArray)??new JArray()).Where(p=>(int?)p["type"]==0&&(bool?)p["windows_available"]==true&&Regex.IsMatch((string)p["id"]??"","^[0-9]{1,12}$")).Take(8).Select(p=>new HomeGame{id="steam:"+(string)p["id"],appId=(string)p["id"],name=Text((string)p["name"],180),image=(string)p["large_capsule_image"]??(string)p["header_image"],url="https://store.steampowered.com/app/"+(string)p["id"]+"/"}).ToList();
    result.popular=section("top_sellers");result.releases=section("new_releases");
    result.message=result.regionSet?"Подборка витрины Steam · "+a.country:"Каталог Steam (US). Выбери регион аккаунта, чтобы видеть свои цены.";
   }catch{ct.ThrowIfCancellationRequested();result.message="Каталог временно недоступен. Библиотека и поиск по ней продолжают работать.";}
   result.calendar=await calendar.ConfigureAwait(false);return result;
  }
  public async Task<List<HomeGame>> CatalogSearch(string query,PriceAccount a,CancellationToken ct) {
   var found=await Search("steam",query,CatalogAccount(a),false,ct,false).ConfigureAwait(false);
   return found.products.Take(12).Select(p=>CatalogProduct(p,HomePrice(a,p.preview))).ToList();
  }
  public static HomeGame ParseCatalogDetails(string json,string id,PriceAccount a) {
   var node=JObject.Parse(json)[id];var p=node?["data"];if((bool?)node?["success"]!=true||p==null||(string)p["type"]!="game"||(bool?)p.SelectToken("platforms.windows")!=true)return null;
   var country=CatalogAccount(a).country;var o=SteamOffer(p["price_overview"],Text((string)p["name"]),"https://store.steampowered.com/app/"+id+"/",country,DateTime.UtcNow,(bool?)p["is_free"]==true);
   CheckCurrency(o,a);
   return new HomeGame{id="steam:"+id,appId=id,name=Text((string)p["name"],180),image=(string)p["header_image"],cover=(string)p["header_image"],description=Text((string)p["short_description"],2200),url="https://store.steampowered.com/app/"+id+"/",release=Text((string)p.SelectToken("release_date.date"),100),genres=((p["genres"] as JArray)??new JArray()).Select(x=>Text((string)x["description"],100)).ToArray(),features=((p["categories"] as JArray)??new JArray()).Select(x=>Feature((int?)x["id"],(string)x["description"])).ToArray(),price=HomePrice(a,o)?o?.amount:null,currency=HomePrice(a,o)?o?.currency:null};
  }
  static string Feature(int? id,string label){switch(id){case 2:return "Single-player";case 9:return "Co-op";case 38:return "Online Co-op";case 39:return "Shared/Split Screen Co-op";default:return Text(label,100);}}
  public async Task<HomeGame> CatalogDetails(string id,PriceAccount a,CancellationToken ct) {
   if(!Regex.IsMatch(id??"","^[0-9]{1,12}$"))throw new ArgumentException("Неверный ID игры.");
   var raw=await Get("https://store.steampowered.com/api/appdetails?appids="+id+"&cc="+Country(CatalogAccount(a).country).ToLowerInvariant()+"&l=russian",false,ct,5).ConfigureAwait(false);
   return ParseCatalogDetails(raw.body,id,a);
  }
 }
}
