// Sync.cs — 跨电脑同步：把设置 / 领取记录 / 房间 / 字符数打成一个包，支持合并
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WuwaChest
{
    /// <summary>一个同步包里的全部内容（源码里就是几个字符串列表，方便合并）</summary>
    internal sealed class SyncPackage
    {
        public string ExportedAt = "";
        public string Machine = "";
        public string AppVersion = "";
        public string Config = "";                       // config.json 原文
        public List<string> Claims = new List<string>(); // 领取成功的日志行
        public List<string> Rooms = new List<string>();  // rooms.txt 行
        public List<string> Stats = new List<string>();  // stats.txt 行
    }

    internal sealed class SyncResult
    {
        public int ClaimsAdded;
        public int RoomsMerged;
        public int StatsMerged;
        public bool ConfigApplied;
        public bool ConfigKept;
        public List<string> Notes = new List<string>();
    }

    internal static class SyncService
    {
        public const string FormatTag = "wuwachest-sync";
        public const int FormatVersion = 1;
        public const string Extension = ".wcsync";

        // ==================================================================
        // 导出
        // ==================================================================
        public static SyncPackage Collect(string appDir, string logsDir)
        {
            var pkg = new SyncPackage
            {
                ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Machine = SafeMachineName(),
                AppVersion = Program.Version
            };

            var cfgPath = Path.Combine(appDir, "config.json");
            try { if (File.Exists(cfgPath)) pkg.Config = File.ReadAllText(cfgPath, Encoding.UTF8); }
            catch { }

            try
            {
                foreach (var file in Directory.GetFiles(logsDir, "chest-*.log"))
                {
                    foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                    {
                        if (line.IndexOf("领取成功", StringComparison.Ordinal) >= 0) pkg.Claims.Add(line);
                    }
                }
            }
            catch { }

            pkg.Rooms.AddRange(ReadLines(Path.Combine(appDir, "rooms.txt")));
            pkg.Stats.AddRange(ReadLines(Path.Combine(appDir, "stats.txt")));
            return pkg;
        }

        public static void ExportToFile(string path, string appDir, string logsDir)
        {
            var pkg = Collect(appDir, logsDir);
            File.WriteAllText(path, ToJson(pkg), new UTF8Encoding(false));
        }

        // ==================================================================
        // 读包
        // ==================================================================
        public static SyncPackage ReadPackage(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            var root = MiniJson.Parse(text) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("不是有效的同步包（JSON 解析失败）");

            object tag;
            if (!root.TryGetValue("format", out tag) || (tag as string) != FormatTag)
                throw new InvalidDataException("不是 WuwaChest 同步包");

            var pkg = new SyncPackage
            {
                ExportedAt = Str(root, "exportedAt"),
                Machine = Str(root, "machine"),
                AppVersion = Str(root, "appVersion"),
                Config = Str(root, "config")
            };
            pkg.Claims.AddRange(StrList(root, "claims"));
            pkg.Rooms.AddRange(StrList(root, "rooms"));
            pkg.Stats.AddRange(StrList(root, "stats"));
            return pkg;
        }

        // ==================================================================
        // 合并（文件层）
        // ==================================================================
        public static SyncResult Apply(SyncPackage pkg, string appDir, string logsDir, bool importSettings)
        {
            var result = new SyncResult();

            // ---- 领取记录：按日期落到对应的 chest-yyyyMMdd.log ----
            var byDate = new Dictionary<string, List<string>>();
            foreach (var line in pkg.Claims)
            {
                var m = Regex.Match(line, @"^(\d{4})-(\d{2})-(\d{2})");
                if (!m.Success) continue;
                string date = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value;
                List<string> list;
                if (!byDate.TryGetValue(date, out list)) { list = new List<string>(); byDate[date] = list; }
                list.Add(line);
            }

            try
            {
                if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);
                foreach (var pair in byDate)
                {
                    string file = Path.Combine(logsDir, "chest-" + pair.Key + ".log");
                    var existing = new HashSet<string>(StringComparer.Ordinal);
                    if (File.Exists(file))
                        foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                            if (line.Trim().Length > 0) existing.Add(line.Trim());

                    var toAdd = new List<string>();
                    foreach (var line in pair.Value)
                    {
                        string trimmed = line.Trim();
                        if (existing.Contains(trimmed)) continue;
                        toAdd.Add(trimmed);
                        existing.Add(trimmed);
                        result.ClaimsAdded++;
                    }
                    if (toAdd.Count == 0) continue;

                    var text = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : "";
                    if (text.Length > 0 && !text.EndsWith("\n")) text += "\r\n";
                    text += string.Join("\r\n", toAdd.ToArray()) + "\r\n";
                    File.WriteAllText(file, text, new UTF8Encoding(false));
                }
            }
            catch (Exception ex) { result.Notes.Add("领取记录合并失败：" + ex.Message); }

            // ---- 房间：同一房间取「较新的时间 + 较大的次数」 ----
            try
            {
                var path = Path.Combine(appDir, "rooms.txt");
                var merged = new List<string[]>();
                var index = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var line in ReadLines(path))
                {
                    var p = line.Split('|');
                    if (p.Length < 4) continue;
                    string id = p[0].Trim();
                    if (id.Length == 0) continue;
                    index[id] = merged.Count;
                    merged.Add(new[] { id, p[1], p[2], p[3] });
                }
                foreach (var line in pkg.Rooms)
                {
                    var p = line.Split('|');
                    if (p.Length < 4) continue;
                    string id = p[0].Trim();
                    if (id.Length == 0) continue;

                    int at;
                    if (index.TryGetValue(id, out at))
                    {
                        var cur = merged[at];
                        DateTime a, b;
                        DateTime.TryParse(cur[2], out a);
                        DateTime.TryParse(p[2], out b);
                        if (b > a) { cur[1] = p[1]; cur[2] = p[2]; }
                        int va, vb;
                        int.TryParse(cur[3], out va);
                        int.TryParse(p[3], out vb);
                        if (vb > va) cur[3] = vb.ToString(CultureInfo.InvariantCulture);
                        result.RoomsMerged++;
                    }
                    else
                    {
                        index[id] = merged.Count;
                        merged.Add(new[] { id, p[1], p[2], p[3] });
                        result.RoomsMerged++;
                    }
                }

                var sb = new StringBuilder();
                foreach (var r in merged)
                    sb.AppendLine(r[0] + "|" + r[1] + "|" + r[2] + "|" + r[3]);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { result.Notes.Add("房间合并失败：" + ex.Message); }

            // ---- 字符数：同一小时取较大的累计值 ----
            try
            {
                var path = Path.Combine(appDir, "stats.txt");
                var hours = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var line in ReadLines(path))
                {
                    var p = line.Split('|');
                    if (p.Length < 2) continue;
                    int v;
                    if (!int.TryParse(p[1], out v)) continue;
                    int old;
                    if (!hours.TryGetValue(p[0], out old) || v > old) hours[p[0]] = v;
                }
                foreach (var line in pkg.Stats)
                {
                    var p = line.Split('|');
                    if (p.Length < 2) continue;
                    int v;
                    if (!int.TryParse(p[1], out v)) continue;
                    int old;
                    if (!hours.TryGetValue(p[0], out old)) { hours[p[0]] = v; result.StatsMerged++; }
                    else if (v > old) { hours[p[0]] = v; result.StatsMerged++; }
                    else result.StatsMerged++;
                }

                var sb = new StringBuilder();
                foreach (var kv in hours) sb.AppendLine(kv.Key + "|" + kv.Value);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { result.Notes.Add("字符数合并失败：" + ex.Message); }

            // ---- 设置 ----
            if (importSettings)
            {
                try
                {
                    if (!string.IsNullOrEmpty(pkg.Config))
                    {
                        File.WriteAllText(Path.Combine(appDir, "config.json"), pkg.Config, new UTF8Encoding(false));
                        result.ConfigApplied = true;
                    }
                }
                catch (Exception ex) { result.Notes.Add("设置写入失败：" + ex.Message); }
            }
            else
            {
                result.ConfigKept = true;
            }

            return result;
        }

        // ==================================================================
        // JSON（手写，不引第三方库）
        // ==================================================================
        private static string ToJson(SyncPackage pkg)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"format\": \"").Append(FormatTag).Append("\",\n");
            sb.Append("  \"version\": ").Append(FormatVersion).Append(",\n");
            sb.Append("  \"exportedAt\": \"").Append(MiniJson.Escape(pkg.ExportedAt)).Append("\",\n");
            sb.Append("  \"machine\": \"").Append(MiniJson.Escape(pkg.Machine)).Append("\",\n");
            sb.Append("  \"appVersion\": \"").Append(MiniJson.Escape(pkg.AppVersion)).Append("\",\n");
            sb.Append("  \"config\": \"").Append(MiniJson.Escape(pkg.Config)).Append("\",\n");
            AppendArray(sb, "claims", pkg.Claims);
            sb.Append(",\n");
            AppendArray(sb, "rooms", pkg.Rooms);
            sb.Append(",\n");
            AppendArray(sb, "stats", pkg.Stats);
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static void AppendArray(StringBuilder sb, string name, List<string> items)
        {
            sb.Append("  \"").Append(name).Append("\": [");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\n    \"").Append(MiniJson.Escape(items[i])).Append("\"");
            }
            if (items.Count > 0) sb.Append("\n  ");
            sb.Append("]");
        }

        private static string Str(Dictionary<string, object> root, string key)
        {
            object v;
            return root.TryGetValue(key, out v) ? (v as string ?? "") : "";
        }

        private static List<string> StrList(Dictionary<string, object> root, string key)
        {
            var list = new List<string>();
            object v;
            if (!root.TryGetValue(key, out v)) return list;
            var arr = v as List<object>;
            if (arr == null) return list;
            foreach (var item in arr)
            {
                var s = item as string;
                if (s != null) list.Add(s);
            }
            return list;
        }

        private static List<string> ReadLines(string path)
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(path)) return list;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                    if (line.Trim().Length > 0) list.Add(line.Trim());
            }
            catch { }
            return list;
        }

        private static string SafeMachineName()
        {
            try { return Environment.MachineName; }
            catch { return "unknown"; }
        }
    }

    /// <summary>够用就好的 JSON 读写：只处理本同步包用到的类型</summary>
    internal static class MiniJson
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 16);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static object Parse(string text)
        {
            int pos = 0;
            var value = ParseValue(text, ref pos);
            return value;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new InvalidDataException("JSON 意外结束");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' || c == 'f') return ParseBool(s, ref i);
            if (c == 'n') { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            while (true)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; break; }
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                dict[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                throw new InvalidDataException("JSON 对象格式错误");
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            while (true)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; break; }
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                throw new InvalidDataException("JSON 数组格式错误");
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new InvalidDataException("JSON 字符串格式错误");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        private static bool ParseBool(string s, ref int i)
        {
            if (s[i] == 't') { i += 4; return true; }
            i += 5; return false;
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "-+.eE0123456789".IndexOf(s[i]) >= 0) i++;
            double v;
            double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
