using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Orbit
{
    public sealed class GameContext
    {
        public string id, name, store, appId, installDirectory, executable;
        public bool installed, running;
    }
    public sealed class DiagnosticCheck { public string title, detail, state; }
    public sealed class DiagnosticEvent { public string time, source, text; public int id; }
    public sealed class DiagnosticReport
    {
        public string gameId, gameName, checkedAt, note;
        public List<DiagnosticCheck> checks = new List<DiagnosticCheck>();
        public List<DiagnosticEvent> events = new List<DiagnosticEvent>();
        public string ToText()
        {
            var b = new StringBuilder("Orbit — локальная диагностика\nИгра: " + gameName + "\nВремя: " + checkedAt + "\n");
            foreach (var c in checks) b.AppendLine(c.title + ": " + c.detail);
            b.AppendLine(note);
            foreach (var e in events) b.AppendLine(e.time + " · " + e.source + " · " + e.id + "\n" + e.text);
            return b.ToString();
        }
    }
    public static class GameDiagnostics
    {
        public static bool IsInside(string file, string directory)
        {
            try { return !string.IsNullOrWhiteSpace(directory) && Path.IsPathRooted(directory) && Path.GetFullPath(directory).TrimEnd('\\').Length > 3 && Path.GetFullPath(file).StartsWith(Path.GetFullPath(directory).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        public static string Redact(string text)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) text = Regex.Replace(text ?? "", Regex.Escape(profile), @"C:\Users\[пользователь]", RegexOptions.IgnoreCase);
            text = Regex.Replace(text ?? "", @"(?i)[a-z]:\\Users\\[^\\\s]+", @"C:\Users\[пользователь]");
            text = Regex.Replace(text, @"(?i)(api[_-]?key|access[_-]?token|password|authorization)\s*[:=]\s*[^\s,;]+", "$1=[скрыто]");
            return text.Length > 1800 ? text.Substring(0, 1800) + "…" : text;
        }
        public static DiagnosticReport Check(GameContext game)
        {
            var report = new DiagnosticReport { gameId = game.id, gameName = game.name, checkedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm"), note = "Это проверка доступности файлов и событий Windows. Целостность содержимого подтверждает только проверка в магазине. Отсутствие событий не доказывает отсутствие ошибок." };
            report.checks.Add(new DiagnosticCheck { title = "Установка", state = game.installed ? "ok" : "info", detail = game.installed ? "Игра отмечена установленной в библиотеке." : "Выбранная копия не установлена." });
            var hasDirectory = !string.IsNullOrWhiteSpace(game.installDirectory);
            var exists = hasDirectory && Directory.Exists(game.installDirectory);
            report.checks.Add(new DiagnosticCheck { title = "Папка игры", state = !hasDirectory ? "info" : exists ? "ok" : "warning", detail = !hasDirectory ? "Интеграция не предоставила путь; наличие папки не проверено." : exists ? Redact(game.installDirectory) : "Папка недоступна: " + Redact(game.installDirectory) });
            if (exists)
            {
                try
                {
                    var free = new DriveInfo(Path.GetPathRoot(game.installDirectory)).AvailableFreeSpace;
                    report.checks.Add(new DiagnosticCheck { title = "Место на диске", state = free < 5L * 1024 * 1024 * 1024 ? "warning" : "ok", detail = (free / 1073741824.0).ToString("0.0") + " ГБ свободно. Не является оценкой места, необходимого конкретной игре." });
                }
                catch { report.checks.Add(new DiagnosticCheck { title = "Место на диске", state = "info", detail = "Не удалось получить свободное место." }); }
            }
            if (!string.IsNullOrWhiteSpace(game.executable)) report.checks.Add(new DiagnosticCheck { title = "Файл запуска", state = File.Exists(game.executable) ? "ok" : "warning", detail = (File.Exists(game.executable) ? "Найден: " : "Недоступен: ") + Redact(game.executable) });
            else report.checks.Add(new DiagnosticCheck { title = "Файл запуска", state = "info", detail = "Запуском управляет магазин; точный EXE не предоставлен." });
            if (!hasDirectory && string.IsNullOrWhiteSpace(game.executable))
            { report.checks.Add(new DiagnosticCheck { title = "События Windows", state = "info", detail = "Не удалось связать события с этой игрой: нет пути к установке или EXE." }); return report; }
            try
            {
                var query = new EventLogQuery("Application", PathType.LogName, "*[System[((EventID=1000) or (EventID=1001) or (EventID=1002)) and TimeCreated[timediff(@SystemTime) <= 604800000]]]") { ReverseDirection = true };
                var clock = Stopwatch.StartNew(); int inspected = 0;
                using (var reader = new EventLogReader(query))
                while (inspected++ < 600 && report.events.Count < 12 && clock.Elapsed < TimeSpan.FromSeconds(6))
                {
                    using (var record = reader.ReadEvent(TimeSpan.FromMilliseconds(250)))
                    {
                        if (record == null) break;
                        var values = string.Join("\n", record.Properties.Select(p => p.Value?.ToString() ?? ""));
                        bool match = !string.IsNullOrEmpty(game.executable) && values.IndexOf(game.executable, StringComparison.OrdinalIgnoreCase) >= 0;
                        match |= hasDirectory && values.IndexOf(game.installDirectory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!match) continue;
                        string message; try { message = record.FormatDescription(); } catch { message = values; }
                        report.events.Add(new DiagnosticEvent { id = record.Id, source = record.ProviderName, time = record.TimeCreated?.ToString("dd.MM HH:mm:ss") ?? "", text = Redact(message) });
                    }
                }
                report.checks.Add(new DiagnosticCheck { title = "События Windows · 7 дней", state = report.events.Count > 0 ? "warning" : "info", detail = report.events.Count > 0 ? "Найдено связанных сообщений: " + report.events.Count + ". Смотри журнал ниже." : "В просмотренных событиях связанных с этой игрой ошибок не найдено. Журнал может быть неполным." });
            }
            catch { report.checks.Add(new DiagnosticCheck { title = "События Windows", state = "info", detail = "Журнал недоступен; ошибки запуска не проверены." }); }
            return report;
        }
    }
}
