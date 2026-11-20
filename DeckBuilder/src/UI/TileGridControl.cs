using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;
using WeissDeckBuilder.Infrastructure;

namespace WeissDeckBuilder.UI
{
    public enum TileImageMode
    {
        Cover,
        Fit
    }

    public sealed class TileGridItem
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string ImagePath { get; set; }
        public Func<string> ImagePathProvider { get; set; }
        public object Tag { get; set; }

        /// <summary>
        /// Optional per-item height. The series overview uses it to give
        /// tiles with many abbreviations enough room while keeping normal
        /// tiles compact.
        /// </summary>
        public int PreferredHeight { get; set; }

        /// <summary>
        /// Copies the player owns of this card. The 我的收藏 page paints the
        /// number as a red sticker on the artwork; 0 draws nothing.
        /// </summary>
        public int OwnedCount { get; set; }

        /// <summary>
        /// Renders the artwork grey instead of in colour. The collection page
        /// uses it for cards (and whole series) the player does not own.
        /// </summary>
        public bool Dimmed { get; set; }

        private bool _imagePathResolved;

        public TileGridItem()
        {
            Title = string.Empty;
            Subtitle = string.Empty;
        }

        internal string ResolveImagePath()
        {
            if (_imagePathResolved)
                return ImagePath;

            _imagePathResolved = true;
            if (string.IsNullOrEmpty(ImagePath) && ImagePathProvider != null)
            {
                try
                {
                    ImagePath = ImagePathProvider();
                }
                catch
                {
                    ImagePath = null;
                }
            }
            return ImagePath;
        }
    }

    public sealed class TileGridItemEventArgs : EventArgs
    {
        public TileGridItem Item { get; private set; }
        public int Index { get; private set; }

        public TileGridItemEventArgs(TileGridItem item, int index)
        {
            Item = item;
            Index = index;
        }
    }

    /// <summary>
    /// Owner-drawn, scrollable tile grid. It only decodes thumbnails for the
    /// visible row range, which keeps the 199-series overview responsive.
    /// </summary>
    public sealed class TileGridControl : ScrollableControl, IThemeAware
    {
        private const int CaptionGap = 6;
        private const int TitleAreaHeight = 36;
        private const int SubtitleGap = 2;
        private const int CaptionBottomPadding = 6;

        /// <summary>
        /// Height an empty block keeps when it is expanded by the page that
        /// hosts it, so the empty text stays readable.
        /// </summary>
        private const int PageEmptyHeight = 96;

        private readonly List<TileGridItem> _items;
        private readonly Font _titleFont;
        private readonly Font _titleSmallFont;
        private readonly Font _titleMicroFont;
        private readonly Font _subtitleFont;
        private readonly Font _subtitleSmallFont;
        private readonly Font _subtitleMicroFont;
        private readonly Font _placeholderTitleFont;
        private readonly Font _placeholderTextFont;
        private readonly StringFormat _centeredFormat;
        private readonly StringFormat _titleFormat;
        private readonly StringFormat _subtitleFormat;
        private SolidBrush _titleBrush;
        private SolidBrush _subtitleBrush;
        private SolidBrush _placeholderBrush;
        private SolidBrush _dimTitleBrush;
        private SolidBrush _dimSubtitleBrush;
        private readonly Font _ownedCountFont;
        private readonly Font _ownedCountSmallFont;

        /// <summary>
        /// Red sticker of an owned card, matching the 缩略 badge of the deck
        /// page but with the collection red instead of the accent blue.
        /// </summary>
        private static readonly Color OwnedBadgeColor =
            Color.FromArgb(220, 38, 38);

        /// <summary>Wash that dims an unowned illustration after the grey pass.</summary>
        private static readonly Color DimVeilColor =
            Color.FromArgb(78, AppTheme.Scrim);

        /// <summary>
        /// Greyscale matrix of an unowned tile: luminance weights halved so the
        /// artwork turns both colourless and darker, which reads as "not owned"
        /// on the dark page without hiding the picture.
        /// </summary>
        private static readonly ImageAttributes DimImageAttributes =
            CreateDimImageAttributes();

        private readonly Bitmap _measureBitmap;
        private readonly Graphics _measureGraphics;

        private int _columns;
        private int _rows;
        private int _xOffset;
        private int[] _rowTops;
        private int[] _rowHeights;
        private int[] _itemHeights;
        private int _hoverIndex;
        private int _pressedIndex;
        private int _rightPressedIndex;
        private int _selectedIndex;
        private bool _recalculating;
        private int _contentHeight;
        private int _pageViewportHeight;

        /// <summary>
        /// Timestamp taken by SetItems, consumed by the first paint afterwards
        /// so the -profile log can tell how long the new list stayed invisible.
        /// Always 0 unless <see cref="LoadProfiler"/> is enabled.
        /// </summary>
        private long _itemsSetMark;

        // Long press stands in for "show details" on the 编辑卡组 card grid,
        // where a plain left click adds the card to the new deck instead.
        private readonly Timer _longPressTimer;
        private int _longPressIndex;
        private bool _longPressFired;
        private Point _pressPoint;

        public ThumbnailCache Thumbnails { get; set; }

        public int TileWidth { get; set; }
        public int TileHeight { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public int GridPadding { get; set; }
        public int Gap { get; set; }
        public TileImageMode ImageMode { get; set; }
        public string EmptyText { get; set; }

        /// <summary>
        /// When on, the grid does not scroll on its own: it lays the tiles out
        /// at the current width, reports the height it needs through
        /// <see cref="ContentHeight"/> and lets the page around it scroll. The
        /// deck overview uses this so 我的卡组 and 预设卡组 read as one page
        /// with a single scrollbar instead of two stacked scrollers.
        /// </summary>
        public bool ExpandToContent { get; set; }

        /// <summary>
        /// Height the tiles need at the current width. Only meaningful with
        /// <see cref="ExpandToContent"/>; the grid keeps its own Height on
        /// that value, so the page scroll host only has to move it.
        /// </summary>
        public int ContentHeight
        {
            get
            {
                RecalculateLayout();
                return _contentHeight;
            }
        }

        /// <summary>
        /// Height of the page viewport that hosts this grid. An expanded but
        /// empty grid takes that height instead of its short placeholder one,
        /// so "没有找到" stays centered on the page. Zero (the default) falls
        /// back to <see cref="PageEmptyHeight"/>.
        /// </summary>
        public int PageViewportHeight
        {
            get { return _pageViewportHeight; }
            set
            {
                int clamped = Math.Max(0, value);
                if (_pageViewportHeight == clamped)
                    return;

                _pageViewportHeight = clamped;
                RecalculateLayout();
            }
        }

        /// <summary>
        /// Raised when the grid is asked to scroll back to the top. An
        /// expanded grid cannot move itself — the page around it owns the
        /// scrollbar — so the host has to do the scrolling.
        /// </summary>
        public event EventHandler ScrollToTopRequested;

        public event EventHandler<TileGridItemEventArgs> ItemClick;

        /// <summary>
        /// Right click on an item. The 编辑卡组 grid uses it to take one copy
        /// out of the new deck.
        /// </summary>
        public event EventHandler<TileGridItemEventArgs> ItemRightClick;

        /// <summary>
        /// Left button held on the same item for
        /// <see cref="LongPressMilliseconds"/>. Used as the "show details"
        /// gesture where a click has a different meaning.
        /// </summary>
        public event EventHandler<TileGridItemEventArgs> ItemLongPress;

        /// <summary>Delay before a held left button counts as a long press.</summary>
        public int LongPressMilliseconds
        {
            get { return _longPressTimer.Interval; }
            set
            {
                int interval = Math.Max(200, Math.Min(3000, value));
                if (_longPressTimer.Interval != interval)
                    _longPressTimer.Interval = interval;
            }
        }

        public TileGridControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            UpdateStyles();

            AutoScroll = true;
            TabStop = true;
            BackColor = AppTheme.MainBackground;
            EmptyText = "没有可显示的内容";
            _items = new List<TileGridItem>();
            _rowTops = new int[0];
            _rowHeights = new int[0];
            _itemHeights = new int[0];
            _measureBitmap = new Bitmap(1, 1);
            _measureGraphics = Graphics.FromImage(_measureBitmap);
            _titleFont = new Font("Microsoft YaHei UI", 9.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _titleSmallFont = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _titleMicroFont = new Font("Microsoft YaHei UI", 7.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _subtitleFont = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _subtitleSmallFont = new Font("Microsoft YaHei UI", 7F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _subtitleMicroFont = new Font("Microsoft YaHei UI", 6F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _placeholderTitleFont = new Font("Microsoft YaHei UI", 26F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _placeholderTextFont = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _centeredFormat = new StringFormat();
            _centeredFormat.Alignment = StringAlignment.Center;
            _centeredFormat.LineAlignment = StringAlignment.Center;
            _centeredFormat.Trimming = StringTrimming.EllipsisCharacter;
            _centeredFormat.FormatFlags = StringFormatFlags.NoWrap;
            _titleFormat = new StringFormat();
            _titleFormat.Alignment = StringAlignment.Center;
            _titleFormat.LineAlignment = StringAlignment.Center;
            _titleFormat.Trimming = StringTrimming.EllipsisCharacter;
            _titleFormat.FormatFlags = 0;
            _subtitleFormat = new StringFormat();
            _subtitleFormat.Alignment = StringAlignment.Center;
            _subtitleFormat.LineAlignment = StringAlignment.Near;
            _subtitleFormat.Trimming = StringTrimming.None;
            CreateBrushes();
            _ownedCountFont = new Font("Microsoft YaHei UI", 10F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _ownedCountSmallFont = new Font("Microsoft YaHei UI", 7.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _hoverIndex = -1;
            _pressedIndex = -1;
            _rightPressedIndex = -1;
            _selectedIndex = -1;
            _longPressIndex = -1;
            _longPressTimer = new Timer();
            _longPressTimer.Interval = 550;
            _longPressTimer.Tick += LongPressTimer_Tick;

            ConfigureSeries();
        }

        /// <summary>
        /// Tile labels reuse their brushes on every paint, so a palette swap has
        /// to rebuild them from the new colours.
        /// </summary>
        public void ApplyTheme()
        {
            _titleBrush.Dispose();
            _subtitleBrush.Dispose();
            _placeholderBrush.Dispose();
            _dimTitleBrush.Dispose();
            _dimSubtitleBrush.Dispose();
            CreateBrushes();
            Invalidate();
        }

        private void CreateBrushes()
        {
            _titleBrush = new SolidBrush(AppTheme.Text);
            _subtitleBrush = new SolidBrush(AppTheme.TextMuted);
            _placeholderBrush = new SolidBrush(AppTheme.Placeholder);
            _dimTitleBrush = new SolidBrush(AppTheme.TextMuted);
            _dimSubtitleBrush = new SolidBrush(AppTheme.Placeholder);
        }


        public IList<TileGridItem> Items
        {
            get { return _items; }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int index = value < 0 || value >= _items.Count ? -1 : value;
                if (_selectedIndex == index)
                    return;
                _selectedIndex = index;
                Invalidate();
            }
        }

        public void ConfigureSeries()
        {
            TileWidth = 192;
            TileHeight = 264;
            ImageWidth = 192;
            ImageHeight = 192;
            ImageMode = TileImageMode.Cover;
            GridPadding = 4;
            Gap = 8;
            RecalculateLayout();
        }

        public int MeasureSeriesItemHeight(string subtitle)
        {
            int subtitleTop = ImageHeight + CaptionGap + TitleAreaHeight +
                SubtitleGap;
            int baseHeight = subtitleTop + CaptionBottomPadding;
            if (string.IsNullOrEmpty(subtitle))
                return Math.Max(TileHeight, baseHeight);

            float width = Math.Max(1, TileWidth - 16);
            const int maxTextHeight = 68;

            float height = MeasureSubtitleHeight(
                _measureGraphics, subtitle, _subtitleFont, width);
            if (height <= maxTextHeight)
                return Math.Max(TileHeight,
                    baseHeight + (int)Math.Ceiling(height));

            height = MeasureSubtitleHeight(
                _measureGraphics, subtitle, _subtitleSmallFont, width);
            if (height <= maxTextHeight)
                return Math.Max(TileHeight,
                    baseHeight + (int)Math.Ceiling(height));

            height = MeasureSubtitleHeight(
                _measureGraphics, subtitle, _subtitleMicroFont, width);
            return Math.Max(TileHeight,
                baseHeight + (int)Math.Ceiling(height));
        }

        public void ConfigureCards()
        {
            TileWidth = 143;
            TileHeight = 264;
            ImageWidth = 140;
            ImageHeight = 196;
            ImageMode = TileImageMode.Fit;
            GridPadding = 4;
            Gap = 8;
            RecalculateLayout();
        }

        public void SetItems(IList<TileGridItem> items)
        {
            _items.Clear();
            if (items != null)
                _items.AddRange(items);

            _hoverIndex = -1;
            _pressedIndex = -1;
            _rightPressedIndex = -1;
            _selectedIndex = -1;
            CancelLongPress();
            _longPressFired = false;
            RecalculateLayout();
            ScrollToTop();
            _itemsSetMark = LoadProfiler.Mark();
            Invalidate();
        }

        public void ScrollToTop()
        {
            AutoScrollPosition = Point.Empty;
            PerformLayout();
            AutoScrollPosition = Point.Empty;

            // An expanded grid has no scrollbar of its own: the page host moves
            // it, so it has to be told to go back to the top as well.
            if (ExpandToContent)
            {
                EventHandler handler = ScrollToTopRequested;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        /// <summary>Index of the first item whose Tag is the given object.</summary>
        public int IndexOfItem(object tag)
        {
            if (tag == null)
                return -1;

            for (int i = 0; i < _items.Count; i++)
            {
                if (ReferenceEquals(_items[i].Tag, tag))
                    return i;
            }
            return -1;
        }

        /// <summary>Tag of the item at the given index, or null.</summary>
        public object GetItemTag(int index)
        {
            if (index < 0 || index >= _items.Count)
                return null;
            return _items[index].Tag;
        }

        private void RecalculateLayout()
        {
            if (_recalculating)
                return;

            _recalculating = true;
            try
            {
                int availableWidth = Math.Max(0,
                    ClientSize.Width - GridPadding * 2);
                _columns = Math.Max(1,
                    (availableWidth + Gap) / (TileWidth + Gap));
                int contentWidth = _columns * TileWidth +
                    Math.Max(0, _columns - 1) * Gap;
                _xOffset = Math.Max(GridPadding,
                    (ClientSize.Width - contentWidth) / 2);
                _rows = _items.Count == 0
                    ? 0
                    : (_items.Count + _columns - 1) / _columns;

                _rowTops = new int[_rows];
                _rowHeights = new int[_rows];
                _itemHeights = new int[_items.Count];
                for (int i = 0; i < _items.Count; i++)
                {
                    int preferred = _items[i].PreferredHeight;
                    _itemHeights[i] = Math.Max(TileHeight, preferred);
                }

                int y = GridPadding;
                for (int row = 0; row < _rows; row++)
                {
                    int rowHeight = TileHeight;
                    for (int column = 0; column < _columns; column++)
                    {
                        int index = row * _columns + column;
                        if (index >= _items.Count)
                            break;
                        rowHeight = Math.Max(rowHeight, _itemHeights[index]);
                    }
                    _rowTops[row] = y;
                    _rowHeights[row] = rowHeight;
                    y += rowHeight + Gap;
                }

                int contentHeight;
                if (_items.Count == 0)
                {
                    // An empty block still fills the page viewport it sits in,
                    // so its hint stays centered instead of hugging the top.
                    contentHeight = ExpandToContent
                        ? Math.Max(PageEmptyHeight, _pageViewportHeight)
                        : ClientSize.Height;
                }
                else
                {
                    contentHeight = y - Gap + GridPadding;
                }

                _contentHeight = contentHeight;
                if (ExpandToContent)
                {
                    // The page scrolls for this grid; a scrollbar here would be
                    // the second one the deck overview must not have.
                    if (AutoScrollMinSize != Size.Empty)
                    {
                        AutoScrollMinSize = Size.Empty;
                        AutoScrollPosition = Point.Empty;
                    }

                    // The grid is the content of the page scroll host, so it
                    // reports the height its tiles need and the host moves it.
                    // The guard above keeps this from recursing.
                    if (Height != _contentHeight)
                        Height = _contentHeight;
                }
                else if (AutoScrollMinSize.Height != contentHeight)
                {
                    AutoScrollMinSize = new Size(0, contentHeight);
                }
            }
            finally
            {
                _recalculating = false;
            }
        }

        private Rectangle GetItemRectangle(int index)
        {
            if (index < 0 || index >= _items.Count || _columns <= 0)
                return Rectangle.Empty;

            int row = index / _columns;
            int column = index % _columns;
            int top = row < _rowTops.Length ? _rowTops[row] : GridPadding;
            int height = index < _itemHeights.Length
                ? _itemHeights[index]
                : TileHeight;
            return new Rectangle(
                _xOffset + column * (TileWidth + Gap),
                top,
                TileWidth,
                height);
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            long paintMark = LoadProfiler.Mark();
            ThumbnailCache thumbnails = Thumbnails;
            long decodeMark = thumbnails == null ? 0L : thumbnails.DecodeCount;
            double decodeMsMark = thumbnails == null
                ? 0.0
                : thumbnails.DecodeMilliseconds;
            int tiles = 0;

            // The whole frame is wrapped so a -profile log line is written even
            // when the empty state or the visible band ends the paint early.
            try
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
                if (ExpandToContent)
                {
                    // The hosting page scrolls, so only the rows inside the page
                    // viewport are painted (and only their thumbnails decode).
                    Rectangle band = GetVisibleBand();
                    if (band.Height <= 0 || band.Width <= 0)
                        return;

                    viewportTop = band.Top;
                    viewportBottom = band.Bottom;
                }

                GraphicsState state = e.Graphics.Save();
                e.Graphics.TranslateTransform(
                    AutoScrollPosition.X, AutoScrollPosition.Y);
                try
                {
                    for (int row = 0; row < _rows; row++)
                    {
                        if (_rowTops[row] + _rowHeights[row] < viewportTop)
                            continue;
                        if (_rowTops[row] > viewportBottom)
                            break;

                        for (int column = 0; column < _columns; column++)
                        {
                            int index = row * _columns + column;
                            if (index >= _items.Count)
                                break;
                            DrawTile(e.Graphics, index, GetItemRectangle(index));
                            tiles++;
                        }
                    }
                }
                finally
                {
                    e.Graphics.Restore(state);
                }
            }
            finally
            {
                ReportPaint(paintMark, thumbnails, decodeMark, decodeMsMark,
                    tiles);
            }
        }

        /// <summary>
        /// Writes one -profile line per paint: the frame time, how many tiles it
        /// drew and how much of that went into decoding thumbnails. The first
        /// paint after <see cref="SetItems"/> additionally reports the delay the
        /// player sees before the new card list appears. No-op without -profile.
        /// </summary>
        private void ReportPaint(long paintMark, ThumbnailCache thumbnails,
            long decodeMark, double decodeMsMark, int tiles)
        {
            if (!LoadProfiler.Enabled)
                return;

            string extra = "tiles " + tiles;
            if (thumbnails != null)
            {
                extra += ", decodes " +
                    (thumbnails.DecodeCount - decodeMark) + " (" +
                    (thumbnails.DecodeMilliseconds - decodeMsMark).ToString(
                        "0.0", CultureInfo.InvariantCulture) + " ms)";
            }

            if (_itemsSetMark != 0L)
            {
                LoadProfiler.Note("TileGrid", "first paint after SetItems",
                    _itemsSetMark, extra);
                _itemsSetMark = 0L;
            }

            // Hover moves repaint the whole viewport, so only frames that took
            // noticeable time are worth a line of their own.
            if (LoadProfiler.Since(paintMark) >= 8.0)
                LoadProfiler.Note("TileGrid", "paint", paintMark, extra);
        }

        /// <summary>
        /// Part of this grid that is actually visible inside its ancestors,
        /// expressed in grid coordinates. A block expanded by a scrolling page
        /// uses it so a long list paints only the rows on screen.
        /// </summary>
        private Rectangle GetVisibleBand()
        {
            Rectangle band = ClientRectangle;
            int x = 0;
            int y = 0;
            Control child = this;
            Control parent = Parent;
            while (parent != null)
            {
                x -= child.Left;
                y -= child.Top;

                Rectangle ancestor = parent.ClientRectangle;
                ancestor.Offset(x, y);
                band = Rectangle.Intersect(band, ancestor);
                if (band.Width <= 0 || band.Height <= 0)
                    return Rectangle.Empty;

                child = parent;
                parent = parent.Parent;
            }

            return band;
        }

        private void DrawTile(Graphics graphics, int index, Rectangle bounds)
        {
            bool hover = index == _hoverIndex;
            bool selected = index == _selectedIndex;
            Color borderColor = selected
                ? AppTheme.Accent
                : hover
                    ? AppTheme.AccentHover
                    : AppTheme.Border;

            using (GraphicsPath path = CreateRoundedRectangle(bounds, 10))
            using (var fill = new SolidBrush(AppTheme.TileBackground))
            using (var border = new Pen(borderColor, selected || hover ? 2F : 1F))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            int imageX = bounds.X + (bounds.Width - ImageWidth) / 2;
            int imageY = bounds.Y;
            var imageBounds = new Rectangle(
                imageX, imageY, ImageWidth, ImageHeight);

            TileGridItem item = _items[index];
            Image image = Thumbnails == null
                ? null
                : Thumbnails.GetOrLoad(
                    item.ResolveImagePath(),
                    ImageWidth,
                    ImageHeight,
                    ImageMode == TileImageMode.Cover);

            if (image != null)
            {
                GraphicsState imageState = graphics.Save();
                using (GraphicsPath imagePath =
                    CreateRoundedRectangle(imageBounds, 10))
                {
                    graphics.SetClip(imagePath);
                    if (item.Dimmed)
                    {
                        graphics.DrawImage(image, imageBounds, 0, 0,
                            image.Width, image.Height, GraphicsUnit.Pixel,
                            DimImageAttributes);
                        using (var veil = new SolidBrush(DimVeilColor))
                            graphics.FillRectangle(veil, imageBounds);
                    }
                    else
                    {
                        graphics.DrawImage(image, imageBounds);
                    }
                }
                graphics.Restore(imageState);
            }
            else
            {
                DrawImagePlaceholder(graphics, imageBounds, item.Title);
            }

            int captionTop = imageBounds.Bottom + CaptionGap;
            float textWidth = bounds.Width - 16;
            var titleBounds = new RectangleF(
                bounds.X + 8, captionTop, textWidth, TitleAreaHeight);
            var subtitleBounds = new RectangleF(
                bounds.X + 8,
                titleBounds.Bottom + SubtitleGap,
                textWidth,
                Math.Max(18, bounds.Bottom - titleBounds.Bottom -
                    SubtitleGap - CaptionBottomPadding));
            Font titleFont = ChooseTitleFont(
                graphics, item.Title, titleBounds.Width,
                titleBounds.Height);
            Font subtitleFont = ChooseSubtitleFont(
                graphics, item.Subtitle, subtitleBounds.Width,
                subtitleBounds.Height);
            Brush titleBrush = item.Dimmed ? _dimTitleBrush : _titleBrush;
            Brush subtitleBrush = item.Dimmed
                ? _dimSubtitleBrush
                : _subtitleBrush;

            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.DrawString(item.Title ?? string.Empty,
                titleFont, titleBrush, titleBounds, _titleFormat);
            graphics.DrawString(item.Subtitle ?? string.Empty,
                subtitleFont, subtitleBrush, subtitleBounds,
                _subtitleFormat);

            if (item.OwnedCount > 0)
                DrawOwnedBadge(graphics, imageBounds, item.OwnedCount);
        }

        /// <summary>
        /// Red "×N" sticker of an owned card. It sits on the top right of the
        /// artwork, a little below the hexagon soul icon the print carries
        /// there, so the count never covers the soul value or the card name.
        /// </summary>
        private void DrawOwnedBadge(Graphics graphics,
            Rectangle imageBounds, int count)
        {
            string text = "×" + count.ToString();
            Font font = _ownedCountFont;
            Size textSize = TextRenderer.MeasureText(text, font,
                Size.Empty, TextFormatFlags.NoPadding);
            int pad = Math.Max(3, imageBounds.Width / 40);
            int diameter = Math.Max(textSize.Width, textSize.Height) + pad * 2;

            int maxDiameter = Math.Max(20, imageBounds.Width / 4);
            if (diameter > maxDiameter)
                diameter = maxDiameter;
            if (textSize.Width > diameter - pad * 2)
            {
                font = _ownedCountSmallFont;
                textSize = TextRenderer.MeasureText(text, font,
                    Size.Empty, TextFormatFlags.NoPadding);
                diameter = Math.Max(diameter,
                    Math.Min(maxDiameter, textSize.Width + pad * 2));
            }

            int margin = Math.Max(4, imageBounds.Width / 24);
            int top = imageBounds.Top +
                (int)Math.Round(imageBounds.Height * 0.18F);
            var badge = new Rectangle(
                imageBounds.Right - diameter - margin,
                top,
                diameter,
                diameter);

            // Same dark halo as the deck count badge, so the red circle stays
            // readable on bright illustration backgrounds.
            using (var halo = new SolidBrush(Color.FromArgb(140, AppTheme.Scrim)))
                graphics.FillEllipse(halo, Rectangle.Inflate(badge, 2, 2));

            using (var fill = new SolidBrush(OwnedBadgeColor))
                graphics.FillEllipse(fill, badge);

            TextRenderer.DrawText(graphics, text, font, badge, Color.White,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }

        /// <summary>Colour matrix that turns a tile greyscale and darker.</summary>
        private static ImageAttributes CreateDimImageAttributes()
        {
            var matrix = new ColorMatrix(new[]
            {
                new[] { 0.15F, 0.15F, 0.15F, 0F, 0F },
                new[] { 0.30F, 0.30F, 0.30F, 0F, 0F },
                new[] { 0.05F, 0.05F, 0.05F, 0F, 0F },
                new[] { 0F, 0F, 0F, 1F, 0F },
                new[] { 0F, 0F, 0F, 0F, 1F }
            });

            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default,
                ColorAdjustType.Bitmap);
            return attributes;
        }

        private Font ChooseTitleFont(
            Graphics graphics,
            string text,
            float width,
            float height)
        {
            if (FitsTitle(graphics, text, _titleFont, width, height))
                return _titleFont;
            if (FitsTitle(graphics, text, _titleSmallFont, width, height))
                return _titleSmallFont;
            return _titleMicroFont;
        }

        private bool FitsTitle(
            Graphics graphics,
            string text,
            Font font,
            float width,
            float height)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            SizeF size = graphics.MeasureString(text, font,
                new SizeF(width, 10000F), _titleFormat);
            return size.Height <= height + 0.5F;
        }

        private Font ChooseSubtitleFont(
            Graphics graphics,
            string text,
            float width,
            float height)
        {
            if (MeasureSubtitleHeight(graphics, text, _subtitleFont, width) <= height)
                return _subtitleFont;
            if (MeasureSubtitleHeight(graphics, text, _subtitleSmallFont, width) <= height)
                return _subtitleSmallFont;
            return _subtitleMicroFont;
        }

        private float MeasureSubtitleHeight(
            Graphics graphics,
            string text,
            Font font,
            float width)
        {
            if (string.IsNullOrEmpty(text))
                return 0F;

            SizeF size = graphics.MeasureString(text, font,
                new SizeF(width, 10000F), _subtitleFormat);
            return size.Height;
        }


        private void DrawImagePlaceholder(
            Graphics graphics,
            Rectangle bounds,
            string title)
        {
            using (GraphicsPath path = CreateRoundedRectangle(bounds, 6))
            using (var fill = new SolidBrush(AppTheme.TileBackground))
            using (var border = new Pen(AppTheme.Border))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            string initial = string.IsNullOrEmpty(title)
                ? "?"
                : title.Substring(0, 1);
            var initialBounds = new RectangleF(
                bounds.X, bounds.Y + bounds.Height / 2 - 34,
                bounds.Width, 44);
            var textBounds = new RectangleF(
                bounds.X, bounds.Y + bounds.Height / 2 + 10,
                bounds.Width, 24);

            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.DrawString(initial, _placeholderTitleFont,
                _placeholderBrush, initialBounds, _centeredFormat);
            graphics.DrawString("无图片", _placeholderTextFont,
                _placeholderBrush, textBounds, _centeredFormat);
        }

        private void DrawEmptyState(Graphics graphics)
        {
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.DrawString(EmptyText ?? string.Empty,
                _subtitleFont, _placeholderBrush, ClientRectangle,
                _centeredFormat);
        }

        private int HitTest(Point location)
        {
            if (_items.Count == 0 || _columns <= 0)
                return -1;

            int x = location.X - AutoScrollPosition.X;
            int y = location.Y - AutoScrollPosition.Y;
            if (x < _xOffset || y < GridPadding)
                return -1;

            int column = (x - _xOffset) / (TileWidth + Gap);
            if (column < 0 || column >= _columns)
                return -1;

            int withinX = (x - _xOffset) % (TileWidth + Gap);
            if (withinX > TileWidth)
                return -1;

            for (int row = 0; row < _rows; row++)
            {
                if (y < _rowTops[row])
                    continue;
                if (y >= _rowTops[row] + _rowHeights[row])
                    continue;

                int index = row * _columns + column;
                if (index >= _items.Count)
                    return -1;

                Rectangle bounds = GetItemRectangle(index);
                return bounds.Contains(x, y) ? index : -1;
            }

            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            CancelLongPressWhenMoved(e.Location);

            int index = HitTest(e.Location);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            CancelLongPress();
            _rightPressedIndex = -1;
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                _rightPressedIndex = HitTest(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            Focus();
            _pressedIndex = HitTest(e.Location);
            _pressPoint = e.Location;
            _longPressFired = false;
            StartLongPress(_pressedIndex);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                int rightIndex = HitTest(e.Location);
                if (rightIndex >= 0 && rightIndex == _rightPressedIndex)
                {
                    SelectedIndex = rightIndex;
                    RaiseItemEvent(ItemRightClick, rightIndex);
                }
                _rightPressedIndex = -1;
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            CancelLongPress();
            if (_longPressFired)
            {
                // The held button already opened the detail page, so the
                // release must not count as a click as well.
                _longPressFired = false;
                _pressedIndex = -1;
                return;
            }

            int index = HitTest(e.Location);
            if (index >= 0 && index == _pressedIndex)
            {
                SelectedIndex = index;
                RaiseItemEvent(ItemClick, index);
            }
            _pressedIndex = -1;
        }

        /// <summary>
        /// Arms the long press timer for the item under the pressed button.
        /// Dragging away, scrolling or releasing the button cancels it.
        /// </summary>
        private void StartLongPress(int index)
        {
            _longPressTimer.Stop();
            _longPressIndex = index;
            if (index < 0)
                return;
            _longPressTimer.Start();
        }

        private void CancelLongPress()
        {
            _longPressTimer.Stop();
            _longPressIndex = -1;
        }

        private void CancelLongPressWhenMoved(Point location)
        {
            if (_longPressIndex < 0)
                return;

            Size drag = SystemInformation.DragSize;
            if (Math.Abs(location.X - _pressPoint.X) <= drag.Width &&
                Math.Abs(location.Y - _pressPoint.Y) <= drag.Height)
            {
                return;
            }

            CancelLongPress();
        }

        private void LongPressTimer_Tick(object sender, EventArgs e)
        {
            _longPressTimer.Stop();
            int index = _longPressIndex;
            _longPressIndex = -1;
            if (index < 0 || index != _pressedIndex)
                return;

            _longPressFired = true;
            RaiseItemEvent(ItemLongPress, index);
        }

        private void RaiseItemEvent(
            EventHandler<TileGridItemEventArgs> handler, int index)
        {
            if (handler == null || index < 0 || index >= _items.Count)
                return;

            handler(this, new TileGridItemEventArgs(_items[index], index));
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
            CancelLongPress();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _titleSmallFont.Dispose();
                _titleMicroFont.Dispose();
                _subtitleFont.Dispose();
                _subtitleSmallFont.Dispose();
                _subtitleMicroFont.Dispose();
                _placeholderTitleFont.Dispose();
                _placeholderTextFont.Dispose();
                _centeredFormat.Dispose();
                _titleFormat.Dispose();
                _subtitleFormat.Dispose();
                _titleBrush.Dispose();
                _subtitleBrush.Dispose();
                _placeholderBrush.Dispose();
                _measureGraphics.Dispose();
                _measureBitmap.Dispose();
                _longPressTimer.Stop();
                _longPressTimer.Tick -= LongPressTimer_Tick;
                _longPressTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath CreateRoundedRectangle(
            Rectangle bounds,
            int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(1, radius * 2);
            if (bounds.Width <= diameter || bounds.Height <= diameter)
            {
                path.AddRectangle(bounds);
                return path;
            }

            var arc = new Rectangle(bounds.Location,
                new Size(diameter, diameter));
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
}

