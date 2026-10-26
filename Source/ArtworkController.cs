using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Threading.Tasks;using System.Windows;using System.Windows.Media;using System.Windows.Media.Imaging;using Newtonsoft.Json.Linq;using Playnite.SDK.Models;using Forms=System.Windows.Forms;
namespace Orbit {
 public sealed class ArtworkController:IDisposable {
  readonly ArtworkStore store;readonly Func<string,Game> game;readonly Func<string,string> art;readonly Func<Window> owner;readonly Action<object> send;readonly Action changed;bool busy,disposed;
  public ArtworkController(ArtworkStore store,Func<string,Game> game,Func<string,string> art,Func<Window> owner,Action<object> send,Action changed){this.store=store;this.game=game;this.art=art;this.owner=owner;this.send=send;this.changed=changed;}
  public Dictionary<string,ArtworkView> View(Game g)=>store.View(g.Id.ToString(),new Dictionary<string,string>{{"icon",art(g.Icon)},{"cover",art(g.CoverImage)},{"background",art(g.BackgroundImage)}});
  public async void Handle(JObject m){
   var kind=(string)m["kind"];var requestId=(string)m["requestId"];var session=(string)m["session"];
   try{
    if(kind=="artworkOpen"){var g=game((string)m["id"]);if(store.LoadError!=null)throw new InvalidOperationException(store.LoadError);session=store.Begin(g.Id.ToString());send(new{kind="artworkOpened",requestId,session,artwork=View(g)});return;}
    if(kind=="artworkCancel"){store.Cancel(session);return;}if(!store.Active(session))return;
    if(kind=="artworkPick"){
     if(busy)throw new InvalidOperationException("Дождись обработки изображения.");var slot=(string)m["slot"];if(!ArtworkStore.Slots.Contains(slot))throw new InvalidDataException("Неизвестный вид изображения.");string path;
     using(var dialog=new Forms.OpenFileDialog{Title="Выбери "+(slot=="icon"?"значок":slot=="cover"?"обложку":"баннер")+" игры",Filter=slot=="icon"?"Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.ico":"Изображения|*.png;*.jpg;*.jpeg;*.bmp",CheckFileExists=true,Multiselect=false,RestoreDirectory=true}){
      if(dialog.ShowDialog(new WindowOwner(owner()))!=Forms.DialogResult.OK){send(new{kind="artworkPicked",requestId,session,cancelled=true});return;}path=dialog.FileName;
     }
     busy=true;byte[] png;try{png=await Task.Run(()=>Normalize(path,slot));}finally{busy=false;}if(disposed||!store.Active(session))return;
     var token=store.Stage(session,slot,png);send(new{kind="artworkPicked",requestId,session,slot,token,url=store.Preview(session,token),name=Path.GetFileName(path)});return;
    }
    if(kind=="artworkSave"){
     if(busy)throw new InvalidOperationException("Изображение ещё обрабатывается.");var ids=((JArray)m["ids"])?.Values<string>().ToArray();if(ids==null||ids.Length>100)throw new InvalidDataException("Слишком много копий игры.");foreach(var id in ids)game(id);
     store.Save(session,ids,m["changes"]?.ToObject<Dictionary<string,ArtworkChange>>());changed();send(new{kind="artworkSaved",requestId,session});
    }
   }catch(Exception e){if(!disposed)send(new{kind="artworkError",requestId,session,message=e is InvalidDataException||e is InvalidOperationException?e.Message:"Не удалось обработать или сохранить изображение. Выбери PNG/JPG/BMP до 20 МБ и проверь доступ к папке Orbit."});}
  }
  static byte[] Normalize(string path,string slot){
   var ext=Path.GetExtension(path).ToLowerInvariant();if(!new[]{".png",".jpg",".jpeg",".bmp"}.Contains(ext)&&!(slot=="icon"&&ext==".ico"))throw new InvalidDataException("Выбери PNG, JPG или BMP. Для значка также подходит ICO.");
   using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
    if(file.Length==0||file.Length>20*1024*1024)throw new InvalidDataException("Максимальный размер исходного изображения — 20 МБ.");
    var frame=BitmapDecoder.Create(file,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None).Frames.OrderByDescending(f=>(long)f.PixelWidth*f.PixelHeight).First();
    if(frame.PixelWidth<=0||frame.PixelHeight<=0||frame.PixelWidth>16384||frame.PixelHeight>16384||(long)frame.PixelWidth*frame.PixelHeight>40000000)throw new InvalidDataException("Выбери изображение до 40 мегапикселей.");
    var max=slot=="icon"?512:slot=="cover"?1800:2560;double scale=Math.Min(1,(double)max/Math.Max(frame.PixelWidth,frame.PixelHeight));BitmapSource bitmap=scale<1?(BitmapSource)new TransformedBitmap(frame,new ScaleTransform(scale,scale)):frame;
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=new MemoryStream()){encoder.Save(output);return output.ToArray();}
   }
  }
  public void Dispose(){disposed=true;store.Dispose();}
 }
}
