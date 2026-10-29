// BackgroundFile.cs — 背景图的安装/清理（独立出来，方便测试与命令行调用）
using System;
using System.IO;

namespace WuwaChest
{
    internal static class BackgroundFile
    {
        /// <summary>
        /// 把 sourcePath 复制到 appDir 下的 background.ext，返回存下来的文件名。
        /// 关键点：先复制到临时文件，再清理旧图 —— 即使选中的就是 appDir 里那张
        /// background.jpg，也不会出现「先删掉自己再复制」导致文件消失的 bug。
        /// </summary>
        public static string Install(string appDir, string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) throw new ArgumentNullException("sourcePath");

            string source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source)) throw new FileNotFoundException("找不到图片：" + source);

            string ext = Path.GetExtension(source).ToLowerInvariant();
            if (ext == ".jpeg") ext = ".jpg";
            if (ext != ".png" && ext != ".jpg" && ext != ".bmp") ext = ".png";

            string target = Path.GetFullPath(Path.Combine(appDir, "background" + ext));
            string temp = target + ".new";

            // 1) 先把选中的图落到临时文件（此时源文件还完好）
            File.Copy(source, temp, true);

            // 2) 清理别的旧背景图（跳过目标与临时文件，只删图片扩展名）
            try
            {
                foreach (var old in Directory.GetFiles(appDir, "background*"))
                {
                    string full = Path.GetFullPath(old);
                    if (string.Equals(full, target, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(full, Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsImage(full)) continue;
                    try { File.Delete(old); } catch { }
                }
            }
            catch { }

            // 3) 临时文件转正
            File.Copy(temp, target, true);
            try { File.Delete(temp); } catch { }

            if (!File.Exists(target)) throw new IOException("复制后找不到 " + target);
            return Path.GetFileName(target);
        }

        /// <summary>删掉目录里所有背景图文件</summary>
        public static void RemoveAll(string appDir)
        {
            try
            {
                foreach (var file in Directory.GetFiles(appDir, "background*"))
                {
                    if (!IsImage(file)) continue;
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        public static bool IsImage(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
        }
    }
}
