// Rooms.cs — 联机房间：读当前房间号、记录历史、一键重连
// 面板窗口（#/panel）里的 .room-module 才有房间信息；重连就是「先离开、再填号、再点加入」，
// 和上游 WuwaTappoPlus 的 rooms.js 走同一套 DOM，不碰协议。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WuwaChest
{
    internal sealed class RoomInfo
    {
        public string Id = "";
        public string Owner = "";
        public bool InRoom { get { return Id.Length > 0; } }
    }

    internal sealed class RoomEntry
    {
        public string Id = "";
        public string Owner = "";
        public DateTime LastSeen = DateTime.MinValue;
        public int Visits;
    }

    internal sealed class RoomService
    {
        private int _port;
        private readonly string _path;
        private Thread _thread;
        private volatile bool _running;
        private readonly object _lock = new object();
        private List<RoomEntry> _rooms = new List<RoomEntry>();
        private RoomInfo _current = new RoomInfo();

        public event Action Changed;      // 当前房间或历史变化（任意线程）
        public event Action<string> Message;

        public RoomService(AppConfig cfg, string appDir)
        {
            _port = cfg.CdpPort;
            _path = Path.Combine(appDir, "rooms.txt");
            Load();
        }

        public void UpdatePort(int port) { _port = port; }

        public RoomInfo Current
        {
            get { lock (_lock) { return new RoomInfo { Id = _current.Id, Owner = _current.Owner }; } }
        }

        public List<RoomEntry> Snapshot()
        {
            lock (_lock)
            {
                var list = new List<RoomEntry>();
                foreach (var entry in _rooms)
                    list.Add(new RoomEntry { Id = entry.Id, Owner = entry.Owner, LastSeen = entry.LastSeen, Visits = entry.Visits });
                return list;
            }
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(new ThreadStart(Loop));
            _thread.IsBackground = true;
            _thread.Name = "WuwaRooms";
            _thread.Start();
        }

        public void Stop() { _running = false; }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    RoomInfo info;
                    string reason;
                    if (ReadNow(out info, out reason))
                    {
                        bool changed = false;
                        lock (_lock)
                        {
                            if (info.Id != _current.Id || info.Owner != _current.Owner)
                            {
                                _current = info;
                                changed = true;
                            }
                        }
                        if (changed)
                        {
                            if (info.InRoom) Record(info);
                            Raise();
                        }
                    }
                }
                catch { }

                for (int i = 0; i < 40 && _running; i++) Thread.Sleep(100);
            }
        }

        private void Note(string text)
        {
            var handler = Message;
            if (handler != null) { try { handler(text); } catch { } }
        }

        private void Raise()
        {
            var handler = Changed;
            if (handler != null) { try { handler(); } catch { } }
        }

        // ---------- 读当前房间 ----------

        private const string ReadScript =
            "(function(){" +
            "var m=document.querySelector('.room-module');if(!m)return '|n|0|0';" +
            "var num=m.querySelector('.room-number');var id='';" +
            "if(num){var t=(num.textContent||'').replace(/\\s+/g,' ');" +
            "var mm=t.match(/\\u623f\\u95f4\\u53f7\\s*[:\\uff1a]?\\s*([0-9A-Za-z]{4,16})/);id=mm?mm[1]:'';}" +
            "var owner=m.querySelector('li.room-owner .room-member-name');" +
            "var leave=m.querySelector('.leave-room-btn');" +
            "var input=m.querySelector('.join-rooms-input');" +
            "var btn=m.querySelector('.join-rooms-btn');" +
            "return [id,(owner?(owner.textContent||'').trim():''),(leave&&!leave.disabled?1:0)," +
            "(input?1:0),(btn?(btn.disabled?2:1):0)].join('|');" +
            "})()";

        public bool ReadNow(out RoomInfo info, out string reason)
        {
            info = new RoomInfo();
            reason = "";
            string raw;
            if (!EvalPanel(ReadScript, 3000, out raw, out reason)) return false;
            if (string.IsNullOrEmpty(raw)) { reason = "面板没返回"; return false; }

            var parts = raw.Split('|');
            if (parts.Length < 5) { reason = "房间状态格式异常"; return false; }

            info.Id = parts[0] == "n" ? "" : parts[0].Trim();
            info.Owner = parts[1] == "n" ? "" : parts[1].Trim();
            return true;
        }

        // ---------- 一键重连 ----------

        public bool Reconnect(string roomId, out string reason)
        {
            reason = "";
            roomId = (roomId ?? "").Trim();
            if (!Regex.IsMatch(roomId, "^[0-9A-Za-z]{4,16}$"))
            {
                reason = "房间号格式不对";
                return false;
            }

            Note("正在重连 " + roomId + "…");

            try
            {
                // 1) 已经在房间里 -> 先离开
                string raw;
                if (EvalPanel(ReadScript, 3000, out raw, out reason) && !string.IsNullOrEmpty(raw))
                {
                    var parts = raw.Split('|');
                    if (parts.Length >= 5 && parts[0] != "n" && parts[0].Length > 0)
                    {
                        if (parts[0] == roomId)
                        {
                            reason = "已经在房间里了";
                            return true;
                        }
                        EvalPanel("(function(){var b=document.querySelector('.leave-room-btn');if(!b)return 'none';b.click();return 'clicked';})()",
                            3000, out raw, out reason);
                        Note("已离开当前房间，等待输入框…");
                        if (!WaitFor("(function(){return document.querySelector('.join-rooms-input')&&document.querySelector('.join-rooms-btn')?'yes':'no';})()",
                                "yes", 12000))
                        {
                            reason = "离开房间后没等到输入框（联机没连上？）";
                            return false;
                        }
                    }
                }

                // 2) 填房间号（Vue 的 v-model 只认原生 setter + input 事件）
                string fill = "(function(id){" +
                              "var input=document.querySelector('.join-rooms-input');" +
                              "var btn=document.querySelector('.join-rooms-btn');" +
                              "if(!input||!btn)return 'missing';" +
                              "var d=Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value');" +
                              "if(d&&d.set)d.set.call(input,id);else input.value=id;" +
                              "try{input.dispatchEvent(new Event('input',{bubbles:true}));}catch(e){}" +
                              "return btn.disabled?'disabled':'ok';" +
                              "})('" + roomId + "')";

                EvalPanel(fill, 3000, out raw, out reason);
                if (raw == "missing")
                {
                    reason = "找不到输入框/加入按钮";
                    return false;
                }

                // 3) 等按钮可用（v-model 是异步同步到桌宠的）
                if (!WaitFor("(function(){var b=document.querySelector('.join-rooms-btn');return (b&&!b.disabled)?'yes':'no';})()",
                        "yes", 8000))
                {
                    reason = "加入按钮一直是灰的（房间号可能不合法，或联机未连接）";
                    return false;
                }

                // 4) 点加入
                string click = "(function(){var b=document.querySelector('.join-rooms-btn');if(!b)return 'missing';b.click();return 'clicked';})()";
                EvalPanel(click, 3000, out raw, out reason);
                Note("已提交，等待进入房间…");

                // 5) 等房间号变成目标（最多 20 秒）
                for (int i = 0; i < 40; i++)
                {
                    Thread.Sleep(500);
                    string state;
                    if (!EvalPanel(ReadScript, 2500, out state, out reason)) continue;
                    var parts = state.Split('|');
                    if (parts.Length >= 2 && parts[0] == roomId)
                    {
                        string owner = parts[1];
                        Record(new RoomInfo { Id = roomId, Owner = owner });
                        reason = "";
                        return true;
                    }
                }

                reason = ExplainFailure();
                return false;
            }
            catch (Exception ex)
            {
                reason = "重连异常：" + ex.Message;
                return false;
            }
        }

        // 失败时从面板上找一句人话（toast / 错误文案）
        private string ExplainFailure()
        {
            string raw;
            string reason;
            const string script =
                "(function(){" +
                "var errs=document.querySelectorAll('.toast,.toast-message,[class*=error],[class*=warn]');" +
                "for(var i=0;i<errs.length;i++){var s=(errs[i].innerText||'').trim();" +
                "if(s&&/\\u5931\\u8d25|\\u9519\\u8bef|\\u4e0d\\u5b58\\u5728|\\u5df2\\u6ee1|\\u65e0\\u6cd5|\\u8d85\\u65f6/.test(s))return s.replace(/\\s+/g,' ').slice(0,120);}" +
                "var m=document.querySelector('.room-module');var t=m?(m.innerText||'').replace(/\\s+/g,' '):'';" +
                "var mm=t.match(/[^。]{0,20}(\\u5931\\u8d25|\\u9519\\u8bef|\\u4e0d\\u5b58\\u5728|\\u5df2\\u6ee1|\\u65e0\\u6cd5)[^。]{0,40}/);" +
                "return mm?mm[0]:'等待进入房间超时';" +
                "})()";
            if (EvalPanel(script, 2500, out raw, out reason) && !string.IsNullOrEmpty(raw))
                return raw.Trim();
            return "等待进入房间超时（房间可能不存在/已满，或联机断开了）";
        }

        private bool WaitFor(string script, string expected, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            string raw;
            string reason;
            while (DateTime.Now < deadline)
            {
                if (EvalPanel(script, 2500, out raw, out reason) && raw != null && raw.Trim() == expected) return true;
                Thread.Sleep(300);
            }
            return false;
        }

        private bool EvalPanel(string script, int timeoutMs, out string result, out string reason)
        {
            result = null;
            reason = "";
            try
            {
                var page = FindPanelPage();
                if (page == null) { reason = "找不到桌宠面板页面"; return false; }

                using (var session = CdpSession.Connect(page.WebSocketUrl, 3000))
                {
                    if (session == null) { reason = "连接调试端口失败"; return false; }
                    result = session.EvaluateString(script, timeoutMs);
                    return result != null;
                }
            }
            catch (Exception ex)
            {
                reason = "读取面板异常：" + ex.Message;
                return false;
            }
        }

        private CdpTarget FindPanelPage()
        {
            foreach (var target in CdpDiscovery.ListTargets(CdpDiscovery.ResolvePort(_port)))
            {
                if (target.Url == null) continue;
                if (target.Url.IndexOf("#/panel", StringComparison.OrdinalIgnoreCase) >= 0) return target;
            }
            return null;
        }

        // ---------- 历史记录 ----------

        private void Record(RoomInfo info)
        {
            if (string.IsNullOrEmpty(info.Id)) return;
            lock (_lock)
            {
                RoomEntry found = null;
                foreach (var entry in _rooms)
                {
                    if (entry.Id == info.Id) { found = entry; break; }
                }
                if (found == null)
                {
                    found = new RoomEntry { Id = info.Id, Visits = 0 };
                    _rooms.Insert(0, found);
                }
                else
                {
                    _rooms.Remove(found);
                    _rooms.Insert(0, found);
                }
                if (!string.IsNullOrEmpty(info.Owner)) found.Owner = info.Owner;
                found.LastSeen = DateTime.Now;
                found.Visits++;

                while (_rooms.Count > 30) _rooms.RemoveAt(_rooms.Count - 1);
            }
            Save();
            Raise();
        }

        public void Forget(string roomId)
        {
            lock (_lock)
            {
                for (int i = _rooms.Count - 1; i >= 0; i--)
                {
                    if (_rooms[i].Id == roomId) _rooms.RemoveAt(i);
                }
            }
            Save();
            Raise();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var line in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    var parts = line.Split('|');
                    if (parts.Length < 4) continue;
                    DateTime seen;
                    int visits;
                    DateTime.TryParse(parts[2], out seen);
                    int.TryParse(parts[3], out visits);
                    _rooms.Add(new RoomEntry { Id = parts[0], Owner = parts[1], LastSeen = seen, Visits = visits });
                }
            }
            catch { }
        }

        private void Save()
        {
            try
            {
                var sb = new StringBuilder();
                lock (_lock)
                {
                    foreach (var entry in _rooms)
                    {
                        sb.AppendLine(entry.Id + "|" + (entry.Owner ?? "") + "|" +
                                      entry.LastSeen.ToString("yyyy-MM-dd HH:mm:ss") + "|" + entry.Visits);
                    }
                }
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
