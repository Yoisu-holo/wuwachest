// GlassPanel.cs — 半透明卡片：先让父级把背景图/底色画进来，再盖一层半透明奶油色
using System.Drawing;
using System.Windows.Forms;

namespace WuwaChest
{
    internal class GlassPanel : Panel
    {
        public int Alpha = 190;
        public Color TintColor = Color.FromArgb(255, 252, 246);

        public GlassPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            using (var brush = new SolidBrush(Color.FromArgb(Alpha, TintColor)))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }

    internal class GlassTable : TableLayoutPanel
    {
        public int Alpha = 190;
        public Color TintColor = Color.FromArgb(255, 252, 246);

        public GlassTable()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            using (var brush = new SolidBrush(Color.FromArgb(Alpha, TintColor)))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }
}

