using System;
using System.IO;
using System.Linq;
using Orbit;
class DownloadTelemetryTests
{
    static int checks;
    static void Check(bool ok, string name) { if(!ok)throw new Exception(name);checks++; }
    static void Bad(string text, string id="42") { try { SteamTransfer.Parse(text,id); } catch(InvalidDataException) { checks++;return; } throw new Exception("Accepted invalid manifest: "+text); }
    static string Vdf(string fields) => "\"AppState\" { \"appid\" \"42\" "+fields+" }";
    static SteamTransfer Sample(long got,long total=1000)=>new SteamTransfer{downloaded=got,total=total};
    static void Main()
    {
        var manifest=Vdf("\"BytesDownloaded\" \"12000000000\" \"BytesToDownload\" \"30000000000\" \"BytesStaged\" \"100\" \"BytesToStage\" \"200\" \"UserConfig\" { \"appid\" \"99\" \"BytesDownloaded\" \"999\" }");
        var p=SteamTransfer.Parse(manifest,"42");
        Check(p.downloaded==12000000000L&&p.total==30000000000L&&p.staged==100&&p.toStage==200,"64-bit counters and nested isolation");
        p=SteamTransfer.Parse(Vdf(""),"42");Check(p.downloaded==null&&p.total==null,"Missing counters stay unknown");
        p=SteamTransfer.Parse(Vdf("\"BytesDownloaded\" \"0\" \"BytesToDownload\" \"0\""),"42");Check(p.downloaded==0&&p.total==0,"Real zero distinct from unknown");
        Bad(manifest,"43");Bad(manifest,"../42");Bad(manifest.Substring(0,manifest.Length-2));
        Bad(Vdf("\"BytesDownloaded\" \"12\""));Bad(Vdf("\"BytesDownloaded\" \"-1\" \"BytesToDownload\" \"30\""));
        Bad(Vdf("\"BytesDownloaded\" \"31\" \"BytesToDownload\" \"30\""));
        Bad(Vdf("\"BytesDownloaded\" \"9223372036854775808\" \"BytesToDownload\" \"30\""));
        Bad(Vdf("\"BytesDownloaded\" \"1\" \"BytesDownloaded\" \"2\" \"BytesToDownload\" \"30\""));
        Bad(Vdf("\"BytesStaged\" \"2\" \"BytesToStage\" \"1\""));Bad(Vdf("\"BytesStaged\" \"2\""));
        Bad(Vdf("")+" }");Bad("\"AppState\" { \"appid\" \"42\" \"Orphan\" }");
        var dir=Path.Combine(Path.GetTempPath(),"orbit-transfer-"+Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(dir,"steamapps"));
        try {
            var file=Path.Combine(dir,"steamapps","appmanifest_42.acf");File.WriteAllText(file,manifest);
            using(var shared=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete))
                Check(SteamTransfer.Read(dir,"42")?.downloaded==12000000000L,"Shared read while client has manifest open");
            Check(File.ReadAllText(file)==manifest,"Read does not change Steam data");
            Check(SteamTransfer.Read(dir,"../42")==null&&SteamTransfer.Read(dir,"55")==null,"Invalid path and missing manifest remain unknown");
            File.WriteAllText(file,manifest.Substring(0,25));Check(SteamTransfer.Read(dir,"42")==null,"Partial write remains unknown");
        } finally {Directory.Delete(dir,true);}
        var start=new DateTime(2026,10,24,12,0,0,DateTimeKind.Utc);var t=new DownloadTracker();
        var m=t.Observe(Sample(100),"handed",start,start);
        Check(m.speed==null&&m.etaSeconds==null&&m.percent==10,"No speed or ETA from one sample");
        m=t.Observe(Sample(130),"handed",start,start.AddSeconds(3));
        Check(m.phase=="downloading"&&m.speed==10&&m.etaSeconds==87&&m.remaining==870,"Speed and ETA from byte delta/time");
        m=t.Observe(Sample(190),"handed",start,start.AddSeconds(6));
        Check(m.speed==15&&m.etaSeconds==54&&m.peak==15,"Rolling average and peak");
        var old=m.history; m.history[0].speed=12345;
        m=t.Observe(Sample(190),"handed",start,start.AddSeconds(9));Check(m.history[0].speed==null,"Returned history is detached");
        for(int second=12;second<=42;second+=3)m=t.Observe(Sample(190),"handed",start,start.AddSeconds(second));
        Check(m.speed==null&&m.etaSeconds==null&&m.phase=="waiting","Stale counters don't invent a zero speed or ETA");
        m=t.Observe(Sample(50),"handed",start,start.AddSeconds(45));Check(m.speed==null&&m.peak==null&&m.history.Count==1,"Counter rollback resets measurements");
        m=t.Observe(Sample(100,2000),"handed",start,start.AddSeconds(48));Check(m.speed==null&&m.percent==5,"Changed transfer size resets baseline");
        m=t.Observe(Sample(200,2000),"handed",start,start.AddSeconds(120));Check(m.speed==null&&m.history.Count==1,"Sleep/wake gap resets speed");
        m=t.Observe(null,"handed",start,start.AddSeconds(123));Check(m.downloaded==null&&m.speed==null,"Missing snapshot never reuses volume or speed");
        m=t.Observe(Sample(300,2000),"handed",start,start.AddSeconds(126));Check(m.speed==null,"First sample after gap is a new baseline");
        m=t.Observe(Sample(2000,2000),"handed",start,start.AddSeconds(129));Check(m.phase=="processing"&&m.percent==100&&m.etaSeconds==null,"Downloaded 100 percent is not installed");
        m=t.Observe(new SteamTransfer{staged=200,toStage=1000},"handed",start,start.AddSeconds(132));Check(m.phase=="processing"&&m.percent==null,"Staging is separate from network progress");
        m=t.Observe(null,"complete",start,start.AddSeconds(135));Check(m.phase=="complete"&&m.percent==100&&m.remaining==0&&m.etaSeconds==null,"SDK completion is authoritative");
        m=t.Observe(null,"complete",start,start.AddSeconds(150));Check(m.elapsedSeconds==135,"Completed install clock stops");
        t=new DownloadTracker();for(int i=0;i<200;i++)m=t.Observe(Sample(i,100000),"handed",null,start.AddSeconds(i*3));
        Check(m.history.Count<=101&&m.history.Last().at-m.history.First().at<=300,"History bounded to five minutes");
        t=new DownloadTracker();for(int i=0;i<200;i++)m=t.Observe(Sample(i,100000),"handed",null,start.AddMilliseconds(i*100));
        Check(m.history.Count==120,"History hard cap");
        t=new DownloadTracker();m=t.Observe(null,"preparing",null,start);Check(m.phase=="preparing"&&m.elapsedSeconds==0,"Legacy job starts observation clock");
        m=t.Observe(null,"attention",null,start.AddSeconds(3));Check(m.phase=="attention","Attention preserved without data");
        Console.WriteLine("Download telemetry PASS: "+checks+" checks (manifest parsing, shared reads, honest unknowns, rolling speed, ETA, resets, phases, bounded history).");
    }
}
