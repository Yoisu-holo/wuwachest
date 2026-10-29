// CdpSetup.cs — 启动时自动开启 WebView2 调试端口（CDP），退出时撤销
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WuwaChest
{
    internal static class CdpSetup
    {
        private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments";
        private const string PolicyName = "WuwaTappo.exe";
        private const string PetProcessName = "WuwaTappo";

        /// <summary>本次运行是否由我们写入策略（只有自己写的，退出时才撤销）</summary>
        public static bool WrotePolicy { get; private set; }

        /// <summary>本次是否重启过桌宠</summary>
        public static bool RestartedPet { get; private set; }

        /// <summary>给界面/日志用的一句话状态</summary>
        public static string LastMessage = "";

        // ------------------------------------------------------------------
        public static bool IsElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        /// <summary>读策略里的端口；0 表示没有策略</summary>
        public static int ReadPolicyPort()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(PolicyPath))
                {
                    if (key == null) return 0;
                    var value = key.GetValue(PolicyName) as string;
                    if (string.IsNullOrEmpty(value)) return 0;
                    var m = Regex.Match(value, @"--remote-debugging-port=(\d+)");
                    int port;
                    if (m.Success && int.TryParse(m.Groups[1].Value, out port)) return port;
                }
            }
            catch { }
            return 0;
        }

        public static bool WritePolicy(int port)
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(PolicyPath))
                {
                    if (key == null) return false;
                    key.SetValue(PolicyName, "--remote-debugging-port=" + port, RegistryValueKind.String);
                }
                WrotePolicy = true;
                return true;
            }
            catch { return false; }
        }

        public static bool RemovePolicy()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(PolicyPath, true))
                {
                    if (key == null) return true;      // 没有就是已经干净了
                    key.DeleteValue(PolicyName, false);
                }
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        public static bool IsPetRunning()
        {
            try { return Process.GetProcessesByName(PetProcessName).Length > 0; }
            catch { return false; }
        }

        public static string FindPetExe()
        {
            try
            {
                string[] roots =
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };
                foreach (var root in roots)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (key == null) continue;
                        foreach (var sub in key.GetSubKeyNames())
                        {
                            using (var item = key.OpenSubKey(sub))
                            {
                                if (item == null) continue;
                                var name = item.GetValue("DisplayName") as string;
                                if (name == null) continue;
                                if (name.IndexOf("Wuwa", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    name.IndexOf("Tappo", StringComparison.OrdinalIgnoreCase) < 0) continue;

                                var loc = item.GetValue("InstallLocation") as string;
                                if (string.IsNullOrEmpty(loc)) continue;
                                loc = loc.Trim().Trim('"');
                                var exe = Path.Combine(loc, "WuwaTappo.exe");
                                if (File.Exists(exe)) return exe;
                            }
                        }
                    }
                }
            }
            catch { }

            // 兜底：常见安装位置
            string[] guesses =
            {
                @"D:\Program Files\WuwaTappo\WuwaTappo.exe",
                @"C:\Program Files\WuwaTappo\WuwaTappo.exe",
                @"C:\Program Files (x86)\WuwaTappo\WuwaTappo.exe"
            };
            foreach (var g in guesses) { if (File.Exists(g)) return g; }
            return null;
        }

        public static bool RestartPet(out string detail)
        {
            detail = "";
            var exe = FindPetExe();
            if (exe == null) { detail = "找不到桌宠安装目录"; return false; }

            try
            {
                var ps = Process.Start(new ProcessStartInfo("taskkill", "/F /IM WuwaTappo.exe /T")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (ps != null) ps.WaitForExit(5000);
            }
            catch { }

            Thread.Sleep(2500);

            try
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe) });
                Thread.Sleep(1200);
                detail = "已重启：" + exe;
                return true;
            }
            catch (Exception ex)
            {
                detail = "启动失败：" + ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// 启动流程：确保策略存在。
        /// 返回 true 表示「已经拉起了一个提权实例，本进程应立刻退出」。
        /// </summary>
        public static bool PrepareAndMaybeElevate(AppConfig cfg, string[] args)
        {
            if (!cfg.CdpAutoEnable) { LastMessage = "CDP 自动开启：已关闭"; return false; }

            int want = cfg.CdpPort > 0 ? cfg.CdpPort : 9333;
            int have = ReadPolicyPort();

            if (have == want)
            {
                LastMessage = "CDP 策略已存在（端口 " + want + "）";
                return false;
            }

            if (IsElevated())
            {
                if (!WritePolicy(want))
                {
                    LastMessage = "CDP 策略写入失败（注册表被拒绝）";
                    Log.Warn(LastMessage);
                    return false;
                }

                Log.Action("已写入 CDP 调试策略（端口 " + want + "）");

                // 端口其实已经能用（桌宠上次就是带调试参数启动的），就不用再重启它
                bool alreadyLive = false;
                try { alreadyLive = CdpDiscovery.IsAvailable(want); } catch { }

                if (alreadyLive)
                {
                    LastMessage = "策略已写入；桌宠当前进程已带调试端口，无需重启";
                    Log.Info(LastMessage);
                    return false;
                }

                if (cfg.CdpAutoRestartPet && IsPetRunning())
                {
                    string detail;
                    if (RestartPet(out detail))
                    {
                        RestartedPet = true;
                        LastMessage = "已重启桌宠以加载调试端口";
                        Log.Action(LastMessage + "：" + detail);
                    }
                    else
                    {
                        LastMessage = "重启桌宠失败：" + detail;
                        Log.Warn(LastMessage);
                    }
                }
                else
                {
                    LastMessage = "策略已写入，桌宠下次启动时生效";
                }
                return false;
            }

            // 没提权：尝试用 runas 拉起一个提权实例
            if (HasArg(args, "--elevated"))
            {
                LastMessage = "未获得管理员授权，本次退回 UIA";
                Log.Warn(LastMessage);
                return false;
            }

            if (TryRelaunchElevated(args))
            {
                LastMessage = "已请求管理员权限重新启动";
                return true;
            }

            LastMessage = "未获得管理员授权，本次退回 UIA";
            Log.Warn(LastMessage);
            return false;
        }

        private static bool TryRelaunchElevated(string[] args)
        {
            try
            {
                string exe = Application.ExecutablePath;
                string arguments = JoinArgs(args);
                if (arguments.IndexOf("--elevated", StringComparison.OrdinalIgnoreCase) < 0)
                    arguments = (arguments.Length == 0 ? "" : arguments + " ") + "--elevated";

                var psi = new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;   // 用户在 UAC 上点了“否”
            }
        }

        private static string JoinArgs(string[] args)
        {
            if (args == null || args.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a);
            }
            return sb.ToString();
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (var a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>退出流程：撤销本次写入的策略</summary>
        public static void RestoreOnExit(AppConfig cfg)
        {
            if (!cfg.CdpDisableOnExit) return;
            if (!WrotePolicy) return;      // 不是我们写的就别动

            if (RemovePolicy())
                Log.Action("已撤销 CDP 调试策略（桌宠下次重启后端口关闭）");
            else
                Log.Warn("撤销 CDP 策略失败（需要管理员权限）");
        }
    }
}
