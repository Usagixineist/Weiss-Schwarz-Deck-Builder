using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using WeissDeckBuilder.Data;

namespace WeissDeckBuilder.UI
{
    /// <summary>Right click on one card of the new deck tray.</summary>
    public sealed class NewDeckCardEventArgs : EventArgs
    {
        public string Code { get; private set; }
        public int Count { get; private set; }

        public NewDeckCardEventArgs(string code, int count)
        {
            Code = code ?? string.Empty;
            Count = count;
        }
    }

    /// <summary>Buttons of the action band above the tray.</summary>
    public enum NewDeckAction
    {
        Clear = 0,
        Tidy = 1,
        Stash = 2,
        Save = 3,
        Import = 4
    }

    public sealed class NewDeckActionEventArgs : EventArgs
    {
        public NewDeckAction Action { get; private set; }

        public NewDeckActionEventArgs(NewDeckAction action)
        {
            Action = action;
        }
    }

    /// <summary>Deck name typed into the tab of the tray.</summary>
    public sealed class NewDeckNameEventArgs : EventArgs
    {
        public string Name { get; private set; }

        public NewDeckNameEventArgs(string name)
        {
            Name = name ?? string.Empty;
        }
    }

    /// <summary>
    /// Tray pinned under the card grid of 编辑卡组. The body is a little taller
    /// than one card image and the left half of its top edge rises by one title
    /// line to carry the deck name — the raised tab a physical deck box has.
    /// The tab is editable in place, the copy counter and the action buttons
    /// share the same band, and the blue handle in the middle of the band pulls
    /// the tray up to about two and a half rows of cards: the cards then move
    /// into a six column list on the left and the 卡组信息 numbers of 查看卡组
    /// fill a divided pane on the right. Holding a card of the tray opens its
    /// details, right clicking one takes a copy out.
    /// </summary>
    internal sealed class NewDeckStrip : Control
    {
        // The tab carries the deck name in the same 16 pt title font the
        // content pages use. The line box of that font is 22 px tall, so 40 px
        // of tab leave the same gap above the title as the ink inset keeps on
        // its left side.
        internal const int TabHeight = 40;
        internal const int BodyHeight = 204;
        internal const int TotalHeight = TabHeight + BodyHeight;

        // Pulled up the tray shows two and a half rows of the 188 px cards of
        // the compact row plus the padding around them.
        internal const int ExpandedBodyHeight = 486;
        internal const int ExpandedTotalHeight = TabHeight + ExpandedBodyHeight;

        private const int BodyRadius = 10;
        private const int TabRadius = 8;
        private const int BodyPadding = 8;
        private const int CardGap = 6;
        private const int CardRadius = 8;
        private const int CardRatioWidth = 63;
        private const int CardRatioHeight = 88;
        private const int ExpandedColumns = 6;
        private const int StatsWidth = 268;
        private const int StatsMinWidth = 190;
        private const int StatsReservedWidth = 220;
        private const int StatsGap = 14;
        private const int ButtonHeight = 22;
        private const int ButtonGap = 6;
        private const int ButtonPadding = 12;
        private const int BandPadding = 14;
        private const int CountGap = 12;
        private const int ToggleWidth = 58;
        private const int ToggleHeight = 20;

        // A 16 pt Chinese glyph carries about four pixels of left bearing, so
        // the text rectangle starts that far left of the visual inset: the
        // title ink then keeps the same gap to the left border that the centred
        // line leaves above it.
        private const int NameInkInset = 9;
        private const int NameBearing = 4;

        // Held cards open their details after the same delay as the card grid.
        private const int LongPressDelay = 550;

        private static readonly Color ClearFill = AppTheme.Danger;
        private static readonly Color ClearHoverFill = Color.FromArgb(239, 68, 68);
        private static readonly Color SaveFill = AppTheme.Accent;
        private static readonly Color SaveHoverFill = AppTheme.AccentHover;

        private static readonly NewDeckAction[] BandActions =
        {
            NewDeckAction.Import,
            NewDeckAction.Clear,
            NewDeckAction.Tidy,
            NewDeckAction.Save
        };

        private struct BandButton
        {
            public NewDeckAction Action;
            public string Text;
            public Rectangle Bounds;
        }

        private readonly List<DeckDraftCard> _cards;
        private readonly List<BandButton> _buttons;
        private readonly Font _nameFont;
        private readonly Font _hintFont;
        private readonly Font _countFont;
        private readonly Font _buttonFont;
        private readonly StringFormat _leftFormat;
        private readonly StringFormat _rightFormat;
        private readonly StringFormat _centeredFormat;
        private readonly ToolTip _toolTip;
        private readonly DeckNameBox _nameEditor;
        private readonly DeckStatsPanel _statsPanel;
        private readonly Timer _pressTimer;

        private string _deckName;
        private int _totalCount;
        private int _maxCount;
        private bool _expanded;
        private int _scrollOffset;
        private int _scrollTop;
        private int _hoverIndex;
        private NewDeckAction? _hoverAction;
        private NewDeckAction? _pressedAction;
        private bool _hoverToggle;
        private bool _hoverName;
        private bool _editingName;
        private int _pressIndex;
        private Point _pressPoint;
        private bool _longPressFired;
        private string _toolTipText;

        public NewDeckStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            UpdateStyles();

            TabStop = true;
            BackColor = AppTheme.MainBackground;
            _cards = new List<DeckDraftCard>();
            _buttons = new List<BandButton>();
            _deckName = DeckDraft.DefaultName;
            _maxCount = DeckDraft.MaxCards;
            _hoverIndex = -1;
            _pressIndex = -1;
            _toolTipText = string.Empty;

            _nameFont = new Font("Microsoft YaHei UI", 16F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _hintFont = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _countFont = new Font("Microsoft YaHei UI", 10F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _buttonFont = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);

            _leftFormat = new StringFormat();
            _leftFormat.Alignment = StringAlignment.Near;
            _leftFormat.LineAlignment = StringAlignment.Center;
            _leftFormat.Trimming = StringTrimming.EllipsisCharacter;
            _leftFormat.FormatFlags = StringFormatFlags.NoWrap;
            _rightFormat = new StringFormat();
            _rightFormat.Alignment = StringAlignment.Far;
            _rightFormat.LineAlignment = StringAlignment.Center;
            _rightFormat.Trimming = StringTrimming.EllipsisCharacter;
            _rightFormat.FormatFlags = StringFormatFlags.NoWrap;
            _centeredFormat = new StringFormat();
            _centeredFormat.Alignment = StringAlignment.Center;
            _centeredFormat.LineAlignment = StringAlignment.Center;
            _centeredFormat.Trimming = StringTrimming.EllipsisCharacter;
            _centeredFormat.FormatFlags = StringFormatFlags.NoWrap;
            _toolTip = new ToolTip();

            // The deck name is edited in place: a borderless box on the tab,
            // created up front and only shown while the tab is being edited.
            _nameEditor = new DeckNameBox();
            _nameEditor.BorderStyle = BorderStyle.None;
            _nameEditor.BackColor = AppTheme.TileBackground;
            _nameEditor.ForeColor = AppTheme.Text;
            _nameEditor.Font = _nameFont;
            _nameEditor.Visible = false;
            _nameEditor.CommitRequested += delegate { EndNameEdit(true); };
            _nameEditor.CancelRequested += delegate { EndNameEdit(false); };
            _nameEditor.Leave += NameEditor_Leave;
            Controls.Add(_nameEditor);

            // The pulled up tray reuses the very panel of 查看卡组, so both
            // places print the same 卡组信息 numbers.
            _statsPanel = new DeckStatsPanel();
            _statsPanel.BackColor = AppTheme.TileBackground;
            _statsPanel.Visible = false;
            _statsPanel.SetStatistics(null);
            Controls.Add(_statsPanel);

            // Holding a card of the tray opens its details with the same delay
            // the card grid uses, so both gestures feel like one feature.
            _pressTimer = new Timer();
            _pressTimer.Interval = LongPressDelay;
            _pressTimer.Tick += PressTimer_Tick;

            AccessibleName = "新卡组栏位";
        }

        /// <summary>
        /// Resolves the image of a card code. The tray only stores codes, the
        /// page knows which file belongs to them.
        /// </summary>
        public Func<string, string> ImagePathProvider { get; set; }

        public ThumbnailCache Thumbnails { get; set; }

        /// <summary>Raised when a card of the tray is right clicked.</summary>
        public event EventHandler<NewDeckCardEventArgs> CardRemoveRequested;

        /// <summary>
        /// Raised when a card of the tray is clicked with the left button: one
        /// more copy of it joins the new deck, the gesture the card grid
        /// already uses.
        /// </summary>
        public event EventHandler<NewDeckCardEventArgs> CardAddRequested;

        /// <summary>
        /// Raised when a card of the tray is held down, which asks the page for
        /// the card detail — the gesture the card grid already uses.
        /// </summary>
        public event EventHandler<NewDeckCardEventArgs> CardDetailRequested;

        /// <summary>Raised by the 导入 / 清空 / 整理 / 暂存·保存 band.</summary>
        public event EventHandler<NewDeckActionEventArgs> ActionRequested;

        /// <summary>Raised after the deck name was typed into the tab.</summary>
        public event EventHandler<NewDeckNameEventArgs> DeckNameChanged;

        /// <summary>Raised when the tray is pulled up or folded back.</summary>
        public event EventHandler ExpandedChanged;

        /// <summary>True while the tray is pulled up to show the whole deck.</summary>
        public bool Expanded
        {
            get { return _expanded; }
            set
            {
                if (_expanded == value)
                    return;

                _expanded = value;
                _scrollOffset = 0;
                _scrollTop = 0;
                _hoverIndex = -1;
                ClampScroll();
                LayoutBand();
                LayoutChildren();
                UpdateAccessibleDescription();
                Invalidate();

                EventHandler handler = ExpandedChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Height the row of the browse page has to reserve for the tray. The
        /// page keeps the row in step with <see cref="Expanded"/>.
        /// </summary>
        public int DesiredHeight
        {
            get { return _expanded ? ExpandedTotalHeight : TotalHeight; }
        }

        /// <summary>
        /// Replaces the painted content. Called after every add / remove, so
        /// the tray always mirrors <see cref="DeckDraft"/>.
        /// </summary>
        public void SetCards(string deckName, IList<DeckDraftCard> cards,
            int totalCount, int maxCount)
        {
            _deckName = string.IsNullOrEmpty(deckName)
                ? DeckDraft.DefaultName
                : deckName;
            if (_editingName)
                EndNameEdit(false);
            CancelLongPress();

            _cards.Clear();
            if (cards != null)
                _cards.AddRange(cards);
            _totalCount = Math.Max(0, totalCount);
            _maxCount = maxCount <= 0 ? DeckDraft.MaxCards : maxCount;
            _hoverIndex = -1;
            ClampScroll();
            LayoutBand();
            LayoutChildren();
            UpdateAccessibleDescription();
            Invalidate();
        }

        /// <summary>
        /// Numbers of the divided pane. Null paints the empty tables, which is
        /// what a fresh deck shows.
        /// </summary>
        public void SetStatistics(DeckStatistics statistics)
        {
            if (_statsPanel == null)
                return;

            _statsPanel.SetStatistics(statistics);
            LayoutChildren();
            Invalidate();
        }

        /// <summary>
        /// Chinese names of the triggers, forwarded to the 卡组信息 tables so the
        /// strip and the deck sidebar always agree.
        /// </summary>
        public TriggerGlossary Glossary
        {
            get { return _statsPanel == null ? null : _statsPanel.Glossary; }
            set
            {
                if (_statsPanel == null)
                    return;

                _statsPanel.Glossary = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Repaints the tables after the shared glossary was edited in place.
        /// </summary>
        public void RefreshTriggerNames()
        {
            if (_statsPanel == null)
                return;

            _statsPanel.RefreshNames();
            Invalidate();
        }


        /// <summary>Height of one card image of the compact row.</summary>
        private int CardHeight
        {
            get { return Math.Max(1, BodyHeight - BodyPadding * 2); }
        }

        private int CardWidth
        {
            get
            {
                return Math.Max(1, (int)Math.Round(
                    CardHeight * (double)CardRatioWidth / CardRatioHeight));
            }
        }

        private string CountText
        {
            get { return _totalCount + " / " + _maxCount + " 张"; }
        }

        /// <summary>
        /// True when the tray holds exactly the 50 张 a deck should have. Only
        /// then the shared button turns into 保存; every other count (too few
        /// or too many) can only be staged.
        /// </summary>
        private bool IsComplete
        {
            get { return _totalCount == _maxCount; }
        }

        /// <summary>
        /// Counter colour: 50 张 is the size a deck should have, so the number
        /// turns green exactly there and red as soon as the tray holds more.
        /// </summary>
        private Color CountColor
        {
            get
            {
                if (_totalCount > _maxCount)
                    return AppTheme.Danger;
                if (_totalCount > 0 && _totalCount == _maxCount)
                    return AppTheme.Success;
                return AppTheme.TextMuted;
            }
        }

        private string HintText
        {
            get
            {
                return _expanded
                    ? "左键加一张 · 长按查看信息 · 右键移除 · 点上方箭头收起"
                    : "左键点击卡片加入新卡组 · 长按查看卡片信息 · 右键移除一张";
            }
        }

        private Rectangle BodyRectangle
        {
            get
            {
                return new Rectangle(0, TabHeight, Width,
                    Math.Max(0, Height - TabHeight));
            }
        }

        private Rectangle InteriorRectangle
        {
            get
            {
                Rectangle body = BodyRectangle;
                return new Rectangle(
                    body.X + BodyPadding,
                    body.Y + BodyPadding,
                    Math.Max(0, body.Width - BodyPadding * 2),
                    Math.Max(0, body.Height - BodyPadding * 2));
            }
        }

        /// <summary>Width of the divided 卡组信息 pane, 0 while folded.</summary>
        private int StatsPaneWidth
        {
            get
            {
                if (!_expanded)
                    return 0;

                int available = InteriorRectangle.Width;
                int width = Math.Min(StatsWidth, Math.Max(StatsMinWidth, Width / 4));
                return Math.Max(0, Math.Min(width, available - StatsReservedWidth));
            }
        }

        /// <summary>Smaller left area that holds the six column card list.</summary>
        private Rectangle CardsRectangle
        {
            get
            {
                Rectangle interior = InteriorRectangle;
                int statsWidth = StatsPaneWidth;
                if (statsWidth <= 0)
                    return interior;

                return new Rectangle(interior.X, interior.Y,
                    Math.Max(0, interior.Width - statsWidth - StatsGap),
                    interior.Height);
            }
        }

        private Rectangle StatsRectangle
        {
            get
            {
                int width = StatsPaneWidth;
                if (width <= 0)
                    return Rectangle.Empty;

                Rectangle interior = InteriorRectangle;
                return new Rectangle(interior.Right - width, interior.Y,
                    width, interior.Height);
            }
        }


        /// <summary>
        /// Size of one cell of the pulled up six column list. The width follows
        /// the pane; the height keeps the printed card ratio but is capped so
        /// the tray never shows less than about two and a half rows.
        /// </summary>
        private void MeasureCells(out int width, out int height)
        {
            Rectangle area = CardsRectangle;
            int gaps = (ExpandedColumns - 1) * CardGap;
            width = Math.Max(1, (area.Width - gaps) / ExpandedColumns);
            height = Math.Max(1, (int)Math.Round(
                width * (double)CardRatioHeight / CardRatioWidth));

            int maxHeight = Math.Max(1, (int)Math.Round(
                (area.Height - CardGap * 2) / 2.5));
            if (height <= maxHeight)
                return;

            height = maxHeight;
            width = Math.Max(1, (int)Math.Round(
                height * (double)CardRatioWidth / CardRatioHeight));
        }

        private int CellHeight
        {
            get
            {
                int width;
                int height;
                MeasureCells(out width, out height);
                return height;
            }
        }

        /// <summary>Width of the compact row of cards.</summary>
        private int ContentWidth
        {
            get
            {
                if (_cards.Count == 0)
                    return 0;
                return _cards.Count * CardWidth +
                    (_cards.Count - 1) * CardGap;
            }
        }

        /// <summary>Height of the six column list of cards.</summary>
        private int ContentHeight
        {
            get
            {
                if (_cards.Count == 0)
                    return 0;

                int width;
                int height;
                MeasureCells(out width, out height);
                int rows = (_cards.Count + ExpandedColumns - 1) /
                    ExpandedColumns;
                return rows * height + (rows - 1) * CardGap;
            }
        }

        private Rectangle GetCardRectangle(int index)
        {
            Rectangle area = CardsRectangle;
            if (!_expanded)
            {
                return new Rectangle(
                    area.X + index * (CardWidth + CardGap) - _scrollOffset,
                    area.Y, CardWidth, CardHeight);
            }

            int cellWidth;
            int cellHeight;
            MeasureCells(out cellWidth, out cellHeight);
            int column = index % ExpandedColumns;
            int row = index / ExpandedColumns;
            return new Rectangle(
                area.X + column * (cellWidth + CardGap),
                area.Y + row * (cellHeight + CardGap) - _scrollTop,
                cellWidth, cellHeight);
        }


        /// <summary>
        /// Width of the raised name tab. It takes at least half of the tray and
        /// always reaches past the pull handle, so the handle sits on the tab
        /// instead of floating on the action band.
        /// </summary>
        private int TabWidth
        {
            get
            {
                int bandLeft = _buttons.Count > 0
                    ? _buttons[0].Bounds.X
                    : Width;
                int overHandle = ToggleRectangle.Right + NameInkInset;
                int width = Math.Max(Width / 2, overHandle);
                int limit = Math.Max(0, bandLeft - ButtonGap * 2);
                return Math.Max(0, Math.Min(width, Math.Min(limit, Width)));
            }
        }

        /// <summary>
        /// Text area of the tab. The rectangle starts a bearing to the left of
        /// the visual inset so the ink of the title keeps the same gap to the
        /// left border that the centred line leaves above it, and it stops
        /// short of the pull handle that now sits on the tab.
        /// </summary>
        private Rectangle NameRectangle
        {
            get
            {
                int left = NameInkInset - NameBearing;
                int right = Math.Max(left + 1, TabWidth - NameInkInset);
                Rectangle toggle = ToggleRectangle;
                if (toggle.Left < right)
                    right = Math.Max(left + 1, toggle.Left - NameInkInset);

                return new Rectangle(left, 0, right - left, TabHeight);
            }
        }

        private Rectangle ToggleRectangle
        {
            get
            {
                int left = Math.Max(0, (Width - ToggleWidth) / 2);
                int top = Math.Max(0, (TabHeight - ToggleHeight) / 2);
                return new Rectangle(left, top, ToggleWidth, ToggleHeight);
            }
        }

        private Rectangle EditorRectangle
        {
            get
            {
                Rectangle name = NameRectangle;
                int height = _nameEditor.PreferredHeight;
                return new Rectangle(name.X,
                    Math.Max(0, (TabHeight - height) / 2),
                    Math.Max(60, name.Width), height);
            }
        }

        /// <summary>
        /// Lays out the action band: 导入 清空 整理 暂存/保存 sit right aligned
        /// in front of the copy counter, the pull handle sits in the middle of
        /// the top edge.
        /// </summary>
        private void LayoutBand()
        {
            _buttons.Clear();

            Size countSize = TextRenderer.MeasureText(CountText, _countFont,
                Size.Empty, TextFormatFlags.NoPadding);
            int cursor = Width - BandPadding - countSize.Width - CountGap;
            int top = Math.Max(0, (TabHeight - ButtonHeight) / 2);

            for (int i = BandActions.Length - 1; i >= 0; i--)
            {
                string text = ActionText(BandActions[i]);
                Size size = TextRenderer.MeasureText(text, _buttonFont,
                    Size.Empty, TextFormatFlags.NoPadding);
                int width = size.Width + ButtonPadding * 2;
                cursor -= width;

                var button = new BandButton();
                button.Action = BandActions[i];
                button.Text = text;
                button.Bounds = new Rectangle(cursor, top, width, ButtonHeight);
                _buttons.Insert(0, button);

                cursor -= ButtonGap;
            }
        }

        private string ActionText(NewDeckAction action)
        {
            switch (action)
            {
                case NewDeckAction.Import:
                    return "导入";
                case NewDeckAction.Clear:
                    return "清空";
                case NewDeckAction.Tidy:
                    return "整理";
                case NewDeckAction.Stash:
                    return "暂存";
                default:
                    // One button for both: a deck that is not 50 张 yet can
                    // only be staged, a finished one is saved as a deck.
                    return IsComplete ? "保存" : "暂存";
            }
        }

        /// <summary>
        /// Places the two child controls: the 卡组信息 pane of the pulled up
        /// tray and the inline deck name editor.
        /// </summary>
        private void LayoutChildren()
        {
            if (_statsPanel != null)
            {
                Rectangle stats = StatsRectangle;
                bool visible = _expanded && stats.Width > 60 &&
                    stats.Height > 60;
                if (_statsPanel.Visible != visible)
                    _statsPanel.Visible = visible;
                if (visible)
                {
                    int height = Math.Min(_statsPanel.MeasureHeight(),
                        stats.Height);
                    var bounds = new Rectangle(stats.X, stats.Y, stats.Width,
                        Math.Max(1, height));
                    if (_statsPanel.Bounds != bounds)
                        _statsPanel.Bounds = bounds;
                }
            }

            if (_nameEditor != null && _editingName)
            {
                Rectangle editor = EditorRectangle;
                if (_nameEditor.Bounds != editor)
                    _nameEditor.Bounds = editor;
            }
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var background = new SolidBrush(BackColor))
                graphics.FillRectangle(background, ClientRectangle);

            Rectangle body = BodyRectangle;
            if (body.Width <= BodyRadius * 2 || body.Height <= BodyRadius * 2)
                return;

            LayoutBand();
            DrawBody(graphics, body);
            DrawTab(graphics, body);
            DrawBand(graphics);
            DrawCards(graphics);
            DrawStatsDivider(graphics);
        }

        private static void DrawBody(Graphics graphics, Rectangle body)
        {
            using (GraphicsPath path = CreateBodyPath(body))
            using (var fill = new SolidBrush(AppTheme.TileBackground))
            using (var border = new Pen(AppTheme.Border, 1F))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }
        }

        /// <summary>
        /// Outline of the tray body: rounded everywhere except the top left
        /// corner, where the raised name tab takes over. Keeping that corner
        /// square is what removes the notch between the two shapes.
        /// </summary>
        private static GraphicsPath CreateBodyPath(Rectangle bounds)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(2, BodyRadius * 2);
            if (bounds.Width <= diameter * 2 || bounds.Height <= diameter * 2)
            {
                path.AddRectangle(bounds);
                return path;
            }

            var arc = new Rectangle(bounds.Right - diameter, bounds.Top,
                diameter, diameter);
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.AddLine(bounds.Left, bounds.Bottom - diameter / 2,
                bounds.Left, bounds.Top);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// The raised name tab covers the left half of the tray and reaches
        /// past the pull handle so the handle sits on it. It sits one title
        /// line above the body and its outline stays open at the bottom so the
        /// tab and the body read as one shape.
        /// </summary>
        private void DrawTab(Graphics graphics, Rectangle body)
        {
            int width = TabWidth;
            if (width <= TabRadius * 2)
                return;

            // The band reaches a little into the body so the body border
            // cannot show through as a seam under the name.
            var fill = new Rectangle(body.X, 0, width,
                Math.Min(Height, TabHeight + 6));
            using (GraphicsPath fillPath = CreateTabPath(fill, TabRadius))
            using (var fillBrush = new SolidBrush(AppTheme.TileBackground))
            {
                graphics.FillPath(fillBrush, fillPath);
            }

            var outline = new Rectangle(body.X, 0, width,
                Math.Min(Height, TabHeight));
            using (GraphicsPath outlinePath = CreateTabOutlinePath(outline,
                TabRadius))
            using (var border = new Pen(AppTheme.Border, 1F))
            {
                graphics.DrawPath(border, outlinePath);
            }

            if (_editingName)
                return;

            Rectangle textBounds = NameRectangle;
            textBounds.Height = Math.Min(TabHeight, Height);
            using (var textBrush = new SolidBrush(
                _hoverName ? Color.White : AppTheme.Text))
            {
                graphics.DrawString(_deckName ?? string.Empty, _nameFont,
                    textBrush, textBounds, _leftFormat);
            }
        }


        /// <summary>
        /// Action band of the right half: the four buttons, the pull handle in
        /// the middle of the top edge and the copy counter at the far right.
        /// </summary>
        private void DrawBand(Graphics graphics)
        {
            for (int i = 0; i < _buttons.Count; i++)
                DrawButton(graphics, _buttons[i]);

            DrawToggle(graphics);

            string countText = CountText;
            using (var textBrush = new SolidBrush(CountColor))
            {
                var band = new Rectangle(0, 0,
                    Math.Max(1, Width - BandPadding), TabHeight);
                graphics.DrawString(countText, _countFont, textBrush, band,
                    _rightFormat);
            }
        }

        private void DrawButton(Graphics graphics, BandButton button)
        {
            // 导入 works on an empty tray too: importing is how a deck that
            // does not exist yet gets started.
            bool enabled = button.Action == NewDeckAction.Import ||
                _cards.Count > 0;
            bool hot = enabled && (_hoverAction.HasValue &&
                _hoverAction.Value == button.Action ||
                _pressedAction.HasValue && _pressedAction.Value == button.Action);

            Color fill;
            Color border;
            Color text;
            switch (button.Action)
            {
                case NewDeckAction.Clear:
                    fill = enabled
                        ? (hot ? ClearHoverFill : ClearFill)
                        : AppTheme.Border;
                    border = fill;
                    text = enabled ? Color.White : AppTheme.Placeholder;
                    break;
                case NewDeckAction.Save:
                    fill = enabled
                        ? (hot ? SaveHoverFill : SaveFill)
                        : AppTheme.Border;
                    border = fill;
                    text = enabled ? Color.White : AppTheme.Placeholder;
                    break;
                default:
                    fill = enabled && hot
                        ? AppTheme.Divider
                        : AppTheme.InputBackground;
                    border = hot ? AppTheme.Accent : AppTheme.Border;
                    text = enabled ? AppTheme.TextSoft : AppTheme.Placeholder;
                    break;
            }

            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(button.Bounds, 6))
            using (var fillBrush = new SolidBrush(fill))
            using (var borderPen = new Pen(border))
            {
                graphics.FillPath(fillBrush, path);
                graphics.DrawPath(borderPen, path);
            }

            using (var textBrush = new SolidBrush(text))
            {
                graphics.DrawString(button.Text, _buttonFont, textBrush,
                    button.Bounds, _centeredFormat);
            }
        }

        /// <summary>
        /// Pull handle in the middle of the top edge, drawn as a blue pill so
        /// it stands out from the dark chrome on top of the name tab. The
        /// chevron points up while the tray is folded and down once it is
        /// pulled up.
        /// </summary>
        private void DrawToggle(Graphics graphics)
        {
            Rectangle bounds = ToggleRectangle;
            if (bounds.Width <= 4 || bounds.Height <= 4)
                return;

            Color fill = _hoverToggle
                ? AppTheme.AccentHover
                : (_expanded ? AppTheme.AccentPressed : AppTheme.Accent);
            int radius = Math.Max(2, (bounds.Height - 1) / 2);
            using (GraphicsPath path = AppTheme.CreateRoundedRectangle(bounds,
                radius))
            using (var fillBrush = new SolidBrush(fill))
            using (var border = new Pen(fill))
            {
                graphics.FillPath(fillBrush, path);
                graphics.DrawPath(border, path);
            }

            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            Point[] chevron = _expanded
                ? new[]
                {
                    new Point(centerX - 6, centerY - 3),
                    new Point(centerX, centerY + 3),
                    new Point(centerX + 6, centerY - 3)
                }
                : new[]
                {
                    new Point(centerX - 6, centerY + 3),
                    new Point(centerX, centerY - 3),
                    new Point(centerX + 6, centerY + 3)
                };

            using (var pen = new Pen(Color.White, 2.2F))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                graphics.DrawLines(pen, chevron);
            }
        }

        private void DrawStatsDivider(Graphics graphics)
        {
            Rectangle stats = StatsRectangle;
            if (stats.Width <= 0)
                return;

            int x = stats.Left - StatsGap / 2;
            using (var pen = new Pen(AppTheme.Divider))
            {
                graphics.DrawLine(pen, x, stats.Top + 2, x, stats.Bottom - 2);
            }
        }


        private void DrawCards(Graphics graphics)
        {
            Rectangle area = CardsRectangle;
            if (area.Width <= 0 || area.Height <= 0)
                return;

            if (_cards.Count == 0)
            {
                using (var hintBrush = new SolidBrush(AppTheme.TextMuted))
                {
                    graphics.DrawString(HintText, _hintFont, hintBrush, area,
                        _centeredFormat);
                }
                return;
            }

            GraphicsState state = graphics.Save();
            graphics.SetClip(area);
            try
            {
                for (int i = 0; i < _cards.Count; i++)
                {
                    Rectangle bounds = GetCardRectangle(i);
                    if (!bounds.IntersectsWith(area))
                        continue;

                    DrawCard(graphics, bounds, _cards[i], i == _hoverIndex);
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private void DrawCard(Graphics graphics, Rectangle bounds,
            DeckDraftCard card, bool hover)
        {
            string path = ImagePathProvider == null
                ? null
                : ImagePathProvider(card.Code);
            Image image = Thumbnails == null
                ? null
                : Thumbnails.GetOrLoad(path, bounds.Width, bounds.Height, false);

            GraphicsState state = graphics.Save();
            using (GraphicsPath clip = AppTheme.CreateRoundedRectangle(bounds,
                CardRadius))
            {
                graphics.SetClip(clip, CombineMode.Intersect);
                using (var fill = new SolidBrush(AppTheme.InputBackground))
                    graphics.FillRectangle(fill, bounds);

                if (image != null)
                {
                    graphics.DrawImage(image, bounds);
                }
                else
                {
                    using (var placeholder =
                        new SolidBrush(AppTheme.Placeholder))
                    {
                        graphics.DrawString(card.Code ?? string.Empty,
                            _hintFont, placeholder, bounds, _centeredFormat);
                    }
                }
            }
            graphics.Restore(state);

            using (GraphicsPath outline = AppTheme.CreateRoundedRectangle(bounds,
                CardRadius))
            using (var border = new Pen(
                hover ? AppTheme.Accent : AppTheme.Border, hover ? 2F : 1F))
            {
                graphics.DrawPath(border, outline);
            }

            if (card.Count > 1)
                DrawRepeatBadge(graphics, bounds, card.Count);
        }

        private void DrawRepeatBadge(Graphics graphics, Rectangle bounds,
            int count)
        {
            string text = "×" + count;
            SizeF size = graphics.MeasureString(text, _countFont);
            int width = (int)Math.Ceiling(size.Width) + 10;
            int height = (int)Math.Ceiling(size.Height) + 2;
            var badge = new Rectangle(bounds.Right - width - 4,
                bounds.Bottom - height - 4, width, height);

            using (GraphicsPath path = AppTheme.CreateRoundedRectangle(badge, 7))
            using (var fill = new SolidBrush(AppTheme.Accent))
            using (var textBrush = new SolidBrush(Color.White))
            {
                graphics.FillPath(fill, path);
                graphics.DrawString(text, _countFont, textBrush,
                    new RectangleF(badge.X, badge.Y, badge.Width, badge.Height),
                    _centeredFormat);
            }
        }


        /// <summary>
        /// Fill path of the tab: rounded top corners, straight sides and a
        /// straight bottom that ends inside the tray body.
        /// </summary>
        private static GraphicsPath CreateTabPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(2, Math.Min(radius * 2,
                Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(new Rectangle(bounds.X, bounds.Y,
                diameter, diameter), 180, 90);
            path.AddArc(new Rectangle(bounds.Right - diameter, bounds.Y,
                diameter, diameter), 270, 90);
            path.AddLine(bounds.Right, bounds.Y + diameter / 2,
                bounds.Right, bounds.Bottom);
            path.AddLine(bounds.Right, bounds.Bottom, bounds.X, bounds.Bottom);
            path.AddLine(bounds.X, bounds.Bottom, bounds.X,
                bounds.Y + diameter / 2);
            return path;
        }

        /// <summary>
        /// Open outline of the tab: rounded top corners plus the two sides and
        /// no bottom line, so the tab merges with the body below it.
        /// </summary>
        private static GraphicsPath CreateTabOutlinePath(Rectangle bounds,
            int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(2, Math.Min(radius * 2,
                Math.Min(bounds.Width, bounds.Height)));
            path.AddLine(bounds.X, bounds.Bottom,
                bounds.X, bounds.Y + diameter / 2);
            path.AddArc(new Rectangle(bounds.X, bounds.Y,
                diameter, diameter), 180, 90);
            path.AddArc(new Rectangle(bounds.Right - diameter, bounds.Y,
                diameter, diameter), 270, 90);
            path.AddLine(bounds.Right, bounds.Y + diameter / 2,
                bounds.Right, bounds.Bottom);
            return path;
        }

        private int HitTest(Point location)
        {
            Rectangle area = CardsRectangle;
            if (!area.Contains(location))
                return -1;

            for (int i = 0; i < _cards.Count; i++)
            {
                Rectangle visible = Rectangle.Intersect(
                    GetCardRectangle(i), area);
                if (visible.Width > 0 && visible.Contains(location))
                    return i;
            }
            return -1;
        }

        private NewDeckAction? HitTestAction(Point location)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i].Bounds.Contains(location))
                    return _buttons[i].Action;
            }
            return null;
        }

        private void ClampScroll()
        {
            if (_expanded)
            {
                int maxTop = Math.Max(0,
                    ContentHeight - CardsRectangle.Height);
                if (_scrollTop > maxTop)
                    _scrollTop = maxTop;
                if (_scrollTop < 0)
                    _scrollTop = 0;
                _scrollOffset = 0;
                return;
            }

            int maxOffset = Math.Max(0, ContentWidth - CardsRectangle.Width);
            if (_scrollOffset > maxOffset)
                _scrollOffset = maxOffset;
            if (_scrollOffset < 0)
                _scrollOffset = 0;
            _scrollTop = 0;
        }

        /// <summary>
        /// Arms the hold timer for a card of the tray. Releasing the button,
        /// dragging away, scrolling or leaving the tray cancels it, so a plain
        /// click on a tray card only adds one copy instead.
        /// </summary>
        private void StartLongPress(int index, Point location)
        {
            _pressTimer.Stop();
            _pressIndex = index;
            _pressPoint = location;
            _longPressFired = false;
            if (index >= 0)
                _pressTimer.Start();
        }

        private void CancelLongPress()
        {
            _pressTimer.Stop();
            _pressIndex = -1;
            _longPressFired = false;
        }

        private void CancelLongPressWhenMoved(Point location)
        {
            if (_pressIndex < 0)
                return;

            Size drag = SystemInformation.DragSize;
            if (Math.Abs(location.X - _pressPoint.X) <= drag.Width &&
                Math.Abs(location.Y - _pressPoint.Y) <= drag.Height)
            {
                return;
            }

            CancelLongPress();
        }

        private void PressTimer_Tick(object sender, EventArgs e)
        {
            _pressTimer.Stop();
            int index = _pressIndex;
            _pressIndex = -1;
            if (index < 0 || index >= _cards.Count)
                return;

            _longPressFired = true;

            EventHandler<NewDeckCardEventArgs> handler = CardDetailRequested;
            if (handler == null)
                return;

            DeckDraftCard card = _cards[index];
            handler(this, new NewDeckCardEventArgs(card.Code, card.Count));
        }

        /// <summary>
        /// Enter and Escape belong to the name editor while the tab is being
        /// edited: they are dialog keys, so a plain TextBox would swallow them.
        /// </summary>
        private sealed class DeckNameBox : TextBox
        {
            public event EventHandler CommitRequested;
            public event EventHandler CancelRequested;

            protected override bool ProcessDialogKey(Keys keyData)
            {
                if (keyData == Keys.Enter)
                {
                    EventHandler handler = CommitRequested;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                    return true;
                }

                if (keyData == Keys.Escape)
                {
                    EventHandler handler = CancelRequested;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                    return true;
                }

                return base.ProcessDialogKey(keyData);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
            LayoutBand();
            LayoutChildren();
            Invalidate();
        }

        /// <summary>
        /// UIA probe hook: the tray name, its copy count and the state of the
        /// pane are reachable without decoding a screenshot.
        /// </summary>
        private void UpdateAccessibleDescription()
        {
            AccessibleDescription = string.Format("{0} · {1}/{2} 张{3}",
                _deckName, _totalCount, _maxCount,
                _expanded ? " · 已展开" : string.Empty);
        }


        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            CancelLongPressWhenMoved(e.Location);

            bool toggle = ToggleRectangle.Contains(e.Location);
            NewDeckAction? action = toggle
                ? null
                : HitTestAction(e.Location);
            bool name = !toggle && !action.HasValue && !_editingName &&
                NameRectangle.Contains(e.Location);
            int index = toggle || action.HasValue || name
                ? -1
                : HitTest(e.Location);

            if (toggle != _hoverToggle || name != _hoverName ||
                !Nullable.Equals(_hoverAction, action) ||
                index != _hoverIndex)
            {
                _hoverToggle = toggle;
                _hoverName = name;
                _hoverAction = action;
                _hoverIndex = index;

                if (name)
                    Cursor = Cursors.IBeam;
                else if (toggle || action.HasValue || index >= 0)
                    Cursor = Cursors.Hand;
                else
                    Cursor = Cursors.Default;

                Invalidate();
            }

            string text = string.Empty;
            if (index >= 0)
            {
                DeckDraftCard card = _cards[index];
                text = card.Count > 1
                    ? card.Code + "  ×" + card.Count
                    : card.Code ?? string.Empty;
            }

            if (string.Equals(text, _toolTipText, StringComparison.Ordinal))
                return;

            _toolTipText = text;
            _toolTip.SetToolTip(this, text);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            CancelLongPress();
            if (_hoverIndex != -1 || _hoverToggle || _hoverName ||
                _hoverAction.HasValue)
            {
                _hoverIndex = -1;
                _hoverToggle = false;
                _hoverName = false;
                _hoverAction = null;
                Cursor = Cursors.Default;
                Invalidate();
            }

            if (_toolTipText.Length == 0)
                return;
            _toolTipText = string.Empty;
            _toolTip.SetToolTip(this, string.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            Focus();
            _longPressFired = false;

            if (ToggleRectangle.Contains(e.Location))
            {
                Expanded = !Expanded;
                return;
            }

            NewDeckAction? action = HitTestAction(e.Location);
            if (action.HasValue)
            {
                _pressedAction = action;
                Invalidate();
                return;
            }

            if (NameRectangle.Contains(e.Location))
            {
                BeginNameEdit();
                return;
            }

            int index = HitTest(e.Location);
            if (index >= 0)
                StartLongPress(index, e.Location);
        }

        /// <summary>
        /// Right clicking a card of the tray takes one copy out of the deck,
        /// the same operation as right clicking it in the card grid. A left
        /// click adds one more copy, holding the button opened its details and
        /// is therefore not counted as a click.
        /// </summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Right)
            {
                CancelLongPress();
                int index = HitTest(e.Location);
                if (index < 0)
                    return;

                EventHandler<NewDeckCardEventArgs> handler = CardRemoveRequested;
                if (handler != null)
                {
                    DeckDraftCard card = _cards[index];
                    handler(this, new NewDeckCardEventArgs(card.Code, card.Count));
                }
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            // The held button already opened the card details, so the release
            // must not count as anything else.
            bool held = _longPressFired;
            int pressedCard = _pressIndex;
            CancelLongPress();
            if (held)
            {
                Invalidate();
                return;
            }

            // A plain click on a card of the tray adds one more copy of it,
            // the gesture the card grid already uses.
            if (pressedCard >= 0)
            {
                int cardIndex = HitTest(e.Location);
                if (cardIndex == pressedCard && cardIndex < _cards.Count)
                {
                    EventHandler<NewDeckCardEventArgs> addHandler =
                        CardAddRequested;
                    if (addHandler != null)
                    {
                        DeckDraftCard card = _cards[cardIndex];
                        addHandler(this, new NewDeckCardEventArgs(card.Code,
                            card.Count));
                    }
                }

                Invalidate();
                return;
            }

            if (!_pressedAction.HasValue)
                return;

            NewDeckAction pressed = _pressedAction.Value;
            _pressedAction = null;

            NewDeckAction? released = HitTestAction(e.Location);
            if (!released.HasValue || released.Value != pressed ||
                (_cards.Count == 0 && pressed != NewDeckAction.Import))
            {
                Invalidate();
                return;
            }

            // The shared button stages an unfinished deck and saves a finished
            // one, so the page only ever sees one of the two actions.
            if (pressed == NewDeckAction.Save && !IsComplete)
                pressed = NewDeckAction.Stash;

            Invalidate();
            EventHandler<NewDeckActionEventArgs> handler2 = ActionRequested;
            if (handler2 != null)
                handler2(this, new NewDeckActionEventArgs(pressed));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            CancelLongPress();

            if (_expanded)
            {
                int maxTop = Math.Max(0,
                    ContentHeight - CardsRectangle.Height);
                if (maxTop <= 0)
                {
                    base.OnMouseWheel(e);
                    return;
                }

                int step = Math.Max(1, CellHeight + CardGap);
                int top = _scrollTop - Math.Sign(e.Delta) * step;
                top = Math.Max(0, Math.Min(maxTop, top));
                if (top == _scrollTop)
                    return;

                _scrollTop = top;
                _hoverIndex = -1;
                Invalidate();
                return;
            }

            int maxOffset = Math.Max(0, ContentWidth - CardsRectangle.Width);
            if (maxOffset <= 0)
            {
                base.OnMouseWheel(e);
                return;
            }

            int step2 = Math.Max(1, CardWidth + CardGap);
            int offset = _scrollOffset - Math.Sign(e.Delta) * step2;
            offset = Math.Max(0, Math.Min(maxOffset, offset));
            if (offset == _scrollOffset)
                return;

            _scrollOffset = offset;
            _hoverIndex = -1;
            Invalidate();
        }


        /// <summary>
        /// Turns the name on the tab into a borderless edit box. The box has no
        /// frame and the same fill as the tab, so it reads as the text itself.
        /// </summary>
        private void BeginNameEdit()
        {
            if (_editingName || _nameEditor == null)
                return;

            _editingName = true;
            _nameEditor.Text = _deckName ?? string.Empty;
            _nameEditor.Bounds = EditorRectangle;
            _nameEditor.Visible = true;
            _nameEditor.BringToFront();
            _nameEditor.Focus();
            _nameEditor.SelectAll();
            Invalidate();
        }

        private void EndNameEdit(bool commit)
        {
            if (!_editingName)
                return;

            _editingName = false;
            string text = (_nameEditor.Text ?? string.Empty).Trim();
            _nameEditor.Visible = false;
            Invalidate();

            if (!commit || text.Length == 0 ||
                string.Equals(text, _deckName, StringComparison.Ordinal))
            {
                return;
            }

            _deckName = text;
            Invalidate();

            EventHandler<NewDeckNameEventArgs> handler = DeckNameChanged;
            if (handler != null)
                handler(this, new NewDeckNameEventArgs(text));
        }

        private void NameEditor_Leave(object sender, EventArgs e)
        {
            EndNameEdit(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nameFont.Dispose();
                _hintFont.Dispose();
                _countFont.Dispose();
                _buttonFont.Dispose();
                _leftFormat.Dispose();
                _rightFormat.Dispose();
                _centeredFormat.Dispose();
                _toolTip.Dispose();
                _pressTimer.Stop();
                _pressTimer.Tick -= PressTimer_Tick;
                _pressTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
