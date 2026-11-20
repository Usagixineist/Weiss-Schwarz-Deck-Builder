using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using WeissDeckBuilder.Data;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// A control that answers file drops itself (卡图 and 异画 boxes). The
    /// window wide 卡表压缩包 import keeps its hands off those controls,
    /// otherwise dropping a png would also be read as an archive and answered
    /// with 添加失败.
    /// </summary>
    internal interface IOwnFileDrop
    {
    }

    /// <summary>
    /// Which controls the 卡表压缩包 drop handlers are attached to. Kept apart
    /// from MainForm so the rule can be checked without opening a window.
    /// </summary>
    internal static class CardPackDropScope
    {
        public static bool Accepts(Control control)
        {
            if (control == null)
                return false;

            // A box with its own drop handling reports the drop itself; the
            // archive import must not speak for it.
            return !(control is IOwnFileDrop);
        }
    }

    /// <summary>
    /// Rounded input frame used by the card editor. The TextBox itself is
    /// borderless and sits inside the painted frame, so the whole form keeps
    /// one dark look and a multiline box grows with its cell.
    /// </summary>
    internal sealed class FieldBox : Panel
    {
        private const int InnerPadding = 8;
        private readonly TextBox _input;
        private bool _multiline;

        public int CornerRadius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public Color FocusBorderColor { get; set; }

        public TextBox Input
        {
            get { return _input; }
        }

        public bool Multiline
        {
            get { return _multiline; }
            set
            {
                _multiline = value;
                _input.Multiline = value;
                _input.AcceptsReturn = value;
                _input.WordWrap = value;
                _input.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None;
                PositionInput();
            }
        }

        public override string Text
        {
            get { return _input.Text; }
            set { _input.Text = value ?? string.Empty; }
        }

        public FieldBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            CornerRadius = 7;
            FillColor = AppTheme.InputBackground;
            BorderColor = AppTheme.Border;
            FocusBorderColor = AppTheme.Accent;
            Margin = new Padding(0);

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.None;
            _input.BackColor = FillColor;
            _input.ForeColor = AppTheme.Text;
            _input.Font = new Font("Microsoft YaHei UI", 9.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _input.GotFocus += delegate { Invalidate(); };
            _input.LostFocus += delegate { Invalidate(); };
            Controls.Add(_input);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionInput();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // The native EDIT control draws a light scrollbar even inside a dark
            // frame; the same DarkMode_Explorer theme the main window uses keeps
            // the multiline boxes dark on Windows versions that support it.
            DarkModeHelper.Apply(_input.Handle, AppTheme.IsDark);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _input.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0,
                Math.Max(1, ClientSize.Width - 1),
                Math.Max(1, ClientSize.Height - 1));

            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(bounds, CornerRadius))
            using (var fill = new SolidBrush(FillColor))
            using (var pen = new Pen(_input.Focused
                ? FocusBorderColor
                : BorderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private void PositionInput()
        {
            if (_input == null)
                return;

            int height;
            int top;
            if (_multiline)
            {
                height = Math.Max(22, ClientSize.Height - 2 * InnerPadding);
                top = InnerPadding;
            }
            else
            {
                height = _input.PreferredHeight;
                top = Math.Max(3, (ClientSize.Height - height) / 2);
            }

            _input.SetBounds(InnerPadding, top,
                Math.Max(10, ClientSize.Width - 2 * InnerPadding), height);
        }
    }

    /// <summary>
    /// One row of rounded chips that behave like radio buttons (类别 / 颜色 /
    /// 卡包). The row keeps its own selected value so the editor can read it
    /// back without mapping buttons to enum values.
    /// </summary>
    internal sealed class ChipRow : FlowLayoutPanel, IThemeAware
    {
        private readonly List<RoundedButton> _chips = new List<RoundedButton>();
        private readonly Font _font;
        private Color _surface;
        private Color _active;
        private object _selected;

        public event EventHandler SelectionChanged;

        public ChipRow(Font font, Color surface, Color active)
        {
            _font = font;
            _surface = surface;
            _active = active;
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = false;
            AutoScroll = false;
            BackColor = Color.Transparent;
            Margin = new Padding(0);
            Padding = new Padding(0, 1, 0, 0);
        }

        /// <summary>
        /// Remembers the row's two colours across a palette swap so chips added
        /// later (a new effect row, a different category) match the new theme.
        /// </summary>
        public void ApplyTheme()
        {
            _surface = AppTheme.MapFromPrevious(_surface);
            _active = AppTheme.MapFromPrevious(_active);
        }

        public object SelectedValue
        {
            get { return _selected; }
        }

        public bool Wrap
        {
            get { return WrapContents; }
            set { WrapContents = value; }
        }

        public void ClearChips()
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                Controls.Remove(_chips[i]);
                _chips[i].Dispose();
            }
            _chips.Clear();
            _selected = null;
        }

        public void AddChip(string text, object value)
        {
            var chip = new RoundedButton();
            chip.Text = text ?? string.Empty;
            chip.Tag = value;
            chip.AutoSize = false;
            chip.Height = 24;
            chip.Width = Math.Max(42,
                TextRenderer.MeasureText(chip.Text, _font).Width + 20);
            chip.CornerRadius = 8;
            chip.SurfaceColor = _surface;
            chip.NormalColor = _surface;
            chip.HoverColor = AppTheme.Divider;
            chip.PressedColor = AppTheme.Border;
            chip.SelectedColor = _active;
            chip.ForeColor = AppTheme.TextSoft;
            chip.Font = _font;
            chip.Margin = new Padding(0, 0, 6, 4);
            chip.Cursor = Cursors.Hand;
            chip.Click += Chip_Click;
            _chips.Add(chip);
            Controls.Add(chip);
        }

        public void Select(object value)
        {
            _selected = value;
            for (int i = 0; i < _chips.Count; i++)
            {
                RoundedButton chip = _chips[i];
                bool active = value != null && Equals(chip.Tag, value);
                Color color = active ? Color.White : AppTheme.TextSoft;
                if (chip.Selected == active && chip.ForeColor == color)
                    continue;

                chip.Selected = active;
                chip.ForeColor = color;
                chip.Invalidate();
            }
        }

        private void Chip_Click(object sender, EventArgs e)
        {
            var chip = sender as RoundedButton;
            if (chip == null)
                return;

            Select(chip.Tag);
            EventHandler handler = SelectionChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }

    internal sealed class CardImageDroppedEventArgs : EventArgs
    {
        /// <summary>Every image the drop carried, in Explorer order.</summary>
        public IList<string> Paths { get; private set; }

        /// <summary>The first image of the drop, i.e. what a single-art box uses.</summary>
        public string Path
        {
            get { return Paths.Count == 0 ? string.Empty : Paths[0]; }
        }

        public CardImageDroppedEventArgs(string path)
            : this(path == null ? null : new string[] { path })
        {
        }

        public CardImageDroppedEventArgs(IList<string> paths)
        {
            var list = new List<string>();
            if (paths != null)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (!string.IsNullOrEmpty(paths[i]))
                        list.Add(paths[i]);
                }
            }
            Paths = list;
        }
    }

    /// <summary>
    /// The 卡图 box of the editor: shows the art scaled inside a card shaped
    /// frame, takes an image dragged from Explorer and opens a file picker on
    /// click or Space / Enter.
    /// </summary>
    internal sealed class CardImageDropBox : Control, IOwnFileDrop
    {
        private Image _image;
        private string _hint = string.Empty;
        private bool _hover;
        private bool _showCaption = true;
        private int _innerPadding = 12;

        public Font CaptionFont { get; set; }
        public Font HintFont { get; set; }
        public Color SurfaceColor { get; set; }

        /// <summary>
        /// The 卡图 caption above the art. The 异画 preview drops it so the
        /// same height shows a noticeably bigger picture.
        /// </summary>
        public bool ShowCaption
        {
            get { return _showCaption; }
            set
            {
                if (_showCaption == value)
                    return;

                _showCaption = value;
                Invalidate();
            }
        }

        /// <summary>Gap between the frame and the art, i.e. how tight it fits.</summary>
        public int InnerPadding
        {
            get { return _innerPadding; }
            set
            {
                int padding = Math.Max(0, value);
                if (_innerPadding == padding)
                    return;

                _innerPadding = padding;
                Invalidate();
            }
        }

        public event EventHandler BrowseRequested;
        public event EventHandler<CardImageDroppedEventArgs> FileDropped;

        public CardImageDropBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.Selectable, true);

            BackColor = Color.Transparent;
            AllowDrop = true;
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public void SetImage(Image image, string hint)
        {
            _image = image;
            _hint = hint ?? string.Empty;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (!SurfaceColor.IsEmpty)
            {
                using (var surface = new SolidBrush(SurfaceColor))
                    e.Graphics.FillRectangle(surface, ClientRectangle);
            }

            var bounds = new Rectangle(0, 0,
                Math.Max(1, ClientSize.Width - 1),
                Math.Max(1, ClientSize.Height - 1));
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(bounds, 12))
            using (var fill = new SolidBrush(AppTheme.InputBackground))
            using (var pen = new Pen(_hover ? AppTheme.Accent : AppTheme.Border,
                _hover ? 2F : 1.4F))
            {
                pen.DashStyle = _image == null ? DashStyle.Dash : DashStyle.Solid;
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }

            bool hasCaption = _showCaption && CaptionFont != null;
            if (hasCaption)
            {
                var caption = new Rectangle(14, 7,
                    Math.Max(1, ClientSize.Width - 28), 16);
                TextRenderer.DrawText(e.Graphics, "卡图", CaptionFont, caption,
                    AppTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }

            int padding = _innerPadding;
            var inner = new Rectangle(
                bounds.Left + padding,
                bounds.Top + (hasCaption ? 26 : padding),
                Math.Max(1, bounds.Width - padding * 2),
                Math.Max(1, bounds.Height - (hasCaption ? 26 + padding : padding * 2)));
            if (inner.Width <= 4 || inner.Height <= 4)
                return;

            if (_image == null)
            {
                TextRenderer.DrawText(e.Graphics, _hint,
                    HintFont ?? Font, inner, AppTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPrefix);
                return;
            }

            Size size = Fit(_image.Size, inner.Size);
            var target = new Rectangle(
                inner.Left + (inner.Width - size.Width) / 2,
                inner.Top + (inner.Height - size.Height) / 2,
                size.Width, size.Height);
            e.Graphics.DrawImage(_image, target);

            if (!string.IsNullOrEmpty(_hint) && inner.Bottom - target.Bottom > 18)
            {
                var hintBounds = new Rectangle(inner.Left, target.Bottom + 2,
                    inner.Width, inner.Bottom - target.Bottom - 2);
                TextRenderer.DrawText(e.Graphics, _hint, HintFont ?? Font,
                    hintBounds, AppTheme.Placeholder,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.Top |
                    TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left)
                RaiseBrowse();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            RaiseBrowse();
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            bool supported = HasImage(drgevent.Data);
            drgevent.Effect = supported
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            if (supported && !_hover)
            {
                _hover = true;
                Invalidate();
            }
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            base.OnDragOver(drgevent);
            drgevent.Effect = HasImage(drgevent.Data)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            _hover = false;
            Invalidate();

            List<string> paths = GetImagePaths(drgevent.Data);
            if (paths.Count == 0)
                return;

            EventHandler<CardImageDroppedEventArgs> handler = FileDropped;
            if (handler != null)
                handler(this, new CardImageDroppedEventArgs(paths));
        }

        private static bool HasImage(IDataObject data)
        {
            return GetImagePaths(data).Count > 0;
        }

        /// <summary>
        /// Every supported image a drop carries. A dropped folder contributes
        /// the png / jpg files inside it in name order, so a whole 异画 folder
        /// can be queued in one gesture.
        /// </summary>
        internal static List<string> GetImagePaths(IDataObject data)
        {
            var paths = new List<string>();
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
                return paths;

            string[] files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null)
                return paths;

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                if (string.IsNullOrEmpty(file))
                    continue;

                if (Directory.Exists(file))
                {
                    AddFolderImages(paths, file);
                    continue;
                }

                if (CardDataWriter.IsSupportedImage(file) && !paths.Contains(file))
                    paths.Add(file);
            }
            return paths;
        }

        private static void AddFolderImages(List<string> paths, string folder)
        {
            string[] patterns = { "*.png", "*.jpg", "*.jpeg" };
            var found = new List<string>();
            try
            {
                for (int i = 0; i < patterns.Length; i++)
                    found.AddRange(Directory.GetFiles(folder, patterns[i]));
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            // Explorer hands a folder over unsorted; the numbers the queue
            // assigns should follow the names the player sees.
            found.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < found.Count; i++)
            {
                if (!paths.Contains(found[i]))
                    paths.Add(found[i]);
            }
        }

        private void RaiseBrowse()
        {
            EventHandler handler = BrowseRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private static Size Fit(Size image, Size box)
        {
            if (image.Width <= 0 || image.Height <= 0 ||
                box.Width <= 0 || box.Height <= 0)
            {
                return new Size(1, 1);
            }

            float scale = Math.Min(
                (float)box.Width / image.Width,
                (float)box.Height / image.Height);
            int width = Math.Max(1, (int)(image.Width * scale));
            int height = Math.Max(1, (int)(image.Height * scale));
            return new Size(width, height);
        }
    }

    /// <summary>
    /// One image waiting to become an 异画: the file the player dropped, the
    /// variant number typed for it and the thumbnail the queue paints. The
    /// thumbnail is scaled once on load instead of on every paint, because the
    /// strip redraws while the mouse moves over it.
    /// </summary>
    internal sealed class AltArtQueueItem : IDisposable
    {
        private const int ThumbnailHeight = 200;

        /// <summary>Image the player dropped.</summary>
        public string SourcePath { get; set; }

        /// <summary>Variant number the game will show, e.g. `AW/S18-E101b`.</summary>
        public string AltId { get; set; }

        /// <summary>Why this image cannot be saved yet, or null while it can.</summary>
        public string Problem { get; set; }

        public Image Thumbnail { get; private set; }

        public AltArtQueueItem(string sourcePath, string altId)
        {
            SourcePath = sourcePath ?? string.Empty;
            AltId = altId ?? string.Empty;
        }

        /// <summary>Card shaped copy of the image, or null when it cannot be read.</summary>
        public void LoadThumbnail()
        {
            DisposeThumbnail();
            if (string.IsNullOrEmpty(SourcePath) || !File.Exists(SourcePath))
                return;

            try
            {
                using (Image source = Image.FromFile(SourcePath))
                {
                    int width = Math.Max(1, (int)Math.Round(
                        source.Width * (double)ThumbnailHeight / source.Height));
                    var bitmap = new Bitmap(width, ThumbnailHeight);
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.InterpolationMode =
                            InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(source, 0, 0, width, ThumbnailHeight);
                    }
                    Thumbnail = bitmap;
                }
            }
            catch (Exception)
            {
                // A broken png fails later with the writer's own message; the
                // queue keeps the row instead of throwing while it paints.
                Thumbnail = null;
            }
        }

        public void DisposeThumbnail()
        {
            if (Thumbnail == null)
                return;

            Thumbnail.Dispose();
            Thumbnail = null;
        }

        public void Dispose()
        {
            DisposeThumbnail();
        }
    }

    /// <summary>
    /// The 添加异画 queue: one card shaped tile per dropped image, numbered in
    /// the order the images were added, a ✕ on the selected tile and a trailing
    /// ＋ tile that opens the file picker. The strip grows sideways, so the
    /// block keeps the height of a single image box while showing a handful of
    /// large thumbnails (~56x79 in the 83px band).
    /// </summary>
    internal sealed class AltArtQueueStrip : Control
    {
        private const int TileGap = 6;
        private const int CornerRadius = 6;
        private const int CloseSize = 13;

        private readonly List<AltArtQueueItem> _items =
            new List<AltArtQueueItem>();
        private int _selected = -1;
        private int _hover = -1;
        private int _firstVisible;
        private bool _addHover;

        public Font BadgeFont { get; set; }
        public Color SelectedColor { get; set; }
        public Color ProblemColor { get; set; }

        public event EventHandler SelectionChanged;
        public event EventHandler RemoveRequested;
        public event EventHandler AddRequested;

        public AltArtQueueStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.Selectable, true);

            BackColor = Color.Transparent;
            TabStop = true;
            Cursor = Cursors.Hand;
            SelectedColor = AppTheme.Accent;
            ProblemColor = AppTheme.Danger;
        }

        public int Count
        {
            get { return _items.Count; }
        }

        public int SelectedIndex
        {
            get { return _selected; }
        }

        public IList<AltArtQueueItem> Items
        {
            get { return _items; }
        }

        /// <summary>
        /// Mirrors the page's queue without raising
        /// <see cref="SelectionChanged"/>, so a click cannot feed back into the
        /// selection it came from.
        /// </summary>
        public void SetItems(IList<AltArtQueueItem> items, int selectedIndex)
        {
            _items.Clear();
            if (items != null)
                _items.AddRange(items);

            _hover = -1;
            _addHover = false;
            _selected = Math.Max(-1, Math.Min(selectedIndex, _items.Count - 1));
            EnsureVisible();
            Invalidate();
        }

        private int TileHeight
        {
            get { return Math.Max(18, ClientSize.Height - 2); }
        }

        /// <summary>Card aspect: the tiles read as small card backs.</summary>
        private int TileWidth
        {
            get { return Math.Max(12, (int)(TileHeight * 0.716)); }
        }

        /// <summary>The images plus the trailing add tile.</summary>
        private int Slots
        {
            get { return _items.Count + 1; }
        }

        private int VisibleSlots
        {
            get
            {
                int stride = TileWidth + TileGap;
                return Math.Max(1,
                    (ClientSize.Width + TileGap) / Math.Max(1, stride));
            }
        }

        private Rectangle SlotBounds(int slot)
        {
            return new Rectangle(
                (slot - _firstVisible) * (TileWidth + TileGap), 1,
                TileWidth, TileHeight);
        }

        private Rectangle CloseBounds(Rectangle tile)
        {
            return new Rectangle(tile.Right - CloseSize - 2, tile.Top + 2,
                CloseSize, CloseSize);
        }

        private int HitTest(Point point)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return -1;

            int stride = TileWidth + TileGap;
            int slot = point.X / Math.Max(1, stride);
            int index = _firstVisible + slot;
            return index >= 0 && index < Slots ? index : -1;
        }

        private void EnsureVisible()
        {
            int visible = VisibleSlots;
            int maximum = Math.Max(0, Slots - visible);
            if (_selected >= 0 && _selected < _firstVisible)
                _firstVisible = _selected;
            else if (_selected >= _firstVisible + visible)
                _firstVisible = _selected - visible + 1;

            _firstVisible = Math.Max(0, Math.Min(_firstVisible, maximum));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            EnsureVisible();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int visible = VisibleSlots;
            for (int slot = 0; slot < visible; slot++)
            {
                int index = _firstVisible + slot;
                if (index >= Slots)
                    break;

                Rectangle bounds = SlotBounds(index);
                if (index < _items.Count)
                    PaintTile(e.Graphics, bounds, index);
                else
                    PaintAddTile(e.Graphics, bounds);
            }
        }

        private void PaintTile(Graphics graphics, Rectangle bounds, int index)
        {
            AltArtQueueItem item = _items[index];
            bool selected = index == _selected;
            bool problem = !string.IsNullOrEmpty(item.Problem);
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(bounds, CornerRadius))
            using (var fill = new SolidBrush(AppTheme.InputBackground))
            {
                graphics.FillPath(fill, path);

                if (item.Thumbnail != null)
                {
                    graphics.SetClip(path);
                    try
                    {
                        graphics.DrawImage(item.Thumbnail,
                            FitRect(item.Thumbnail.Size, bounds));
                    }
                    finally
                    {
                        graphics.ResetClip();
                    }
                }
            }

            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(bounds, CornerRadius))
            using (var pen = new Pen(
                problem ? ProblemColor : (selected ? SelectedColor : AppTheme.Border),
                problem || selected ? 2F : 1.2F))
            {
                graphics.DrawPath(pen, path);
            }

            Font font = BadgeFont ?? Font;
            string number = (index + 1).ToString();
            Size measured = TextRenderer.MeasureText(number, font);
            var badge = new Rectangle(bounds.Left + 2, bounds.Top + 2,
                measured.Width + 2, Math.Max(11, measured.Height - 3));
            using (var back = new SolidBrush(Color.FromArgb(200, AppTheme.InputBackground)))
                graphics.FillRectangle(back, badge);
            TextRenderer.DrawText(graphics, number, font, badge, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);

            if (selected || _hover == index)
            {
                Rectangle close = CloseBounds(bounds);
                using (var back = new SolidBrush(Color.FromArgb(215, AppTheme.InputBackground)))
                    graphics.FillRectangle(back, close);
                TextRenderer.DrawText(graphics, "\u00D7", font, close, Color.White,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private void PaintAddTile(Graphics graphics, Rectangle bounds)
        {
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(bounds, CornerRadius))
            using (var pen = new Pen(
                _addHover ? AppTheme.Accent : AppTheme.Border, 1.2F))
            {
                pen.DashStyle = DashStyle.Dash;
                graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(graphics, "\uFF0B", BadgeFont ?? Font, bounds,
                _addHover ? AppTheme.TextSoft : AppTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }

        private static Rectangle FitRect(Size image, Rectangle box)
        {
            if (image.Width <= 0 || image.Height <= 0 ||
                box.Width <= 0 || box.Height <= 0)
            {
                return box;
            }

            float scale = Math.Min(
                (float)box.Width / image.Width,
                (float)box.Height / image.Height);
            int width = Math.Max(1, (int)(image.Width * scale));
            int height = Math.Max(1, (int)(image.Height * scale));
            return new Rectangle(box.Left + (box.Width - width) / 2,
                box.Top + (box.Height - height) / 2, width, height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int index = HitTest(e.Location);
            int hover = index >= 0 && index < _items.Count ? index : -1;
            bool addHover = index >= 0 && index == _items.Count;
            if (hover == _hover && addHover == _addHover)
                return;

            _hover = hover;
            _addHover = addHover;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover < 0 && !_addHover)
                return;

            _hover = -1;
            _addHover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left)
                return;

            int index = HitTest(e.Location);
            if (index < 0)
                return;

            if (index == _items.Count)
            {
                RaiseAddRequested();
                return;
            }

            if ((_selected == index || _hover == index) &&
                CloseBounds(SlotBounds(index)).Contains(e.Location))
            {
                RaiseRemoveRequested();
                return;
            }

            SelectSilently(index);
            RaiseSelectionChanged();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right ||
                keyData == Keys.Delete)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_items.Count == 0)
                return;

            if (e.KeyCode == Keys.Left && _selected > 0)
            {
                SelectSilently(_selected - 1);
                RaiseSelectionChanged();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right && _selected < _items.Count - 1)
            {
                SelectSilently(_selected + 1);
                RaiseSelectionChanged();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete && _selected >= 0)
            {
                RaiseRemoveRequested();
                e.Handled = true;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_items.Count <= 1)
                return;

            int next = _selected < 0 ? 0 : _selected + (e.Delta > 0 ? -1 : 1);
            next = Math.Max(0, Math.Min(_items.Count - 1, next));
            if (next == _selected)
                return;

            SelectSilently(next);
            RaiseSelectionChanged();
        }

        private void SelectSilently(int index)
        {
            _selected = Math.Max(-1, Math.Min(index, _items.Count - 1));
            EnsureVisible();
            Invalidate();
        }

        private void RaiseSelectionChanged()
        {
            EventHandler handler = SelectionChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void RaiseRemoveRequested()
        {
            EventHandler handler = RemoveRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void RaiseAddRequested()
        {
            EventHandler handler = AddRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}

