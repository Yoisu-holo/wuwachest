// Autostart.cs — 开机自启（HKCU Run，无需管理员）
using System;
using Microsoft.Win32;

namespace WuwaChest
{
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WuwaChest";

        public static bool IsEnabled(string exePath)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    var value = key.GetValue(ValueName) as string;
                    if (string.IsNullOrEmpty(value)) return false;
                    return value.IndexOf(exePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        public static void Enable(string exePath)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new InvalidOperationException("无法打开注册表启动项");
                key.SetValue(ValueName, "\"" + exePath + "\" --tray", RegistryValueKind.String);
            }
        }

        public static void Disable()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }
}
