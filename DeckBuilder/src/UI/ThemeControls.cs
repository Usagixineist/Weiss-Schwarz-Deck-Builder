using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WeissDeckBuilder.UI
{
    internal sealed class GradientPanel : Panel
    {
        public Color StartColor { get; set; }
        public Color EndColor { get; set; }

        public GradientPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            StartColor = AppTheme.Navigation;
            EndColor = AppTheme.MainBackground;
            BackColor = AppTheme.MainBackground;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            using (var brush = new LinearGradientBrush(
                bounds, StartColor, EndColor, LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }
        }
    }

    internal sealed class RoundedTextBox : UserControl
    {
        private readonly TextBox _input;

        public int CornerRadius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }

        public TextBox Input
        {
            get { return _input; }
        }

        public override string Text
        {
            get { return _input.Text; }
            set { _input.Text = value ?? string.Empty; }
        }

        public RoundedTextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            CornerRadius = 8;
            FillColor = AppTheme.InputBackground;
            BorderColor = AppTheme.Border;
            BackColor = Color.Transparent;

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.None;
            _input.BackColor = FillColor;
            _input.ForeColor = AppTheme.Text;
            _input.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            Controls.Add(_input);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionInput();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0,
                Math.Max(1, ClientSize.Width - 1),
                Math.Max(1, ClientSize.Height - 1));
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(bounds, CornerRadius))
            using (var fill = new SolidBrush(FillColor))
            using (var border = new Pen(BorderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }

        private void PositionInput()
        {
            int height = _input.PreferredHeight;
            int width = Math.Max(10, ClientSize.Width - 24);
            int top = Math.Max(2, (ClientSize.Height - height) / 2);
            _input.SetBounds(12, top, width, height);
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public int CornerRadius { get; set; }
        public Color NormalColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color SelectedColor { get; set; }

        /// <summary>
        /// Colour painted behind the rounded fill. A solid colour keeps the
        /// rounded corners crisp; leaving it empty lets the parent paint the
        /// background like a normally "transparent" button.
        /// </summary>
        public Color SurfaceColor { get; set; }

        public bool Selected { get; set; }

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            CornerRadius = 8;
            NormalColor = AppTheme.Accent;
            HoverColor = AppTheme.AccentHover;
            PressedColor = AppTheme.AccentPressed;
            SelectedColor = AppTheme.Divider;
            SurfaceColor = Color.Empty;
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TabStop = true;

            // A flat button asks Windows to skip background erasing, which
            // leaves stale pixels behind rounded corners.
            SetStyle(ControlStyles.Opaque, false);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (SurfaceColor.IsEmpty)
            {
                base.OnPaintBackground(e);
                return;
            }

            using (var brush = new SolidBrush(SurfaceColor))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        /// <summary>
        /// Fills the control surface before the rounded rectangle is drawn, so
        /// nothing from an earlier paint can shine through the corners.
        /// </summary>
        private void PaintSurface(Graphics graphics)
        {
            Color surface = SurfaceColor;
            if (surface.IsEmpty && Parent != null)
                surface = Parent.BackColor;
            if (surface.IsEmpty || surface.A == 0)
                return;

            using (var brush = new SolidBrush(surface))
            {
                graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled
                ? AppTheme.Border
                : _pressed
                    ? PressedColor
                    : _hover
                        ? HoverColor
                        : Selected
                            ? SelectedColor
                            : NormalColor;

            PaintSurface(e.Graphics);
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(bounds, CornerRadius))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
            }

            Rectangle textBounds = bounds;
            if (Padding.Left != 0 || Padding.Top != 0 ||
                Padding.Right != 0 || Padding.Bottom != 0)
            {
                textBounds = new Rectangle(
                    Padding.Left, Padding.Top,
                    Math.Max(1, ClientSize.Width - Padding.Horizontal),
                    Math.Max(1, ClientSize.Height - Padding.Vertical));
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix;
            switch (TextAlign)
            {
                case ContentAlignment.TopLeft:
                case ContentAlignment.MiddleLeft:
                case ContentAlignment.BottomLeft:
                    flags |= TextFormatFlags.Left;
                    break;
                case ContentAlignment.TopRight:
                case ContentAlignment.MiddleRight:
                case ContentAlignment.BottomRight:
                    flags |= TextFormatFlags.Right;
                    break;
                default:
                    flags |= TextFormatFlags.HorizontalCenter;
                    break;
            }

            Color textColor = !Enabled
                ? AppTheme.Placeholder
                : _hover || _pressed
                    ? Color.White
                    : ForeColor;

            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
                textColor, flags);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressed = false;
            Invalidate();
        }
    }

    internal sealed class FilterCheckBox : CheckBox
    {
        private bool _hover;

        public int BoxSize { get; set; }
        public int TextGap { get; set; }

        public FilterCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            BoxSize = 14;
            TextGap = 9;
            AutoSize = false;
            BackColor = Color.Transparent;
            ForeColor = AppTheme.Text;
            Cursor = Cursors.Hand;
        }

        public int MeasureContentWidth()
        {
            Font font = Font ?? SystemFonts.DefaultFont;
            Size textSize = TextRenderer.MeasureText(Text ?? string.Empty, font);
            return Math.Max(32, BoxSize + TextGap + textSize.Width + 4);
        }

        public int MeasureContentHeight(int width)
        {
            Font font = Font ?? SystemFonts.DefaultFont;
            int textWidth = Math.Max(20, width - BoxSize - TextGap - 4);
            Size textSize = TextRenderer.MeasureText(Text ?? string.Empty,
                font, new Size(textWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return Math.Max(24, textSize.Height + 8);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaintBackground(pevent);
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pevent.Graphics.TextRenderingHint =
                System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var box = new Rectangle(1, (Height - BoxSize) / 2,
                BoxSize, BoxSize);
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(box, 4))
            using (var fill = new SolidBrush(Checked
                ? AppTheme.Accent
                : AppTheme.InputBackground))
            using (var border = new Pen(Checked
                ? AppTheme.AccentHover
                : _hover
                    ? AppTheme.TextMuted
                    : AppTheme.Border))
            {
                pevent.Graphics.FillPath(fill, path);
                pevent.Graphics.DrawPath(border, path);
            }

            if (Checked)
            {
                using (var check = new Pen(Color.White, 2F))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    pevent.Graphics.DrawLines(check, new[]
                    {
                        new Point(box.Left + 3, box.Top + 7),
                        new Point(box.Left + 6, box.Top + 10),
                        new Point(box.Left + 11, box.Top + 4)
                    });
                }
            }

            var textBounds = new Rectangle(
                box.Right + TextGap, 0,
                Math.Max(1, Width - box.Right - TextGap), Height);
            TextRenderer.DrawText(pevent.Graphics, Text ?? string.Empty,
                Font, textBounds, Enabled ? ForeColor : AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }
    }

    internal sealed class DarkScrollPanel : Panel
    {
        private const int ScrollbarWidth = 8;
        private const int ScrollbarMargin = 3;
        private const int WheelStep = 56;

        private Control _content;
        private int _offset;
        private bool _dragging;
        private int _dragStartY;
        private int _dragStartOffset;
        private Rectangle _thumbBounds;
        private int _scrollbarReserve;

        public Control Content
        {
            get { return _content; }
            set
            {
                if (_content == value)
                    return;
                if (_content != null)
                {
                    _content.SizeChanged -= ContentSizeChanged;
                    Controls.Remove(_content);
                }

                _content = value;
                if (_content != null)
                {
                    _content.Dock = DockStyle.None;
                    _content.SizeChanged += ContentSizeChanged;
                    Controls.Add(_content);
                }

                _offset = 0;
                UpdateScrollState();
            }
        }

        public DarkScrollPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void ScrollToTop()
        {
            SetOffset(0);
        }

        /// <summary>
        /// Moves the content by one wheel notch. Returns false when the host has
        /// nothing to scroll, so the caller can let the gesture travel on.
        /// </summary>
        public bool ScrollByWheel(int delta)
        {
            if (delta == 0 || MaxOffset <= 0)
                return false;

            int steps = delta / 120;
            if (steps == 0)
                steps = delta > 0 ? 1 : -1;
            SetOffset(_offset - steps * WheelStep);
            return true;
        }

        /// <summary>Current vertical offset of <see cref="Content"/>.</summary>
        public int ScrollOffset
        {
            get { return _offset; }
        }

        /// <summary>
        /// Width kept clear on the right edge of <see cref="Content"/> for the
        /// slim scrollbar. The bar belongs to this host and is painted under
        /// the children, so content that fills the full width hides it and the
        /// page looks like it cannot scroll at all. Zero (the default) keeps
        /// the full width layout the other hosts rely on.
        /// </summary>
        public int ScrollbarReserve
        {
            get { return _scrollbarReserve; }
            set
            {
                int clamped = Math.Max(0, value);
                if (_scrollbarReserve == clamped)
                    return;

                _scrollbarReserve = clamped;
                // The content width follows the reserve, so lay the host out
                // again instead of waiting for the next resize.
                PerformLayout();
                Invalidate();
            }
        }

        /// <summary>
        /// Raised when <see cref="ScrollOffset"/> changed. A host uses it to
        /// keep pinned captions in place while the content moves.
        /// </summary>
        public event EventHandler ScrollOffsetChanged;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutContent();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateScrollState();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_content == null || MaxOffset <= 0)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = GetTrackBounds();
            using (var trackBrush = new SolidBrush(Color.FromArgb(22, 31, 46)))
                e.Graphics.FillRectangle(trackBrush, track);

            _thumbBounds = GetThumbBounds(track);
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(_thumbBounds, 4))
            using (var thumbBrush = new SolidBrush(
                _dragging ? AppTheme.TextMuted : Color.FromArgb(71, 85, 105)))
            {
                e.Graphics.FillPath(thumbBrush, path);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (ScrollByWheel(e.Delta))
            {
                // The notch is spent here: Windows would otherwise hand the
                // same gesture to the parent form as well and the page doubles.
                var handled = e as HandledMouseEventArgs;
                if (handled != null)
                    handled.Handled = true;
                return;
            }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (MaxOffset <= 0 || e.Button != MouseButtons.Left)
                return;

            Rectangle track = GetTrackBounds();
            _thumbBounds = GetThumbBounds(track);
            if (_thumbBounds.Contains(e.Location))
            {
                _dragging = true;
                _dragStartY = e.Y;
                _dragStartOffset = _offset;
                Capture = true;
            }
            else if (track.Contains(e.Location))
            {
                SetOffset(_offset +
                    (e.Y < _thumbBounds.Top
                        ? -ClientSize.Height
                        : ClientSize.Height));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || MaxOffset <= 0)
                return;

            Rectangle track = GetTrackBounds();
            int thumbHeight = Math.Max(28, _thumbBounds.Height);
            int travel = Math.Max(1, track.Height - thumbHeight);
            int delta = e.Y - _dragStartY;
            SetOffset(_dragStartOffset + delta * MaxOffset / travel);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                Invalidate();
            }
        }

        private void ContentSizeChanged(object sender, EventArgs e)
        {
            UpdateScrollState();
        }

        private int MaxOffset
        {
            get
            {
                if (_content == null)
                    return 0;
                return Math.Max(0, _content.Height - ClientSize.Height);
            }
        }

        private void LayoutContent()
        {
            if (_content == null)
                return;

            _content.Width = Math.Max(1, ClientSize.Width - _scrollbarReserve);
            _content.Top = -_offset;
            _content.Left = 0;
            UpdateScrollState();
        }

        private void UpdateScrollState()
        {
            int maxOffset = MaxOffset;
            bool clamped = false;
            if (_offset > maxOffset)
            {
                _offset = maxOffset;
                clamped = true;
            }
            if (_content != null)
                _content.Top = -_offset;
            Invalidate();
            if (clamped)
                OnScrollOffsetChanged();
        }

        private void SetOffset(int value)
        {
            int clamped = Math.Max(0, Math.Min(MaxOffset, value));
            if (clamped == _offset)
                return;

            _offset = clamped;
            if (_content != null)
                _content.Top = -_offset;
            Invalidate();
            OnScrollOffsetChanged();
        }

        private void OnScrollOffsetChanged()
        {
            EventHandler handler = ScrollOffsetChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private Rectangle GetTrackBounds()
        {
            return new Rectangle(
                Math.Max(0,
                    ClientSize.Width - ScrollbarWidth - ScrollbarMargin),
                4,
                ScrollbarWidth,
                Math.Max(1, ClientSize.Height - 8));
        }

        private Rectangle GetThumbBounds(Rectangle track)
        {
            int contentHeight = Math.Max(1, _content.Height);
            int viewportHeight = Math.Max(1, ClientSize.Height);
            int thumbHeight = Math.Max(28,
                track.Height * viewportHeight / contentHeight);
            thumbHeight = Math.Min(track.Height, thumbHeight);
            int travel = Math.Max(1, track.Height - thumbHeight);
            int top = track.Top +
                (MaxOffset == 0 ? 0 : travel * _offset / MaxOffset);
            return new Rectangle(track.Left, top, track.Width, thumbHeight);
        }
    }

    internal sealed class FilterRadioButton : RadioButton
    {
        private bool _hover;

        public int CircleSize { get; set; }
        public int TextGap { get; set; }

        public FilterRadioButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            CircleSize = 14;
            TextGap = 8;
            AutoSize = false;
            BackColor = Color.Transparent;
            ForeColor = AppTheme.Text;
            Cursor = Cursors.Hand;
        }

        public int MeasureContentWidth()
        {
            Font font = Font ?? SystemFonts.DefaultFont;
            Size textSize = TextRenderer.MeasureText(Text ?? string.Empty, font);
            return Math.Max(32, CircleSize + TextGap + textSize.Width + 4);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaintBackground(pevent);
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pevent.Graphics.TextRenderingHint =
                System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var circle = new Rectangle(1, (Height - CircleSize) / 2,
                CircleSize, CircleSize);
            using (var fill = new SolidBrush(Checked
                ? AppTheme.Accent
                : AppTheme.InputBackground))
            using (var border = new Pen(Checked
                ? AppTheme.AccentHover
                : _hover
                    ? AppTheme.TextMuted
                    : AppTheme.Border))
            {
                pevent.Graphics.FillEllipse(fill, circle);
                pevent.Graphics.DrawEllipse(border, circle);
            }

            if (Checked)
            {
                var dot = new Rectangle(circle.Left + 4, circle.Top + 4,
                    Math.Max(2, CircleSize - 8), Math.Max(2, CircleSize - 8));
                using (var dotBrush = new SolidBrush(Color.White))
                    pevent.Graphics.FillEllipse(dotBrush, dot);
            }

            var textBounds = new Rectangle(
                circle.Right + TextGap, 0,
                Math.Max(1, Width - circle.Right - TextGap), Height);
            TextRenderer.DrawText(pevent.Graphics, Text ?? string.Empty,
                Font, textBounds, Enabled ? ForeColor : AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }
    }

    /// <summary>
    /// Square icon-only button used by the sidebar. The picture is painted with
    /// GDI primitives (a gear) so the toolbar needs no image assets.
    /// </summary>
    internal sealed class GearButton : Button
    {
        private const int ToothCount = 8;

        private bool _hover;
        private bool _pressed;

        public int CornerRadius { get; set; }
        public int IconSize { get; set; }
        public int IconTextGap { get; set; }
        public bool ShowText { get; set; }
        public bool IsActive { get; set; }
        public Color SurfaceColor { get; set; }
        public Color NormalColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color ActiveColor { get; set; }

        public GearButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            CornerRadius = 10;
            IconSize = 20;
            IconTextGap = 10;
            ShowText = false;
            IsActive = false;
            SurfaceColor = Color.Empty;
            NormalColor = Color.Transparent;
            HoverColor = Color.FromArgb(55, 65, 81);
            PressedColor = Color.FromArgb(75, 85, 99);
            ActiveColor = Color.FromArgb(45, 57, 75);
            BackColor = Color.Transparent;
            ForeColor = AppTheme.TextSoft;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TabStop = true;

            // Flat buttons skip background erasing by default; doing it here
            // keeps the gear area free of leftover pixels.
            SetStyle(ControlStyles.Opaque, false);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (SurfaceColor.IsEmpty)
            {
                base.OnPaintBackground(e);
                return;
            }

            using (var brush = new SolidBrush(SurfaceColor))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        private void PaintSurface(Graphics graphics)
        {
            Color surface = SurfaceColor;
            if (surface.IsEmpty && Parent != null)
                surface = Parent.BackColor;
            if (surface.IsEmpty || surface.A == 0)
                return;

            using (var brush = new SolidBrush(surface))
            {
                graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            PaintSurface(e.Graphics);

            Color fill = !Enabled
                ? Color.Transparent
                : _pressed
                    ? PressedColor
                    : _hover
                        ? HoverColor
                        : IsActive
                            ? ActiveColor
                            : NormalColor;

            if (fill != Color.Transparent)
            {
                using (GraphicsPath path =
                    AppTheme.CreateRoundedRectangle(bounds, CornerRadius))
                using (var brush = new SolidBrush(fill))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            Color iconColor = !Enabled
                ? AppTheme.Placeholder
                : _hover || _pressed || IsActive
                    ? Color.White
                    : AppTheme.TextSoft;

            Rectangle iconBounds = bounds;
            Rectangle textBounds = Rectangle.Empty;
            if (ShowText && !string.IsNullOrEmpty(Text))
            {
                iconBounds = new Rectangle(
                    bounds.Left + Padding.Left,
                    bounds.Top,
                    Math.Min(IconSize + 10, bounds.Width),
                    bounds.Height);

                int textLeft = iconBounds.Right + IconTextGap;
                int textWidth = bounds.Right - textLeft - Padding.Right;
                if (textWidth > 4)
                {
                    textBounds = new Rectangle(
                        textLeft, bounds.Top, textWidth, bounds.Height);
                }
            }

            DrawGear(e.Graphics, iconBounds, iconColor);

            if (textBounds != Rectangle.Empty)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
                    iconColor, TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.Left);
            }
        }

        private void DrawGear(Graphics graphics, Rectangle bounds, Color color)
        {
            float size = Math.Min(IconSize,
                Math.Min(bounds.Width, bounds.Height) - 8);
            if (size < 8F)
                return;

            float centerX = bounds.Left + bounds.Width / 2F;
            float centerY = bounds.Top + bounds.Height / 2F;
            float outer = size / 2F;
            float ring = outer * 0.62F;

            using (var ringPen = new Pen(color, 1.7F))
            using (var toothPen = new Pen(color, 2.4F))
            {
                toothPen.StartCap = LineCap.Round;
                toothPen.EndCap = LineCap.Round;

                for (int i = 0; i < ToothCount; i++)
                {
                    double angle = Math.PI * 2D * i / ToothCount;
                    float cos = (float)Math.Cos(angle);
                    float sin = (float)Math.Sin(angle);
                    graphics.DrawLine(toothPen,
                        centerX + cos * ring,
                        centerY + sin * ring,
                        centerX + cos * outer,
                        centerY + sin * outer);
                }

                graphics.DrawEllipse(ringPen,
                    centerX - ring, centerY - ring, ring * 2F, ring * 2F);

                float hub = ring * 0.34F;
                graphics.DrawEllipse(ringPen,
                    centerX - hub, centerY - hub, hub * 2F, hub * 2F);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressed = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }
    }
}
