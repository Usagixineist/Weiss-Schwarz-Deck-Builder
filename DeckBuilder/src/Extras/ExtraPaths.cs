using System;
using System.IO;

namespace WeissDeckBuilder.Extras
{
    /// <summary>
    /// extra\ 插件目录与日志的位置。规则和 settings.txt 完全一致：
    /// 从 <c>DeckBuilder\dist\WeissDeckBuilder.exe</c> 运行时都落在
    /// <c>DeckBuilder\</c> 里，重新构建、覆盖 dist 都不会碰到用户的插件；
    /// 从别处运行时就是程序目录旁边。
    ///
    /// 故意不往 StreamingAssets 或 dist 里放：dist 每次构建都被重写，
    /// 用户装的插件会跟着消失。
    /// </summary>
    public static class ExtraPaths
    {
        /// <summary>插件目录名，与启动程序同级（dist 的上一层）。</summary>
        public const string FolderName = "extra";

        /// <summary>插件日志文件名，和插件目录并排。</summary>
        public const string LogFileName = "extras.log";

        /// <summary>当前运行程序的插件目录；路径无法确定时返回空字符串。</summary>
        public static string RootDirectory()
        {
            return RootDirectory(AppDomain.CurrentDomain.BaseDirectory);
        }

        /// <summary>
        /// 以 <paramref name="baseDirectory"/> 为程序目录推出插件目录。
        /// 目录名是 <c>dist</c> 时取它的上一层，这样测试和实际运行走同一条规则。
        /// </summary>
        public static string RootDirectory(string baseDirectory)
        {
            if (string.IsNullOrEmpty(baseDirectory))
                return string.Empty;

            try
            {
                string directory = baseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (directory.Length == 0)
                    return string.Empty;

                var info = new DirectoryInfo(directory);
                if (info.Parent != null &&
                    string.Equals(info.Name, "dist", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(info.Parent.FullName, FolderName);
                }

                return Path.Combine(info.FullName, FolderName);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (PathTooLongException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 插件日志的完整路径：插件目录的下一层，即 <c>DeckBuilder\extras.log</c>。
        /// 加载失败、清单警告和插件自己的日志都写这里，方便用户回帖时附上。
        /// </summary>
        public static string LogFilePath()
        {
            string root = RootDirectory();
            if (string.IsNullOrEmpty(root))
                return string.Empty;

            string parent = Path.GetDirectoryName(root);
            return string.IsNullOrEmpty(parent)
                ? string.Empty
                : Path.Combine(parent, LogFileName);
        }
    }
}
