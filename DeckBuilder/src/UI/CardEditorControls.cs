using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WeissDeckBuilder.Data;

namespace WeissDeckBuilder.UI
{
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
            DarkModeHelper.Apply(_input.Handle);
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
    internal sealed class ChipRow : FlowLayoutPanel
    {
        private readonly List<RoundedButton> _chips = new List<RoundedButton>();
        private readonly Font _font;
        private readonly Color _surface;
        private readonly Color _active;
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
        public string Path { get; private set; }

        public CardImageDroppedEventArgs(string path)
        {
            Path = path ?? string.Empty;
        }
    }

    /// <summary>
    /// The 卡图 box of the editor: shows the art scaled inside a card shaped
    /// frame, takes an image dragged from Explorer and opens a file picker on
    /// click or Space / Enter.
    /// </summary>
    internal sealed class CardImageDropBox : Control
    {
        private Image _image;
        private string _hint = string.Empty;
        private bool _hover;

        public Font CaptionFont { get; set; }
        public Font HintFont { get; set; }
        public Color SurfaceColor { get; set; }

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
            using (var fill = new SolidBrush(Color.FromArgb(16, 24, 38)))
            using (var pen = new Pen(_hover ? AppTheme.Accent : AppTheme.Border,
                _hover ? 2F : 1.4F))
            {
                pen.DashStyle = _image == null ? DashStyle.Dash : DashStyle.Solid;
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }

            bool hasCaption = CaptionFont != null;
            if (hasCaption)
            {
                var caption = new Rectangle(14, 7,
                    Math.Max(1, ClientSize.Width - 28), 16);
                TextRenderer.DrawText(e.Graphics, "卡图", CaptionFont, caption,
                    AppTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }

            var inner = new Rectangle(
                bounds.Left + 12,
                bounds.Top + (hasCaption ? 26 : 12),
                Math.Max(1, bounds.Width - 24),
                Math.Max(1, bounds.Height - (hasCaption ? 38 : 24)));
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
            bool supported = GetImagePath(drgevent.Data) != null;
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
            drgevent.Effect = GetImagePath(drgevent.Data) != null
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

            string path = GetImagePath(drgevent.Data);
            if (path == null)
                return;

            EventHandler<CardImageDroppedEventArgs> handler = FileDropped;
            if (handler != null)
                handler(this, new CardImageDroppedEventArgs(path));
        }

        private void RaiseBrowse()
        {
            EventHandler handler = BrowseRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private static string GetImagePath(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
                return null;

            string[] files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null)
                return null;

            for (int i = 0; i < files.Length; i++)
            {
                if (CardDataWriter.IsSupportedImage(files[i]))
                    return files[i];
            }
            return null;
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
}
