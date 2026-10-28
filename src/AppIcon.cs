// AppIcon.cs — 从内嵌的 icon.png 生成不同尺寸的图标（托盘 32 / 标题栏 32）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WuwaChest
{
    internal static class AppIcon
    {
        private const string ResourceName = "AppIcon.png";

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Get(int size)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) return null;
                    using (var source = Image.FromStream(stream))
                    {
                        return FromImage(source, size);
                    }
                }
            }
            catch { return null; }
        }

        private static Icon FromImage(Image source, int size)
        {
            using (var bitmap = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, new Rectangle(0, 0, size, size));
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (var temp = Icon.FromHandle(handle)) { return (Icon)temp.Clone(); }
                }
                finally { DestroyIcon(handle); }
            }
        }

        // 导出 256 原图，供 exe 图标/其他用途
        public static Image LoadOriginal()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) return null;
                    using (var image = Image.FromStream(stream)) { return new Bitmap(image); }
                }
            }
            catch { return null; }
        }
    }
}
