using System;
using System.Drawing;
using System.IO;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 程序图标。build.ps1 把 assets\WeissDeckBuilder.ico 同时写成 exe 的资源节
    /// （资源管理器与任务栏读的是那一份）和托管资源，这里取托管那份给窗口用，
    /// 省得 dist 目录多带一个只给标题栏服务的文件。
    /// <para>
    /// 图标是官方「Weiß Schwarz」鸢尾花徽标，白底黑标转成的深底银标，由
    /// assets\make-icon.ps1 生成。取不到资源时返回 null，窗口退回系统默认图标，
    /// 不影响启动——测试工程只编一部分 UI 文件，本来就没有这份资源。
    /// </para>
    /// </summary>
    internal static class AppIcon
    {
        private const string ResourceName = "WeissDeckBuilder.ico";

        /// <summary>
        /// 按需要的边长取图标；ico 里有 16/20/24/32/40/48/64/96/128/256 十档，
        /// 让 Windows 自己挑，别用缩放过的糊图。
        /// </summary>
        public static Icon Load(int size)
        {
            try
            {
                Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
                if (stream == null)
                    return null;
                using (stream)
                    return new Icon(stream, size, size);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>窗口与任务栏用的默认尺寸（32px，标题栏的 16px 由系统缩放）。</summary>
        public static Icon Load()
        {
            return Load(32);
        }
    }
}
