// ChartPanel.cs — 字符数统计图表（逐小时 / 逐天），GDI+ 手绘，不引第三方库
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace WuwaChest
{
    internal enum ChartMode
    {
        Hourly,
        Daily
    }

    internal sealed class ChartPanel : Control
    {
        private StatsStore _stats;
        private ChartMode _mode = ChartMode.Hourly;

        private static readonly Color AxisColor = Color.FromArgb(120, 96, 72);
        private static readonly Color BarColor = Color.FromArgb(150, 196, 152);
        private static readonly Color BarEdge = Color.FromArgb(96, 150, 104);
        private static readonly Color LineColor = Color.FromArgb(196, 120, 72);
        private static readonly Color TextColor = Color.FromArgb(70, 56, 44);
        private static readonly Color PaperColor = Color.FromArgb(242, 255, 253, 247);

        public ChartPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public ChartMode Mode
        {
            get { return _mode; }
            set { if (_mode != value) { _mode = value; Invalidate(); } }
        }

        public void Bind(StatsStore stats)
        {
            _stats = stats;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var area = new Rectangle(0, 0, Width, Height);
            if (area.Width < 80 || area.Height < 80) return;

            using (var paper = new SolidBrush(PaperColor))
            using (var path = RoundRect(area, 10))
                g.FillPath(paper, path);

            List<StatPoint> points = null;
            string title = _mode == ChartMode.Hourly ? "最近 24 小时" : "最近 14 天";
            string gainLabel = _mode == ChartMode.Hourly ? "每小时新增" : "每天新增";

            if (_stats != null)
                points = _mode == ChartMode.Hourly ? _stats.Hourly(24) : _stats.Daily(14);

            using (var titleFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold))
            using (var smallFont = new Font("Microsoft YaHei UI", 7.5f))
            using (var textBrush = new SolidBrush(TextColor))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(140, 120, 100)))
            {
                g.DrawString(title, titleFont, textBrush, 12, 8);

                // 图例
                int legendX = 12 + (int)g.MeasureString(title, titleFont).Width + 14;
                using (var barBrush = new SolidBrush(BarColor))
                using (var linePen = new Pen(LineColor, 2f))
                {
                    g.FillRectangle(barBrush, legendX, 13, 9, 9);
                    g.DrawString(gainLabel, smallFont, mutedBrush, legendX + 13, 10);
                    float lx = legendX + 13 + g.MeasureString(gainLabel, smallFont).Width + 12;
                    g.DrawLine(linePen, lx, 18, lx + 14, 18);
                    g.DrawString("累计", smallFont, mutedBrush, lx + 18, 10);
                }

                if (points == null || points.Count == 0)
                {
                    g.DrawString("还没有数据：监控运行时会按小时自动记录。", smallFont, mutedBrush, 14, Height / 2f - 8);
                    return;
                }

                // 只记了一两个小时的时候画出来全是 0，不如直接说明
                if (_stats != null && _stats.Count < 2)
                {
                    int currentCumulative = 0;
                    foreach (var point in points) if (point.Cumulative > currentCumulative) currentCumulative = point.Cumulative;
                    g.DrawString("正在记录…已记 " + _stats.Count + " 个小时，满 2 小时后出图。",
                        smallFont, mutedBrush, 14, Height / 2f - 20);
                    g.DrawString("当前累计：" + currentCumulative.ToString("N0", CultureInfo.InvariantCulture),
                        titleFont, textBrush, 14, Height / 2f + 2);
                    return;
                }

                int left = 66, right = 16, top = 42, bottom = 30;
                var plot = new Rectangle(left, top, Width - left - right, Height - top - bottom);
                if (plot.Width < 40 || plot.Height < 30) return;

                int maxGain = 1, maxCum = 1;
                foreach (var point in points)
                {
                    if (point.Gain > maxGain) maxGain = point.Gain;
                    if (point.Cumulative > maxCum) maxCum = point.Cumulative;
                }

                // 网格 + 左侧刻度（新增）
                using (var gridPen = new Pen(Color.FromArgb(60, 140, 120, 100), 1f))
                using (var axisPen = new Pen(AxisColor, 1.2f))
                {
                    for (int i = 0; i <= 4; i++)
                    {
                        int y = plot.Bottom - plot.Height * i / 4;
                        g.DrawLine(gridPen, plot.Left, y, plot.Right, y);

                        int value = maxGain * i / 4;
                        string label = Compact(value);
                        var size = g.MeasureString(label, smallFont);
                        g.DrawString(label, smallFont, mutedBrush, plot.Left - size.Width - 5, y - size.Height / 2);
                    }
                    g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
                    g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
                }

                int count = points.Count;
                float slot = (float)plot.Width / count;
                float barWidth = Math.Max(3f, slot * 0.55f);

                // 柱子：每时段新增
                using (var barBrush = new SolidBrush(BarColor))
                using (var barPen = new Pen(BarEdge, 1f))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float cx = plot.Left + slot * i + slot / 2f;
                        int h = (int)Math.Round((double)points[i].Gain / maxGain * (plot.Height - 4));
                        if (h > 0)
                        {
                            var rect = new RectangleF(cx - barWidth / 2f, plot.Bottom - h, barWidth, h);
                            g.FillRectangle(barBrush, rect);
                            g.DrawRectangle(barPen, rect.X, rect.Y, rect.Width, rect.Height);
                        }
                    }
                }

                // 折线：累计
                var linePoints = new PointF[count];
                for (int i = 0; i < count; i++)
                {
                    float cx = plot.Left + slot * i + slot / 2f;
                    float cy = plot.Bottom - 3 - (float)points[i].Cumulative / maxCum * (plot.Height - 12);
                    linePoints[i] = new PointF(cx, cy);
                }
                using (var linePen = new Pen(LineColor, 2f) { LineJoin = LineJoin.Round })
                {
                    if (linePoints.Length > 1) g.DrawLines(linePen, linePoints);
                    using (var dotBrush = new SolidBrush(LineColor))
                    {
                        foreach (var point in linePoints)
                            g.FillEllipse(dotBrush, point.X - 2f, point.Y - 2f, 4f, 4f);
                    }
                }

                // 横轴标签：最多 8 个
                int step = Math.Max(1, (int)Math.Ceiling(count / 8.0));
                for (int i = 0; i < count; i += step)
                {
                    float cx = plot.Left + slot * i + slot / 2f;
                    var size = g.MeasureString(points[i].Label, smallFont);
                    g.DrawString(points[i].Label, smallFont, mutedBrush,
                        cx - size.Width / 2, plot.Bottom + 6);
                }

                // 顶部右侧：当前累计
                var last = points[count - 1];
                string current = "累计 " + last.Cumulative.ToString("N0", CultureInfo.InvariantCulture);
                var currentSize = g.MeasureString(current, smallFont);
                g.DrawString(current, smallFont, textBrush,
                    Width - currentSize.Width - 14, 10);

                // 本期新增合计
                int sum = 0;
                foreach (var point in points) sum += point.Gain;
                string total = "共 +" + sum.ToString("N0", CultureInfo.InvariantCulture);
                g.DrawString(total, smallFont, mutedBrush, Width - g.MeasureString(total, smallFont).Width - 14, 24);
            }
        }

        private static string Compact(int value)
        {
            if (value >= 1000000) return (value / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (value >= 10000) return (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
