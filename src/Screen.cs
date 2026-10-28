// Screen.cs — 截屏保存
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace WuwaChest
{
    internal static class Screen
    {
        public static bool Capture(int x, int y, int width, int height, string path)
        {
            try
            {
                if (width <= 0 || height <= 0) return false;

                var bounds = SystemInformation.VirtualScreen;
                int left = Math.Max(bounds.Left, x);
                int top = Math.Max(bounds.Top, y);
                int right = Math.Min(bounds.Right, x + width);
                int bottom = Math.Min(bounds.Bottom, y + height);

                if (right - left < 8 || bottom - top < 8) return false;

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var bitmap = new Bitmap(right - left, bottom - top, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(left, top, 0, 0, new Size(bitmap.Width, bitmap.Height),
                            CopyPixelOperation.SourceCopy);
                    }
                    bitmap.Save(path, ImageFormat.Png);
                }
                return true;
            }
            catch { return false; }
        }

        // 截取「领到了什么」：优先用弹窗元素的包围盒，退化为桌宠上方的一块
        public static string CaptureReward(string logsDir, ToastInfo toast, PetSnapshot snap, int index)
        {
            int x, y, w, h;

            if (toast != null && toast.Found)
            {
                double totalArea = (double)toast.Width * toast.Height;

                // 弹窗外面常有一层整屏透明的容器，按面积占比重挑出「卡片本体」
                ElementInfo card = null;
                double cardArea = 0;
                foreach (var element in toast.Elements)
                {
                    double area = (double)element.W * element.H;
                    if (area > totalArea * 0.4) continue;
                    if (area > cardArea) { cardArea = area; card = element; }
                }

                // 找不到明显比整体小的元素时（例如整层就只有这一张卡片），取面积最大的那个
                if (card == null)
                {
                    foreach (var element in toast.Elements)
                    {
                        double area = (double)element.W * element.H;
                        if (area > cardArea) { cardArea = area; card = element; }
                    }
                }

                if (card != null && card.W >= 120 && card.H >= 120)
                {
                    // 弹窗元素在小分辨率下本身就是卡片本体（实测 1920×1080 上只有 165×165），
                    // 再按比例往内缩会缩进画面里、只剩立绘碎片。
                    // 只有元素足够大（2560×1600 上是 293×293，周围带整屏透明留白）时才按比例取中间那块。
                    if (card.W < 200 || card.H < 200)
                    {
                        x = (int)card.X - 8;
                        y = (int)card.Y - 8;
                        w = (int)card.W + 16;
                        h = (int)card.H + 16;
                    }
                    else
                    {
                        // 弹窗元素是个正方形，卡片本体只占中间一块、周围是透明留白，
                        // 按实测比例取景：横向 24.5%~79.5%，纵向 24.5%~93%（含下方说明文字）
                        x = (int)(card.X + card.W * 0.245);
                        y = (int)(card.Y + card.H * 0.245);
                        w = (int)(card.W * 0.55);
                        h = (int)(card.H * 0.685);
                    }
                }
                else
                {
                    x = toast.X;
                    y = toast.Y;
                    w = toast.Width;
                    h = toast.Height;
                }
            }
            else if (snap != null && snap.Chest)
            {
                // 没有弹窗元素时，用宝箱按钮位置往上框一块
                x = (int)snap.ChestX - 160;
                y = (int)snap.ChestY - 270;
                w = 400;
                h = 220;
            }
            else
            {
                // 既没有弹窗也没有宝箱：不瞎猜区域（联机时那块是头像栏），这次就不截图
                return null;
            }

            string dir = Path.Combine(logsDir, "rewards");
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + index + ".png";
            string full = Path.Combine(dir, name);

            if (!Capture(x, y, w, h, full)) return null;

            // 诊断：把这次识别到的弹窗元素记在图片旁边，方便之后校准取景
            if (toast != null && !string.IsNullOrEmpty(toast.Debug))
            {
                try
                {
                    File.WriteAllText(full + ".txt",
                        "region=" + x + "," + y + " " + w + "x" + h + "\r\n" + toast.Debug,
                        new System.Text.UTF8Encoding(false));
                }
                catch { }
            }

            return "rewards/" + name;
        }

        private static ElementInfo FindDock(PetSnapshot snap)
        {
            if (snap == null || snap.Elements == null) return null;
            foreach (var element in snap.Elements)
            {
                if (element.ClassName == "pet-dock-content") return element;
            }
            return null;
        }
    }
}

