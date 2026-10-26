using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace Orbit
{
    public sealed class SteamTransfer
    {
        public long? downloaded, total, staged, toStage;
        public static SteamTransfer Parse(string text, string appId)
        {
            if (text == null || text.Length > 1048576 || !Regex.IsMatch(appId ?? "", @"\A[0-9]{1,12}\z")) throw new InvalidDataException();
            var tokens = Regex.Matches(text, "\"(?:\\\\.|[^\"\\\\])*\"|[{}]");
            if (tokens.Count < 4 || tokens[0].Value != "\"AppState\"" || tokens[1].Value != "{") throw new InvalidDataException();
            int depth = 1; string key = null;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 2; i < tokens.Count; i++)
            {
                var t = tokens[i].Value;
                if (t == "{") { if (depth == 1 && key == null) throw new InvalidDataException(); depth++; continue; }
                if (t == "}") { depth--; if (depth < 0 || (depth == 0 && (key != null || i != tokens.Count - 1))) throw new InvalidDataException(); if (depth == 1) key = null; continue; }
                if (depth != 1) continue;
                t = t.Substring(1, t.Length - 2);
                if (key == null) key = t;
                else { if (values.ContainsKey(key)) throw new InvalidDataException(); values[key] = t; key = null; }
            }
            if (depth != 0 || key != null || !values.ContainsKey("appid") || values["appid"] != appId) throw new InvalidDataException();
            Func<string, long?> number = k => {
                string v; long n;
                if (!values.TryGetValue(k, out v)) return null;
                if (!long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out n)) throw new InvalidDataException();
                return n;
            };
            var r = new SteamTransfer { downloaded = number("BytesDownloaded"), total = number("BytesToDownload"), staged = number("BytesStaged"), toStage = number("BytesToStage") };
            if (r.downloaded.HasValue != r.total.HasValue || r.staged.HasValue != r.toStage.HasValue || r.downloaded > r.total || r.staged > r.toStage) throw new InvalidDataException();
            return r;
        }
        public static SteamTransfer Read(string library, string appId)
        {
            if (!Regex.IsMatch(appId ?? "", @"\A[0-9]{1,12}\z")) return null;
            try
            {
                var path = Path.Combine(library, "steamapps", "appmanifest_" + appId + ".acf");
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (f.Length > 1048576) return null;
                    using (var reader = new StreamReader(f)) return Parse(reader.ReadToEnd(), appId);
                }
            } catch { return null; }
        }
    }
    public sealed class DownloadPoint { public double at; public double? speed; }
    public sealed class DownloadMetrics
    {
        public string phase = "waiting", source, note;
        public long? downloaded, total, remaining, staged, toStage;
        public double? percent, speed, peak, etaSeconds;
        public double elapsedSeconds;
        public DateTime sampledAt;
        public List<DownloadPoint> history = new List<DownloadPoint>();
    }
    public sealed class DownloadTracker
    {
        sealed class Counter { public DateTime at; public long bytes; }
        readonly List<Counter> counters = new List<Counter>();
        readonly List<DownloadPoint> history = new List<DownloadPoint>();
        DateTime? started, finished, lastAdvance, lastRead;
        long? lastTotal; double? peak;
        static double Unix(DateTime t) => (t - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        void Reset() { counters.Clear(); lastAdvance = null; peak = null; history.Clear(); }
        public DownloadMetrics Observe(SteamTransfer sample, string state, DateTime? createdAt, DateTime now, DateTime? completedAt = null)
        {
            if (!started.HasValue) started = createdAt.HasValue && createdAt.Value <= now ? createdAt : now;
            if (state == "complete" && !finished.HasValue) finished = completedAt.HasValue && completedAt.Value <= now ? completedAt : now;
            var r = new DownloadMetrics { sampledAt = now, elapsedSeconds = Math.Max(0, ((finished ?? now) - started.Value).TotalSeconds), source = "Steam · локальные данные" };
            if (lastRead.HasValue && (now <= lastRead.Value || (now - lastRead.Value).TotalSeconds > 60)) Reset();
            lastRead = now;
            if (state == "complete")
            {
                r.phase = "complete"; r.percent = 100; r.note = "Установка подтверждена библиотекой.";
                r.downloaded = sample?.downloaded; r.total = sample?.total; r.remaining = 0;
                counters.Clear(); lastAdvance = null;
            }
            else if (sample == null)
            {
                r.phase = state == "preparing" ? "preparing" : state == "attention" ? "attention" : "waiting";
                r.note = "Ожидаем данные Steam. Скорость и время появятся после изменения счётчиков.";
                counters.Clear(); lastAdvance = null;
            }
            else
            {
                r.downloaded = sample.downloaded; r.total = sample.total; r.staged = sample.staged; r.toStage = sample.toStage;
                if (sample.total > 0 && sample.downloaded.HasValue)
                {
                    if (lastTotal != sample.total || (counters.Count > 0 && sample.downloaded < counters.Last().bytes)) Reset();
                    lastTotal = sample.total;
                    r.percent = 100.0 * sample.downloaded.Value / sample.total.Value;
                    r.remaining = sample.total - sample.downloaded;
                    if (counters.Count > 0 && sample.downloaded > counters.Last().bytes) lastAdvance = now;
                    counters.Add(new Counter { at = now, bytes = sample.downloaded.Value });
                    while (counters.Count > 2 && (now - counters[1].at).TotalSeconds >= 30) counters.RemoveAt(0);
                    var seconds = (now - counters[0].at).TotalSeconds;
                    if (seconds >= 3 && lastAdvance.HasValue && (now - lastAdvance.Value).TotalSeconds <= 30 && r.remaining > 0)
                    {
                        var delta = sample.downloaded.Value - counters[0].bytes;
                        if (delta > 0) { r.speed = delta / seconds; r.etaSeconds = r.remaining.Value / r.speed.Value; peak = Math.Max(peak ?? 0, r.speed.Value); }
                    }
                    r.phase = r.remaining == 0 ? "processing" : r.speed.HasValue ? "downloading" : "waiting";
                    r.note = r.phase == "processing" ? "Скачивание завершено. Steam распаковывает, проверяет или применяет файлы." :
                        r.speed.HasValue ? "Средняя скорость по обновлениям Steam за последние 30 секунд. Время — до конца скачивания, без установки." : "Steam ещё не обновил счётчики. Пауза или ожидание уточняются в клиенте.";
                }
                else
                {
                    counters.Clear(); lastAdvance = null; r.phase = sample.toStage > 0 ? "processing" : "waiting";
                    r.note = "Объём сетевой загрузки пока не опубликован Steam.";
                }
            }
            r.peak = peak; history.Add(new DownloadPoint { at = Unix(now), speed = r.speed });
            history.RemoveAll(p => p.at < Unix(now.AddMinutes(-5)));
            if (history.Count > 120) history.RemoveRange(0, history.Count - 120);
            r.history = history.Select(p => new DownloadPoint { at = p.at, speed = p.speed }).ToList(); return r;
        }
    }
}
