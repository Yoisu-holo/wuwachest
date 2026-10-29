// DefaultBackground.cs — 读取内嵌的默认面板背景图
using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace WuwaChest
{
    internal static class DefaultBackground
    {
        private const string ResourceName = "DefaultBackground.jpg";

        public static Image Load()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) return null;

                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        buffer.Position = 0;
                        using (var image = Image.FromStream(buffer))
                        {
                            return new Bitmap(image);   // 复制一份，避免流被释放后无法绘制
                        }
                    }
                }
            }
            catch { return null; }
        }
    }
}
