using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Orbit {
 public sealed class HomeController:IDisposable {
  readonly string directory;readonly Func<List<HomeGame>> library;readonly Func<bool> aiReady;readonly Func<JObject,CancellationToken,Task<string>> ask;readonly Func<PriceProduct,string> register;readonly Action<object> send;
  readonly PriceService catalog;readonly object gate=new object();readonly Dictionary<string,CancellationTokenSource> pending=new Dictionary<string,CancellationTokenSource>();readonly Dictionary<string,HomeGame> known=new Dictionary<string,HomeGame>();readonly List<string> conversation=new List<string>();
  HomeFeed feed;bool disposed;bool? previousSharing;
  public HomeController(string directory,Func<List<HomeGame>> library,Func<bool> ready,Func<JObject,CancellationToken,Task<string>> ask,Func<PriceProduct,string> register,Action<object> send,PriceService service=null) {this.directory=directory;this.library=library;aiReady=ready;this.ask=ask;this.register=register;this.send=send;catalog=service??new PriceService(null,directory);}
  PriceAccount Account(){try{var s=JsonConvert.DeserializeObject<PriceSettings>(File.ReadAllText(Path.Combine(directory,"price-settings.json")));var a=s?.Account("steam")??new PriceAccount();if(!string.IsNullOrEmpty(a.country))a.country=PriceService.Country(a.country);a.currency=PriceService.Currency(a.currency);return a;}catch{return new PriceAccount();}}
  void Cancel(string lane){lock(gate){CancellationTokenSource c;if(pending.TryGetValue(lane,out c)){pending.Remove(lane);c.Cancel();c.Dispose();}}}
  public void Handle(JObject m){var kind=(string)m["kind"];if(kind=="homeCancel"){var lane=(string)m["lane"];if(new[]{"feed","query","detail"}.Contains(lane))Cancel(lane);return;}if(kind=="homeClear"){Cancel("query");lock(gate)conversation.Clear();return;}if(kind=="homeLoad"||kind=="homeSearch"||kind=="homeRecommend"||kind=="homeDetails")Start(m);}
  List<HomeGame> Library()=>library().Where(g=>!string.IsNullOrEmpty(g.id)&&!string.IsNullOrEmpty(g.name)).GroupBy(g=>g.id).Select(g=>g.First()).ToList();
  HomeGame Tag(HomeGame g,List<HomeGame> games) {
   if(g==null)return null;
   if(g.id.StartsWith("steam:")){g.owned=false;g.installed=false;g.libraryId=null;}
   if(!g.owned){var match=games.FirstOrDefault(x=>x.store=="steam"&&x.appId==g.appId)??games.Where(x=>x.store!="steam"&&PriceService.Normalize(x.name)==PriceService.Normalize(g.name)).OrderByDescending(x=>x.installed).FirstOrDefault();if(match!=null){g.owned=true;g.libraryId=match.id;g.installed=match.installed;}}
   if(!string.IsNullOrEmpty(g.appId)&&g.id.StartsWith("steam:"))g.priceToken=register(new PriceProduct{store="steam",id=g.appId,name=g.name,image=g.image,url=g.url});
   lock(gate){if(known.Count>600)known.Clear();known[g.id]=g;}return g;
  }
  async Task<HomeGame> Hydrate(HomeGame g,PriceAccount a,CancellationToken ct){
   if(g.owned&&g.store!="steam")return g;
   if(string.IsNullOrEmpty(g.appId))return g;
   var detail=await catalog.CatalogDetails(g.appId,a,ct).ConfigureAwait(false);if(detail==null)return null;
   if(g.owned){detail.id=g.id;detail.owned=true;detail.libraryId=g.libraryId??g.id;detail.installed=g.installed;detail.image=string.IsNullOrEmpty(g.image)?detail.image:g.image;detail.cover=string.IsNullOrEmpty(g.cover)?detail.cover:g.cover;}
   detail.reason=g.reason;return detail;
  }
  async void Start(JObject m) {
   string kind=(string)m["kind"],rid=(string)m["requestId"];if(string.IsNullOrEmpty(rid)||rid.Length>80||disposed)return;var lane=kind=="homeLoad"?"feed":kind=="homeDetails"?"detail":"query";Cancel(lane);var cts=new CancellationTokenSource();lock(gate)pending[lane]=cts;var ct=cts.Token;
   try {
    var games=Library();var a=Account();object result;
    if(kind=="homeLoad") {var f=await catalog.Home(a,(bool?)m["force"]==true,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();feed=f;foreach(var g in f.deals.Concat(f.popular).Concat(f.releases))Tag(g,games);result=new{kind="homeFeed",requestId=rid,data=f,aiReady=aiReady()};}
    else if(kind=="homeDetails"){
     var id=(string)m["id"];HomeGame g=games.FirstOrDefault(x=>x.id==id);if(g==null)lock(gate)known.TryGetValue(id??"",out g);if(g==null)throw new InvalidOperationException("Карточка устарела. Обнови подборку.");
     var detail=await Hydrate(g,a,ct).ConfigureAwait(false);if(detail==null)throw new InvalidOperationException("Игра сейчас недоступна в каталоге для этого региона.");Tag(detail,games);result=new{kind="homeDetail",requestId=rid,game=detail};
    }else {
     var query=RecommendationRules.Query((string)m["query"]);var mode=RecommendationRules.Mode((string)m["mode"]??"any");var only=(bool?)m["ownedOnly"]==true;var share=(bool?)m["shareLibrary"]==true;var found=new List<HomeGame>();string reply="";int excluded=0,contextCount=0;
     if(kind=="homeSearch") {
      if(query.Length>150)throw new ArgumentException("Название должно быть не длиннее 150 символов.");
      found.AddRange(games.Where(g=>PriceService.Normalize(g.name).Contains(PriceService.Normalize(query))).OrderByDescending(g=>g.installed).Take(24));
      if(!only)try{var matches=await catalog.CatalogSearch(query,a,ct).ConfigureAwait(false);foreach(var g in matches){if(found.Any(x=>PriceService.Normalize(x.name)==PriceService.Normalize(g.name)))continue;found.Add(g);}}catch{ct.ThrowIfCancellationRequested();reply="Каталог недоступен. Показаны совпадения в библиотеке.";}
      if(mode!="any") {var checkedGames=new List<HomeGame>();foreach(var g in found.Take(12)){try{var d=await Hydrate(g,a,ct).ConfigureAwait(false);if(d!=null&&RecommendationRules.Matches(d,mode,false))checkedGames.Add(d);else excluded++;}catch{ct.ThrowIfCancellationRequested();excluded++;}}found=checkedGames;}
      if(reply=="")reply=found.Count==0?"Совпадений не найдено. Попробуй оригинальное название или другой способ игры.":"Открой карточку, чтобы прочитать описание и посмотреть способы игры.";
     }else {
      if(!aiReady())throw new InvalidOperationException("Подключи Gemini через «Настроить ИИ». Используется тот же ключ, что у игрового помощника Orbit.");
      if(only&&!share)throw new ArgumentException("Для подбора по библиотеке включи передачу её названий или отключи «Только мои игры».");
      var context=share?games.OrderByDescending(g=>g.favorite).ThenByDescending(g=>g.installed).Take(300).ToList():new List<HomeGame>();contextCount=context.Count;
      if(only&&context.Count==0)throw new InvalidOperationException("В библиотеке пока нет игр. Подключи магазины или отключи «Только мои игры».");
      var currentFeed=feed;
      if(currentFeed==null||RecommendationRules.WantsNew(query)){currentFeed=await catalog.Home(a,false,ct).ConfigureAwait(false);}
      string[] history;lock(gate){if(previousSharing!=share)conversation.Clear();previousSharing=share;history=conversation.ToArray();}
      var body=RecommendationRules.Request(query,mode,only,context,history,(currentFeed?.releases??new List<HomeGame>()));
      var answer=await ask(body,ct).ConfigureAwait(false);var advice=RecommendationRules.Parse(answer,context,only);reply=advice.reply;excluded+=advice.discarded;
      foreach(var p in advice.games){ct.ThrowIfCancellationRequested();try{
       HomeGame g=p.libraryId!=""?games.FirstOrDefault(x=>x.id==p.libraryId):null;
       if(g==null){var matches=await catalog.CatalogSearch(p.name,a,ct).ConfigureAwait(false);var exact=matches.Where(x=>PriceService.Normalize(x.name)==PriceService.Normalize(p.name)).ToList();if(exact.Count!=1){excluded++;continue;}g=exact[0];}
       var verified=await Hydrate(g,a,ct).ConfigureAwait(false);if(verified==null||!RecommendationRules.Matches(verified,mode,RecommendationRules.WantsCoop(query))){excluded++;continue;}
       if(RecommendationRules.WantsNew(query)&&!currentFeed.releases.Any(x=>x.appId==verified.appId||PriceService.Normalize(x.name)==PriceService.Normalize(verified.name))){excluded++;continue;}
       verified.reason=p.reason;found.Add(verified);
      }catch{ct.ThrowIfCancellationRequested();excluded++;}}
      ct.ThrowIfCancellationRequested();lock(gate){conversation.Add(query+"\nРанее предложены: "+string.Join(", ",found.Select(x=>x.name)));if(conversation.Count>4)conversation.RemoveAt(0);}
      if(excluded>0)reply="Проверка каталога оставила "+found.Count+" игр. "+excluded+" вариантов не подтверждены по названию, доступности или способу игры и скрыты.";
      if(found.Count==0&&string.IsNullOrWhiteSpace(reply))reply="Подтверждённых вариантов пока нет. Уточни запрос или попробуй другой режим.";
     }
     ct.ThrowIfCancellationRequested();foreach(var g in found)Tag(g,games);result=new{kind="homeResults",requestId=rid,games=found,reply,contextCount,totalLibrary=games.Count,ai=kind=="homeRecommend"};
    }
    lock(gate){CancellationTokenSource active;if(!disposed&&pending.TryGetValue(lane,out active)&&active==cts&&!ct.IsCancellationRequested)send(result);}
   }catch(Exception e){lock(gate){CancellationTokenSource active;if(!disposed&&pending.TryGetValue(lane,out active)&&active==cts&&!ct.IsCancellationRequested)send(new{kind="homeError",requestId=rid,lane,message=e is ArgumentException||e is InvalidOperationException?e.Message:"Не удалось получить ответ. Проверь сеть и повтори запрос."});}}
   finally{lock(gate){CancellationTokenSource active;if(pending.TryGetValue(lane,out active)&&active==cts){pending.Remove(lane);cts.Dispose();}}}
  }
  public void Dispose(){lock(gate){disposed=true;foreach(var p in pending.Values){p.Cancel();p.Dispose();}pending.Clear();catalog.Dispose();}}
 }
}
