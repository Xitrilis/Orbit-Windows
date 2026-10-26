using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace Orbit
{
    public static class InstallPaths
    {
        public static string SafeName(string name) { foreach(var c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');return name.Trim().TrimEnd('.'); }
        public static bool Same(string a,string b) { try{return string.Equals(Path.GetFullPath(a).TrimEnd('\\'),Path.GetFullPath(b).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}catch{return false;} }
        public static string Validate(string path)
        {
            if(string.IsNullOrWhiteSpace(path)||!Regex.IsMatch(path,@"^[a-zA-Z]:\\"))throw new ArgumentException("Нужен полный путь на локальном диске.");
            path=Path.GetFullPath(path.Trim());
            if(path.TrimEnd('\\').Length<3)throw new ArgumentException("Выбери папку, а не корень диска.");
            var win=Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if(Same(path,win)||path.StartsWith(win+"\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Системная папка Windows не подходит для установки игр.");
            var drive=new DriveInfo(Path.GetPathRoot(path));if(!drive.IsReady)throw new IOException("Выбранный диск недоступен.");
            return path.TrimEnd('\\');
        }
        public static long Free(string path) { try{return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace;}catch{return 0;} }
        public static string ReadShared(string path) { using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var r=new StreamReader(f))return r.ReadToEnd(); }
        public static List<string> SteamLibraries()
        {
            var list=new List<string>();
            var steam=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) as string;
            if(string.IsNullOrWhiteSpace(steam))return list;
            steam=steam.Replace('/','\\');if(Directory.Exists(steam))list.Add(steam);
            var file=Path.Combine(steam,"steamapps","libraryfolders.vdf");
            if(File.Exists(file))foreach(Match m in Regex.Matches(ReadShared(file),"\"path\"\\s*\"((?:\\\\.|[^\"])*)\"",RegexOptions.IgnoreCase))
            {var p=m.Groups[1].Value.Replace("\\\\","\\").Replace("\\\"","\"").Replace('/','\\');if(Directory.Exists(p)&&!list.Any(x=>Same(x,p)))list.Add(p);}
            return list;
        }
    }
}
