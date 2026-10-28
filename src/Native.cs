// Native.cs — 少量 Win32 封装（DPI、鼠标兜底、完整性级别、控制台附着）
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WuwaChest
{
    internal static class Native
    {
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string message);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleOutputCP(uint codePage);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int len, out int retLen);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static void MakeDpiAware()
        {
            try { SetProcessDPIAware(); } catch { }
        }

        // 移动鼠标点击 (x, y)；restoreCursor=true 时点完把光标放回原处
        public static void ClickAt(int x, int y, bool restoreCursor)
        {
            POINT old;
            GetCursorPos(out old);
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(120);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(45);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            if (restoreCursor)
            {
                System.Threading.Thread.Sleep(60);
                SetCursorPos(old.X, old.Y);
            }
        }

        // 连续点击用：取/设光标、快速一次点击
        public static void GetCursor(out int x, out int y)
        {
            POINT point;
            GetCursorPos(out point);
            x = point.X;
            y = point.Y;
        }

        public static void MoveCursor(int x, int y)
        {
            SetCursorPos(x, y);
        }

        public static void QuickClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        }

        // 返回当前进程完整性级别 RID：0x1000=Low，0x2000=Medium，0x3000=High
        public static int IntegrityRid()
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out token)) return -1;
            try
            {
                int len;
                GetTokenInformation(token, 25, IntPtr.Zero, 0, out len);
                if (len <= 0) return -1;
                IntPtr buffer = Marshal.AllocHGlobal(len);
                try
                {
                    if (!GetTokenInformation(token, 25, buffer, len, out len)) return -1;
                    IntPtr sid = Marshal.ReadIntPtr(buffer);
                    int count = Marshal.ReadByte(sid, 1);
                    return Marshal.ReadInt32(new IntPtr(sid.ToInt64() + 8 + (count - 1) * 4));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseHandle(token); }
        }

        public static bool IsLowIntegrity()
        {
            int rid = IntegrityRid();
            return rid > 0 && rid < 0x2000;
        }

        // winexe 也能在命令行里输出：附着到父控制台并重定向 stdout
        public static bool TryAttachConsole()
        {
            try
            {
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) return false;
                SetConsoleOutputCP(65001);
                IntPtr handle = CreateFileW("CONOUT$", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
                var stream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(handle, true), FileAccess.Write);
                var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.AutoFlush = true;
                Console.SetOut(writer);
                return true;
            }
            catch { return false; }
        }
    }
}
