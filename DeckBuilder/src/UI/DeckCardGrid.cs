using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// One card image cell. <see cref="Count"/> is only painted in the 缩略
    /// view, where duplicate codes collapse into a single image.
    /// </summary>
    public sealed class DeckCardTile
    {
        public string Code { get; set; }
        public int Count { get; set; }
        public string ImagePath { get; set; }
        public Func<string> ImagePathProvider { get; set; }

        public DeckCardTile()
        {
            Count = 1;
        }

        public string ResolveImagePath()
        {
            return ImagePathProvider != null
                ? ImagePathProvider()
                : ImagePath;
        }
    }

    /// <summary>
    /// Fixed-column image grid used by 编辑卡组. Cards are laid out like a
    /// physical deck: the 展开 view shows every copy in a 10 wide grid (5 rows
    /// for a 50 card deck) with a tight gap, the 缩略 view shows one image per
    /// code in a 7 x 4 block with a wider gap and the repeat count in the
    /// bottom right corner.
    /// </summary>
    public sealed class DeckCardGrid : ScrollableControl
    {
        private const int GridPadding = 6;
        private const int CellRadius = 6;
        private const int CountBadgeMargin = 4;
        private const int ThumbWidth = 176;
        private const int ThumbHeight = 246;
        private const int CardRatioHeight = 88;
        private const int CardRatioWidth = 63;

        private readonly List<DeckCardTile> _items;
        private readonly Font _codeFont;
        private readonly Font _countFont;
        private readonly Font _countSmallFont;
        private readonly Font _emptyFont;
        private readonly ToolTip _toolTip;

        private int _columns;
        private int _gap;
        private int _rows;
        private int _cellWidth;
        private int _cellHeight;
        private int _xOffset;
        private int _yOffset;
        private int _targetRows;
        private bool _showCounts;
        private float _countBadgeScale;
        private string _toolTipText;

        public ThumbnailCache Thumbnails { get; set; }
        public string EmptyText { get; set; }

        /// <summary>
        /// Number of images per row. The 展开 view uses 10, the 缩略 view 7.
        /// </summary>
        public int Columns
        {
            get { return _columns; }
            set
            {
                int columns = Math.Max(1, Math.Min(32, value));
                if (_columns == columns)
                    return;
                _columns = columns;
                RecalculateLayout();
                Invalidate();
            }
        }

        /// <summary>Space between two cells, horizontally and vertically.</summary>
        public int Gap
        {
            get { return _gap; }
            set
            {
                int gap = Math.Max(0, Math.Min(96, value));
                if (_gap == gap)
                    return;
                _gap = gap;
                RecalculateLayout();
                Invalidate();
            }
        }

        /// <summary>
        /// Target number of fully visible rows, 0 to size cells from the width
        /// only. The 缩略 view asks for 4 so 7 x 4 cards fill the viewport.
        /// </summary>
        public int TargetRows
        {
            get { return _targetRows; }
            set
            {
                int rows = Math.Max(0, Math.Min(16, value));
                if (_targetRows == rows)
                    return;
                _targetRows = rows;
                RecalculateLayout();
                Invalidate();
            }
        }

        /// <summary>Draws the duplicate count badge in the bottom right.</summary>
        public bool ShowCounts
        {
            get { return _showCounts; }
            set
            {
                if (_showCounts == value)
                    return;
                _showCounts = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Scales the whole count badge, 1 keeps the diameter derived from the
        /// count text. The 缩略 view asks for 0.7 so the sticker shrinks while
        /// the count keeps its font size.
        /// </summary>
        public float CountBadgeScale
        {
            get { return _countBadgeScale; }
            set
            {
                float scale = value < 0.5F ? 0.5F : (value > 2F ? 2F : value);
                if (Math.Abs(_countBadgeScale - scale) < 0.001F)
                    return;
                _countBadgeScale = scale;
                Invalidate();
            }
        }

        public event EventHandler<DeckCardTileEventArgs> ItemClick;

        public DeckCardGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            UpdateStyles();

            AutoScroll = true;
            BackColor = AppTheme.MainBackground;
            EmptyText = "没有可显示的卡片";

            _items = new List<DeckCardTile>();
            _columns = 10;
            _gap = 6;
            _cellWidth = ThumbWidth;
            _cellHeight = ThumbHeight;
            _countBadgeScale = 1F;
            _toolTipText = string.Empty;
            _toolTip = new ToolTip();

            _codeFont = new Font("Microsoft YaHei UI", 7.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _countFont = new Font("Microsoft YaHei UI", 12F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _countSmallFont = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _emptyFont = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
        }

        public IList<DeckCardTile> Items
        {
            get { return _items; }
        }

        public void SetItems(IList<DeckCardTile> items)
        {
            _items.Clear();
            if (items != null)
                _items.AddRange(items);

            RecalculateLayout();
            ScrollToTop();
            Invalidate();
        }

        public void ScrollToTop()
        {
            AutoScrollPosition = Point.Empty;
            PerformLayout();
            AutoScrollPosition = Point.Empty;
        }

        private void RecalculateLayout()
        {
            int columns = Math.Max(1, _columns);
            int availableWidth = Math.Max(0, ClientSize.Width - GridPadding * 2);
            int gaps = Math.Max(0, columns - 1) * _gap;
            int cell = (availableWidth - gaps) / columns;

            if (_targetRows > 0)
            {
                // Fit the requested rows into the viewport as well, so the
                // 缩略 view spreads the deck over the whole page instead of
                // leaving a tall empty strip at the bottom.
                int availableHeight =
                    Math.Max(0, ClientSize.Height - GridPadding * 2);
                int rowGaps = Math.Max(0, _targetRows - 1) * _gap;
                int cellFromHeight = (availableHeight - rowGaps) / _targetRows;
                if (cellFromHeight > 0)
                {
                    int widthFromHeight = (int)Math.Round(
                        cellFromHeight * (double)CardRatioWidth /
                        CardRatioHeight);
                    if (widthFromHeight < cell)
                        cell = widthFromHeight;
                }
            }

            if (cell < 32)
                cell = 32;

            _cellWidth = cell;
            _cellHeight = Math.Max(1,
                (int)Math.Round(cell * (double)CardRatioHeight / CardRatioWidth));
            _rows = _items.Count == 0
                ? 0
                : (_items.Count + columns - 1) / columns;

            int contentWidth = columns * _cellWidth + gaps;
            _xOffset = Math.Max(GridPadding,
                (ClientSize.Width - contentWidth) / 2);

            int contentHeight = _items.Count == 0
                ? ClientSize.Height
                : GridPadding * 2 + _rows * _cellHeight +
                    Math.Max(0, _rows - 1) * _gap;
            _yOffset = contentHeight < ClientSize.Height
                ? Math.Max(GridPadding,
                    (ClientSize.Height - contentHeight) / 2 + GridPadding)
                : GridPadding;
            if (AutoScrollMinSize.Height != contentHeight)
                AutoScrollMinSize = new Size(0, contentHeight);
        }

        private Rectangle GetItemRectangle(int index)
        {
            if (index < 0 || index >= _items.Count)
                return Rectangle.Empty;

            int row = index / _columns;
            int column = index % _columns;
            return new Rectangle(
                _xOffset + column * (_cellWidth + _gap),
                _yOffset + row * (_cellHeight + _gap),
                _cellWidth,
                _cellHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var background = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(background, ClientRectangle);

            if (_items.Count == 0)
            {
                DrawEmptyState(e.Graphics);
                return;
            }

            int viewportTop = -AutoScrollPosition.Y;
            int viewportBottom = viewportTop + ClientSize.Height;

            GraphicsState state = e.Graphics.Save();
            e.Graphics.TranslateTransform(
                AutoScrollPosition.X, AutoScrollPosition.Y);
            try
            {
                for (int row = 0; row < _rows; row++)
                {
                    int top = _yOffset + row * (_cellHeight + _gap);
                    if (top + _cellHeight < viewportTop)
                        continue;
                    if (top > viewportBottom)
                        break;

                    for (int column = 0; column < _columns; column++)
                    {
                        int index = row * _columns + column;
                        if (index >= _items.Count)
                            break;
                        DrawCard(e.Graphics, index, GetItemRectangle(index));
                    }
                }
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        private void DrawCard(Graphics graphics, int index, Rectangle bounds)
        {
            DeckCardTile tile = _items[index];
            Image image = Thumbnails == null
                ? null
                : Thumbnails.GetOrLoad(
                    tile.ResolveImagePath(), ThumbWidth, ThumbHeight, false);

            if (image != null)
            {
                GraphicsState state = graphics.Save();
                using (GraphicsPath path =
                    AppTheme.CreateRoundedRectangle(bounds, CellRadius))
                {
                    graphics.SetClip(path);
                    graphics.InterpolationMode =
                        InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(image, bounds);
                }
                graphics.Restore(state);
            }
            else
            {
                using (GraphicsPath path =
                    AppTheme.CreateRoundedRectangle(bounds, CellRadius))
                using (var fill = new SolidBrush(AppTheme.TileBackground))
                using (var border = new Pen(AppTheme.Border))
                {
                    graphics.FillPath(fill, path);
                    graphics.DrawPath(border, path);
                }

                Rectangle textBounds = Rectangle.Inflate(bounds, -6, -6);
                TextRenderer.DrawText(graphics, tile.Code ?? string.Empty,
                    _codeFont, textBounds, AppTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPrefix);
            }

            if (_showCounts && tile.Count > 1)
                DrawCountBadge(graphics, bounds, tile.Count);
        }

        private void DrawCountBadge(Graphics graphics, Rectangle bounds, int count)
        {
            string text = "×" + count.ToString();
            Font font = _countFont;
            Size textSize = TextRenderer.MeasureText(text, font,
                Size.Empty, TextFormatFlags.NoPadding);
            // The badge hugs the count: the pad only keeps the glyphs off
            // the edge of the circle, so the sticker stays small on the
            // artwork instead of covering a corner of the illustration.
            int pad = Math.Max(2, bounds.Width / 70);
            int diameter = Math.Max(textSize.Width, textSize.Height) + pad * 2;

            int maxDiameter = Math.Max(20, Math.Min(
                bounds.Width - CountBadgeMargin * 2, bounds.Height / 2));
            if (diameter > maxDiameter)
                diameter = maxDiameter;

            // A cell too narrow for the full size count gets the smaller
            // font instead of text spilling out of the circle.
            if (textSize.Width > diameter - pad * 2)
            {
                font = _countSmallFont;
                textSize = TextRenderer.MeasureText(text, font,
                    Size.Empty, TextFormatFlags.NoPadding);
                diameter = Math.Max(diameter,
                    Math.Min(maxDiameter, textSize.Width + 2));
            }

            // The 缩略 view scales the sticker down while the count keeps its
            // font size, so the number fills the circle instead of floating
            // inside it.
            if (_countBadgeScale < 0.999F || _countBadgeScale > 1.001F)
                diameter = Math.Max(14,
                    (int)Math.Round(diameter * _countBadgeScale));

            var badge = new Rectangle(
                bounds.Right - diameter - CountBadgeMargin,
                bounds.Bottom - diameter - CountBadgeMargin,
                diameter,
                diameter);

            // The dark halo keeps the accent circle readable on bright
            // artwork, where the plain fill used to blend into the illustration.
            using (var halo = new SolidBrush(Color.FromArgb(140, 3, 8, 18)))
                graphics.FillEllipse(halo, Rectangle.Inflate(badge, 2, 2));

            using (var fill = new SolidBrush(AppTheme.Accent))
                graphics.FillEllipse(fill, badge);

            TextRenderer.DrawText(graphics, text, font, badge, Color.White,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }

        private void DrawEmptyState(Graphics graphics)
        {
            TextRenderer.DrawText(graphics, EmptyText ?? string.Empty,
                _emptyFont, ClientRectangle, AppTheme.TextMuted,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix);
        }

        private int HitTest(Point location)
        {
            if (_items.Count == 0 || _columns <= 0 || _cellWidth <= 0)
                return -1;

            int x = location.X - AutoScrollPosition.X;
            int y = location.Y - AutoScrollPosition.Y;
            if (x < _xOffset || y < _yOffset)
                return -1;

            int columnStep = _cellWidth + _gap;
            int column = (x - _xOffset) / columnStep;
            if (column < 0 || column >= _columns)
                return -1;
            if ((x - _xOffset) % columnStep > _cellWidth)
                return -1;

            int rowStep = _cellHeight + _gap;
            int row = (y - _yOffset) / rowStep;
            if (row < 0 || row >= _rows)
                return -1;
            if ((y - _yOffset) % rowStep > _cellHeight)
                return -1;

            int index = row * _columns + column;
            return index < _items.Count ? index : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button != MouseButtons.None)
                return;

            int index = HitTest(e.Location);
            string text = string.Empty;
            if (index >= 0)
            {
                DeckCardTile tile = _items[index];
                text = tile.Count > 1
                    ? tile.Code + "  ×" + tile.Count
                    : tile.Code ?? string.Empty;
            }

            if (string.Equals(text, _toolTipText, StringComparison.Ordinal))
                return;

            _toolTipText = text;
            _toolTip.SetToolTip(this, text);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            int index = HitTest(e.Location);
            if (index < 0)
                return;

            EventHandler<DeckCardTileEventArgs> handler = ItemClick;
            if (handler != null)
                handler(this, new DeckCardTileEventArgs(_items[index], index));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_toolTipText.Length == 0)
                return;

            _toolTipText = string.Empty;
            _toolTip.SetToolTip(this, string.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecalculateLayout();
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _codeFont.Dispose();
                _countFont.Dispose();
                _countSmallFont.Dispose();
                _emptyFont.Dispose();
                _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Click information for <see cref="DeckCardGrid"/>.</summary>
    public sealed class DeckCardTileEventArgs : EventArgs
    {
        public DeckCardTile Tile { get; private set; }
        public int Index { get; private set; }

        public DeckCardTileEventArgs(DeckCardTile tile, int index)
        {
            Tile = tile;
            Index = index;
        }
    }
}
