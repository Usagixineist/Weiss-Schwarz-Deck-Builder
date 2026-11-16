using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Outcome of a copy or export action. Failures are returned instead of
    /// thrown so the deck page can print the reason on its hint line.
    /// </summary>
    internal sealed class DeckExportResult
    {
        private DeckExportResult(bool success, string filePath,
            int pageCount, string error)
        {
            Success = success;
            FilePath = filePath;
            PageCount = pageCount;
            Error = error;
        }

        public bool Success { get; private set; }
        public string FilePath { get; private set; }
        public int PageCount { get; private set; }
        public string Error { get; private set; }

        public static DeckExportResult Completed(string filePath, int pageCount)
        {
            return new DeckExportResult(true, filePath, pageCount, null);
        }

        public static DeckExportResult Failed(string error)
        {
            return new DeckExportResult(false, null, 0, error);
        }
    }

    /// <summary>
    /// Deck detail exports: the card code list for the clipboard, the expanded
    /// and compact deck sheets as PNG, and the printable A4 PDF. Everything is
    /// written below an Output folder next to the DeckBuilder folder, never
    /// into the game's StreamingAssets tree.
    /// </summary>
    internal static class DeckExportService
    {
        public const string OutputFolderName = "Output";
        public const string WatermarkText =
            "Weiss Schwarz Deck Builder Made by xineist";

        private const int ImageCardWidth = 252;
        private const int ImageCardHeight = 352;
        private const int ImageMargin = 56;
        private const int ExpandedColumns = 10;
        private const int CompactColumns = 7;
        private const int ExpandedGap = 6;
        private const int CompactGap = 28;
        private const int TitleTop = 38;
        private const int TitleHeight = 50;
        private const int SubtitleHeight = 28;
        private const int HeaderToGrid = 26;
        private const int FooterGap = 26;
        private const int FooterHeight = 28;
        private const int BottomPadding = 30;
        private const int PdfCardPixelWidth = 744;
        private const int PdfCardPixelHeight = 1039;

        private static readonly Color BackgroundTop = Color.FromArgb(31, 41, 55);
        private static readonly Color BackgroundBottom = Color.FromArgb(15, 23, 42);
        private static readonly Color TileFill = Color.FromArgb(30, 41, 59);
        private static readonly Color DividerColor = Color.FromArgb(45, 57, 75);
        private static readonly Color BorderColor = Color.FromArgb(51, 65, 85);
        private static readonly Color TextColor = Color.FromArgb(241, 245, 249);
        private static readonly Color TextSoftColor = Color.FromArgb(203, 213, 225);
        private static readonly Color TextMutedColor = Color.FromArgb(148, 163, 184);
        private static readonly Color AccentColor = Color.FromArgb(59, 130, 246);

        /// <summary>
        /// Folder next to DeckBuilder (or, if the executable was moved, next to
        /// the executable folder). Never returns a path inside DeckBuilder.
        /// </summary>
        public static string GetOutputDirectory(string dataRoot)
        {
            string appBase = AppDomain.CurrentDomain.BaseDirectory;
            string deckBuilderRoot = FindAncestorNamed(appBase, "DeckBuilder");
            if (deckBuilderRoot != null)
            {
                DirectoryInfo parent = Directory.GetParent(deckBuilderRoot);
                if (parent != null)
                    return Path.Combine(parent.FullName, OutputFolderName);
            }

            string gameRoot = FindGameRoot(dataRoot);
            if (!string.IsNullOrEmpty(gameRoot))
                return Path.Combine(gameRoot, OutputFolderName);

            string trimmed = (appBase ?? string.Empty).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            DirectoryInfo appParent = trimmed.Length == 0
                ? null
                : Directory.GetParent(trimmed);
            return Path.Combine(
                appParent == null ? appBase : appParent.FullName,
                OutputFolderName);
        }

        private static string FindAncestorNamed(string path, string name)
        {
            try
            {
                var directory = new DirectoryInfo(Path.GetFullPath(path));
                while (directory != null)
                {
                    if (string.Equals(directory.Name, name,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return directory.FullName;
                    }
                    directory = directory.Parent;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }

        private static string FindGameRoot(string dataRoot)
        {
            if (string.IsNullOrEmpty(dataRoot))
                return null;

            try
            {
                var directory = new DirectoryInfo(Path.GetFullPath(dataRoot));
                while (directory != null)
                {
                    if (string.Equals(directory.Name, "StreamingAssets",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        DirectoryInfo dataDirectory = directory.Parent;
                        if (dataDirectory != null &&
                            dataDirectory.Name.EndsWith("_Data",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            DirectoryInfo gameRoot = dataDirectory.Parent;
                            return gameRoot == null ? null : gameRoot.FullName;
                        }

                        return dataDirectory == null
                            ? null
                            : dataDirectory.FullName;
                    }
                    directory = directory.Parent;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }

        /// <summary>Every card code of the deck, one per line, in file order.</summary>
        public static string BuildCardCodeText(DeckDefinition deck)
        {
            var builder = new StringBuilder();
            if (deck == null)
                return string.Empty;

            IList<string> codes = deck.CardCodes;
            for (int i = 0; i < codes.Count; i++)
            {
                string code = codes[i] == null
                    ? string.Empty
                    : codes[i].Trim();
                if (code.Length == 0)
                    continue;

                if (builder.Length > 0)
                    builder.Append("\r\n");
                builder.Append(code);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Writes every copy of the deck to a single wide PNG, one card after
        /// another in ten columns. The image is laid out like the 展开 view.
        /// </summary>
        public static DeckExportResult ExportExpandedPng(DeckDefinition deck,
            IList<string> cardCodes, IList<string> imagePaths, string subtitle)
        {
            return ExportSheet(deck, cardCodes, imagePaths, null, subtitle,
                false);
        }

        /// <summary>
        /// Writes the 缩略 view: one tile per distinct card with a repeat count
        /// badge, seven columns wide.
        /// </summary>
        public static DeckExportResult ExportCompactPng(DeckDefinition deck,
            IList<DeckCardCount> groups, IList<string> imagePaths,
            string subtitle)
        {
            return ExportSheet(deck, null, imagePaths, groups, subtitle, true);
        }

        private static DeckExportResult ExportSheet(DeckDefinition deck,
            IList<string> cardCodes, IList<string> imagePaths,
            IList<DeckCardCount> groups, string subtitle, bool compact)
        {
            if (deck == null)
                return DeckExportResult.Failed("没有可导出的卡组。");

            int count = groups != null
                ? groups.Count
                : (cardCodes == null ? 0 : cardCodes.Count);
            if (count == 0)
                return DeckExportResult.Failed("卡组没有卡牌，无法导出。");

            try
            {
                int columns = compact ? CompactColumns : ExpandedColumns;
                int gap = compact ? CompactGap : ExpandedGap;
                int rows = (count + columns - 1) / columns;
                int width = ImageMargin * 2 + columns * ImageCardWidth +
                    (columns - 1) * gap;
                int header = TitleTop + TitleHeight + SubtitleHeight +
                    HeaderToGrid;
                int gridHeight = rows * ImageCardHeight + (rows - 1) * gap;
                int height = header + gridHeight + FooterGap +
                    FooterHeight + BottomPadding;

                using (var bitmap = new Bitmap(width, height,
                    PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.SmoothingMode = SmoothingMode.HighQuality;
                        graphics.InterpolationMode =
                            InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.TextRenderingHint =
                            TextRenderingHint.ClearTypeGridFit;
                        graphics.Clear(BackgroundBottom);
                        DrawBackdrop(graphics, width, height);
                        DrawHeader(graphics, width, deck.Name, subtitle);

                        for (int i = 0; i < count; i++)
                        {
                            int row = i / columns;
                            int column = i % columns;
                            int x = ImageMargin +
                                column * (ImageCardWidth + gap);
                            int y = header + row * (ImageCardHeight + gap);
                            var tile = new Rectangle(x, y, ImageCardWidth,
                                ImageCardHeight);

                            string code = groups != null
                                ? groups[i].Code
                                : (cardCodes[i] ?? string.Empty);
                            string path = imagePaths != null &&
                                i < imagePaths.Count ? imagePaths[i] : null;
                            DrawCardTile(graphics, tile, code, path);
                            if (groups != null && groups[i].Count > 1)
                                DrawCountBadge(graphics, tile, groups[i].Count);
                        }

                        DrawFooter(graphics, width, height, deck, count);
                    }

                    string outputDirectory = GetOutputDirectory(deck.FilePath);
                    Directory.CreateDirectory(outputDirectory);
                    string outputPath = BuildExportPath(outputDirectory, deck,
                        compact ? "prev" : "full", ".png");
                    bitmap.Save(outputPath, ImageFormat.Png);
                    return DeckExportResult.Completed(outputPath, 1);
                }
            }
            catch (Exception ex)
            {
                return DeckExportResult.Failed("导出图片失败：" + ex.Message);
            }
        }

        private static void DrawBackdrop(Graphics graphics, int width, int height)
        {
            using (var brush = new LinearGradientBrush(
                new Rectangle(0, 0, width, height),
                BackgroundTop, BackgroundBottom, LinearGradientMode.Vertical))
            {
                graphics.FillRectangle(brush, 0, 0, width, height);
            }

            using (var accent = new SolidBrush(AccentColor))
            {
                graphics.FillRectangle(accent, ImageMargin, TitleTop - 12,
                    52, 4);
            }
        }

        private static void DrawHeader(Graphics graphics, int width,
            string title, string subtitle)
        {
            using (var titleFont = new Font("Microsoft YaHei UI", 27F,
                FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subtitleFont = new Font("Microsoft YaHei UI", 14F,
                FontStyle.Regular, GraphicsUnit.Pixel))
            using (var titleBrush = new SolidBrush(TextColor))
            using (var subtitleBrush = new SolidBrush(TextMutedColor))
            using (var format = new StringFormat())
            {
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.LineAlignment = StringAlignment.Center;

                graphics.DrawString(title ?? string.Empty, titleFont,
                    titleBrush, new RectangleF(ImageMargin, TitleTop,
                        width - ImageMargin * 2, TitleHeight), format);
                graphics.DrawString(subtitle ?? string.Empty, subtitleFont,
                    subtitleBrush, new RectangleF(ImageMargin,
                        TitleTop + TitleHeight, width - ImageMargin * 2,
                        SubtitleHeight), format);
            }
        }

        private static void DrawFooter(Graphics graphics, int width, int height,
            DeckDefinition deck, int count)
        {
            int y = height - BottomPadding - FooterHeight;
            using (var font = new Font("Microsoft YaHei UI", 12F,
                FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(TextMutedColor))
            using (var format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                graphics.DrawString((deck.Name ?? string.Empty) + " · " +
                    count + " 张 · " + DateTime.Now.ToString("yyyy-MM-dd"),
                    font, brush, new RectangleF(ImageMargin, y,
                        width - ImageMargin * 2, FooterHeight), format);

                format.Alignment = StringAlignment.Far;
                graphics.DrawString(WatermarkText, font,
                    brush, new RectangleF(ImageMargin, y,
                        width - ImageMargin * 2, FooterHeight), format);
            }
        }

        private static void DrawCardTile(Graphics graphics, Rectangle tile,
            string code, string imagePath)
        {
            Image image = LoadImage(imagePath);
            if (image != null)
            {
                using (image)
                {
                    graphics.DrawImage(image, tile);
                }
            }
            else
            {
                using (var fill = new SolidBrush(TileFill))
                using (var border = new Pen(BorderColor, 2F))
                using (var font = new Font("Microsoft YaHei UI", 15F,
                    FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(TextMutedColor))
                using (var format = new StringFormat())
                {
                    graphics.FillRectangle(fill, tile);
                    graphics.DrawRectangle(border, tile);
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    graphics.DrawString(string.IsNullOrEmpty(code)
                        ? "缺少图片" : code, font, brush, tile, format);
                }
            }

            using (var border = new Pen(DividerColor, 1F))
            {
                graphics.DrawRectangle(border, tile);
            }
        }

        /// <summary>
        /// Padding between the count text and the edge of its round sticker.
        /// It follows the card size so the exported sticker keeps the same
        /// weight as the grid badge it copies.
        /// </summary>
        internal static int CountBadgePadding(float scale)
        {
            return Math.Max(3, (int)Math.Round(3F * scale));
        }

        /// <summary>
        /// Distance between the round sticker and the card edge, scaled like
        /// the grid badge (4 px on a 176 px thumb).
        /// </summary>
        internal static int CountBadgeMargin(float scale)
        {
            return Math.Max(4, (int)Math.Round(4F * scale));
        }

        /// <summary>
        /// Places the round count sticker in the bottom right corner of a
        /// tile, the same corner the deck grid uses, so the exported sheet
        /// never carries the old label on the top right.
        /// </summary>
        internal static Rectangle PlaceCountBadge(Rectangle tile,
            int diameter, float scale)
        {
            int margin = CountBadgeMargin(scale);
            return new Rectangle(
                tile.Right - diameter - margin,
                tile.Bottom - diameter - margin,
                diameter,
                diameter);
        }

        /// <summary>
        /// Repeat count sticker of the 缩略 sheet: the same "×N" circle the
        /// deck grid draws in the bottom right corner of a card (round accent
        /// fill, dark halo, white count) instead of the old rounded "xN" pill
        /// that sat on the top right.
        /// </summary>
        private static void DrawCountBadge(Graphics graphics, Rectangle tile,
            int count)
        {
            string text = "\u00D7" + count;

            // The grid sizes its sticker from a 176 px wide thumb; the
            // exported tile is ImageCardWidth wide, so the gap, the margin
            // and the font grow with the card and the badge keeps exactly
            // the same weight on the artwork.
            float scale = tile.Width / 176F;
            int pad = CountBadgePadding(scale);
            int margin = CountBadgeMargin(scale);

            using (var format =
                (StringFormat)StringFormat.GenericTypographic.Clone())
            using (var font = new Font("Microsoft YaHei UI",
                Math.Max(12F, 16F * scale), FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (var smallFont = new Font("Microsoft YaHei UI",
                Math.Max(9F, 11F * scale), FontStyle.Bold,
                GraphicsUnit.Pixel))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                // The badge hugs the count: text size plus a small pad, so
                // the sticker stays a dot on the art instead of a bar.
                Font used = font;
                SizeF size = graphics.MeasureString(text, used,
                    PointF.Empty, format);
                int diameter = (int)Math.Ceiling(
                    Math.Max(size.Width, size.Height)) + pad * 2;
                int maxDiameter = Math.Max(20, Math.Min(
                    tile.Width - margin * 2, tile.Height / 2));
                if (diameter > maxDiameter)
                    diameter = maxDiameter;

                // A cell too narrow for the full size count gets the smaller
                // font instead of text spilling out of the circle.
                if (size.Width > diameter - pad * 2)
                {
                    used = smallFont;
                    size = graphics.MeasureString(text, used,
                        PointF.Empty, format);
                    diameter = Math.Max(diameter, Math.Min(maxDiameter,
                        (int)Math.Ceiling(size.Width) + pad * 2));
                }

                Rectangle badge = PlaceCountBadge(tile, diameter, scale);

                // The dark halo keeps the accent circle readable on bright
                // artwork, matching the grid badge.
                int halo = Math.Max(2, (int)Math.Round(2F * scale));
                using (var haloBrush = new SolidBrush(
                    Color.FromArgb(140, 3, 8, 18)))
                using (var fill = new SolidBrush(AccentColor))
                using (var textBrush = new SolidBrush(Color.White))
                {
                    graphics.FillEllipse(haloBrush,
                        Rectangle.Inflate(badge, halo, halo));
                    graphics.FillEllipse(fill, badge);
                    graphics.DrawString(text, used, textBrush, badge, format);
                }
            }
        }

        /// <summary>
        /// Writes the deck to an A4 PDF with nine cards per page. Every copy of
        /// the deck appears once, so the printed sheets can be cut and sleeved
        /// directly.
        /// </summary>
        public static DeckExportResult ExportPdf(DeckDefinition deck,
            IList<string> cardCodes, IList<string> imagePaths)
        {
            if (deck == null)
                return DeckExportResult.Failed("没有可导出的卡组。");
            if (cardCodes == null || cardCodes.Count == 0)
                return DeckExportResult.Failed("卡组没有卡牌，无法导出。");

            try
            {
                var pages = new List<IList<DeckPdfImage>>();
                IList<DeckPdfImage> page = null;
                var shared = new Dictionary<string, DeckPdfImage>(
                    StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < cardCodes.Count; i++)
                {
                    string code = cardCodes[i] ?? string.Empty;
                    string path = imagePaths != null &&
                        i < imagePaths.Count ? imagePaths[i] : null;
                    string key = string.IsNullOrEmpty(path)
                        ? "missing:" + code
                        : path;

                    DeckPdfImage image;
                    if (!shared.TryGetValue(key, out image))
                    {
                        image = RenderPdfImage(code, path);
                        shared[key] = image;
                    }

                    if (i % DeckPdfWriter.CardsPerPage == 0)
                    {
                        page = new List<DeckPdfImage>(
                            DeckPdfWriter.CardsPerPage);
                        pages.Add(page);
                    }
                    page.Add(image);
                }

                string outputDirectory = GetOutputDirectory(deck.FilePath);
                Directory.CreateDirectory(outputDirectory);
                string pathOut = BuildExportPath(outputDirectory, deck,
                    null, ".pdf");
                DeckPdfWriter.Write(pathOut, pages, WatermarkText);
                return DeckExportResult.Completed(pathOut, pages.Count);
            }
            catch (Exception ex)
            {
                return DeckExportResult.Failed("导出 PDF 失败：" + ex.Message);
            }
        }

        private static DeckPdfImage RenderPdfImage(string code, string path)
        {
            Image source = LoadImage(path);
            using (var bitmap = new Bitmap(PdfCardPixelWidth,
                PdfCardPixelHeight, PixelFormat.Format24bppRgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.InterpolationMode =
                        InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.Clear(Color.White);

                    if (source != null)
                    {
                        using (source)
                        {
                            graphics.DrawImage(source,
                                new Rectangle(0, 0, PdfCardPixelWidth,
                                    PdfCardPixelHeight));
                        }
                    }
                    else
                    {
                        using (var fill = new SolidBrush(TileFill))
                        using (var font = new Font("Microsoft YaHei UI", 42F,
                            FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var brush = new SolidBrush(TextMutedColor))
                        using (var format = new StringFormat())
                        {
                            var area = new Rectangle(0, 0, PdfCardPixelWidth,
                                PdfCardPixelHeight);
                            graphics.FillRectangle(fill, area);
                            format.Alignment = StringAlignment.Center;
                            format.LineAlignment = StringAlignment.Center;
                            graphics.DrawString(string.IsNullOrEmpty(code)
                                ? "缺少图片" : code, font, brush, area, format);
                        }
                    }
                }

                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Jpeg);
                    return new DeckPdfImage(stream.ToArray(),
                        PdfCardPixelWidth, PdfCardPixelHeight);
                }
            }
        }



        /// <summary>Copies the card codes to the clipboard, one per line.</summary>
        public static DeckExportResult CopyCardCodes(DeckDefinition deck)
        {
            string text = BuildCardCodeText(deck);
            if (text.Length == 0)
                return DeckExportResult.Failed("卡组没有可复制的卡号。");

            try
            {
                System.Windows.Forms.Clipboard.SetText(text);
                return DeckExportResult.Completed(string.Empty, 0);
            }
            catch (Exception ex)
            {
                return DeckExportResult.Failed("复制失败：" + ex.Message);
            }
        }

        /// <summary>
        /// Builds the full path of an export file. The deck name is sanitized
        /// for the file system. PNG sheets add _full / _prev, while the PDF
        /// uses the bare deck name, so the three exports never collide.
        /// </summary>
        public static string BuildExportPath(string outputDirectory,
            DeckDefinition deck, string suffix, string extension)
        {
            string name = deck == null ? string.Empty : deck.Name;
            string fileName = SanitizeFileName(name);
            if (!string.IsNullOrEmpty(suffix))
                fileName += "_" + suffix;
            return Path.Combine(outputDirectory ?? string.Empty,
                fileName + (extension ?? string.Empty));
        }

        /// <summary>
        /// Replaces the characters Windows forbids in file names and trims the
        /// trailing dots and spaces Explorer cannot handle. An empty result
        /// falls back to "deck" so the export always has a usable name.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "deck";

            var builder = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < name.Length; i++)
            {
                char value = name[i];
                bool bad = false;
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (value == invalid[j])
                    {
                        bad = true;
                        break;
                    }
                }
                builder.Append(bad ? '_' : value);
            }

            string result = builder.ToString().Trim().TrimEnd('.', ' ');
            if (result.Length == 0)
                return "deck";

            // CON, PRN, AUX, NUL and COM1..LPT9 are reserved even with an
            // extension.
            string stem = result.Split('.')[0];
            string[] reserved = { "CON", "PRN", "AUX", "NUL" };
            for (int i = 0; i < reserved.Length; i++)
            {
                if (string.Equals(stem, reserved[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return "_" + result;
                }
            }
            if (stem.Length == 4 &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                stem[3] >= '1' && stem[3] <= '9')
            {
                return "_" + result;
            }

            return result;
        }

        private static Image LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite))
                using (Image source = Image.FromStream(stream, true, true))
                {
                    return new Bitmap(source);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }
            catch (OutOfMemoryException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }
    }
}
