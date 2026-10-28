// TrayWatcher.cs — 监听 TaskbarCreated：资源管理器重启后把托盘图标补回去
using System;
using System.Windows.Forms;

namespace WuwaChest
{
    internal sealed class TrayWatcher : NativeWindow
    {
        private readonly uint _taskbarCreated;
        public event Action TaskbarRestarted;

        public TrayWatcher()
        {
            _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            var cp = new CreateParams();
            cp.Caption = "WuwaChestTrayWatcher";
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (_taskbarCreated != 0 && m.Msg == (int)_taskbarCreated)
            {
                var handler = TaskbarRestarted;
                if (handler != null)
                {
                    try { handler(); } catch { }
                }
            }
            base.WndProc(ref m);
        }
    }
}
