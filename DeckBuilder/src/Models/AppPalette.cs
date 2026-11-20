using System.Drawing;

namespace WeissDeckBuilder.Models
{
    /// <summary>
    /// 一整套界面配色。界面里到处出现的近似色先归并成十个槽位，主题切换时
    /// 整张表一起换掉：导航栏、主背景、卡片底、输入底、分隔线、描边、三级
    /// 文字，再加一个遮罩色。强调色（蓝）与状态色（红 / 黄 / 绿）在所有主题
    /// 里都相同，因此不在这张表里。
    /// <para>
    /// 放在 Models 里是为了让导出服务（Data）也能取到当前配色，画出来的
    /// 卡组 PNG 背景和界面完全一致，而不必让 Data 反过来依赖 UI。
    /// </para>
    /// </summary>
    public sealed class AppPalette
    {
        /// <summary>深色主题：数值与合并前逐个控件写死的颜色完全相同。</summary>
        public static readonly AppPalette Dark = new AppPalette(
            AppThemeMode.Dark,
            Color.FromArgb(31, 41, 55),     // Navigation
            Color.FromArgb(15, 23, 42),     // MainBackground
            Color.FromArgb(30, 41, 59),     // TileBackground
            Color.FromArgb(10, 17, 31),     // InputBackground
            Color.FromArgb(45, 57, 75),     // Divider
            Color.FromArgb(51, 65, 85),     // Border
            Color.FromArgb(241, 245, 249),  // Text
            Color.FromArgb(203, 213, 225),  // TextSoft
            Color.FromArgb(148, 163, 184),  // TextMuted
            Color.FromArgb(3, 8, 18));      // Scrim

        /// <summary>
        /// 浅色主题：导航栏最亮，页面底色略暗，卡片与输入框比底色再亮回来。
        /// 没有任何一档是纯白或纯黑，白色因此始终只表示"强调色上的文字"，
        /// 换主题时不会被误当成某个槽位重新映射。
        /// </summary>
        public static readonly AppPalette Light = new AppPalette(
            AppThemeMode.Light,
            Color.FromArgb(247, 249, 252),  // Navigation
            Color.FromArgb(232, 237, 244),  // MainBackground
            Color.FromArgb(253, 254, 255),  // TileBackground
            Color.FromArgb(240, 244, 249),  // InputBackground
            Color.FromArgb(221, 228, 237),  // Divider
            Color.FromArgb(194, 206, 221),  // Border
            Color.FromArgb(23, 32, 51),     // Text
            Color.FromArgb(58, 71, 89),     // TextSoft
            Color.FromArgb(93, 107, 128),   // TextMuted
            Color.FromArgb(11, 18, 32));    // Scrim

        private AppPalette(
            AppThemeMode mode,
            Color navigation,
            Color mainBackground,
            Color tileBackground,
            Color inputBackground,
            Color divider,
            Color border,
            Color text,
            Color textSoft,
            Color textMuted,
            Color scrim)
        {
            Mode = mode;
            Navigation = navigation;
            MainBackground = mainBackground;
            TileBackground = tileBackground;
            InputBackground = inputBackground;
            Divider = divider;
            Border = border;
            Text = text;
            TextSoft = textSoft;
            TextMuted = textMuted;
            Scrim = scrim;
        }

        /// <summary>正在使用的配色。启动时是深色，直到设置页换了主题。</summary>
        public static AppPalette Current { get; private set; } = Dark;

        /// <summary>
        /// 切换之前的那套配色。重绘时只用它来判断"这个颜色属于哪个槽位"，
        /// 与当前配色相同就表示还没有切换过。
        /// </summary>
        public static AppPalette Previous { get; private set; } = Dark;

        public AppThemeMode Mode { get; private set; }

        /// <summary>侧边栏底色，也是导出图渐变的上端。</summary>
        public Color Navigation { get; private set; }

        /// <summary>页面底色，也是导出图渐变的下端。</summary>
        public Color MainBackground { get; private set; }

        /// <summary>卡片、面板、弹出层与小按钮的表面。</summary>
        public Color TileBackground { get; private set; }

        /// <summary>输入框、下拉框与下沉区域。</summary>
        public Color InputBackground { get; private set; }

        /// <summary>分隔线、悬停底色与次要按钮。</summary>
        public Color Divider { get; private set; }

        /// <summary>描边、按下态与滚动条滑块。</summary>
        public Color Border { get; private set; }

        /// <summary>正文文字。</summary>
        public Color Text { get; private set; }

        /// <summary>次要文字与侧边栏标题。</summary>
        public Color TextSoft { get; private set; }

        /// <summary>说明文字与占位文字。</summary>
        public Color TextMuted { get; private set; }

        /// <summary>弹层遮罩与阴影底色，两套主题里都是深色。</summary>
        public Color Scrim { get; private set; }

        /// <summary>按名字取配色；未识别的值一律当深色。</summary>
        public static AppPalette For(AppThemeMode mode)
        {
            return mode == AppThemeMode.Light ? Light : Dark;
        }

        /// <summary>
        /// 换到另一套配色，并记下刚才那套，好让界面上的旧颜色能被重新映射。
        /// 已经是目标配色时什么也不做，重复点同一个按钮不会累积映射。
        /// </summary>
        public static void Select(AppThemeMode mode)
        {
            AppPalette target = For(mode);
            if (ReferenceEquals(target, Current))
                return;

            Previous = Current;
            Current = target;
        }

        /// <summary>
        /// 把一个旧配色下的颜色换成当前配色里的同一槽位。只会命中整套表里
        /// 有的十种颜色，强调色、状态色与白色原样返回，所以蓝色按钮和红色
        /// 删除按钮不会被主题牵连。
        /// </summary>
        public static Color MapFromPrevious(Color color)
        {
            return Current.Map(color, Previous);
        }

        private Color Map(Color color, AppPalette source)
        {
            if (source == null || color.IsEmpty || color.A == 0)
                return color;

            if (Same(color, source.Navigation))
                return Navigation;
            if (Same(color, source.MainBackground))
                return MainBackground;
            if (Same(color, source.TileBackground))
                return TileBackground;
            if (Same(color, source.InputBackground))
                return InputBackground;
            if (Same(color, source.Divider))
                return Divider;
            if (Same(color, source.Border))
                return Border;
            if (Same(color, source.Text))
                return Text;
            if (Same(color, source.TextSoft))
                return TextSoft;
            if (Same(color, source.TextMuted))
                return TextMuted;
            if (Same(color, source.Scrim))
                return Scrim;

            return color;
        }

        private static bool Same(Color left, Color right)
        {
            return left.ToArgb() == right.ToArgb();
        }
    }
}

