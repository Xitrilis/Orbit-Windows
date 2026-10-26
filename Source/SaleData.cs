using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Parser.Html;
using Newtonsoft.Json;
namespace Orbit
{
 public sealed class SaleEvent { public string source,name,url,start,end,scope; public DateTime checkedAt; }
 public sealed class SaleCalendar { public List<SaleEvent> events=new List<SaleEvent>(); public string message; public bool cached; public DateTime checkedAt; }
 public sealed class PricePromotion { public DateTime start,end; public decimal? amount; public string currency,url,note; }
 public sealed class PriceObservation { public decimal amount; public decimal? originalAmount; public DateTime at; }
 public sealed class PriceHistoryView { public PriceObservation previousDiscount,previousLower,lowest; public string message; public int observations; }
 // Bounded, actual observations, isolated by SKU/listing, region and currency. Never a complete store history.
 public sealed class PriceHistoryStore
 {
  readonly string path; readonly object gate=new object(); bool blocked;
  Dictionary<string,List<PriceObservation>> rows=new Dictionary<string,List<PriceObservation>>();
  public PriceHistoryStore(string directory) {
   if(string.IsNullOrEmpty(directory))return;path=Path.Combine(directory,"price-history.json");
   if(!File.Exists(path)&&!File.Exists(path+".bak"))return;
   try{rows=Load(path);}catch{try{rows=Load(path+".bak");}catch{blocked=true;}}
  }
  static Dictionary<string,List<PriceObservation>> Load(string file) {
   if(new FileInfo(file).Length>16*1024*1024)throw new InvalidDataException();
   var v=JsonConvert.DeserializeObject<Dictionary<string,List<PriceObservation>>>(File.ReadAllText(file));
   if(v==null||v.Count>1000||v.Any(p=>p.Key.Length>2000||p.Value==null||p.Value.Count>96||p.Value.Any(x=>x==null||x.amount<0||x.amount>1000000000m||x.originalAmount<0||x.at.Year<2000||x.at.Year>2100)))throw new InvalidDataException();return v;
  }
  static string Key(PriceOffer o)=>string.Join("|",o.source,o.productKey??o.url,o.country??"",o.currency,o.kind,o.region,o.platform);
  public void Observe(IEnumerable<PriceOffer> input,DateTime now) {
   var offers=input.Where(o=>o!=null).ToList();lock(gate){
    var next=rows.ToDictionary(p=>p.Key,p=>p.Value.ToList());
    foreach(var o in offers.Where(o=>o.amount.HasValue&&o.amount>=0&&o.amount<1000000000m&&Regex.IsMatch(o.currency??"","^[A-Z]{3}$")&&!string.IsNullOrEmpty(o.productKey??o.url)&&o.checkedAt<=now.AddMinutes(5)&&o.checkedAt>=now.AddDays(-1))) {
     var key=Key(o);if(key.Length>2000)continue;List<PriceObservation> list;if(!next.TryGetValue(key,out list))next[key]=list=new List<PriceObservation>();
     if(!list.Any(x=>x.at==o.checkedAt)) {
      var last=list.OrderByDescending(x=>x.at).FirstOrDefault();
      if(last!=null&&last.amount==o.amount&&last.originalAmount==o.originalAmount&&last.at.Date==o.checkedAt.Date&&last.at<o.checkedAt)list.Remove(last);
      list.Add(new PriceObservation{amount=o.amount.Value,originalAmount=o.originalAmount,at=o.checkedAt});
     }
     next[key]=list.Where(x=>x.at>=now.AddYears(-2)&&x.at<=now.AddMinutes(5)).OrderByDescending(x=>x.at).Take(96).OrderBy(x=>x.at).ToList();
    }
    next=next.Where(p=>p.Value.Count>0).OrderByDescending(p=>p.Value.Max(x=>x.at)).Take(1000).ToDictionary(p=>p.Key,p=>p.Value);
    var message=blocked?"История повреждена. Новые наблюдения не записываются; текущие цены доступны.":path==null?"Наблюдения только за текущий сеанс.":"Наблюдения Orbit для этого товара, региона и валюты. Это не полная история магазина; данные до первой проверки отсутствуют.";
    if(!blocked)try{if(path!=null){Directory.CreateDirectory(Path.GetDirectoryName(path));var tmp=path+".tmp";var json=JsonConvert.SerializeObject(next);if(System.Text.Encoding.UTF8.GetByteCount(json)>16*1024*1024)throw new InvalidDataException();File.WriteAllText(tmp,json);if(File.Exists(path))File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);}rows=next;}catch{message="Не удалось сохранить историю. Текущая цена получена; прежние наблюдения оставлены без изменений.";}
    foreach(var o in offers){List<PriceObservation> list;rows.TryGetValue(Key(o),out list);list=(list??new List<PriceObservation>()).Where(x=>x.at<=now.AddMinutes(5)).ToList();o.history=new PriceHistoryView{previousDiscount=list.Where(x=>x.at<o.checkedAt&&x.originalAmount>x.amount).OrderByDescending(x=>x.at).FirstOrDefault(),previousLower=list.Where(x=>x.at<o.checkedAt&&x.amount<o.amount).OrderByDescending(x=>x.at).FirstOrDefault(),lowest=list.OrderBy(x=>x.amount).ThenByDescending(x=>x.at).FirstOrDefault(),observations=list.Count,message=message};}
   }
  }
 }
 public static class SteamSaleCalendar
 {
  public const string Url="https://partner.steamgames.com/doc/marketing/upcoming_events?l=english";
  static bool Date(string s,out DateTime d)=>DateTime.TryParseExact(s.Trim(),new[]{"MMMM d, yyyy","d MMMM, yyyy","MMM d yyyy","MMMM d yyyy"},CultureInfo.InvariantCulture,DateTimeStyles.None,out d);
  static SaleEvent Event(string name,DateTime from,DateTime to,string scope,DateTime at)=>new SaleEvent{source="steam",name=name,start=from.ToString("yyyy-MM-dd"),end=to.ToString("yyyy-MM-dd"),scope=scope,checkedAt=at,url=scope=="seasonal"?"https://partner.steamgames.com/doc/marketing/discounts/seasonalsales":Url};
  public static List<SaleEvent> Parse(string html,DateTime at) {
   var doc=new HtmlParser().Parse(html);var list=new List<SaleEvent>();
   foreach(var h in doc.QuerySelectorAll("h2")) {
    var text=Regex.Replace(h.TextContent,@"\s+"," ").Trim();var m=Regex.Match(text,@"^(Winter|Spring|Summer|Autumn) Sale (20\d{2})\s*\|\s*(.+?)\s*[-–]\s*(.+)$");DateTime from,to;
    if(m.Success&&Date(m.Groups[3].Value,out from)&&Date(m.Groups[4].Value,out to)&&to>=from&&to<=from.AddDays(31)){
     var names=new Dictionary<string,string>{{"Winter","Зимняя распродажа"},{"Spring","Весенняя распродажа"},{"Summer","Летняя распродажа"},{"Autumn","Осенняя распродажа"}};list.Add(Event(names[m.Groups[1].Value]+" "+m.Groups[2].Value,from,to,"seasonal",at));
    }
    var y=Regex.Match(text,@"^(20\d{2}) Fests$");if(!y.Success)continue;var table=h.NextElementSibling;if(table==null||table.LocalName!="table")continue;var year=int.Parse(y.Groups[1].Value);
    foreach(var row in table.QuerySelectorAll("tr")){var cells=row.QuerySelectorAll("td");if(cells.Length<2)continue;var dates=Regex.Matches(cells[0].InnerHtml,@"\b[A-Z][a-z]{2}\s+\d{1,2}\b");if(dates.Count!=2||!Date(dates[0].Value+" "+year,out from)||!Date(dates[1].Value+" "+year,out to))continue;if(to<from)to=to.AddYears(1);if(to>from.AddDays(31))continue;var name=cells[1].TextContent.Trim();if(name.Length>0&&name.Length<160)list.Add(Event(name,from,to,"themed",at));}
   }
   if(!list.Any(x=>x.scope=="seasonal"))throw new InvalidDataException();return list.GroupBy(x=>x.name+x.start).Select(x=>x.First()).Take(80).ToList();
  }
  public static SaleCalendar Bundled(){var at=new DateTime(2026,10,24,0,0,0,DateTimeKind.Utc);return new SaleCalendar{cached=true,checkedAt=at,message="Сохранённый календарь от 24.10.2026. Свежие даты не получены — проверь анонс магазина.",events=new List<SaleEvent>{
   Event("Осенняя распродажа 2026",new DateTime(2026,10,1),new DateTime(2026,10,8),"seasonal",at),
   Event("Steam Scream V",new DateTime(2026,10,26),new DateTime(2026,11,2),"themed",at),
   Event("Auto-Battler RPG Fest",new DateTime(2026,11,16),new DateTime(2026,11,23),"themed",at),
   Event("Зимняя распродажа 2026",new DateTime(2026,12,17),new DateTime(2027,1,4),"seasonal",at),
   Event("Весенняя распродажа 2027",new DateTime(2027,3,18),new DateTime(2027,3,25),"seasonal",at),
   Event("Летняя распродажа 2027",new DateTime(2027,6,24),new DateTime(2027,7,8),"seasonal",at)}};}
 }
}
