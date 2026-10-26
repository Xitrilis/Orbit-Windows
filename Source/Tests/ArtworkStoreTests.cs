using System;using System.IO;using System.Linq;using System.Collections.Generic;using Orbit;
class ArtworkStoreTests {
 static int n;static void Check(bool yes,string text){if(!yes)throw new Exception(text);n++;}
 static void Throws(Action a,string text){try{a();}catch{n++;return;}throw new Exception(text);}
 static Dictionary<string,ArtworkChange> Changes()=>ArtworkStore.Slots.ToDictionary(s=>s,s=>new ArtworkChange());
 static readonly Dictionary<string,string> Original=new Dictionary<string,string>{{"icon","original-icon"},{"cover","original-cover"},{"background","original-banner"}};
 static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6lTQAAAAASUVORK5CYII=");
 static void Main(){
  var dir=Path.Combine(Path.GetTempPath(),"orbit-art-test-"+Guid.NewGuid());var a=Guid.NewGuid().ToString();var b=Guid.NewGuid().ToString();
  Directory.CreateDirectory(dir);
  try{
   var store=new ArtworkStore(dir);Check(store.View(a,Original)["cover"].url=="original-cover","Defaults intact");
   var s=store.Begin(a);var token=store.Stage(s,"cover",Png);var uri=store.Preview(s,token);Check(File.Exists(new Uri(uri).LocalPath),"Staged preview exists");
   store.Cancel(s);Check(!File.Exists(new Uri(uri).LocalPath),"Cancel removes unsaved asset");Throws(()=>store.Save(s,new[]{a},Changes()),"Closed session cannot save");
   s=store.Begin(a);token=store.Stage(s,"cover",Png);var c=Changes();c["cover"]=new ArtworkChange{mode="set",token=token,x=21,y=75,zoom=1.4};store.Save(s,new[]{a,b},c);
   var va=store.View(a,Original);var vb=store.View(b,Original);Check(va["cover"].custom&&va["cover"].url==vb["cover"].url,"All copies share selected image");Check(va["cover"].x==21&&va["cover"].y==75&&va["cover"].zoom==1.4,"Crop persisted");Check(va["cover"].original=="original-cover","Original source retained");
   Check(!store.Active(s),"Session consumed on save");Throws(()=>store.Stage(s,"cover",Png),"Stale stage rejected");
   store.Dispose();store=new ArtworkStore(dir);Check(store.View(a,Original)["cover"].url==va["cover"].url,"Image survives restart");Check(File.Exists(new Uri(va["cover"].url).LocalPath),"Owned copy survives restart");
   s=store.Begin(b);token=store.Stage(s,"icon",Png);c=Changes();c["icon"]=new ArtworkChange{mode="set",token=token};store.Save(s,new[]{b},c);var iconB=store.View(b,Original)["icon"].url;
   s=store.Begin(a);c=Changes();c["background"]=new ArtworkChange{mode="adjust",x=12,y=90,zoom=1.2};store.Save(s,new[]{a,b},c);
   Check(store.View(a,Original)["icon"].url=="original-icon"&&store.View(b,Original)["icon"].url==iconB,"Untouched slots preserve each copy's own art");
   Check(store.View(a,Original)["background"].url=="original-banner"&&store.View(b,Original)["background"].x==12,"Original image can be reframed without copying");
   s=store.Begin(a);token=store.Stage(s,"cover",Png);c=Changes();c["icon"]=new ArtworkChange{mode="set",token=token};Throws(()=>store.Save(s,new[]{a},c),"Token cannot target another slot");
   c=Changes();c["cover"]=new ArtworkChange{mode="set",token="../../outside"};Throws(()=>store.Save(s,new[]{a},c),"Arbitrary paths rejected");
   c=Changes();c["cover"]=new ArtworkChange{mode="adjust",x=double.NaN};var before=File.ReadAllText(Path.Combine(dir,"artwork.json"));Throws(()=>store.Save(s,new[]{a},c),"NaN rejected");Check(File.ReadAllText(Path.Combine(dir,"artwork.json"))==before,"Validation failure leaves saved state intact");
   c["cover"].x=50;c["cover"].zoom=3;Throws(()=>store.Save(s,new[]{a},c),"Invalid zoom rejected");
   Throws(()=>store.Save(s,new[]{b},Changes()),"Edited game must belong to target copies");
   Throws(()=>store.Stage(s,"script",Png),"Unknown slot rejected");Throws(()=>store.Stage(s,"icon",new byte[]{1,2,3}),"Non-PNG staging rejected");
   c=Changes();c["cover"]=new ArtworkChange{mode="set",token=token};Directory.CreateDirectory(Path.Combine(dir,"artwork.json.tmp"));
   Throws(()=>store.Save(s,new[]{a},c),"Disk failure reported");Check(store.Active(s)&&store.View(a,Original)["cover"].url==va["cover"].url,"Disk failure preserves original state and retry");
   Directory.Delete(Path.Combine(dir,"artwork.json.tmp"));store.Save(s,new[]{a},c);Check(store.View(a,Original)["cover"].url!=va["cover"].url,"Retry succeeds with same staged image");
   s=store.Begin(a);c=Changes();c["cover"]=new ArtworkChange{mode="original"};store.Save(s,new[]{a},c);
   Check(!store.View(a,Original)["cover"].custom&&store.View(a,Original)["cover"].url=="original-cover","Reset returns current store image");
   Check(store.View(b,Original)["cover"].custom,"Reset does not delete another game's shared asset");
   s=store.Begin(a);c=Changes();foreach(var slot in ArtworkStore.Slots)c[slot].mode="original";store.Save(s,new[]{a},c);
   Check(store.View(a,Original).Values.All(v=>!v.custom&&v.x==50&&v.y==50&&v.zoom==1),"Reset all restores default crop");
   store.Dispose();File.WriteAllText(Path.Combine(dir,"artwork.json"),"{broken");store=new ArtworkStore(dir);
   Check(store.LoadError==null,"Valid backup recovers corrupted manifest");Check(File.Exists(new Uri(store.View(b,Original)["icon"].url).LocalPath),"Backup image preserved for recovery");store.Dispose();
   File.WriteAllText(Path.Combine(dir,"artwork.json.bak"),"{broken");store=new ArtworkStore(dir);Check(store.LoadError!=null,"Corrupt primary and backup disable writes");
   s=store.Begin(a);Throws(()=>store.Save(s,new[]{a},Changes()),"Unreadable settings never overwritten");store.Dispose();
   Console.WriteLine("Artwork store PASS: "+n+" checks, restart/cancel/reset, copies, crop, validation, rollback and backup recovery.");
  } finally{Directory.Delete(dir,true);}
 }
}
