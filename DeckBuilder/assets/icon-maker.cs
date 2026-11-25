// 官方 Weiß Schwarz 徽标 -> 多尺寸应用图标的绘制核心。
// 由 make-icon.ps1 通过 Add-Type 编译，不参与 DeckBuilder 主程序的构建。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace WsIcon
{
    /// <summary>
    /// 图标画法：圆角深色（或白色）底板 + 居中的鸢尾花徽标。
    /// 所有尺寸都独立绘制，再靠圆角矩形的有符号距离做抗锯齿，所以 16px 也不会脏。
    /// </summary>
    public static class Maker
    {
        /// <summary>ico 里打包的尺寸。Windows 会按 DPI 自己挑最合适的那一档。</summary>
        private static readonly int[] IcoSizes = new int[]
        {
            16, 20, 24, 32, 40, 48, 64, 96, 128, 256
        };

        /// <summary>徽标占底板边长的比例：留一圈呼吸感，同时保证 16px 下形状还能认出来。</summary>
        private const float EmblemFill = 0.70f;

        /// <summary>圆角半径占边长的比例，接近 Windows 11 应用图标的观感。</summary>
        private const float CornerRatio = 0.195f;

        /// <summary>
        /// 把白底黑徽标重画成 size×size 的「透明底 + 上浅下深」彩色徽标。
        /// 覆盖率来自原图的灰度，4 倍超采样后缩回来，边缘是干净的抗锯齿而不是阶梯。
        /// </summary>
        public static Bitmap BuildEmblem(string sourcePath, int size, bool dark)
        {
            Bitmap top = new Bitmap(sourcePath);
            try
            {
                using (Bitmap src = top.Clone(new Rectangle(0, 0, top.Width, top.Height),
                           PixelFormat.Format32bppArgb))
                {
                    RectangleF box = ContentBox(src);
                    const int Supersample = 4;
                    int work = size * Supersample;

                    using (Bitmap hi = new Bitmap(work, work, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(hi))
                        {
                            g.Clear(Color.White);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            float scale = size * 1f / Math.Max(box.Width, box.Height) * Supersample;
                            g.TranslateTransform(work / 2f, work / 2f);
                            g.ScaleTransform(scale, scale);
                            g.TranslateTransform(-(box.X + (box.Width / 2f)), -(box.Y + (box.Height / 2f)));
                            g.DrawImage(src, new RectangleF(0, 0, src.Width, src.Height));
                        }

                        using (Bitmap gray = new Bitmap(size, size, PixelFormat.Format24bppRgb))
                        {
                            using (Graphics g = Graphics.FromImage(gray))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.DrawImage(hi, new Rectangle(0, 0, size, size));
                            }
                            return Paint(gray, size, dark);
                        }
                    }
                }
            }
            finally
            {
                top.Dispose();
            }
        }

        /// <summary>把灰度覆盖率涂成彩色徽标（深色版银白，浅色版近黑）。</summary>
        private static Bitmap Paint(Bitmap gray, int size, bool dark)
        {
            Color head = dark ? Color.FromArgb(255, 255, 255) : Color.FromArgb(255, 15, 21, 33);
            Color tail = dark ? Color.FromArgb(255, 190, 203, 220) : Color.FromArgb(255, 15, 21, 33);

            Bitmap emblem = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            BitmapData gd = gray.LockBits(new Rectangle(0, 0, size, size),
                ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData ed = emblem.LockBits(new Rectangle(0, 0, size, size),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] source = new byte[gd.Stride * size];
                byte[] target = new byte[ed.Stride * size];
                Marshal.Copy(gd.Scan0, source, 0, source.Length);

                for (int y = 0; y < size; y++)
                {
                    float t = size <= 1 ? 0f : y / (size - 1f);
                    int r = (int)Math.Round(head.R + ((tail.R - head.R) * t));
                    int gch = (int)Math.Round(head.G + ((tail.G - head.G) * t));
                    int b = (int)Math.Round(head.B + ((tail.B - head.B) * t));
                    int rowIn = y * gd.Stride;
                    int rowOut = y * ed.Stride;

                    for (int x = 0; x < size; x++)
                    {
                        int i = rowIn + (x * 3);
                        // 24bpp 是 BGR；白底黑图，取绿通道反相即覆盖率。
                        double coverage = (255 - source[i + 1]) / 255.0;
                        double alpha = Smooth(coverage, 0.33, 0.67);
                        int o = rowOut + (x * 4);
                        target[o] = (byte)b;
                        target[o + 1] = (byte)gch;
                        target[o + 2] = (byte)r;
                        target[o + 3] = (byte)Math.Round(alpha * 255);
                    }
                }

                Marshal.Copy(target, 0, ed.Scan0, target.Length);
            }
            finally
            {
                gray.UnlockBits(gd);
                emblem.UnlockBits(ed);
            }
            return emblem;
        }


        /// <summary>原图里黑色形状的包围盒：裁掉四周白边，居中时才不会偏。</summary>
        private static RectangleF ContentBox(Bitmap src)
        {
            int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
            BitmapData d = src.LockBits(new Rectangle(0, 0, src.Width, src.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] buffer = new byte[d.Stride * src.Height];
                Marshal.Copy(d.Scan0, buffer, 0, buffer.Length);
                for (int y = 0; y < src.Height; y++)
                {
                    int row = y * d.Stride;
                    for (int x = 0; x < src.Width; x++)
                    {
                        int i = row + (x * 4);
                        if (buffer[i + 3] < 16)
                            continue;
                        int lum = ((buffer[i + 2] * 30) + (buffer[i + 1] * 59) + (buffer[i] * 11)) / 100;
                        if (lum >= 200)
                            continue;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            finally
            {
                src.UnlockBits(d);
            }

            if (maxX < 0)
                return new RectangleF(0, 0, src.Width, src.Height);
            return new RectangleF(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>标准 smoothstep：把糊开的一小段灰度收成干净的一像素过渡。</summary>
        private static double Smooth(double value, double edge0, double edge1)
        {
            double t = (value - edge0) / (edge1 - edge0);
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return t * t * (3 - (2 * t));
        }

        /// <summary>画一块 size×size 的圆角底板，并把徽标摆到正中。</summary>
        public static Bitmap Compose(Bitmap emblem, int size, bool dark, float fill)
        {
            Bitmap plate = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(plate))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);

                Color head = dark ? Color.FromArgb(255, 33, 46, 66) : Color.FromArgb(255, 255, 255, 255);
                Color tail = dark ? Color.FromArgb(255, 9, 14, 26) : Color.FromArgb(255, 226, 232, 242);
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    new Rectangle(0, 0, size, Math.Max(size, 2)), head, tail, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(bg, 0, 0, size, size);
                }

                if (dark)
                {
                    // 徽标后一团很淡的蓝白辉光：大尺寸下显得有层次，小尺寸被徽标盖住。
                    float radius = size * 0.58f;
                    using (GraphicsPath glow = new GraphicsPath())
                    {
                        glow.AddEllipse(size / 2f - radius, (size * 0.44f) - radius, radius * 2f, radius * 2f);
                        using (PathGradientBrush brush = new PathGradientBrush(glow))
                        {
                            brush.CenterPoint = new PointF(size / 2f, size * 0.44f);
                            brush.CenterColor = Color.FromArgb(72, 148, 178, 224);
                            brush.SurroundColors = new Color[] { Color.FromArgb(0, 148, 178, 224) };
                            g.FillPath(brush, glow);
                        }
                    }
                }

                float box = size * fill;
                int boxSize = (int)Math.Round(box);
                int offset = (size - boxSize) / 2;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(emblem, new Rectangle(offset, offset, boxSize, boxSize),
                        0, 0, emblem.Width, emblem.Height, GraphicsUnit.Pixel, attributes);
                }

                // 一圈细描边：浅色桌面、深色桌面都能看清图标边界。
                float inset = Math.Max(0.5f, size / 170f);
                float penWidth = Math.Max(0.7f, size / 170f);
                Color border = dark
                    ? Color.FromArgb(96, 148, 178, 224)
                    : Color.FromArgb(44, 15, 23, 42);
                using (Pen pen = new Pen(border, penWidth))
                using (GraphicsPath path = Rounded(
                    new RectangleF(inset, inset, size - (inset * 2f), size - (inset * 2f)),
                    (size * CornerRatio) - inset))
                {
                    g.DrawPath(pen, path);
                }
            }

            ApplyRoundedAlpha(plate, size * CornerRatio);
            return plate;
        }

        /// <summary>圆角矩形之外全部抹成透明；有符号距离保证边缘是抗锯齿的而不是锯齿块。</summary>
        private static void ApplyRoundedAlpha(Bitmap bmp, float radius)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                byte[] buffer = new byte[d.Stride * height];
                Marshal.Copy(d.Scan0, buffer, 0, buffer.Length);
                float halfW = width / 2f;
                float halfH = height / 2f;
                for (int y = 0; y < height; y++)
                {
                    float qy = Math.Abs((y + 0.5f) - halfH) - halfH + radius;
                    float outsideY = Math.Max(qy, 0f);
                    int row = y * d.Stride;
                    for (int x = 0; x < width; x++)
                    {
                        float qx = Math.Abs((x + 0.5f) - halfW) - halfW + radius;
                        float outsideX = Math.Max(qx, 0f);
                        double outside = Math.Sqrt((outsideX * outsideX) + (outsideY * outsideY));
                        double inside = Math.Min(Math.Max(qx, qy), 0f);
                        double distance = outside + inside - radius;
                        double coverage = 0.5 - distance;
                        int index = (row + (x * 4)) + 3;
                        if (coverage <= 0)
                        {
                            buffer[index] = 0;
                            continue;
                        }
                        if (coverage >= 1)
                            continue;
                        buffer[index] = (byte)Math.Round(buffer[index] * coverage);
                    }
                }
                Marshal.Copy(buffer, 0, d.Scan0, buffer.Length);
            }
            finally
            {
                bmp.UnlockBits(d);
            }
        }

        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Max(0f, Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height)));
            if (d <= 0.01f)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>写出多尺寸 ico（Vista 之后支持 PNG 压缩的条目）。</summary>
        public static void WriteIco(string emblemPath, string icoPath, bool dark)
        {
            using (Bitmap master = BuildEmblem(emblemPath, 1024, dark))
            {
                byte[][] blobs = new byte[IcoSizes.Length][];
                for (int i = 0; i < IcoSizes.Length; i++)
                {
                    using (Bitmap plate = Compose(master, IcoSizes[i], dark, EmblemFill))
                    using (MemoryStream stream = new MemoryStream())
                    {
                        plate.Save(stream, ImageFormat.Png);
                        blobs[i] = stream.ToArray();
                    }
                }

                using (FileStream file = File.Create(icoPath))
                using (BinaryWriter writer = new BinaryWriter(file))
                {
                    writer.Write((ushort)0);                    // reserved
                    writer.Write((ushort)1);                    // 1 = icon
                    writer.Write((ushort)IcoSizes.Length);
                    int offset = 6 + (16 * IcoSizes.Length);
                    for (int i = 0; i < IcoSizes.Length; i++)
                    {
                        int side = IcoSizes[i];
                        writer.Write((byte)(side >= 256 ? 0 : side));
                        writer.Write((byte)(side >= 256 ? 0 : side));
                        writer.Write((byte)0);                  // 调色板个数
                        writer.Write((byte)0);                  // reserved
                        writer.Write((ushort)1);                // 颜色平面
                        writer.Write((ushort)32);               // 位深
                        writer.Write((uint)blobs[i].Length);
                        writer.Write((uint)offset);
                        offset += blobs[i].Length;
                    }
                    for (int i = 0; i < IcoSizes.Length; i++)
                        writer.Write(blobs[i]);
                    writer.Flush();
                }
            }
        }

        /// <summary>对比图：深色 / 浅色两种桌面底色上，白底黑标与深底银标各个尺寸的效果。</summary>
        public static void WritePreview(string emblemPath, string pngPath)
        {
            int[] shots = new int[] { 256, 128, 96, 64, 48, 32, 16 };
            const int Gap = 26;
            const int Margin = 30;
            const int RowHeight = 300;

            int rowWidth = Margin * 2;
            for (int i = 0; i < shots.Length; i++)
                rowWidth += shots[i] + (i == 0 ? 0 : Gap);

            using (Bitmap lightEmblem = BuildEmblem(emblemPath, 512, false))
            using (Bitmap darkEmblem = BuildEmblem(emblemPath, 512, true))
            using (Bitmap sheet = new Bitmap(rowWidth, ((RowHeight + Gap) * 4) + Gap))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                Color dark = Color.FromArgb(255, 18, 23, 32);
                Color light = Color.FromArgb(255, 238, 241, 246);
                Color[] strips = new Color[] { dark, dark, light, light };
                bool[] variants = new bool[] { false, true, false, true };

                for (int row = 0; row < strips.Length; row++)
                {
                    int top = (row * (RowHeight + Gap)) + Gap;
                    using (SolidBrush brush = new SolidBrush(strips[row]))
                        g.FillRectangle(brush, 0, top, rowWidth, RowHeight);

                    int x = Margin;
                    for (int i = 0; i < shots.Length; i++)
                    {
                        int side = shots[i];
                        Bitmap emblem = variants[row] ? darkEmblem : lightEmblem;
                        using (Bitmap plate = Compose(emblem, side, variants[row], EmblemFill))
                            g.DrawImageUnscaled(plate, x, top + ((RowHeight - side) / 2));
                        x += side + Gap;
                    }
                }

                sheet.Save(pngPath, ImageFormat.Png);
            }
        }

        /// <summary>1024×1024 母版，改配色或做宣传图时直接用。</summary>
        public static void WriteMaster(string emblemPath, string pngPath, bool dark)
        {
            using (Bitmap master = BuildEmblem(emblemPath, 1024, dark))
            using (Bitmap plate = Compose(master, 1024, dark, EmblemFill))
            {
                plate.Save(pngPath, ImageFormat.Png);
            }
        }
    }
}

