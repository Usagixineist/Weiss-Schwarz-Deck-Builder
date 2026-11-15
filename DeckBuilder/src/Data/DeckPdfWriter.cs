using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// One card image already encoded as JPEG, ready to be embedded into a
    /// deck PDF. The bytes are shared by every copy of the same card.
    /// </summary>
    internal sealed class DeckPdfImage
    {
        public DeckPdfImage(byte[] data, int width, int height)
        {
            Data = data ?? new byte[0];
            Width = width;
            Height = height;
        }

        public byte[] Data { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
    }

    /// <summary>
    /// Writes a deck as a plain A4 PDF without a PDF library. Every page holds
    /// nine cards in a 3 x 3 grid; cards are 63 x 88 mm with a 1 mm gutter.
    /// JPEG images are embedded directly with DCTDecode.
    /// </summary>
    internal static class DeckPdfWriter
    {
        public const float A4WidthPoints = 595.2756F;
        public const float A4HeightPoints = 841.8898F;
        public const int Columns = 3;
        public const int Rows = 3;
        public const int CardsPerPage = Columns * Rows;

        private const float CardWidthMillimeters = 63F;
        private const float CardHeightMillimeters = 88F;
        private const float GapMillimeters = 1F;

        /// <summary>
        /// Writes the pages to <paramref name="path"/>. Each inner list is one
        /// page and may hold at most nine images. The order is row by row,
        /// left to right, so the top left card is the first card of the deck.
        /// </summary>
        public static void Write(string path, IList<IList<DeckPdfImage>> pages,
            string watermarkText = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("PDF path is empty", "path");
            if (pages == null || pages.Count == 0)
                throw new ArgumentException("PDF needs at least one page", "pages");

            float cardWidth = MillimetersToPoints(CardWidthMillimeters);
            float cardHeight = MillimetersToPoints(CardHeightMillimeters);
            float gap = MillimetersToPoints(GapMillimeters);
            float gridWidth = Columns * cardWidth + (Columns - 1) * gap;
            float gridHeight = Rows * cardHeight + (Rows - 1) * gap;
            float left = (A4WidthPoints - gridWidth) / 2F;
            float bottom = (A4HeightPoints - gridHeight) / 2F;
            float stepX = cardWidth + gap;
            float stepY = cardHeight + gap;

            var pageInfos = new List<PageInfo>(pages.Count);
            int nextObject = 3;
            for (int p = 0; p < pages.Count; p++)
            {
                IList<DeckPdfImage> images = pages[p];
                int count = images == null ? 0 : images.Count;
                if (count > CardsPerPage)
                {
                    throw new InvalidOperationException(
                        "PDF page " + (p + 1) + " holds more than " +
                        CardsPerPage + " cards");
                }

                var info = new PageInfo();
                info.PageObject = nextObject++;
                info.ContentObject = nextObject++;
                info.Images = new List<DeckPdfImage>(count);
                info.ImageObjects = new List<int>(count);
                for (int i = 0; i < count; i++)
                {
                    info.Images.Add(images[i]);
                    info.ImageObjects.Add(nextObject++);
                }
                pageInfos.Add(info);
            }

            int objectCount = nextObject - 1;
            var offsets = new long[objectCount + 1];

            using (var stream = new FileStream(path, FileMode.Create,
                FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                WriteAscii(writer, "%PDF-1.4\n");
                writer.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });

                offsets[1] = stream.Position;
                WriteAscii(writer,
                    "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                var kids = new StringBuilder();
                for (int i = 0; i < pageInfos.Count; i++)
                {
                    if (i > 0)
                        kids.Append(' ');
                    kids.Append(pageInfos[i].PageObject).Append(" 0 R");
                }

                offsets[2] = stream.Position;
                WriteAscii(writer, "2 0 obj\n<< /Type /Pages /Kids [" +
                    kids + "] /Count " + pageInfos.Count +
                    " >>\nendobj\n");

                for (int p = 0; p < pageInfos.Count; p++)
                {
                    PageInfo info = pageInfos[p];
                    WritePage(writer, stream, offsets, info);
                    WriteContent(writer, stream, offsets, info,
                        cardWidth, cardHeight, left, bottom, stepX, stepY,
                        watermarkText);
                    for (int i = 0; i < info.Images.Count; i++)
                    {
                        WriteImage(writer, stream, offsets,
                            info.ImageObjects[i], info.Images[i]);
                    }
                }

                long xref = stream.Position;
                WriteAscii(writer, "xref\n0 " + (objectCount + 1) + "\n");
                WriteAscii(writer, "0000000000 65535 f \n");
                for (int i = 1; i <= objectCount; i++)
                {
                    WriteAscii(writer, offsets[i].ToString(
                        "D10", CultureInfo.InvariantCulture) +
                        " 00000 n \n");
                }

                WriteAscii(writer, "trailer\n<< /Size " +
                    (objectCount + 1) + " /Root 1 0 R >>\nstartxref\n" +
                    xref.ToString(CultureInfo.InvariantCulture) +
                    "\n%%EOF\n");
                writer.Flush();
            }
        }

        private static void WritePage(BinaryWriter writer, FileStream stream,
            long[] offsets, PageInfo info)
        {
            var resources = new StringBuilder();
            for (int i = 0; i < info.ImageObjects.Count; i++)
            {
                if (i > 0)
                    resources.Append(' ');
                resources.Append("/Im").Append(i).Append(' ')
                    .Append(info.ImageObjects[i]).Append(" 0 R");
            }

            offsets[info.PageObject] = stream.Position;
            WriteAscii(writer, info.PageObject +
                " 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " +
                FormatFloat(A4WidthPoints) + " " +
                FormatFloat(A4HeightPoints) +
                "] /Resources << /ProcSet [/PDF /ImageC /Text] /Font << " +
                "/F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> " +
                ">> /XObject << " +
                resources + " >> >> /Contents " + info.ContentObject +
                " 0 R >>\nendobj\n");
        }

        private static void WriteContent(BinaryWriter writer,
            FileStream stream, long[] offsets, PageInfo info,
            float cardWidth, float cardHeight, float left, float bottom,
            float stepX, float stepY, string watermarkText)
        {
            var content = new StringBuilder();
            for (int i = 0; i < info.Images.Count; i++)
            {
                int row = i / Columns;
                int column = i % Columns;
                float x = left + column * stepX;
                float y = bottom + (Rows - 1 - row) * stepY;
                content.Append("q\n");
                content.AppendFormat(CultureInfo.InvariantCulture,
                    "{0} 0 0 {1} {2} {3} cm\n",
                    FormatFloat(cardWidth), FormatFloat(cardHeight),
                    FormatFloat(x), FormatFloat(y));
                content.Append("/Im").Append(i).Append(" Do\nQ\n");
            }

            if (!string.IsNullOrEmpty(watermarkText))
            {
                const float fontSize = 6F;
                const float rightMargin = 24F;
                const float bottomBaseline = 18F;
                float textWidth = MeasureHelveticaWidth(watermarkText,
                    fontSize);
                content.Append("BT\n/F1 ").Append(FormatFloat(fontSize))
                    .Append(" Tf\n0.45 0.5 0.58 rg\n");
                content.AppendFormat(CultureInfo.InvariantCulture,
                    "1 0 0 1 {0} {1} Tm\n({2}) Tj\nET\n",
                    FormatFloat(A4WidthPoints - rightMargin - textWidth),
                    FormatFloat(bottomBaseline),
                    EscapePdfText(watermarkText));
            }

            byte[] bytes = Encoding.ASCII.GetBytes(content.ToString());
            offsets[info.ContentObject] = stream.Position;
            WriteAscii(writer, info.ContentObject +
                " 0 obj\n<< /Length " + bytes.Length +
                " >>\nstream\n");
            writer.Write(bytes);
            WriteAscii(writer, "\nendstream\nendobj\n");
        }

        private static void WriteImage(BinaryWriter writer, FileStream stream,
            long[] offsets, int objectNumber, DeckPdfImage image)
        {
            offsets[objectNumber] = stream.Position;
            WriteAscii(writer, objectNumber +
                " 0 obj\n<< /Type /XObject /Subtype /Image /Width " +
                image.Width + " /Height " + image.Height +
                " /ColorSpace /DeviceRGB /BitsPerComponent 8 " +
                "/Filter /DCTDecode /Length " + image.Data.Length +
                " >>\nstream\n");
            writer.Write(image.Data);
            WriteAscii(writer, "\nendstream\nendobj\n");
        }

        private static float MillimetersToPoints(float millimeters)
        {
            return millimeters * 72F / 25.4F;
        }

        /// <summary>
        /// Right-aligns the footer text without loading font metrics. Helvetica
        /// averages roughly 0.53 em per character over the ASCII credit line.
        /// </summary>
        private static float MeasureHelveticaWidth(string text,
            float fontSize)
        {
            return (text == null ? 0 : text.Length) * fontSize * 0.53F;
        }

        private static string EscapePdfText(string text)
        {
            var builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char value = text[i];
                if (value == '(' || value == ')' || value == '\\')
                    builder.Append('\\');
                if (value >= 32 && value <= 126)
                    builder.Append(value);
                else
                    builder.Append('?');
            }
            return builder.ToString();
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static void WriteAscii(BinaryWriter writer, string text)
        {
            writer.Write(Encoding.ASCII.GetBytes(text));
        }

        private sealed class PageInfo
        {
            public int PageObject;
            public int ContentObject;
            public List<DeckPdfImage> Images;
            public List<int> ImageObjects;
        }
    }
}

