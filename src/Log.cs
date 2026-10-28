// Log.cs — 日志（格式与旧版一致，面板/记录页照旧能解析）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WuwaChest
{
    internal sealed class LogEntry
    {
        public DateTime Time;
        public string Level;
        public string Message;
        public string Line;
    }

    internal static class Log
    {
        private static readonly object Sync = new object();
        private static string _dir;
        private static string _path;
        private static string _date;
        private static int _retention = 30;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static event Action<LogEntry> Written;

        public static string Dir { get { return _dir; } }
        public static string CurrentFile { get { return _path; } }

        public static void Init(string dir, int retentionDays)
        {
            lock (Sync)
            {
                _dir = dir;
                _retention = retentionDays < 1 ? 30 : retentionDays;
                try
                {
                    if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
                    var limit = DateTime.Now.AddDays(-_retention);
                    foreach (var file in Directory.GetFiles(_dir, "chest-*.log"))
                    {
                        try { if (File.GetLastWriteTime(file) < limit) File.Delete(file); }
                        catch { }
                    }
                }
                catch { }
                _date = null;
                Roll();
            }
        }

        private static void Roll()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            if (_date == today && _path != null) return;
            _date = today;
            _path = Path.Combine(_dir, "chest-" + today + ".log");
        }

        public static void Write(string level, string message)
        {
            var entry = new LogEntry
            {
                Time = DateTime.Now,
                Level = level,
                Message = message
            };
            entry.Line = string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}", entry.Time, level, message);

            lock (Sync)
            {
                try
                {
                    Roll();
                    File.AppendAllText(_path, entry.Line + "\r\n", Utf8);
                }
                catch { }
            }

            var handler = Written;
            if (handler != null)
            {
                try { handler(entry); } catch { }
            }
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message) { Write("ERROR", message); }
        public static void Action(string message) { Write("ACTION", message); }

        // 读取指定日期的日志行（新到旧）。日期为 null 时读今天。
        public static List<string> ReadDay(string dir, DateTime? day)
        {
            var list = new List<string>();
            try
            {
                string file = Path.Combine(dir, "chest-" + (day.HasValue ? day.Value : DateTime.Now).ToString("yyyyMMdd") + ".log");
                if (!File.Exists(file)) return list;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) list.Add(line);
                }
            }
            catch { }
            return list;
        }
    }
}
