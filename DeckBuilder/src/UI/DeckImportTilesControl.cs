using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using WeissDeckBuilder.Data;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// One cell of the 导入卡组 grid: the 卡组代码 placeholder, or one staged
    /// deck. Only the caption is painted, exactly like the small title a deck
    /// tile of 查看卡组 prints under its cover.
    /// </summary>
    internal sealed class DeckImportTile
    {
        public string Caption { get; set; }

        /// <summary>Resolves the cover image, null for the blank code cell.</summary>
        public Func<string> ImagePathProvider { get; set; }

        /// <summary>Slot number of a staged deck, 0 for the 卡组代码 cell.</summary>
        public int SlotIndex { get; set; }

        /// <summary>Text the hover tooltip prints, empty for no tooltip.</summary>
        public string ToolTipText { get; set; }

        public bool IsCodeCell
        {
            get { return SlotIndex <= 0; }
        }
    }

    internal sealed class DeckImportTileEventArgs : EventArgs
    {
        public DeckImportTile Tile { get; private set; }
        public int Index { get; private set; }

        public DeckImportTileEventArgs(DeckImportTile tile, int index)
        {
            Tile = tile;
            Index = index;
        }
    }

    /// <summary>
    /// The six tiles of the 导入卡组 layer: rounded deck tiles in three
    /// columns, the covers sized like the ones of 查看卡组 but reduced. Tiles
    /// that have no staged deck behind them are not created at all, so the grid
    /// only ever shows the 卡组代码 cell plus the decks that exist.
    /// </summary>
    internal sealed class DeckImportTilesControl : Control
    {
        private const int Columns = 3;
        private const int TileWidth = 132;
        private const int TileHeight = 176;
        private const int TileGap = 14;
        private const int TilePadding = 10;
        private const int TileRadius = 10;
        private const int ImageRadius = 8;
        private const int CaptionGap = 6;
        private const int CaptionHeight = 30;

        private readonly List<DeckImportTile> _tiles;
        private readonly Font _captionFont;
        private readonly Font _glyphFont;
        private readonly StringFormat _centeredFormat;
        private readonly ToolTip _toolTip;

        private int _hoverIndex;
        private int _pressedIndex;
        private int _armedIndex;
        private string _toolTipText;

        public DeckImportTilesControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            UpdateStyles();

            BackColor = AppTheme.MainBackground;
            _tiles = new List<DeckImportTile>();
            _hoverIndex = -1;
            _armedIndex = -1;
            _toolTipText = string.Empty;

            // Same face and weight as the 卡牌效果 text of 卡片信息: a regular
            // line in the soft text colour, only smaller to fit the reduced
            // tile.
            _captionFont = new Font("Microsoft YaHei UI", 9.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _glyphFont = new Font("Microsoft YaHei UI", 20F,
                FontStyle.Bold, GraphicsUnit.Point, 134);

            _centeredFormat = new StringFormat();
            _centeredFormat.Alignment = StringAlignment.Center;
            _centeredFormat.LineAlignment = StringAlignment.Center;
            _centeredFormat.Trimming = StringTrimming.EllipsisCharacter;
            _centeredFormat.FormatFlags = StringFormatFlags.NoWrap;

            _toolTip = new ToolTip();
            AccessibleName = "导入卡组网格";
        }

        public ThumbnailCache Thumbnails { get; set; }

        public event EventHandler<DeckImportTileEventArgs> TileClick;

        public event EventHandler<DeckImportTileEventArgs> TileRightClick;

        public IList<DeckImportTile> Tiles
        {
            get { return _tiles; }
        }

        /// <summary>
        /// Tile whose next right click deletes it. The 导入卡组 layer arms a
        /// tile on the first right click and clears it on any other gesture,
        /// so the tile is painted in the danger colour while it waits for the
        /// click that actually deletes the staged deck.
        /// </summary>
        public int ArmedIndex
        {
            get { return _armedIndex; }
            set
            {
                int index = value < 0 || value >= _tiles.Count ? -1 : value;
                if (index == _armedIndex)
                    return;

                _armedIndex = index;
                Invalidate();
            }
        }

        public void SetTiles(IList<DeckImportTile> tiles)
        {
            _tiles.Clear();
            if (tiles != null)
                _tiles.AddRange(tiles);
            _armedIndex = -1;
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var background = new SolidBrush(BackColor))
                graphics.FillRectangle(background, ClientRectangle);

            for (int i = 0; i < _tiles.Count; i++)
            {
                DrawTile(graphics, GetTileRectangle(i), _tiles[i],
                    i == _hoverIndex, i == _armedIndex);
            }
        }

        /// <summary>Rectangle of one tile, the whole grid centred in the control.</summary>
        private Rectangle GetTileRectangle(int index)
        {
            int usedColumns = Math.Max(1, Math.Min(_tiles.Count, Columns));
            int rows = Math.Max(1, (_tiles.Count + Columns - 1) / Columns);
            int gridWidth = usedColumns * TileWidth + (usedColumns - 1) * TileGap;
            int gridHeight = rows * TileHeight + (rows - 1) * TileGap;
            int left = Math.Max(0, (Width - gridWidth) / 2);
            int top = Math.Max(0, (Height - gridHeight) / 2);
            int row = index / Columns;
            int column = index % Columns;
            return new Rectangle(left + column * (TileWidth + TileGap),
                top + row * (TileHeight + TileGap), TileWidth, TileHeight);
        }

        private static Rectangle GetImageRectangle(Rectangle bounds)
        {
            int size = Math.Max(1, Math.Min(bounds.Width - TilePadding * 2,
                bounds.Height - TilePadding * 2 - CaptionHeight - CaptionGap));
            return new Rectangle(bounds.X + (bounds.Width - size) / 2,
                bounds.Y + TilePadding, size, size);
        }

        private void DrawTile(Graphics graphics, Rectangle bounds,
            DeckImportTile tile, bool hover, bool armed)
        {
            // An armed tile is the one that deletes on the next right click, so
            // it swaps the blue hover edge for the danger colour and takes a
            // light red wash: the second click can never come as a surprise.
            Color borderColor = armed
                ? AppTheme.Danger
                : hover ? AppTheme.Accent : AppTheme.Border;
            float borderWidth = armed || hover ? 2F : 1F;

            using (GraphicsPath path = AppTheme.CreateRoundedRectangle(bounds,
                TileRadius))
            using (var fill = new SolidBrush(AppTheme.TileBackground))
            using (var border = new Pen(borderColor, borderWidth))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            Rectangle imageBounds = GetImageRectangle(bounds);
            string imagePath = tile.ImagePathProvider == null
                ? null
                : tile.ImagePathProvider();
            Image image = Thumbnails == null
                ? null
                : Thumbnails.GetOrLoad(imagePath, imageBounds.Width,
                    imageBounds.Height, true);

            if (image != null)
            {
                GraphicsState state = graphics.Save();
                using (GraphicsPath clip = AppTheme.CreateRoundedRectangle(
                    imageBounds, ImageRadius))
                {
                    graphics.SetClip(clip, CombineMode.Intersect);
                    graphics.DrawImage(image, imageBounds);
                }
                graphics.Restore(state);
            }
            else
            {
                DrawImagePlaceholder(graphics, imageBounds, tile);
            }

            if (armed)
            {
                // The wash lands on top of the cover, so a tile with a picture
                // still reads as red while it waits for the second right click.
                using (GraphicsPath wash = AppTheme.CreateRoundedRectangle(
                    bounds, TileRadius))
                using (var tint = new SolidBrush(
                    Color.FromArgb(52, AppTheme.Danger)))
                {
                    graphics.FillPath(tint, wash);
                }
            }

            int captionTop = imageBounds.Bottom + CaptionGap;
            var captionBounds = new Rectangle(bounds.X + 6, captionTop,
                Math.Max(1, bounds.Width - 12),
                Math.Max(14, bounds.Bottom - TilePadding - captionTop));
            using (var textBrush = new SolidBrush(armed
                ? AppTheme.Danger
                : hover ? AppTheme.Text : AppTheme.TextSoft))
            {
                graphics.DrawString(tile.Caption ?? string.Empty, _captionFont,
                    textBrush, captionBounds, _centeredFormat);
            }
        }

        /// <summary>
        /// Blank icon of the 卡组代码 cell and the fallback of a staged deck
        /// whose cover cannot be resolved.
        /// </summary>
        private void DrawImagePlaceholder(Graphics graphics, Rectangle bounds,
            DeckImportTile tile)
        {
            using (GraphicsPath path = AppTheme.CreateRoundedRectangle(bounds,
                ImageRadius))
            using (var fill = new SolidBrush(AppTheme.InputBackground))
            using (var border = new Pen(AppTheme.Border))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            string caption = tile.Caption ?? string.Empty;
            string glyph = tile.IsCodeCell
                ? "＋"
                : (caption.Length > 0 ? caption.Substring(0, 1) : "?");
            using (var brush = new SolidBrush(AppTheme.Placeholder))
            {
                graphics.DrawString(glyph, _glyphFont, brush, bounds,
                    _centeredFormat);
            }
        }

        private int HitTest(Point location)
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (GetTileRectangle(i).Contains(location))
                    return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            string text = index < 0
                ? string.Empty
                : _tiles[index].ToolTipText ?? string.Empty;
            if (string.Equals(text, _toolTipText, StringComparison.Ordinal))
                return;

            _toolTipText = text;
            _toolTip.SetToolTip(this, text);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex == -1 && _toolTipText.Length == 0)
                return;

            _hoverIndex = -1;
            Cursor = Cursors.Default;
            _toolTipText = string.Empty;
            _toolTip.SetToolTip(this, string.Empty);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _pressedIndex = e.Button == MouseButtons.Left ||
                e.Button == MouseButtons.Right
                ? HitTest(e.Location)
                : -1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int pressed = _pressedIndex;
            _pressedIndex = -1;

            if (e.Button != MouseButtons.Right && e.Button != MouseButtons.Left)
                return;

            int index = HitTest(e.Location);
            if (index < 0 || index != pressed)
                return;

            EventHandler<DeckImportTileEventArgs> handler =
                e.Button == MouseButtons.Right ? TileRightClick : TileClick;
            if (handler != null)
                handler(this, new DeckImportTileEventArgs(_tiles[index], index));
        }
    }
}
