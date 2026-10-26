using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

namespace Orbit
{
    // Isolated Windows UI Automation process. Never clicks coordinates, accepts terms,
    // writes client config files, or presses Install without a matching game and verified path.
    static class InstallHelper
    {
        [STAThread]static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            try
            {
                if(args.Length!=1)return Result(false,"Не найден запрос установки.");
                var req=JObject.Parse(File.ReadAllText(args[0]));var path=InstallPaths.Validate((string)req["path"]);
                var name=(string)req["name"];var store=(string)req["store"];
                if(string.IsNullOrWhiteSpace(Key(name)))return Result(false,"Название игры не подтверждено. Автодействия остановлены.");
                var started=DateTime.UtcNow;
                while((DateTime.UtcNow-started).TotalSeconds<42)
                {
                    foreach(var root in Windows(store))
                    {
                        var nodes=Nodes(root);
                        if(!nodes.Any(n=>Key(Text(n)).Contains(Key(name))))continue;
                        var buttons=nodes.Where(IsInstallButton).ToList();if(buttons.Count!=1)continue;var button=buttons[0];
                        if(nodes.Any(NeedsTerms))return Result(false,"Магазин просит подтвердить соглашение. Открой клиент: Orbit не принимает условия за тебя.");
                        if(!SelectPath(root,nodes,path,store))continue;
                        // Re-read after mutation: stale nodes and dialogs cannot authorize a click.
                        nodes=Nodes(root);if(!nodes.Any(n=>Key(Text(n)).Contains(Key(name)))||!PathSelected(nodes,path))continue;
                        buttons=nodes.Where(IsInstallButton).ToList();if(buttons.Count!=1)continue;button=buttons[0];
                        if(button!=null&&button.TryGetCurrentPattern(InvokePattern.Pattern,out var action))
                        {
                            ((InvokePattern)action).Invoke();
                            var handle=root.Current.NativeWindowHandle;if(handle!=0)ShowWindowAsync(new IntPtr(handle),6);
                            return Result(true,"Путь подтверждён. Команда установки передана клиенту; ожидаем состояние загрузки.");
                        }
                    }
                    Thread.Sleep(500);
                }
                return Result(false,"Не удалось подтвердить игру и папку в окне магазина. Автодействия остановлены. Открой клиент: может требоваться вход или ручной выбор пути.");
            }
            catch(Exception){return Result(false,"Клиент не предоставил доступ к окну установки. Автодействия остановлены; проверь клиент.");}
        }
        static int Result(bool ok,string message){Console.WriteLine(new JObject{{"ok",ok},{"message",message}}.ToString(Newtonsoft.Json.Formatting.None));return ok?0:3;}
        static string Key(string s)=>Regex.Replace((s??"").ToLowerInvariant(),@"[^\p{L}\p{N}]","");
        static string Text(AutomationElement e){try{return e.Current.Name??"";}catch{return "";}}
        static IEnumerable<AutomationElement> Windows(string store)
        {
            var allowed=new Dictionary<string,string[]>{{"steam",new[]{"steam","steamwebhelper"}},{"epic",new[]{"EpicGamesLauncher"}},{"ea",new[]{"EADesktop","EALauncher"}},{"gog",new[]{"GalaxyClient"}},{"ubisoft",new[]{"upc","UbisoftConnect","Uplay"}},{"battlenet",new[]{"Battle.net"}}};
            if(!allowed.ContainsKey(store))yield break;
            var roots=AutomationElement.RootElement.FindAll(TreeScope.Children,Condition.TrueCondition);
            foreach(AutomationElement e in roots)
            {
                bool match=false;
                try { using(var p=Process.GetProcessById(e.Current.ProcessId))match=allowed[store].Contains(p.ProcessName,StringComparer.OrdinalIgnoreCase)&&!e.Current.IsOffscreen; }catch{}
                if(match)yield return e;
            }
        }
        static List<AutomationElement> Nodes(AutomationElement root)
        {
            var result=new List<AutomationElement>();var pending=new Queue<AutomationElement>();pending.Enqueue(root);var walk=TreeWalker.ControlViewWalker;
            while(pending.Count>0&&result.Count<3500){var e=pending.Dequeue();result.Add(e);try{var c=walk.GetFirstChild(e);while(c!=null&&pending.Count<3500){pending.Enqueue(c);c=walk.GetNextSibling(c);}}catch{}}
            return result;
        }
        static bool IsInstallButton(AutomationElement e)
        {
            try{return e.Current.ControlType==ControlType.Button&&e.Current.IsEnabled&&!e.Current.IsOffscreen&&Regex.IsMatch(Text(e).Trim(),@"^(Install|Install now|Start installation|Установить|Установить сейчас|Начать установку)$",RegexOptions.IgnoreCase);}catch{return false;}
        }
        static bool NeedsTerms(AutomationElement e)
        {
            try { if(e.Current.ControlType!=ControlType.CheckBox||!Regex.IsMatch(Text(e),@"(agree|accept|соглас|принимаю)",RegexOptions.IgnoreCase))return false;return e.TryGetCurrentPattern(TogglePattern.Pattern,out var t)&&((TogglePattern)t).Current.ToggleState!=ToggleState.On; }catch{return false;}
        }
        static bool SelectPath(AutomationElement root,List<AutomationElement> nodes,string path,string store)
        {
            if(PathSelected(nodes,path))return true;
            var edits=nodes.Where(e=>{try{return e.Current.ControlType==ControlType.Edit&&e.TryGetCurrentPattern(ValuePattern.Pattern,out var p)&&!((ValuePattern)p).Current.IsReadOnly&&Regex.IsMatch(((ValuePattern)p).Current.Value??"",@"^[a-zA-Z]:\\");}catch{return false;}}).ToList();
            if(edits.Count==1){var p=(ValuePattern)edits[0].GetCurrentPattern(ValuePattern.Pattern);p.SetValue(path);Thread.Sleep(250);return InstallPaths.Same(p.Current.Value,path);}
            // Existing library choices only. A drive letter alone is not enough to identify a library.
            foreach(var combo in nodes.Where(e=>{try{return e.Current.ControlType==ControlType.ComboBox;}catch{return false;}}))
            {try{if(combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var p))((ExpandCollapsePattern)p).Expand();}catch{}}
            nodes=Nodes(root);
            foreach(var item in nodes)
            {
                if(!ContainsPath(Text(item),path))continue;
                try{if(item.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var p)){((SelectionItemPattern)p).Select();Thread.Sleep(250);return PathSelected(Nodes(root),path);}}catch{}
            }
            return false;
        }
        static bool ContainsPath(string text,string path)
        {
            return Regex.IsMatch(text??"",Regex.Escape(path)+@"(?=$|[\s)\]])",RegexOptions.IgnoreCase);
        }
        static bool PathSelected(List<AutomationElement> nodes,string path)
        {
            foreach(var e in nodes)try
            {
                if(e.Current.IsOffscreen||!e.Current.IsEnabled)continue;
                if((e.Current.ControlType==ControlType.Edit||e.Current.ControlType==ControlType.ComboBox)&&e.TryGetCurrentPattern(ValuePattern.Pattern,out var value)&&InstallPaths.Same(((ValuePattern)value).Current.Value,path))return true;
                if(ContainsPath(Text(e),path)&&e.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var item)&&((SelectionItemPattern)item).Current.IsSelected)return true;
                if(e.Current.ControlType==ControlType.ComboBox&&e.TryGetCurrentPattern(SelectionPattern.Pattern,out var sel)&&((SelectionPattern)sel).Current.GetSelection().Any(s=>ContainsPath(Text(s),path)))return true;
            }catch{}
            return false;
        }
        [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr hwnd,int cmd);
    }
}
