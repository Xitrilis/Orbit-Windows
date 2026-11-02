using System;
using System.Linq;
using System.Text.RegularExpressions;
namespace Orbit {
 public static class SteamLinks {
  public static bool Allowed(string url) {
   if (String.IsNullOrEmpty(url) || url.Length > 200) return false;
   if (url == "steam://open/friends") return true;
   var lobby = Regex.Match(url, @"\Asteam://joinlobby/([1-9][0-9]{0,9})/([1-9][0-9]{0,19})/([1-9][0-9]{0,19})\z", RegexOptions.CultureInvariant);
   uint app; ulong lobbyId, ownerId;
   if (lobby.Success) return uint.TryParse(lobby.Groups[1].Value, out app) && app > 0 && ulong.TryParse(lobby.Groups[2].Value, out lobbyId) && lobbyId > 0 && ulong.TryParse(lobby.Groups[3].Value, out ownerId) && ownerId > 0;
   var server = Regex.Match(url, @"\Asteam://connect/([^/:?&#\s]{1,253}):([0-9]{1,5})(?:/([A-Za-z0-9._~-]{1,64}))?\z", RegexOptions.CultureInvariant);
   if (!server.Success) return false;
   int port;if(!int.TryParse(server.Groups[2].Value,out port)||port<1||port>65535)return false;
   var host=server.Groups[1].Value;
   if(Regex.IsMatch(host,@"\A[0-9.]+\z"))return Regex.IsMatch(host,@"\A[0-9]{1,3}(?:\.[0-9]{1,3}){3}\z")&&host.Split('.').All(part=>{int octet;return int.TryParse(part,out octet)&&octet<=255;});
   return Regex.IsMatch(host,@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+\z",RegexOptions.CultureInvariant);
  }
 }
}
