// CompositedTabControl.cs — 原生 TabControl 的悬停闪烁靠合成渲染消除
// WinForms 的标签控件是原生控件，DoubleBuffered 对它无效；加 WS_EX_COMPOSITED 后
// 整个控件（连同子页面）先离屏合成再一次性绘制，鼠标移上去不会再闪。
using System;
using System.Windows.Forms;

namespace WuwaChest
{
    internal sealed class CompositedTabControl : TabControl
    {
        private const int WS_EX_COMPOSITED = 0x02000000;

        public CompositedTabControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_COMPOSITED;
                return parameters;
            }
        }
    }
}
