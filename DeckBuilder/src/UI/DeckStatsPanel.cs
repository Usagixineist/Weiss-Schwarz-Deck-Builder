using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// Owner-drawn 卡组信息 block of the deck detail sidebar. It repeats the
    /// look of the info table at the top of the card detail page: one rounded
    /// box per table, a muted caption above it and one line per row. A colour
    /// row whose cards are all missing from the deck stays empty, matching the
    /// "0 张不显示" rule of the type / colour matrix.
    /// </summary>
    internal sealed class DeckStatsPanel : Control
    {
        private const int RowHeight = 24;
        private const int CaptionHeight = 17;
        private const int CaptionGap = 4;
        private const int BlockGap = 14;
        private const int BoxRadius = 8;
        private const int CellInset = 4;

        private const TextFormatFlags CenteredFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix;

        private const TextFormatFlags CaptionFlags =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        private static readonly Color YellowColor = Color.FromArgb(250, 204, 21);
        private static readonly Color GreenColor = Color.FromArgb(74, 222, 128);
        private static readonly Color RedColor = Color.FromArgb(248, 113, 113);
        private static readonly Color BlueColor = Color.FromArgb(96, 165, 250);

        private static readonly string[] TypeHeaders =
        {
            "合计", "角色", "事件", "高潮"
        };

        private static readonly string[] LevelHeaders = { "0", "1", "2", "3" };
        private static readonly string[] SoulHeaders = { "魂", "2魂" };

        private readonly Font _captionFont;
        private readonly Font _headerFont;
        private readonly Font _valueFont;
        private readonly SolidBrush _boxBrush;
        private readonly Pen _dividerPen;

        private DeckStatistics _stats;

        public DeckStatsPanel()
        {
            SetStyle(ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            Margin = new Padding(0);

            _captionFont = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _headerFont = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _valueFont = new Font("Microsoft YaHei UI", 9.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _boxBrush = new SolidBrush(AppTheme.TileBackground);
            _dividerPen = new Pen(AppTheme.Divider);

            // The panel is pure read-only text; StaticText lets screen readers
            // announce the numbers as content instead of an empty pane.
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = "卡组信息";
        }

        /// <summary>Statistics drawn by the panel, null when no deck is open.</summary>
        public DeckStatistics Statistics
        {
            get { return _stats; }
        }

        /// <summary>
        /// Swaps the numbers and resizes the panel to the height the tables
        /// need, so the sidebar stack can wrap it tightly.
        /// </summary>
        public void SetStatistics(DeckStatistics statistics)
        {
            _stats = statistics;
            AccessibleName = BuildAccessibleText(statistics);

            int height = MeasureHeight(statistics);
            if (Height != height)
                Height = height;

            Invalidate();
        }

        public int MeasureHeight()
        {
            return MeasureHeight(_stats);
        }

        /// <summary>
        /// Height of the three tables plus their captions for a given set of
        /// numbers. Passing null measures the empty state, which is what the
        /// sidebar uses before a deck is opened.
        /// </summary>
        public static int MeasureHeight(DeckStatistics statistics)
        {
            int colorRows = CountColorRows(statistics);
            int typeTable = RowHeight * (2 + colorRows);
            int levelTable = RowHeight * 2;
            int soulTable = RowHeight * 2;

            return CaptionHeight + CaptionGap + typeTable + BlockGap +
                CaptionHeight + CaptionGap + levelTable + BlockGap +
                CaptionHeight + CaptionGap + soulTable;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int y = DrawTypeTable(graphics, 0);
            y += BlockGap;
            y = DrawLevelTable(graphics, y);
            y += BlockGap;
            DrawSoulTable(graphics, y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _captionFont.Dispose();
                _headerFont.Dispose();
                _valueFont.Dispose();
                _boxBrush.Dispose();
                _dividerPen.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// 6 x 4 matrix by default: a header row of 合计 / 角色 / 事件 / 高潮, a
        /// white total row and one colour row per colour the deck actually
        /// uses. Cells holding 0 stay blank.
        /// </summary>
        private int DrawTypeTable(Graphics graphics, int top)
        {
            DeckStatistics stats = _stats;
            CardCategory[] categories = DeckStatistics.MatrixCategories;
            int columns = TypeHeaders.Length;

            int y = DrawCaption(graphics, "类型 / 颜色", top);
            Rectangle box = DrawBox(graphics, y, 2 + CountColorRows(stats),
                columns);

            for (int column = 0; column < columns; column++)
            {
                DrawCell(graphics, Cell(box, column, 0, columns),
                    TypeHeaders[column], _headerFont, AppTheme.TextMuted);
            }

            DrawCell(graphics, Cell(box, 0, 1, columns),
                stats == null ? "0" : stats.Total.ToString(),
                _valueFont, AppTheme.Text);
            for (int i = 0; i < categories.Length; i++)
            {
                int value = stats == null
                    ? 0
                    : stats.GetCategoryTotal(categories[i]);
                DrawCell(graphics, Cell(box, i + 1, 1, columns),
                    value.ToString(), _valueFont, AppTheme.Text);
            }

            int row = 2;
            for (int i = 0; i < DeckStatistics.MatrixColors.Length; i++)
            {
                CardColor color = DeckStatistics.MatrixColors[i];
                if (stats == null || stats.GetColorTotal(color) <= 0)
                    continue;

                Color textColor = ColorOf(color);
                DrawCell(graphics, Cell(box, 0, row, columns),
                    stats.GetColorTotal(color).ToString(), _valueFont, textColor);

                for (int c = 0; c < categories.Length; c++)
                {
                    int value = stats.GetColorCategoryTotal(color, categories[c]);
                    if (value <= 0)
                        continue;

                    DrawCell(graphics, Cell(box, c + 1, row, columns),
                        value.ToString(), _valueFont, textColor);
                }

                row++;
            }

            return box.Bottom;
        }

        /// <summary>Header row 0 / 1 / 2 / 3 with the card counts below.</summary>
        private int DrawLevelTable(Graphics graphics, int top)
        {
            DeckStatistics stats = _stats;
            int columns = LevelHeaders.Length;

            int y = DrawCaption(graphics, "等级", top);
            Rectangle box = DrawBox(graphics, y, 2, columns);

            for (int column = 0; column < columns; column++)
            {
                DrawCell(graphics, Cell(box, column, 0, columns),
                    LevelHeaders[column], _headerFont, AppTheme.TextMuted);
            }

            for (int level = 0; level < columns; level++)
            {
                int value = stats == null ? 0 : stats.GetLevelTotal(level);
                DrawCell(graphics, Cell(box, level, 1, columns),
                    value.ToString(), _valueFont, AppTheme.Text);
            }

            return box.Bottom;
        }

        /// <summary>魂 / 2魂 trigger icons of the deck.</summary>
        private int DrawSoulTable(Graphics graphics, int top)
        {
            DeckStatistics stats = _stats;
            int columns = SoulHeaders.Length;

            int y = DrawCaption(graphics, "魂标", top);
            Rectangle box = DrawBox(graphics, y, 2, columns);

            for (int column = 0; column < columns; column++)
            {
                DrawCell(graphics, Cell(box, column, 0, columns),
                    SoulHeaders[column], _headerFont, AppTheme.TextMuted);
            }

            DrawCell(graphics, Cell(box, 0, 1, columns),
                (stats == null ? 0 : stats.SoulTriggerCards).ToString(),
                _valueFont, AppTheme.Text);
            DrawCell(graphics, Cell(box, 1, 1, columns),
                (stats == null ? 0 : stats.DoubleSoulTriggerCards).ToString(),
                _valueFont, AppTheme.Text);

            return box.Bottom;
        }

        private int DrawCaption(Graphics graphics, string text, int top)
        {
            TextRenderer.DrawText(graphics, text, _captionFont,
                new Rectangle(0, top, Math.Max(1, Width), CaptionHeight),
                AppTheme.TextMuted, CaptionFlags);
            return top + CaptionHeight + CaptionGap;
        }

        /// <summary>
        /// Rounded background of one table plus the grid lines that separate
        /// its cells, drawn the same way as the card detail info area.
        /// </summary>
        private Rectangle DrawBox(Graphics graphics, int top, int rows,
            int columns)
        {
            int height = Math.Max(RowHeight, rows * RowHeight);
            var box = new Rectangle(0, top, Math.Max(1, Width), height);

            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(box, BoxRadius))
            {
                graphics.FillPath(_boxBrush, path);
            }

            int columnWidth = box.Width / columns;
            for (int column = 1; column < columns; column++)
            {
                int x = box.Left + columnWidth * column;
                graphics.DrawLine(_dividerPen,
                    x, box.Top + 6, x, box.Bottom - 6);
            }

            for (int row = 1; row < rows; row++)
            {
                int y = box.Top + row * RowHeight;
                graphics.DrawLine(_dividerPen,
                    box.Left + 8, y, box.Right - 8, y);
            }

            return box;
        }

        private static Rectangle Cell(Rectangle box, int column, int row,
            int columns)
        {
            int columnWidth = box.Width / columns;
            int left = box.Left + column * columnWidth;
            int right = column == columns - 1
                ? box.Right
                : left + columnWidth;

            return new Rectangle(left, box.Top + row * RowHeight,
                Math.Max(1, right - left), RowHeight);
        }

        private static void DrawCell(Graphics graphics, Rectangle bounds,
            string text, Font font, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            TextRenderer.DrawText(graphics, text, font,
                new Rectangle(bounds.X + CellInset, bounds.Y,
                    Math.Max(1, bounds.Width - CellInset * 2), bounds.Height),
                color, CenteredFlags);
        }

        private static int CountColorRows(DeckStatistics stats)
        {
            if (stats == null)
                return 0;

            int rows = 0;
            for (int i = 0; i < DeckStatistics.MatrixColors.Length; i++)
            {
                if (stats.GetColorTotal(DeckStatistics.MatrixColors[i]) > 0)
                    rows++;
            }

            return rows;
        }

        private static Color ColorOf(CardColor color)
        {
            switch (color)
            {
                case CardColor.Yellow: return YellowColor;
                case CardColor.Green: return GreenColor;
                case CardColor.Red: return RedColor;
                case CardColor.Blue: return BlueColor;
                default: return AppTheme.Text;
            }
        }

        /// <summary>
        /// Screen reader text of the tables. The control paints everything
        /// itself, so the numbers also live here to stay reachable.
        /// </summary>
        private static string BuildAccessibleText(DeckStatistics stats)
        {
            if (stats == null)
                return "卡组信息";

            var parts = new List<string>();
            parts.Add("合计 " + stats.Total);
            for (int i = 0; i < DeckStatistics.MatrixCategories.Length; i++)
            {
                CardCategory category = DeckStatistics.MatrixCategories[i];
                parts.Add(WeissCard.CategoryText(category) + " " +
                    stats.GetCategoryTotal(category));
            }

            for (int i = 0; i < DeckStatistics.MatrixColors.Length; i++)
            {
                CardColor color = DeckStatistics.MatrixColors[i];
                if (stats.GetColorTotal(color) <= 0)
                    continue;

                parts.Add(WeissCard.ColorText(color) + " " +
                    stats.GetColorTotal(color));
            }

            for (int level = 0; level <= DeckStatistics.MaxLevel; level++)
                parts.Add(level + "级 " + stats.GetLevelTotal(level));

            parts.Add("魂 " + stats.SoulTriggerCards);
            parts.Add("2魂 " + stats.DoubleSoulTriggerCards);

            return "卡组信息 " + string.Join(" · ", parts.ToArray());
        }
    }
}
