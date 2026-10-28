// CdpWatcher.cs — 常驻 CDP 会话：让桌宠页面把状态变化主动推上来
// 做法：Runtime.addBinding 注册一个 JS 绑定，页面里注入 MutationObserver + 250ms 兜底定时器，
// 状态一变就调用 __wucPush(...)，我们这边收 Runtime.bindingCalled 事件即可，不用轮询。
using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WuwaChest
{
    internal sealed class CdpWatcher
    {
        private readonly AppConfig _cfg;
        private readonly int _port;
        private Thread _thread;
        private volatile bool _running;
        private int _reconnects;

        // 在引擎线程上触发
        public event Action<PetSnapshot> Snapshot;

        public CdpWatcher(int port, AppConfig cfg)
        {
            _port = port;
            _cfg = cfg;
        }

        public bool IsRunning { get { return _running; } }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(new ThreadStart(Run));
            _thread.IsBackground = true;
            _thread.Name = "WuwaCdpWatcher";
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
        }

        private const string InstallScript =
            "(function(){" +
            "if(window.__wucWatch) return 'exists';" +
            "window.__wucWatch=true;" +
            "function snap(){" +
            "var n='';var els=document.querySelectorAll('.text.web-bold-font');" +
            "for(var i=0;i<els.length;i++){var t=(els[i].innerText||'').trim();if(/^[0-9,]+$/.test(t)){n=t;break;}}" +
            "var b=document.querySelector('.reward-btn');" +
            "var cds=document.querySelectorAll('.reward-countdown');var next=0;" +
            "for(var j=0;j<cds.length;j++){var p=((cds[j].innerText||'').match(/\\d+/g)||[]).map(Number);" +
            "var s=p.length===3?p[0]*3600+p[1]*60+p[2]:(p.length===2?p[0]*60+p[1]:0);if(s>0&&(next===0||s<next))next=s;}" +
            "var app=document.querySelector('#app');" +
            "var pinia=app&&app.__vue_app__?app.__vue_app__.config.globalProperties.$pinia:null;" +
            "var st=pinia?pinia._s.get('GlobalState'):null;" +
            "var u=st&&st.$state?st.$state.userInfo||{}:{};" +
            "var bal=Number(u.total_click_count||0)-Number(u.total_click_consume||0);" +
            "var logged=st&&st.isLoggedIn?1:0;" +
            "return [n,b?1:0,next,bal,logged,Math.round(Number(u.total_click_count||0))].join('|');" +
            "}" +
            "var last='';" +
            "function tick(){var s=snap();if(s!==last){last=s;try{window.__wucPush(s);}catch(e){}}}" +
            "setInterval(tick,250);" +
            "try{var mo=new MutationObserver(tick);mo.observe(document.body,{subtree:true,childList:true,characterData:true});}catch(e){}" +
            "tick();" +
            "return 'installed';" +
            "})()";

        private void Run()
        {
            while (_running)
            {
                try
                {
                    RunOnce();
                }
                catch { }

                if (!_running) break;

                // 断开后退避重连
                _reconnects++;
                int wait = Math.Min(30, 2 + _reconnects * 2);
                for (int i = 0; i < wait * 10 && _running; i++) Thread.Sleep(100);
            }
        }

        private void RunOnce()
        {
            var target = FindMainPage();
            if (target == null) { Thread.Sleep(3000); return; }

            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            var connect = socket.ConnectAsync(new Uri(target.WebSocketUrl), CancellationToken.None);
            if (!connect.Wait(5000)) { socket.Dispose(); return; }

            try
            {
                _reconnects = 0;

                // 1) 注册绑定
                Send(socket, "{\"id\":1,\"method\":\"Runtime.addBinding\",\"params\":{\"name\":\"__wucPush\"}}");
                ReadUntil(socket, 1, 5000);

                // 2) 注入观察脚本
                Send(socket,
                    "{\"id\":2,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"" +
                    Escape(InstallScript) + "\",\"returnByValue\":true}}");
                ReadUntil(socket, 2, 5000);

                // 3) 收推送；每 20 秒补一次注入（页面重载后重新挂上）
                var lastPing = DateTime.Now;
                while (_running && socket.State == WebSocketState.Open)
                {
                    string message = Receive(socket, 1000);
                    if (message == null)
                    {
                        if ((DateTime.Now - lastPing).TotalSeconds >= 20)
                        {
                            lastPing = DateTime.Now;
                            Send(socket, "{\"id\":9,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"" +
                                        Escape(InstallScript) + "\",\"returnByValue\":true}}");
                        }
                        continue;
                    }
                    Handle(message);
                }
            }
            finally
            {
                try { if (socket.State == WebSocketState.Open) socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(500); }
                catch { }
                socket.Dispose();
            }
        }

        private void Handle(string message)
        {
            if (message.IndexOf("Runtime.bindingCalled", StringComparison.Ordinal) < 0) return;

            var match = Regex.Match(message, "\"payload\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!match.Success) return;

            string payload = CdpDiscovery.Unescape(match.Groups[1].Value);
            var parts = payload.Split('|');
            if (parts.Length < 5) return;

            var snap = new PetSnapshot();
            snap.Ok = true;
            snap.Source = "cdp-push";
            snap.Balance = -1;

            int number;
            int.TryParse(Regex.Replace(parts[0], "[^0-9]", ""), out number);
            snap.Number = number;
            snap.HasNumber = parts[0].Length > 0;
            snap.Chest = parts[1] == "1";

            int next;
            if (int.TryParse(parts[2], out next)) snap.NextChestSeconds = next;

            int balance;
            if (int.TryParse(parts[3], out balance)) snap.Balance = balance;

            snap.LoggedIn = parts[4] == "1";

            if (parts.Length >= 6)
            {
                int cumulative;
                if (int.TryParse(parts[5], out cumulative)) snap.CumulativeClicks = cumulative;
            }
            snap.HasChestTimers = false;

            var handler = Snapshot;
            if (handler != null)
            {
                try { handler(snap); } catch { }
            }
        }

        private CdpTarget FindMainPage()
        {
            foreach (var target in CdpDiscovery.ListTargets(_port))
            {
                if (target.Url == null) continue;
                if (target.Url.IndexOf("tauri.localhost", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (target.Url.EndsWith("#/", StringComparison.Ordinal) ||
                    target.Url.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
                    return target;
            }
            return null;
        }

        private static string Escape(string text)
        {
            return CdpDiscovery.Escape(text);
        }

        private static void Send(ClientWebSocket socket, string payload)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(3000);
            }
            catch { }
        }

        private static string Receive(ClientWebSocket socket, int timeoutMs)
        {
            try
            {
                var buffer = new byte[64 * 1024];
                var sb = new StringBuilder();
                while (true)
                {
                    var task = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (!task.Wait(timeoutMs)) return null;
                    var result = task.Result;
                    if (result.MessageType == WebSocketMessageType.Close) return null;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage) break;
                }
                return sb.Length == 0 ? "" : sb.ToString();
            }
            catch { return null; }
        }

        private static string ReadUntil(ClientWebSocket socket, int id, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                string message = Receive(socket, 500);
                if (message == null) continue;
                if (Regex.IsMatch(message, "\"id\"\\s*:\\s*" + id + "\\b")) return message;
            }
            return null;
        }
    }
}
