using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using Orbit;
class StartupIntegrityTests {
 static int count;static string root=Path.Combine(Path.GetTempPath(),"Orbit тест "+Guid.NewGuid());
 static void Check(string name,Action action){action();Console.WriteLine("PASS "+name);count++;}
 static void Yes(bool v){if(!v)throw new Exception("Assertion failed");}
 static string Row(string name,string data){var b=Encoding.UTF8.GetBytes(data);using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant()+"\t"+b.Length+"\t"+name;}
 static void Main(){Directory.CreateDirectory(root);try {
  var file=Path.Combine(root,"test.dll");File.WriteAllText(file,"1234");var good=Row("test.dll","1234");
  Check("valid components and progress",()=>{int n=0;Yes(StartupIntegrity.Check(root,good,CancellationToken.None,(done,total)=>n=done).Count==0);Yes(n==1);});
  Check("missing component is named",()=>Yes(StartupIntegrity.Check(root,Row("missing.dll","1234"),CancellationToken.None).Single().Contains("missing.dll")));
  Check("truncated component rejected",()=>{File.WriteAllText(file,"12");Yes(StartupIntegrity.Check(root,good,CancellationToken.None).Single().Contains("размер"));});
  Check("same-size corruption rejected by SHA",()=>{File.WriteAllText(file,"4321");Yes(StartupIntegrity.Check(root,good,CancellationToken.None).Single().Contains("содержимое"));File.WriteAllText(file,"1234");});
  Check("manifest absent rejected",()=>Yes(StartupIntegrity.Check(root,null,CancellationToken.None).Count>0));
  Check("malformed manifest rejected",()=>Yes(StartupIntegrity.Check(root,"wrong\t4\ttest.dll",CancellationToken.None).Count>0));
  Check("path traversal rejected",()=>{foreach(var p in new[]{"../test.dll","folder/../test.dll","C:/Windows/file","/test.dll","\\test.dll","./test.dll","test//file"})Yes(StartupIntegrity.Check(root,Row(p,"1234"),CancellationToken.None).Single().Contains("путь"));});
  Check("duplicate entries rejected",()=>Yes(StartupIntegrity.Check(root,good+"\n"+good,CancellationToken.None).Single().Contains("путь")));
  Check("cancellation does not launch",()=>{var c=new CancellationTokenSource();c.Cancel();try{StartupIntegrity.Check(root,good,c.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}});
  Check("Unicode/spaced paths",()=>{Directory.CreateDirectory(Path.Combine(root,"игры папка"));File.WriteAllText(Path.Combine(root,"игры папка","данные.dll"),"abcd");Yes(StartupIntegrity.Check(root,Row("игры папка/данные.dll","abcd"),CancellationToken.None).Count==0);});
  Check("unknown mutable user files ignored",()=>{File.WriteAllText(Path.Combine(root,"preferences.json"),"user data");Yes(StartupIntegrity.Check(root,good,CancellationToken.None).Count==0);});
  Check("real release manifest",()=>{var app=FindApp();var manifest=File.ReadAllText(Path.Combine(app,"RuntimeFiles.sha256"));var errors=StartupIntegrity.Check(app,manifest,CancellationToken.None);if(errors.Count>0)throw new Exception(string.Join("\n",errors));});
  Console.WriteLine(count+" STARTUP INTEGRITY CHECKS PASSED");
 }finally{Directory.Delete(root,true);}}
 static string FindApp(){for(var d=new DirectoryInfo(Environment.CurrentDirectory);d!=null;d=d.Parent){if(File.Exists(Path.Combine(d.FullName,"RuntimeFiles.sha256")))return d.FullName;var p=Path.Combine(d.FullName,"Orbit-Windows");if(File.Exists(Path.Combine(p,"RuntimeFiles.sha256")))return p;}throw new Exception("Run the check from the unpacked Orbit project directory.");}
}
