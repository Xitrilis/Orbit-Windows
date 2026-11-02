using System;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
namespace Orbit {
 public sealed class RadminSettings {public int schema=1;public string network="",exe="";public bool autoStart;}
 public static class RadminRules {
  public static string Network(string value){value=(value??"").Trim();if(value.Length<1||value.Length>100||value.Any(char.IsControl))throw new InvalidOperationException("Укажи название сети Radmin.");return value;}
  public static string Password(string value){if(value==null||value.Length<1||value.Length>128||value.Any(char.IsControl))throw new InvalidOperationException("Введи пароль сети Radmin.");return value;}
  public static void Validate(RadminSettings s){if(s==null||s.schema!=1||s.network==null||s.exe==null||s.exe.Length>1000||s.exe.Any(char.IsControl)||(s.exe.Length>0&&!Path.IsPathRooted(s.exe))||s.network.Length>100||s.network.Any(char.IsControl)||s.autoStart&&string.IsNullOrWhiteSpace(s.network))throw new InvalidOperationException("Настройки сети недоступны.");}
  static string Key(string value)=>Regex.Replace((value??"").Trim().ToLowerInvariant().Replace("…",""),@"[\s&.]+"," ").Trim();
  public static bool JoinButton(string text){return new[]{"join network","join a network","присоединиться к сети","подключиться к сети","войти в сеть"}.Contains(Key(text));}
  public static bool JoinDialog(string text){return new[]{"join network","join a network","присоединиться к сети","подключиться к сети","войти в сеть"}.Contains(Key(text));}
  public static bool SubmitButton(string text){return new[]{"join","connect","присоединиться","подключиться","войти"}.Contains(Key(text));}
  public static bool MainWindow(string text)=>string.Equals((text??"").Trim(),"Radmin VPN",StringComparison.OrdinalIgnoreCase);
  public static bool NetworkItem(string text,string name)=>string.Equals((text??"").Trim(),(name??"").Trim(),StringComparison.Ordinal);
 }
}
