// History.cs — 从日志汇总领取记录
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WuwaChest
{
    internal sealed class ClaimRecord
    {
        public DateTime When;
        public int Before;
        public int After;
        public string Screenshot = "";   // 相对 logs 的路径，如 rewards/20261027-204633-1.png
        public string Reward = "";       // 弹窗上的文字，如「新表情！」
    }

    internal static class History
    {
        private static readonly Regex ClaimLine = new Regex(
            @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}).*领取成功（本次第 \d+ 个）：字符数 (\d+) -> (\d+)",
            RegexOptions.Compiled);

        private static readonly Regex ShotPattern = new Regex(@"｜截图\s+([^\s｜]+\.png)", RegexOptions.Compiled);
        private static readonly Regex RewardPattern = new Regex(@"｜奖励\s+([^｜\r\n]+)", RegexOptions.Compiled);

        // 新的在前
        public static List<ClaimRecord> ReadClaims(string logsDir, int max)
        {
            var list = new List<ClaimRecord>();
            try
            {
                if (!Directory.Exists(logsDir)) return list;
                var files = new List<string>(Directory.GetFiles(logsDir, "chest-*.log"));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                files.Reverse();

                foreach (var file in files)
                {
                    var lines = new List<string>();
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null) lines.Add(line);
                    }

                    for (int i = lines.Count - 1; i >= 0; i--)
                    {
                        var m = ClaimLine.Match(lines[i]);
                        if (!m.Success) continue;

                        DateTime when;
                        if (!DateTime.TryParse(m.Groups[1].Value, out when)) continue;

                        int before, after;
                        int.TryParse(m.Groups[2].Value, out before);
                        int.TryParse(m.Groups[3].Value, out after);

                        var record = new ClaimRecord { When = when, Before = before, After = after };
                        var shot = ShotPattern.Match(lines[i]);
                        if (shot.Success) record.Screenshot = shot.Groups[1].Value;
                        var reward = RewardPattern.Match(lines[i]);
                        if (reward.Success) record.Reward = reward.Groups[1].Value.Trim();

                        list.Add(record);
                        if (list.Count >= max) return list;
                    }
                }
            }
            catch { }
            return list;
        }

        public static int CountToday(string logsDir)
        {
            int count = 0;
            foreach (var line in Log.ReadDay(logsDir, null))
            {
                if (line.IndexOf("领取成功", StringComparison.Ordinal) >= 0) count++;
            }
            return count;
        }
    }
}
