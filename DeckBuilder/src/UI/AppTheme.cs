using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WeissDeckBuilder.UI
{
    internal static class AppTheme
    {
        public static readonly Color Navigation = Color.FromArgb(31, 41, 55);
        public static readonly Color MainBackground = Color.FromArgb(15, 23, 42);
        public static readonly Color TileBackground = Color.FromArgb(30, 41, 59);
        public static readonly Color Divider = Color.FromArgb(45, 57, 75);
        public static readonly Color Border = Color.FromArgb(51, 65, 85);
        public static readonly Color Text = Color.FromArgb(241, 245, 249);
        public static readonly Color TextSoft = Color.FromArgb(203, 213, 225);
        public static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
        public static readonly Color Accent = Color.FromArgb(59, 130, 246);
        public static readonly Color AccentHover = Color.FromArgb(96, 165, 250);
        public static readonly Color AccentPressed = Color.FromArgb(37, 99, 235);
        public static readonly Color InputBackground = Color.FromArgb(10, 17, 31);
        public static readonly Color Placeholder = Color.FromArgb(100, 116, 139);

        /// <summary>Colour of a finished deck, used by the 50 张 counter.</summary>
        public static readonly Color Success = Color.FromArgb(74, 222, 128);

        /// <summary>Colour of a deck that grew past the 50 张 target.</summary>
        public static readonly Color Danger = Color.FromArgb(248, 113, 113);

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

        public static void Apply(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;

            int enabled = 1;
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
                SetWindowTheme(handle, "DarkMode_Explorer", null);
            }
            catch
            {
                // Older Windows versions keep the system scrollbar theme.
            }
        }
    }
}
