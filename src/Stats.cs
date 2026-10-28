// Stats.cs — 字符数历史：按小时记录累计值，供图表使用
// 存储格式（stats.txt，一行一个小时）：
//   yyyyMMddHH|cumulative
// 只保留每个小时里见到的最大累计值（累计只增不减，取最大最稳）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WuwaChest
{
    internal sealed class StatPoint
    {
        public string Label = "";
        public int Gain;          // 该时段新增
        public int Cumulative;    // 时段结束时的累计
    }

    internal sealed class StatsStore
    {
        private readonly string _path;
        private readonly Dictionary<string, int> _hours = new Dictionary<string, int>();
        private DateTime _lastSave = DateTime.MinValue;
        private int _lastValue = -1;

        public StatsStore(string appDir)
        {
            _path = Path.Combine(appDir, "stats.txt");
            Load();
        }

        public bool HasData { get { return _hours.Count > 0; } }

        // 已记录了多少个小时（图表判断数据够不够用）
        public int Count { get { lock (_hours) return _hours.Count; } }

        public bool Note(int cumulative)
        {
            if (cumulative <= 0) return false;
            string key = DateTime.Now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);

            lock (_hours)
            {
                int old;
                if (_hours.TryGetValue(key, out old))
                {
                    if (cumulative <= old) return false;
                    _hours[key] = cumulative;
                }
                else
                {
                    _hours[key] = cumulative;
                }
                _lastValue = cumulative;
            }

            if ((DateTime.Now - _lastSave).TotalSeconds >= 60) Save();
            return true;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                lock (_hours)
                {
                    var keys = new List<string>(_hours.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    foreach (var key in keys) sb.AppendLine(key + "|" + _hours[key]);
                }
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
                _lastSave = DateTime.Now;
            }
            catch { }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var line in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    var parts = line.Split('|');
                    if (parts.Length < 2) continue;
                    int value;
                    if (!int.TryParse(parts[1], out value)) continue;
                    _hours[parts[0]] = value;
                }
            }
            catch { }
        }

        // 最近 count 小时（含当前小时），按时间正序
        public List<StatPoint> Hourly(int count)
        {
            var list = new List<StatPoint>();
            var now = DateTime.Now;
            int previous = -1;

            for (int i = count - 1; i >= 0; i--)
            {
                var slot = now.AddHours(-i);
                string key = slot.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
                int value;
                bool has;
                lock (_hours) has = _hours.TryGetValue(key, out value);
                if (!has) value = previous;   // 没采样的小时沿用上一小时，图不会断

                int gain = (previous >= 0 && value >= 0) ? Math.Max(0, value - previous) : 0;
                list.Add(new StatPoint
                {
                    Label = slot.ToString("HH", CultureInfo.InvariantCulture) + "时",
                    Gain = gain,
                    Cumulative = value < 0 ? 0 : value
                });
                previous = value;
            }
            return list;
        }

        // 最近 count 天（含今天），按时间正序；每天取当天最后一个小时的值
        public List<StatPoint> Daily(int count)
        {
            var list = new List<StatPoint>();
            var now = DateTime.Today;

            // 先按天聚合
            var daily = new Dictionary<string, int>();
            lock (_hours)
            {
                foreach (var pair in _hours)
                {
                    if (pair.Key.Length < 10) continue;
                    string day = pair.Key.Substring(0, 8);
                    int old;
                    if (!daily.TryGetValue(day, out old) || pair.Value > old) daily[day] = pair.Value;
                }
            }

            int previous = -1;
            for (int i = count - 1; i >= 0; i--)
            {
                var slot = now.AddDays(-i);
                string key = slot.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                int value;
                bool has = daily.TryGetValue(key, out value);
                if (!has) value = previous;

                int gain = (previous >= 0 && value >= 0) ? Math.Max(0, value - previous) : 0;
                list.Add(new StatPoint
                {
                    Label = slot.ToString("MM-dd", CultureInfo.InvariantCulture),
                    Gain = gain,
                    Cumulative = value < 0 ? 0 : value
                });
                previous = value;
            }
            return list;
        }
    }
}
