using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 缓存了画刷或画笔的控件在换配色后要重建它们：<see cref="AppTheme.Recolor"/>
    /// 换完整套颜色会回调这个方法。只是每次重绘现取颜色的控件不需要实现它。
    /// </summary>
    public interface IThemeAware
    {
        /// <summary>按当前配色重建缓存的画刷 / 画笔并重绘。</summary>
        void ApplyTheme();
    }

    /// <summary>
    /// 界面用的颜色。名字一直没变，取值改成从 <see cref="AppPalette.Current"/>
    /// 里读，所以全程序几百处调用都不用改；换主题时换的是那张表。
    /// 强调色与状态色不随主题变化，仍然是固定值。
    /// </summary>
    /// <summary>
    /// 配色表。类本身是 public，因为附加功能插件要用
    /// <see cref="AppTheme"/> 取色才能和主界面保持一致；
    /// 具体的调色板（<c>AppPalette</c>）仍然只在程序集内部使用。
    /// </summary>
    public static class AppTheme
    {
        public static Color Navigation
        {
            get { return AppPalette.Current.Navigation; }
        }

        public static Color MainBackground
        {
            get { return AppPalette.Current.MainBackground; }
        }

        public static Color TileBackground
        {
            get { return AppPalette.Current.TileBackground; }
        }

        public static Color InputBackground
        {
            get { return AppPalette.Current.InputBackground; }
        }

        public static Color Divider
        {
            get { return AppPalette.Current.Divider; }
        }

        public static Color Border
        {
            get { return AppPalette.Current.Border; }
        }

        public static Color Text
        {
            get { return AppPalette.Current.Text; }
        }

        public static Color TextSoft
        {
            get { return AppPalette.Current.TextSoft; }
        }

        public static Color TextMuted
        {
            get { return AppPalette.Current.TextMuted; }
        }

        /// <summary>遮罩与阴影的底色，两套主题里都是深色。</summary>
        public static Color Scrim
        {
            get { return AppPalette.Current.Scrim; }
        }

        /// <summary>
        /// 占位文字曾经比说明文字再浅一档，现在两处共用
        /// <see cref="TextMuted"/>，颜色表因此少一个槽位。
        /// </summary>
        public static Color Placeholder
        {
            get { return AppPalette.Current.TextMuted; }
        }

        public static AppThemeMode Mode
        {
            get { return AppPalette.Current.Mode; }
        }

        public static bool IsDark
        {
            get { return AppPalette.Current.Mode == AppThemeMode.Dark; }
        }

        public static readonly Color Accent = Color.FromArgb(59, 130, 246);
        public static readonly Color AccentHover = Color.FromArgb(96, 165, 250);
        public static readonly Color AccentPressed = Color.FromArgb(37, 99, 235);

        /// <summary>Colour of a finished deck, used by the 50 张 counter.</summary>
        public static readonly Color Success = Color.FromArgb(74, 222, 128);

        /// <summary>Colour of a deck that grew past the 50 张 target.</summary>
        public static readonly Color Danger = Color.FromArgb(248, 113, 113);

        /// <summary>
        /// 今日运势 大吉 / 中吉 的金色。它只用于这一张落地页，所以不占
        /// AppPalette 的槽位；深浅主题里都有足够的对比度。
        /// </summary>
        public static readonly Color FortuneGold = Color.FromArgb(202, 138, 4);

        /// <summary>
        /// 在某个填充色上该用白字还是深字。判断依据是填充色本身的亮度，所以
        /// 深色主题里深色填充上的白字、浅色主题里浅色填充上的深字都不用问
        /// 主题是谁；蓝色强调按钮在两套主题里仍然拿到白字。
        /// </summary>
        public static Color ForegroundOn(Color fill)
        {
            if (fill.IsEmpty || fill.A == 0)
                return Text;

            int luminance = (fill.R * 299 + fill.G * 587 + fill.B * 114) / 1000;
            return luminance >= 180 ? Text : Color.White;
        }

        /// <summary>
        /// 换上另一套配色。界面颜色都是从这里读的，所以先把表换掉，再由
        /// 调用方 <see cref="Recolor"/> 一次把已经画出来的控件改过来。
        /// </summary>
        public static void Apply(AppThemeMode mode)
        {
            AppPalette.Select(mode);
        }

        /// <summary>把旧配色里的颜色换成当前配色的同一槽位。</summary>
        public static Color MapFromPrevious(Color color)
        {
            return AppPalette.MapFromPrevious(color);
        }

        /// <summary>
        /// 换主题后把整棵控件树里已经写死的颜色改成新配色。只映射颜色表里
        /// 有的槽位：白色（强调按钮上的文字）、强调色和状态色都原样保留。
        /// 缓存了画刷的控件再走一次 <see cref="IThemeAware.ApplyTheme"/>。
        /// </summary>
        public static void Recolor(Control root)
        {
            if (root == null)
                return;

            RecolorTree(root);
            root.Invalidate(true);
        }

        private static void RecolorTree(Control control)
        {
            RecolorOne(control);
            for (int i = 0; i < control.Controls.Count; i++)
                RecolorTree(control.Controls[i]);
        }

        private static void RecolorOne(Control control)
        {
            control.BackColor = MapFromPrevious(control.BackColor);
            control.ForeColor = MapFromPrevious(control.ForeColor);

            RoundedButton button = control as RoundedButton;
            if (button != null)
            {
                button.NormalColor = MapFromPrevious(button.NormalColor);
                button.HoverColor = MapFromPrevious(button.HoverColor);
                button.PressedColor = MapFromPrevious(button.PressedColor);
                button.SelectedColor = MapFromPrevious(button.SelectedColor);
                button.SurfaceColor = MapFromPrevious(button.SurfaceColor);
            }
            else
            {
                GearButton gear = control as GearButton;
                if (gear != null)
                {
                    gear.NormalColor = MapFromPrevious(gear.NormalColor);
                    gear.HoverColor = MapFromPrevious(gear.HoverColor);
                    gear.PressedColor = MapFromPrevious(gear.PressedColor);
                    gear.ActiveColor = MapFromPrevious(gear.ActiveColor);
                    gear.SurfaceColor = MapFromPrevious(gear.SurfaceColor);
                }
            }

            RoundedTextBox input = control as RoundedTextBox;
            if (input != null)
            {
                input.FillColor = MapFromPrevious(input.FillColor);
                input.BorderColor = MapFromPrevious(input.BorderColor);
            }

            GradientPanel gradient = control as GradientPanel;
            if (gradient != null)
            {
                gradient.StartColor = MapFromPrevious(gradient.StartColor);
                gradient.EndColor = MapFromPrevious(gradient.EndColor);
            }

            LinkLabel link = control as LinkLabel;
            if (link != null)
            {
                link.LinkColor = MapFromPrevious(link.LinkColor);
                link.ActiveLinkColor = MapFromPrevious(link.ActiveLinkColor);
                link.VisitedLinkColor = MapFromPrevious(link.VisitedLinkColor);
                link.DisabledLinkColor = MapFromPrevious(link.DisabledLinkColor);
            }

            IThemeAware aware = control as IThemeAware;
            if (aware != null)
                aware.ApplyTheme();
        }

        public static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(1, radius * 2);
            if (bounds.Width <= diameter || bounds.Height <= diameter)
            {
                path.AddRectangle(bounds);
                return path;
            }

            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);

            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);

            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// 窗口标题栏与滚动条的深浅。深色主题用沉浸式深色标题栏，浅色主题关掉
    /// 它并恢复系统主题，两者都是视觉增强：调用失败也不影响程序运行。
    /// </summary>
    internal static class DarkModeHelper
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(
            IntPtr hwnd,
            string subAppName,
            string subIdList);

        /// <summary>按当前主题给一个句柄刷上窗口外框与滚动条。</summary>
        public static void Apply(IntPtr handle, bool dark)
        {
            if (handle == IntPtr.Zero)
                return;

            int enabled = dark ? 1 : 0;
            try
            {
                int result = DwmSetWindowAttribute(handle,
                    DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(handle,
                        DwmUseImmersiveDarkModeBefore20H1,
                        ref enabled, sizeof(int));
                }
            }
            catch
            {
                // Dark title bars are a visual enhancement only.
            }

            try
            {
                // null 恢复系统默认（浅色）滚动条。
                SetWindowTheme(handle, dark ? "DarkMode_Explorer" : null, null);
            }
            catch
            {
                // Older Windows versions keep the system scrollbar theme.
            }
        }
    }
}
