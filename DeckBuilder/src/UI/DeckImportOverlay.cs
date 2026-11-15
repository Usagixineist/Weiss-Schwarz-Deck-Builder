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
    /// Codes and deck name the 导入卡组 layer hands to the page. They are
    /// raised as soon as the player picks a staged deck or confirms a pasted
    /// block: the layer reads a whole deck into the tray, so replacing what
    /// the tray already holds is the point of the gesture and needs no second
    /// question.
    /// </summary>
    internal sealed class DeckImportRequestEventArgs : EventArgs
    {
        public IList<string> CardCodes { get; private set; }
        public string DeckName { get; private set; }
        public int SlotIndex { get; private set; }

        public DeckImportRequestEventArgs(IList<string> cardCodes,
            string deckName, int slotIndex)
        {
            CardCodes = cardCodes ?? new List<string>();
            DeckName = deckName;
            SlotIndex = slotIndex;
        }
    }

    /// <summary>
    /// The 导入卡组 popup as an in-page layer: the same dimmed backdrop and
    /// rounded panel the card detail layer uses, so importing never opens a
    /// second window on the desktop. The first cell of the grid hands the
    /// player a text page to paste a 卡组代码 into, the other five read one
    /// staged deck back; a slot that was never used is not drawn at all.
    /// Reading a deck replaces the tray right away, and a staged deck is
    /// deleted by right clicking its tile twice: the first click only turns
    /// the tile red, so neither gesture needs a confirmation page.
    /// </summary>
    internal sealed class DeckImportOverlay : UserControl
    {
        private const int DesiredWidth = 520;
        private const int DesiredHeight = 596;
        private const int PanelRadius = 16;
        private const int PanelPadding = 22;
        private const int HeaderHeight = 78;
        private const int ButtonWidth = 96;
        private const int ButtonHeight = 34;
        private const int ButtonGap = 10;
        private const int FooterGap = 18;
        private const int StatusHeight = 20;
        private const int TextTitleHeight = 30;
        private const int TextHintHeight = 22;
        private const int CodeBoxInset = 8;

        private const TextFormatFlags LeftFlags =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix;

        /// <summary>Fill of the popup, the panel colour of the detail layer.</summary>
        private static readonly Color PanelColor = Color.FromArgb(23, 32, 49);

        private readonly DeckDraftStore _store;
        private readonly Func<DeckDefinition, string> _coverPathProvider;
        private readonly Font _titleFont;
        private readonly Font _sectionFont;
        private readonly Font _hintFont;
        private readonly DeckImportTilesControl _tiles;
        private readonly DeckImportCodeBox _codeBox;
        private readonly RoundedButton _closeButton;
        private readonly RoundedButton _backButton;
        private readonly RoundedButton _acceptButton;

        private Bitmap _backdrop;
        private Rectangle _panelBounds;
        private Rectangle _bodyBounds;
        private Rectangle _footerBounds;
        private Rectangle _codeBoxFrame;
        private string _statusText;
        private bool _textPage;
        private int _stagedCount;
        private int _armedIndex;
        private int _armedSlot;

        public DeckImportOverlay(DeckDraftStore store,
            Func<DeckDefinition, string> coverPathProvider,
            ThumbnailCache thumbnails)
        {
            _store = store ?? new DeckDraftStore(string.Empty);
            _coverPathProvider = coverPathProvider;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);

            BackColor = PanelColor;
            TabStop = true;
            Visible = false;

            _titleFont = CreateFont(16F, FontStyle.Bold);
            _sectionFont = CreateFont(12F, FontStyle.Bold);
            _hintFont = CreateFont(9F, FontStyle.Regular);

            _statusText = string.Empty;
            _stagedCount = 0;
            _armedIndex = -1;
            _armedSlot = 0;

            _tiles = new DeckImportTilesControl();
            _tiles.Thumbnails = thumbnails;
            _tiles.BackColor = PanelColor;
            _tiles.TileClick += Tiles_TileClick;
            _tiles.TileRightClick += Tiles_TileRightClick;

            _codeBox = new DeckImportCodeBox();
            _codeBox.Visible = false;
            _codeBox.EscapeRequested += delegate { ShowGridPage(); };

            _closeButton = CreateButton("关闭", AppTheme.Accent,
                AppTheme.AccentHover, AppTheme.AccentPressed, Color.White);
            _closeButton.Click += delegate { RaiseCloseRequested(); };

            _backButton = CreateButton("返回", AppTheme.Border,
                AppTheme.Divider, AppTheme.AccentPressed, AppTheme.Text);
            _backButton.Click += delegate { ShowGridPage(); };

            _acceptButton = CreateButton("导入", AppTheme.Accent,
                AppTheme.AccentHover, AppTheme.AccentPressed, Color.White);
            _acceptButton.Click += delegate { AcceptPressed(); };

            Controls.Add(_tiles);
            Controls.Add(_codeBox);
            Controls.Add(_closeButton);
            Controls.Add(_backButton);
            Controls.Add(_acceptButton);

            ReloadTiles();
        }

        /// <summary>Codes the player picked, raised once the tray may be replaced.</summary>
        public event EventHandler<DeckImportRequestEventArgs> ImportRequested;

        /// <summary>Raised when the layer should be taken down again.</summary>
        public event EventHandler CloseRequested;

        /// <summary>
        /// Cards the tray currently holds. A number above zero only changes the
        /// hint line of the grid: reading a deck always replaces the tray, with
        /// no second question.
        /// </summary>
        public int TrayCardCount { get; set; }

        /// <summary>
        /// Prepares the layer for one 导入: fresh tiles, the grid page and the
        /// dimmed backdrop the page captured right before showing it.
        /// </summary>
        public void ShowImport(Bitmap backdrop)
        {
            ReplaceBackdrop(backdrop);
            _textPage = false;
            _statusText = string.Empty;
            _codeBox.Text = string.Empty;
            Disarm();
            ReloadTiles();
            RecalculateLayout();
            Invalidate();
        }

        /// <summary>Drops the backdrop and every staged choice of the layer.</summary>
        public void Clear()
        {
            _textPage = false;
            _statusText = string.Empty;
            if (_codeBox != null)
                _codeBox.Text = string.Empty;
            Disarm();
            ReplaceBackdrop(null);
            UpdateChildVisibility();
            Invalidate();
        }

        /// <summary>
        /// Rebuilds the cells: the 卡组代码 placeholder first, then one tile
        /// per staged deck. Slots that are empty stay invisible.
        /// </summary>
        private void ReloadTiles()
        {
            IList<DeckDraftSlot> slots = _store.Load();
            var tiles = new List<DeckImportTile>();

            var codeTile = new DeckImportTile();
            codeTile.Caption = "卡组代码";
            codeTile.SlotIndex = 0;
            codeTile.ToolTipText = "粘贴卡组代码导入卡片";
            tiles.Add(codeTile);

            for (int i = 0; i < slots.Count; i++)
            {
                DeckDraftSlot slot = slots[i];
                var deck = new DeckDefinition(slot.DeckName, slot.FilePath,
                    DeckSource.Local, slot.CardCodes);
                DeckDefinition captured = deck;

                var tile = new DeckImportTile();
                tile.Caption = DeckDraftStore.SlotCaption(slot.Index);
                tile.SlotIndex = slot.Index;
                tile.ToolTipText = string.Format("{0} · {1} 张 · {2}",
                    DeckDraftStore.SlotCaption(slot.Index), slot.CardCount,
                    slot.DeckName);
                tile.ImagePathProvider = delegate
                {
                    return _coverPathProvider == null
                        ? null
                        : _coverPathProvider(captured);
                };
                tiles.Add(tile);
            }

            _tiles.SetTiles(tiles);
            _stagedCount = slots.Count;
            _armedIndex = -1;
            _armedSlot = 0;
            RecalculateLayout();
        }

        /// <summary>
        /// Places the panel in the middle of the page and lays the body, the
        /// footer and the buttons out around it. Recalculated whenever the
        /// page, the parser state or the staged slots change.
        /// </summary>
        private void RecalculateLayout()
        {
            int width = Math.Min(DesiredWidth,
                Math.Max(360, ClientSize.Width - 40));
            int height = Math.Min(DesiredHeight,
                Math.Max(300, ClientSize.Height - 40));
            _panelBounds = new Rectangle(
                Math.Max(0, (ClientSize.Width - width) / 2),
                Math.Max(0, (ClientSize.Height - height) / 2),
                width, height);

            _footerBounds = new Rectangle(_panelBounds.X + PanelPadding,
                _panelBounds.Bottom - PanelPadding - ButtonHeight,
                Math.Max(1, _panelBounds.Width - PanelPadding * 2),
                ButtonHeight);

            int bodyTop = _panelBounds.Y + HeaderHeight;
            _bodyBounds = new Rectangle(_panelBounds.X + PanelPadding, bodyTop,
                Math.Max(1, _panelBounds.Width - PanelPadding * 2),
                Math.Max(1, _footerBounds.Top - FooterGap - bodyTop));

            int right = _footerBounds.Right;
            _closeButton.Bounds = new Rectangle(right - ButtonWidth,
                _footerBounds.Y, ButtonWidth, ButtonHeight);
            _acceptButton.Bounds = _closeButton.Bounds;
            _backButton.Bounds = new Rectangle(
                right - ButtonWidth * 2 - ButtonGap, _footerBounds.Y,
                ButtonWidth, ButtonHeight);

            _tiles.Bounds = _bodyBounds;

            var title = new Rectangle(_bodyBounds.X, _bodyBounds.Y,
                _bodyBounds.Width, TextTitleHeight);
            var hint = new Rectangle(_bodyBounds.X, title.Bottom,
                _bodyBounds.Width, TextHintHeight);
            int frameTop = hint.Bottom + 6;
            _codeBoxFrame = new Rectangle(_bodyBounds.X, frameTop,
                _bodyBounds.Width,
                Math.Max(40, _bodyBounds.Bottom - StatusHeight - frameTop));
            _codeBox.SetBounds(_codeBoxFrame.X + CodeBoxInset,
                _codeBoxFrame.Y + CodeBoxInset,
                Math.Max(10, _codeBoxFrame.Width - CodeBoxInset * 2),
                Math.Max(10, _codeBoxFrame.Height - CodeBoxInset * 2));

            UpdateChildVisibility();
        }

        /// <summary>
        /// Only one of the two pages has children on the panel: the tile grid
        /// or the 卡组代码 text page. The footer buttons follow the same state.
        /// </summary>
        private void UpdateChildVisibility()
        {
            _tiles.Visible = !_textPage;
            _codeBox.Visible = _textPage;
            _closeButton.Visible = !_textPage;
            _backButton.Visible = _textPage;
            _acceptButton.Visible = _textPage;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The layer always paints the full backdrop itself.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            DrawBackdrop(graphics);
            DrawShadow(graphics);
            DrawPanel(graphics);
            DrawHeader(graphics);

            if (_textPage)
                DrawTextPage(graphics);

            DrawFooterHint(graphics);
        }

        private void DrawBackdrop(Graphics graphics)
        {
            Rectangle bounds = ClientRectangle;
            if (_backdrop != null)
            {
                graphics.DrawImage(_backdrop, bounds);
            }
            else
            {
                using (var background = new SolidBrush(AppTheme.MainBackground))
                    graphics.FillRectangle(background, bounds);
            }

            using (var dim = new SolidBrush(Color.FromArgb(178, 3, 8, 18)))
                graphics.FillRectangle(dim, bounds);
        }

        private void DrawShadow(Graphics graphics)
        {
            for (int i = 12; i >= 1; i--)
            {
                Rectangle shadow = new Rectangle(
                    _panelBounds.X - i,
                    _panelBounds.Y - i + 5,
                    _panelBounds.Width + i * 2,
                    _panelBounds.Height + i * 2);
                using (GraphicsPath path =
                    AppTheme.CreateRoundedRectangle(shadow, PanelRadius + i))
                using (var brush = new SolidBrush(Color.FromArgb(7, 0, 0, 0)))
                {
                    graphics.FillPath(brush, path);
                }
            }
        }

        private void DrawPanel(Graphics graphics)
        {
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(_panelBounds, PanelRadius))
            using (var fill = new SolidBrush(PanelColor))
            using (var border = new Pen(Color.FromArgb(36, 48, 66)))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }
        }

        private void DrawHeader(Graphics graphics)
        {
            var title = new Rectangle(_panelBounds.X + PanelPadding,
                _panelBounds.Y + 18,
                Math.Max(1, _panelBounds.Width - PanelPadding * 2), 32);
            DrawText(graphics, "导入卡组", _titleFont, title,
                AppTheme.Text, LeftFlags);

            var subtitle = new Rectangle(_panelBounds.X + PanelPadding,
                title.Bottom - 2,
                Math.Max(1, _panelBounds.Width - PanelPadding * 2), 20);
            DrawText(graphics, "粘贴卡组代码，或读取一个暂存卡组", _hintFont,
                subtitle, AppTheme.TextMuted, LeftFlags);
        }

        /// <summary>
        /// Chrome of the 卡组代码 page: its title, the code rule and the frame
        /// the text box sits in. The box itself is a child control, so only
        /// the border and the error line are painted here.
        /// </summary>
        private void DrawTextPage(Graphics graphics)
        {
            var title = new Rectangle(_bodyBounds.X, _bodyBounds.Y,
                _bodyBounds.Width, TextTitleHeight);
            DrawText(graphics, "卡组代码", _sectionFont, title,
                AppTheme.Text, LeftFlags);

            var hint = new Rectangle(_bodyBounds.X, title.Bottom,
                _bodyBounds.Width, TextHintHeight);
            DrawText(graphics,
                "每行一个卡号，例如 MTI/S83-E101 · 空行与 # / // 开头的行会被忽略",
                _hintFont, hint, AppTheme.TextMuted, LeftFlags);

            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(_codeBoxFrame, 8))
            using (var fill = new SolidBrush(AppTheme.InputBackground))
            using (var border = new Pen(AppTheme.Border))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            if (string.IsNullOrEmpty(_statusText))
                return;

            var status = new Rectangle(_bodyBounds.X, _codeBoxFrame.Bottom + 2,
                _bodyBounds.Width, StatusHeight);
            DrawText(graphics, _statusText, _hintFont, status,
                AppTheme.Danger, LeftFlags);
        }

        /// <summary>
        /// Bottom line of the grid: what a click does, whether reading a deck
        /// replaces the tray, and, once a tile is armed, that a second right
        /// click removes it. The 卡组代码 page prints its own line under the
        /// code frame instead.
        /// </summary>
        private void DrawFooterHint(Graphics graphics)
        {
            if (_textPage)
                return;

            bool error = !string.IsNullOrEmpty(_statusText);
            string hint = error ? _statusText : FooterHint;
            if (string.IsNullOrEmpty(hint))
                return;

            var bounds = new Rectangle(_footerBounds.X, _footerBounds.Y,
                Math.Max(1, _footerBounds.Width - ButtonWidth - ButtonGap),
                ButtonHeight);
            DrawText(graphics, hint, _hintFont, bounds,
                error ? AppTheme.Danger : AppTheme.TextMuted, LeftFlags);
        }

        private string FooterHint
        {
            get
            {
                if (_armedSlot > 0)
                {
                    return string.Format("再次右键删除「{0}」· Esc 取消",
                        DeckDraftStore.SlotCaption(_armedSlot));
                }

                if (_stagedCount <= 0)
                    return "还没有暂存卡组 · 单击「卡组代码」粘贴卡号导入";
                if (TrayCardCount > 0)
                    return "单击卡组读取 · 右键两次删除 · 导入会替换新卡组";
                return "单击卡组读取 · 右键两次删除";
            }
        }

        /// <summary>One line of text with the theme fonts, used by every block.</summary>
        private static void DrawText(Graphics graphics, string text, Font font,
            Rectangle bounds, Color color, TextFormatFlags flags)
        {
            TextRenderer.DrawText(graphics, text ?? string.Empty, font,
                bounds, color, flags);
        }

        private static Font CreateFont(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style,
                GraphicsUnit.Point, 134);
        }

        private static RoundedButton CreateButton(string text, Color fill,
            Color hover, Color pressed, Color foreColor)
        {
            var button = new RoundedButton();
            button.Text = text;
            button.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            button.CornerRadius = 8;
            button.SurfaceColor = PanelColor;
            button.NormalColor = fill;
            button.HoverColor = hover;
            button.PressedColor = pressed;
            button.ForeColor = foreColor;
            return button;
        }

        private void ReplaceBackdrop(Bitmap backdrop)
        {
            if (ReferenceEquals(_backdrop, backdrop))
                return;
            if (_backdrop != null)
                _backdrop.Dispose();
            _backdrop = backdrop;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecalculateLayout();
            Invalidate();
        }

        /// <summary>
        /// A click on the dimmed area outside the panel closes the layer, the
        /// gesture the card detail layer already uses.
        /// </summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            if (!_panelBounds.Contains(e.Location))
                RaiseCloseRequested();
        }

        /// <summary>
        /// Escape leaves the text page first and the whole layer only from the
        /// grid or the confirmation, so a paste is never lost by accident.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                HandleEscape();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// The Escape step the layer is on: a red, armed tile is disarmed
        /// first, then the pasted block steps back once, and only the tile
        /// page closes the layer. The window calls this too, so the key
        /// behaves the same no matter which control holds the focus.
        /// </summary>
        public void HandleEscape()
        {
            if (_armedIndex >= 0)
            {
                // A key that means "back out" never deletes a staged deck.
                Disarm();
                _statusText = string.Empty;
                Invalidate();
                return;
            }

            if (_textPage)
            {
                ShowGridPage();
                return;
            }

            RaiseCloseRequested();
        }

        private void Tiles_TileClick(object sender, DeckImportTileEventArgs e)
        {
            if (e == null || e.Tile == null)
                return;

            // A left click never deletes: drop the red state first, so an arm
            // can only be completed by a second right click on the same tile.
            Disarm();
            _statusText = string.Empty;

            if (e.Tile.IsCodeCell)
            {
                ShowTextPage();
                return;
            }

            DeckDraftSlot slot = _store.Read(e.Tile.SlotIndex);
            if (slot == null)
            {
                ReloadTiles();
                Invalidate();
                return;
            }

            RequestImport(slot.CardCodes, slot.DeckName, slot.Index);
        }

        /// <summary>
        /// Right clicking a staged deck arms it: the tile turns red and the
        /// footer asks for one more right click, which deletes the staged
        /// file. Freeing a slot is the only way to stage a sixth deck, so the
        /// first click never destroys anything.
        /// </summary>
        private void Tiles_TileRightClick(object sender, DeckImportTileEventArgs e)
        {
            if (e == null || e.Tile == null || e.Tile.IsCodeCell)
                return;

            if (e.Index != _armedIndex || e.Tile.SlotIndex != _armedSlot)
            {
                Arm(e.Index, e.Tile.SlotIndex);
                return;
            }

            DeleteArmedTile();
        }

        /// <summary>First right click: the tile turns red and waits.</summary>
        private void Arm(int index, int slot)
        {
            _armedIndex = index;
            _armedSlot = slot;
            _statusText = string.Empty;
            _tiles.ArmedIndex = index;
            Invalidate();
            if (CanFocus)
                Focus();
        }

        /// <summary>Takes the red edge off again; nothing is deleted.</summary>
        private void Disarm()
        {
            if (_armedIndex < 0 && _armedSlot == 0)
                return;

            _armedIndex = -1;
            _armedSlot = 0;
            if (_tiles != null)
                _tiles.ArmedIndex = -1;
            Invalidate();
        }

        /// <summary>Second right click on the armed tile: the file is gone.</summary>
        private void DeleteArmedTile()
        {
            int slot = _armedSlot;
            Disarm();
            _statusText = string.Empty;

            if (slot <= 0 || !_store.Delete(slot))
            {
                _statusText = "删除失败 · 暂存文件可能正被占用";
                Invalidate();
                return;
            }

            ReloadTiles();
            Invalidate();
            if (CanFocus)
                Focus();
        }

        private void ShowTextPage()
        {
            Disarm();
            _textPage = true;
            _statusText = string.Empty;
            RecalculateLayout();
            Invalidate();

            if (_codeBox.Visible)
            {
                _codeBox.Focus();
                _codeBox.SelectionStart = _codeBox.TextLength;
            }
        }

        private void ShowGridPage()
        {
            _textPage = false;
            _statusText = string.Empty;
            RecalculateLayout();
            Invalidate();

            // The pasted block lost the keyboard with the page, so the layer
            // takes it back and Escape keeps working without a second press.
            if (!Focus() && _tiles != null && _tiles.CanFocus)
                _tiles.Focus();
        }

        /// <summary>
        /// Typing in the 卡组代码 box turns into cards. Nothing leaves the page
        /// unless at least one line looks like a card code.
        /// </summary>
        private void ConfirmCodeText()
        {
            IList<string> codes = DeckDraftStore.ParseCodes(_codeBox.Text);
            if (codes.Count == 0)
            {
                _statusText = "没有读到卡号 · 每行一个卡号，例如 MTI/S83-E101";
                Invalidate(new Rectangle(_bodyBounds.X,
                    _codeBoxFrame.Bottom, _bodyBounds.Width, StatusHeight));
                return;
            }

            _statusText = string.Empty;
            RequestImport(codes, null, 0);
        }

        /// <summary>
        /// Every import ends here. The tray is replaced directly: the layer was
        /// opened to read one whole deck, so the footer line is the only
        /// warning and no question stands between the click and the cards.
        /// </summary>
        private void RequestImport(IList<string> codes, string name, int slot)
        {
            if (codes == null || codes.Count == 0)
                return;

            Disarm();
            RaiseImportRequested(codes, name, slot);
        }

        /// <summary>Footer button of the 卡组代码 page.</summary>
        private void AcceptPressed()
        {
            ConfirmCodeText();
        }

        private void RaiseImportRequested(IList<string> codes, string name,
            int slot)
        {
            Disarm();
            _textPage = false;
            _statusText = string.Empty;
            _codeBox.Text = string.Empty;

            EventHandler<DeckImportRequestEventArgs> handler = ImportRequested;
            if (handler != null)
                handler(this, new DeckImportRequestEventArgs(codes, name, slot));
        }

        private void RaiseCloseRequested()
        {
            EventHandler handler = CloseRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Multiline paste box of the 卡组代码 page. Escape is a dialog key that
        /// a plain TextBox would swallow, so the page is told to step back.
        /// </summary>
        private sealed class DeckImportCodeBox : TextBox
        {
            public event EventHandler EscapeRequested;

            public DeckImportCodeBox()
            {
                Multiline = true;
                AcceptsReturn = true;
                ScrollBars = ScrollBars.Vertical;
                WordWrap = false;
                BorderStyle = BorderStyle.None;
                BackColor = AppTheme.InputBackground;
                ForeColor = AppTheme.Text;
                Font = new Font("Consolas", 10F, FontStyle.Regular,
                    GraphicsUnit.Point, 134);
                Margin = new Padding(0);
                AccessibleName = "卡组代码";
            }

            protected override bool ProcessDialogKey(Keys keyData)
            {
                if (keyData == Keys.Escape)
                {
                    EventHandler handler = EscapeRequested;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                    return true;
                }

                return base.ProcessDialogKey(keyData);
            }
        }
    }
}
