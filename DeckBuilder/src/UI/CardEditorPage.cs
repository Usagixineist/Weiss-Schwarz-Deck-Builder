using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 编辑卡片 as a page instead of a popup: the left quarter holds the card
    /// art above the base fields, the middle column holds 效果内容 above
    /// 效果代码, and the right column is a reference search over the whole card
    /// table (卡名 / 卡号 and 效果文字), whose result opens the effect text and
    /// the effect code of that card. Only 保存 / 取消 leave the page; the file
    /// itself is written by <see cref="CardDataWriter"/> behind MainForm.
    /// </summary>
    internal sealed class CardEditorPage : UserControl
    {
        private const int HeaderHeight = 62;
        private const int FooterHeight = 26;
        private const int CaptionHeight = 17;
        private const int ChipRowHeight = 46;
        private const int InfoRowHeight = 44;
        private const int ColumnGap = 14;
        private const int BodyPadding = 2;
        private const int SearchRowHeight = 48;
        private const int SearchResultLimit = 120;

        /// <summary>Height of the ＋ 添加效果 / 移除最后一条 strip.</summary>
        private const int EffectToolbarHeight = 34;

        /// <summary>Height of one 效果 row when the card has more than one.</summary>
        private const int EffectRowHeight = 320;

        /// <summary>Gap kept between two 效果 rows.</summary>
        private const int EffectRowGap = 8;

        /// <summary>
        /// Vertical padding a <see cref="FieldBox"/> keeps around its input
        /// (twice its inner 8px), so the 简写提供 box can be measured to exactly
        /// the lines it shows instead of guessing a fixed height.
        /// </summary>
        private const int FieldBoxInnerPadding = 16;

        /// <summary>Width a vertical scrollbar of a multiline
        /// <see cref="FieldBox"/> takes from its text.</summary>
        private const int FieldBoxScrollbarWidth = 18;

        /// <summary>Width the 效果 column keeps free for its own scrollbar.</summary>
        private const int EffectScrollbarReserve = 10;

        /// <summary>
        /// Right margin the caption / box stacks of an 效果 row keep, so the
        /// text of a row never touches the scrollbar column.
        /// </summary>
        private const int EffectRowRightMargin = 8;

        /// <summary>
        /// Caption of the read-only 简写提供 band. It gets its own caption row
        /// above the box, exactly like the 内容 / 代码 captions, so the box
        /// below spans the whole 效果 column.
        /// </summary>
        private const string InheritedLabel = "简写提供（只读）";

        /// <summary>Gap the 简写提供 band keeps to the 效果代码 below it.</summary>
        private const int InheritedBandGap = 2;

        /// <summary>
        /// Height a length measurement may grow to while counting how many
        /// lines a 简写 wraps to; anything longer than this scrolls inside the
        /// box instead of stretching the row.
        /// </summary>
        private const int MeasureHeightLimit = 4096;

        /// <summary>
        /// Most lines the 简写提供 band grows to before it scrolls instead: a
        /// shorthand prints a sentence or two, so anything past this is a
        /// safety net against one bad entry swallowing the whole column.
        /// </summary>
        private const int InheritedMaxLines = 8;

        /// <summary>Height of the 添加异画 block above the reference panel.</summary>
        private const int AltArtSectionHeight = 168;

        /// <summary>
        /// Width of the 异画 preview column. The preview fills the whole body
        /// row, which runs down to the bottom of the 168px block (131px tall
        /// after the caption); at that height a 63 : 88 card draws about
        /// 85x119, so this column leaves it a couple of pixels of slack.
        /// </summary>
        private const int AltArtImageWidth = 112;

        /// <summary>Height of one 保存 / 清空 button.</summary>
        private const int AltArtActionHeight = 30;

        /// <summary>Gap between the stacked 保存 and 清空 buttons.</summary>
        private const int AltArtActionGap = 6;

        /// <summary>
        /// Width of the 保存 / 清空 column beside the queue strip. The pair
        /// stacks vertically there, so 保存 no longer has to fit
        /// “保存 3 张异画” and the queue keeps the rest of the band.
        /// </summary>
        private const int AltArtActionWidth = 60;

        /// <summary>
        /// One line of 写入 … 提示 above the queue strip. 18px still holds the
        /// 8.5pt caption line and leaves the strip its 83px band, which the
        /// strip turns into ~56x79 tiles.
        /// </summary>
        private const int AltArtHintHeight = 18;

        private static Color PanelSurface
        {
            get { return AppTheme.TileBackground; }
        }
        private static Color ChipSurface
        {
            get { return AppTheme.Divider; }
        }

        private readonly Font _titleFont;
        private readonly Font _subtitleFont;
        private readonly Font _captionFont;
        private readonly Font _bodyFont;
        private readonly Font _chipFont;
        private readonly Font _buttonFont;
        private readonly Font _statusFont;

        private Image _preview;
        private CardImageDropBox _imageBox;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _statusLabel;
        private RoundedButton _saveButton;
        private RoundedButton _cancelButton;

        private FieldBox _idBox;
        private FieldBox _nameBox;
        private FieldBox _jpNameBox;
        private FieldBox _imageIdBox;
        private FieldBox _levelBox;
        private FieldBox _costBox;
        private FieldBox _powerBox;
        private FieldBox _soulBox;
        private FieldBox _backupBox;
        private FieldBox _triggerBox;
        private FieldBox _trait1Box;
        private FieldBox _trait2Box;
        private FieldBox _trait3Box;
        private FieldBox _comboBox;
        private FieldBox _textBox;
        private FieldBox _codeBox;

        /// <summary>One 效果: 效果内容 above the 效果代码 that carries it, plus
        /// the read-only text a `*Shorthand` of that 效果 brings in.</summary>
        private sealed class EffectRow
        {
            public Control Cell;

            /// <summary>Wrapper of the 效果内容 caption and box; folded away
            /// while the 简写 supplies the whole 效果.</summary>
            public Control TextCell;

            public Label TextCaption;
            public Label CodeCaption;
            public FieldBox Text;
            public FieldBox Code;

            /// <summary>Read-only 简写提供 preview; hidden while the 效果
            /// carries no `*Shorthand` text.</summary>
            public FieldBox Inherited;

            public Label InheritedCaption;

            /// <summary>Caption + box of the 简写提供 band, hidden as a whole
            /// while there is no shorthand text.</summary>
            public Control InheritedCell;

            /// <summary>Height the preview takes inside the row, 0 when the
            /// 效果 has no shorthand text.</summary>
            public int InheritedHeight;

            /// <summary>The 简写提供 text as shown, so the band can be measured
            /// again whenever the 效果 column changes width.</summary>
            public string InheritedText;

            /// <summary>True while the 效果内容 box is omitted because the
            /// `*Shorthand` already prints the whole 效果.</summary>
            public bool TextHidden;
        }

        private DarkScrollPanel _effectScroll;
        private TableLayoutPanel _effectStack;
        private readonly List<EffectRow> _effectRows = new List<EffectRow>();

        /// <summary>Guards <see cref="LayoutEffectRows"/> against the layout
        /// pass it starts itself (re-measuring a band can resize the stack).</summary>
        private bool _layingOutEffects;

        /// <summary>Set once <see cref="Dispose"/> released the fonts, so a late
        /// layout callback never measures text against a disposed font.</summary>
        private bool _closed;

        private RoundedButton _addEffectButton;
        private RoundedButton _removeEffectButton;

        private ChipRow _categoryRow;
        private ChipRow _colorRow;
        private ChipRow _folderRow;
        private Control _folderCell;
        private TableLayoutPanel _infoContent;
        private DarkScrollPanel _infoScroll;

        private Panel _referenceBody;
        private Panel _referenceSearchPanel;
        private Panel _referenceDetailPanel;
        private FieldBox _refNameBox;
        private FieldBox _refTextBox;
        private Label _refStatusLabel;
        private Label _refDetailTitle;
        private Label _refDetailSummary;
        private FieldBox _refTextBox2;
        private FieldBox _refCodeBox;
        private RoundedButton _refBackButton;
        private DarkScrollPanel _referenceScroll;
        private TileGridControl _referenceGrid;
        private ThumbnailCache _thumbnails;
        private readonly System.Windows.Forms.Timer _searchTimer;
        private readonly Dictionary<string, Dictionary<string, string>> _folderImages;

        private CardReferenceIndex _referenceIndex;
        private bool _referenceBuilding;
        private volatile bool _referenceStop;
        private CardReferenceEntry _referenceSelection;

        private CardEditorDraft _draft;
        private IList<SeriesFolder> _folders;

        /// <summary>卡号 -> 卡包 lookup used by 添加卡片, built on demand.</summary>
        private CardPackIndex _packIndex;

        /// <summary>True while the 卡包 chips of the page are shown.</summary>
        private bool _packChoicesVisible;

        /// <summary>Hint appended to the header while no pack was resolved.</summary>
        private string _packHint;

        /// <summary>True while the fields are filled, so typing does not resolve.</summary>
        private bool _loadingDraft;

        /// <summary>添加异画 section of the right column; hidden for a new card.</summary>
        private Control _altArtSection;
        private Label _altArtCaption;
        private FieldBox _altIdBox;
        private CardImageDropBox _altImageBox;
        private AltArtQueueStrip _altQueueStrip;
        private RoundedButton _altSaveButton;
        private RoundedButton _altClearButton;
        private Label _altStatusLabel;
        private ToolTip _altToolTip;

        /// <summary>Images waiting to become 异画, in the order they were added.</summary>
        private readonly List<AltArtQueueItem> _altQueue = new List<AltArtQueueItem>();

        /// <summary>Rows <see cref="ReadAltArts"/> handed to the writer, in request order.</summary>
        private List<AltArtQueueItem> _altSubmitted;

        /// <summary>Base card the queue was filled for; another card starts empty.</summary>
        private string _altQueueBaseId = string.Empty;

        /// <summary>True while the number field is filled from the queue.</summary>
        private bool _altSyncingId;

        /// <summary>
        /// Whether 添加异画 applies to the open card. Kept next to
        /// <see cref="Control.Visible"/> because a page that was never shown
        /// reports every child as invisible.
        /// </summary>
        private bool _altArtVisible;

        public event EventHandler CloseRequested;
        public event EventHandler SaveRequested;

        /// <summary>
        /// Raised when 保存 is clicked: the page hands the request to
        /// MainForm, which writes it through <see cref="AltArtworkWriter"/>.
        /// </summary>
        public event EventHandler AltArtSaveRequested;

        /// <summary>
        /// Card number of the base card 添加异画 writes for (`AW/S18-E101`).
        /// Empty hides the section, which is the case for 添加卡片: a card that
        /// is not in CardData yet has no variant to attach to.
        /// </summary>
        public string AltArtBaseCardId { get; set; }

        /// <summary>
        /// StreamingAssets\AlternateArtwork, assigned by MainForm. The page only
        /// uses it to preview the target path of a new variant.
        /// </summary>
        public string AlternateArtworkRoot { get; set; }

        /// <summary>
        /// Raised when the card number picked a different card pack, so
        /// MainForm can mirror the header line above the page.
        /// </summary>
        public event EventHandler TargetChanged;

        /// <summary>Card table the reference search reads; null disables it.</summary>
        public CardDatabase Database { get; set; }

        /// <summary>
        /// Chinese name table of the 触发 field: the field shows and accepts the
        /// names from here, while the card table itself keeps the English enum
        /// name. Null keeps the English names only.
        /// </summary>
        public TriggerGlossary Glossary { get; set; }

        /// <summary>
        /// 卡号 -> 卡包 lookup of 添加卡片. The page builds it from
        /// <see cref="Database"/> on demand; tests inject a synthetic one.
        /// </summary>
        public CardPackIndex PackIndex
        {
            get { return _packIndex; }
            set { _packIndex = value; }
        }

        /// <summary>
        /// Thumbnail cache handed to the reference grid. MainForm assigns it
        /// right after the page is built, so the setter also forwards it to a
        /// grid that already exists.
        /// </summary>
        public ThumbnailCache Thumbnails
        {
            get { return _thumbnails; }
            set
            {
                _thumbnails = value;
                if (_referenceGrid != null)
                    _referenceGrid.Thumbnails = value;
            }
        }

        /// <summary>Draft currently shown, updated by <see cref="ReadDraft"/>.</summary>
        public CardEditorDraft Draft
        {
            get { return _draft; }
        }

        /// <summary>
        /// Header line of the page (系列 · 卡包 · 卡号). MainForm mirrors it above
        /// the page whenever the card number picks another pack.
        /// </summary>
        public string Subtitle
        {
            get { return _subtitleLabel == null ? string.Empty : _subtitleLabel.Text; }
        }

        public CardEditorPage()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);

            BackColor = AppTheme.MainBackground;
            TabStop = true;
            Visible = false;

            _titleFont = CreateFont(15F, FontStyle.Bold);
            _subtitleFont = CreateFont(9F, FontStyle.Regular);
            _captionFont = CreateFont(8.5F, FontStyle.Regular);
            _bodyFont = CreateFont(9.5F, FontStyle.Regular);
            _chipFont = CreateFont(9F, FontStyle.Regular);
            _buttonFont = CreateFont(9.5F, FontStyle.Bold);
            _statusFont = CreateFont(9F, FontStyle.Regular);
            _folderImages = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            AltArtBaseCardId = string.Empty;
            AlternateArtworkRoot = string.Empty;

            _searchTimer = new System.Windows.Forms.Timer();
            _searchTimer.Interval = 260;
            _searchTimer.Tick += delegate
            {
                _searchTimer.Stop();
                RunReferenceSearch();
            };

            BuildPage();
            if (_idBox != null && _idBox.Input != null)
                _idBox.Input.TextChanged += IdBox_TextChanged;
        }

        private static Font CreateFont(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style,
                GraphicsUnit.Point, 134);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 布局回调还会量一次字体，字体释放后不再测量。
                _closed = true;
                _referenceStop = true;
                _searchTimer.Stop();
                _searchTimer.Dispose();
                _titleFont.Dispose();
                _subtitleFont.Dispose();
                _captionFont.Dispose();
                _bodyFont.Dispose();
                _chipFont.Dispose();
                _buttonFont.Dispose();
                _statusFont.Dispose();
                DisposePreview();
                for (int i = 0; i < _altQueue.Count; i++)
                    _altQueue[i].Dispose();
                _altQueue.Clear();
                _altSubmitted = null;
                if (_altToolTip != null)
                {
                    _altToolTip.Dispose();
                    _altToolTip = null;
                }
            }
            base.Dispose(disposing);
        }

        public void LoadDraft(CardEditorDraft draft, IList<SeriesFolder> folders)
        {
            if (draft == null)
                return;

            _draft = draft;
            _folders = folders;
            _loadingDraft = true;
            try
            {
                LoadDraftFields(draft);
            }
            finally
            {
                _loadingDraft = false;
            }

            ResetReference();
            EnsureReferenceIndex();

            SetStatus(draft.IsNew
                ? "卡号决定写入哪个卡包：填好卡号与卡名，把卡图拖进左上角的框，然后保存"
                : "修改后点保存：只改写改动过的行，其它卡、注释与简写保持原样（先备份 .bak）",
                false);

            if (draft.IsNew)
            {
                _idBox.Input.Focus();
                _idBox.Input.SelectAll();
            }
            else
            {
                _nameBox.Input.Focus();
                _nameBox.Input.SelectAll();
            }
        }

        /// <summary>Drops the draft and the preview when the page is left.</summary>
        public void UnloadDraft()
        {
            _draft = null;
            _folders = null;
            DisposePreview();
            _imageBox.SetImage(null, "拖入卡图，或点击选择文件");
            SetAltArtVisible(false);
            ClearAltArtQueue();
        }

        public void SetStatus(string text, bool error)
        {
            if (_statusLabel == null)
                return;

            _statusLabel.Text = text ?? string.Empty;
            _statusLabel.ForeColor = error ? AppTheme.Danger : AppTheme.TextMuted;
        }

        /// <summary>
        /// Called after a save: the reference list would still show the card as
        /// it was before the write, so it is rebuilt on the next search.
        /// </summary>
        public void InvalidateReference()
        {
            _referenceIndex = null;
            _packIndex = null;
            _folderImages.Clear();
            _referenceSelection = null;
            ShowReferenceGrid();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                // Esc first leaves the reference detail, then the editor.
                if (_referenceDetailPanel != null && _referenceDetailPanel.Visible)
                {
                    ShowReferenceGrid();
                    return true;
                }

                RaiseCloseRequested();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void RaiseCloseRequested()
        {
            EventHandler handler = CloseRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void BuildPage()
        {
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.BackColor = AppTheme.MainBackground;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            layout.Controls.Add(BuildHeader(), 0, 0);
            layout.Controls.Add(BuildBody(), 0, 1);
            layout.Controls.Add(BuildFooter(), 0, 2);
            Controls.Add(layout);
        }

        private Control BuildHeader()
        {
            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.BackColor = AppTheme.MainBackground;
            header.Margin = new Padding(0);
            header.Padding = new Padding(2, 6, 2, 4);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var titles = new TableLayoutPanel();
            titles.Dock = DockStyle.Fill;
            titles.ColumnCount = 1;
            titles.RowCount = 2;
            titles.BackColor = Color.Transparent;
            titles.Margin = new Padding(0);
            titles.Padding = new Padding(0);
            titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            titles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _titleLabel = new Label();
            _titleLabel.Text = "修改卡片";
            _titleLabel.Dock = DockStyle.Fill;
            _titleLabel.AutoEllipsis = true;
            _titleLabel.Font = _titleFont;
            _titleLabel.ForeColor = AppTheme.Text;
            _titleLabel.BackColor = Color.Transparent;
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _titleLabel.Margin = new Padding(0);
            titles.Controls.Add(_titleLabel, 0, 0);

            _subtitleLabel = new Label();
            _subtitleLabel.Text = string.Empty;
            _subtitleLabel.Dock = DockStyle.Fill;
            _subtitleLabel.AutoEllipsis = true;
            _subtitleLabel.Font = _subtitleFont;
            _subtitleLabel.ForeColor = AppTheme.TextMuted;
            _subtitleLabel.BackColor = Color.Transparent;
            _subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _subtitleLabel.Margin = new Padding(0);
            titles.Controls.Add(_subtitleLabel, 0, 1);

            header.Controls.Add(titles, 0, 0);

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.BackColor = Color.Transparent;
            actions.Margin = new Padding(0);
            actions.Padding = new Padding(0, 2, 0, 0);

            _saveButton = CreateActionButton("保存", AppTheme.Accent, Color.White, 96);
            _saveButton.Click += SaveButton_Click;
            actions.Controls.Add(_saveButton);

            _cancelButton = CreateActionButton("取消", ChipSurface, AppTheme.TextSoft, 96);
            _cancelButton.Click += delegate { RaiseCloseRequested(); };
            actions.Controls.Add(_cancelButton);

            header.Controls.Add(actions, 1, 0);
            return header;
        }

        private Control BuildFooter()
        {
            _statusLabel = new Label();
            _statusLabel.Text = string.Empty;
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Font = _statusFont;
            _statusLabel.ForeColor = AppTheme.TextMuted;
            _statusLabel.BackColor = AppTheme.MainBackground;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Margin = new Padding(0);
            _statusLabel.Padding = new Padding(2, 0, 0, 0);
            return _statusLabel;
        }

        private Control BuildBody()
        {
            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 3;
            body.RowCount = 1;
            body.BackColor = AppTheme.MainBackground;
            body.Margin = new Padding(0);
            body.Padding = new Padding(BodyPadding, 0, BodyPadding, 0);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37.5F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37.5F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Control left = BuildLeftColumn();
            left.Margin = new Padding(0, 0, ColumnGap / 2, 0);
            body.Controls.Add(left, 0, 0);

            Control middle = BuildEffectColumn();
            middle.Margin = new Padding(ColumnGap / 2, 0, ColumnGap / 2, 0);
            body.Controls.Add(middle, 1, 0);

            Control right = BuildReferenceColumn();
            right.Margin = new Padding(ColumnGap / 2, 0, 0, 0);
            body.Controls.Add(right, 2, 0);
            return body;
        }

        private RoundedButton CreateActionButton(string text, Color fill,
            Color fore, int width)
        {
            var button = new RoundedButton();
            button.Text = text;
            button.AutoSize = false;
            button.Size = new Size(width, 30);
            button.CornerRadius = 9;
            button.SurfaceColor = AppTheme.MainBackground;
            button.NormalColor = fill;
            button.HoverColor = ControlPaint.Light(fill, 0.16F);
            button.PressedColor = ControlPaint.Dark(fill, 0.08F);
            button.SelectedColor = fill;
            button.ForeColor = fore;
            button.Font = _buttonFont;
            button.Margin = new Padding(8, 0, 0, 0);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            if (ReadDraft() == null)
                return;

            EventHandler handler = SaveRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private Control BuildLeftColumn()
        {
            var column = new TableLayoutPanel();
            column.Dock = DockStyle.Fill;
            column.ColumnCount = 1;
            column.RowCount = 2;
            column.BackColor = Color.Transparent;
            column.Margin = new Padding(0);
            column.Padding = new Padding(0);
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _imageBox = new CardImageDropBox();
            _imageBox.Dock = DockStyle.Fill;
            _imageBox.Margin = new Padding(0, 0, 0, 8);
            _imageBox.CaptionFont = _captionFont;
            _imageBox.HintFont = _subtitleFont;
            _imageBox.SurfaceColor = PanelSurface;
            _imageBox.BrowseRequested += delegate { BrowseForImage(); };
            _imageBox.FileDropped += ImageBox_FileDropped;
            column.Controls.Add(_imageBox, 0, 0);

            column.Controls.Add(BuildInfoArea(), 0, 1);
            return column;
        }

        private Control BuildInfoArea()
        {
            _infoContent = new TableLayoutPanel();
            _infoContent.ColumnCount = 2;
            _infoContent.RowCount = 12;
            _infoContent.AutoSize = false;
            _infoContent.BackColor = Color.Transparent;
            _infoContent.Margin = new Padding(0);
            _infoContent.Padding = new Padding(0);
            _infoContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _infoContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            float[] heights =
            {
                InfoRowHeight, InfoRowHeight, InfoRowHeight, InfoRowHeight,
                ChipRowHeight, ChipRowHeight, ChipRowHeight,
                InfoRowHeight, InfoRowHeight, InfoRowHeight,
                InfoRowHeight, InfoRowHeight
            };
            for (int i = 0; i < heights.Length; i++)
                _infoContent.RowStyles.Add(new RowStyle(SizeType.Absolute, heights[i]));

            AddFieldCell(_infoContent, 0, 0, "卡号 (Id)", out _idBox);
            AddFieldCell(_infoContent, 1, 0, "图片 (Image)", out _imageIdBox);
            AddFieldCell(_infoContent, 0, 1, "卡名", out _nameBox, true);
            AddFieldCell(_infoContent, 0, 2, "日文名 (JPName)", out _jpNameBox, true);
            AddFieldCell(_infoContent, 0, 3, "联动 (Combo)", out _comboBox, true);

            _folderRow = CreateChipRow(true);
            _folderRow.SelectionChanged += FolderRow_SelectionChanged;
            _folderCell = WrapChips("卡包（新卡写入的位置）", _folderRow);
            _infoContent.Controls.Add(_folderCell, 0, 4);
            _infoContent.SetColumnSpan(_folderCell, 2);

            _categoryRow = CreateChipRow(false);
            _categoryRow.AddChip("角色", CardCategory.Character);
            _categoryRow.AddChip("事件", CardCategory.Event);
            _categoryRow.AddChip("高潮", CardCategory.Climax);
            AddChipCell(_infoContent, 5, "类别", _categoryRow);

            _colorRow = CreateChipRow(false);
            _colorRow.AddChip("黄", CardColor.Yellow);
            _colorRow.AddChip("绿", CardColor.Green);
            _colorRow.AddChip("红", CardColor.Red);
            _colorRow.AddChip("蓝", CardColor.Blue);
            _colorRow.AddChip("紫", CardColor.Purple);
            AddChipCell(_infoContent, 6, "颜色", _colorRow);

            AddFieldCell(_infoContent, 0, 7, "等级", out _levelBox);
            AddFieldCell(_infoContent, 1, 7, "COST", out _costBox);
            AddFieldCell(_infoContent, 0, 8, "战斗力", out _powerBox);
            AddFieldCell(_infoContent, 1, 8, "灵魂", out _soulBox);
            AddFieldCell(_infoContent, 0, 9, "反击", out _backupBox);
            AddFieldCell(_infoContent, 1, 9, "触发（可填中文或英文）", out _triggerBox);
            AddFieldCell(_infoContent, 0, 10, "特征 1", out _trait1Box);
            AddFieldCell(_infoContent, 1, 10, "特征 2", out _trait2Box);
            AddFieldCell(_infoContent, 0, 11, "特征 3", out _trait3Box);

            _infoScroll = new DarkScrollPanel();
            _infoScroll.Dock = DockStyle.Fill;
            _infoScroll.BackColor = Color.Transparent;
            _infoScroll.Margin = new Padding(0);
            _infoScroll.ScrollbarReserve = 10;
            _infoContent.Height = InfoTotalHeight(true);
            _infoScroll.Content = _infoContent;
            return _infoScroll;
        }

        private static int InfoTotalHeight(bool withFolder)
        {
            int total = 9 * InfoRowHeight + 2 * ChipRowHeight;
            if (withFolder)
                total += ChipRowHeight;
            return total;
        }

        private void AddFieldCell(TableLayoutPanel grid, int column, int row,
            string caption, out FieldBox box)
        {
            AddFieldCell(grid, column, row, caption, out box, false);
        }

        private void AddFieldCell(TableLayoutPanel grid, int column, int row,
            string caption, out FieldBox box, bool span)
        {
            box = CreateField(false);
            Control wrapper = WrapField(caption, box);
            grid.Controls.Add(wrapper, column, row);
            if (span)
                grid.SetColumnSpan(wrapper, 2);
        }

        private void AddChipCell(TableLayoutPanel grid, int row, string caption,
            ChipRow chips)
        {
            Control wrapper = WrapChips(caption, chips);
            grid.Controls.Add(wrapper, 0, row);
            grid.SetColumnSpan(wrapper, 2);
        }

        private FieldBox CreateField(bool multiline)
        {
            var box = new FieldBox();
            box.Multiline = multiline;
            box.Input.Font = _bodyFont;
            box.Input.ForeColor = AppTheme.Text;
            box.Input.BackColor = AppTheme.InputBackground;
            box.FillColor = AppTheme.InputBackground;
            box.BorderColor = AppTheme.Border;
            box.FocusBorderColor = AppTheme.Accent;
            box.CornerRadius = 7;
            box.BackColor = Color.Transparent;
            box.Margin = new Padding(0);
            return box;
        }

        private ChipRow CreateChipRow(bool wrap)
        {
            var row = new ChipRow(_chipFont, ChipSurface, AppTheme.Accent);
            row.Wrap = wrap;
            row.BackColor = Color.Transparent;
            row.Margin = new Padding(0);
            return row;
        }

        /// <summary>
        /// 中栏: one row per 效果, its 效果内容 above its own 效果代码. CardData.txt
        /// writes every `Text` line right below the ability that carries it, so
        /// a multi-effect card reads 内容 1 / 代码 1 / 内容 2 / 代码 2 here too
        /// instead of collecting every text above every ability. A single
        /// effect fills the column with the familiar 1 : 2 split; more effects
        /// scroll, each keeping its own pair.
        /// </summary>
        private Control BuildEffectColumn()
        {
            var column = new TableLayoutPanel();
            column.Dock = DockStyle.Fill;
            column.ColumnCount = 1;
            column.RowCount = 2;
            column.BackColor = Color.Transparent;
            column.Margin = new Padding(0);
            column.Padding = new Padding(0);
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            column.RowStyles.Add(new RowStyle(SizeType.Absolute, EffectToolbarHeight));

            _effectStack = new TableLayoutPanel();
            _effectStack.ColumnCount = 1;
            _effectStack.RowCount = 0;
            _effectStack.BackColor = Color.Transparent;
            _effectStack.Margin = new Padding(0);
            _effectStack.Padding = new Padding(0);
            _effectStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _effectScroll = new DarkScrollPanel();
            _effectScroll.Dock = DockStyle.Fill;
            _effectScroll.BackColor = Color.Transparent;
            _effectScroll.Margin = new Padding(0, 0, 0, 6);
            _effectScroll.ScrollbarReserve = EffectScrollbarReserve;
            _effectScroll.Content = _effectStack;
            _effectScroll.Resize += delegate { LayoutEffectRows(); };
            column.Controls.Add(_effectScroll, 0, 0);

            column.Controls.Add(BuildEffectToolbar(), 0, 1);

            SetEffectRows(new List<CardEffectPair> { new CardEffectPair() });
            return column;
        }

        private Control BuildEffectToolbar()
        {
            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.WrapContents = false;
            bar.BackColor = Color.Transparent;
            bar.Margin = new Padding(0);
            bar.Padding = new Padding(0);

            _addEffectButton = CreateActionButton("＋ 添加效果", ChipSurface,
                AppTheme.Text, 104);
            _addEffectButton.Margin = new Padding(0, 0, 8, 0);
            _addEffectButton.Click += delegate
            {
                AddEffectRow(new CardEffectPair(), true);
            };
            bar.Controls.Add(_addEffectButton);

            _removeEffectButton = CreateActionButton("移除最后一条", ChipSurface,
                AppTheme.TextMuted, 112);
            _removeEffectButton.Margin = new Padding(0);
            _removeEffectButton.Click += delegate { RemoveLastEffectRow(); };
            bar.Controls.Add(_removeEffectButton);
            return bar;
        }

        /// <summary>Fills the 效果 rows from the pairs of the opened card.</summary>
        private void SetEffectRows(IList<CardEffectPair> pairs)
        {
            ClearEffectRows();
            if (pairs == null || pairs.Count == 0)
                pairs = new List<CardEffectPair> { new CardEffectPair() };

            for (int i = 0; i < pairs.Count; i++)
                AddEffectRow(pairs[i], false);

            if (_effectScroll != null)
                _effectScroll.ScrollToTop();
        }

        private void ClearEffectRows()
        {
            for (int i = 0; i < _effectRows.Count; i++)
            {
                Control cell = _effectRows[i].Cell;
                if (_effectStack != null && _effectStack.Controls.Contains(cell))
                    _effectStack.Controls.Remove(cell);
                cell.Dispose();
            }

            _effectRows.Clear();
            _textBox = null;
            _codeBox = null;
            if (_effectStack != null)
            {
                _effectStack.RowStyles.Clear();
                _effectStack.RowCount = 0;
                _effectStack.Height = 0;
            }
        }

        private EffectRow AddEffectRow(CardEffectPair pair, bool focus)
        {
            EffectRow row = BuildEffectRow();
            row.Text.Text = ToDisplayText(pair == null ? string.Empty : pair.Text);
            row.Code.Text = ToDisplayText(pair == null ? string.Empty : pair.Code);
            ApplyInheritedText(row, pair == null ? string.Empty : pair.InheritedText);

            int index = _effectRows.Count;
            _effectRows.Add(row);
            _effectStack.RowCount = _effectRows.Count;
            _effectStack.RowStyles.Add(new RowStyle(SizeType.Absolute,
                EffectRowHeight + row.InheritedHeight));
            _effectStack.Controls.Add(row.Cell, 0, index);

            // 焦点默认落在第一条可写的 效果内容 上：简写行省掉了这个框。
            if (_textBox == null && !row.TextHidden)
                _textBox = row.Text;
            if (_codeBox == null)
                _codeBox = row.Code;

            RenumberEffectRows();
            LayoutEffectRows();
            if (focus)
            {
                if (_effectScroll != null)
                    _effectScroll.ScrollToBottom();
                row.Text.Input.Focus();
            }
            return row;
        }

        /// <summary>
        /// Fills the read-only 简写提供 band of one 效果 row. A 效果 without a
        /// `*Shorthand` keeps the band folded away, so a card that prints all
        /// its text itself looks exactly like before. A `*Shorthand` row whose
        /// 卡块 prints no `Text` of its own has nothing to type into: the empty
        /// 效果内容 box is omitted and its room goes to the 效果代码.
        /// </summary>
        private void ApplyInheritedText(EffectRow row, string text)
        {
            string display = ToDisplayText(text);
            if (row.Inherited != null)
                row.Inherited.Text = display;

            row.InheritedText = display;
            RefreshInheritedHeight(row);

            if (row.InheritedCell != null)
                row.InheritedCell.Visible = row.InheritedHeight > 0;

            row.TextHidden = row.InheritedHeight > 0 &&
                (row.Text == null || row.Text.Input.Text.Length == 0);
            if (row.TextCell != null)
                row.TextCell.Visible = !row.TextHidden;

            var cell = row.Cell as TableLayoutPanel;
            if (cell != null && cell.RowStyles.Count > 2)
            {
                cell.RowStyles[1].Height = row.InheritedHeight;
                cell.RowStyles[0].SizeType = row.TextHidden
                    ? SizeType.Absolute
                    : SizeType.Percent;
                cell.RowStyles[0].Height = row.TextHidden ? 0F : 33.34F;
                cell.RowStyles[2].Height = row.TextHidden ? 100F : 66.66F;
            }
        }

        /// <summary>
        /// Re-measures the 简写提供 band of one row against the width the 效果
        /// column currently gives it and hands the new height to the row.
        /// </summary>
        private void RefreshInheritedHeight(EffectRow row)
        {
            if (row == null || _closed)
                return;

            int height = string.IsNullOrEmpty(row.InheritedText)
                ? 0
                : InheritedPreviewHeight(row.InheritedText, InheritedTextWidth());
            if (height == row.InheritedHeight)
                return;

            row.InheritedHeight = height;
            var cell = row.Cell as TableLayoutPanel;
            if (cell != null && cell.RowStyles.Count > 1)
                cell.RowStyles[1].Height = height;
        }

        /// <summary>
        /// Inner width the 简写提供 box gets: the 效果 column minus its scrollbar,
        /// the right margin and the padding of the box. 0 while the column has
        /// not been laid out yet, so a card opened before the page is shown
        /// falls back to counting the lines of the text.
        /// </summary>
        private int InheritedTextWidth()
        {
            if (_effectScroll == null)
                return 0;

            int width = _effectScroll.ClientSize.Width - EffectScrollbarReserve -
                EffectRowRightMargin - FieldBoxInnerPadding -
                FieldBoxScrollbarWidth;
            return width < 80 ? 0 : width;
        }

        /// <summary>
        /// Height the 简写提供 band needs for <paramref name="display"/>: the
        /// caption row on top, one text line per line the 简写 wraps to, plus
        /// the padding a <see cref="FieldBox"/> keeps around its input and the
        /// gap to the 效果代码 below. A one-line 简写 therefore gets a
        /// one-line box, a two-line one a two-line box, and nothing is clipped
        /// until it passes <see cref="InheritedMaxLines"/>.
        /// </summary>
        private int InheritedPreviewHeight(string display, int width)
        {
            int lineHeight = Math.Max(16, _bodyFont.Height + 2);
            int lines = 1;
            for (int i = 0; i < display.Length; i++)
            {
                if (display[i] == '\n')
                    lines++;
            }

            if (width > 0)
            {
                const TextFormatFlags wrapFlags =
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
                var limit = new Size(width, MeasureHeightLimit);
                Size oneLine = TextRenderer.MeasureText("測", _bodyFont,
                    limit, wrapFlags);
                Size wrapped = TextRenderer.MeasureText(display, _bodyFont,
                    limit, wrapFlags);
                if (oneLine.Height > 0)
                {
                    int wrappedLines = (int)Math.Round(
                        wrapped.Height / (double)oneLine.Height);
                    if (wrappedLines > lines)
                        lines = wrappedLines;
                }
            }

            if (lines > InheritedMaxLines)
                lines = InheritedMaxLines;

            return CaptionHeight + lines * lineHeight + FieldBoxInnerPadding +
                InheritedBandGap;
        }

        private void RemoveLastEffectRow()
        {
            if (_effectRows.Count <= 1)
                return;

            EffectRow row = _effectRows[_effectRows.Count - 1];
            _effectRows.RemoveAt(_effectRows.Count - 1);
            _effectStack.RowStyles.RemoveAt(_effectStack.RowStyles.Count - 1);
            _effectStack.RowCount = _effectRows.Count;
            _effectStack.Controls.Remove(row.Cell);
            row.Cell.Dispose();

            RenumberEffectRows();
            LayoutEffectRows();
        }

        private EffectRow BuildEffectRow()
        {
            var row = new EffectRow();

            var cell = new TableLayoutPanel();
            cell.Dock = DockStyle.Fill;
            cell.ColumnCount = 1;
            cell.RowCount = 3;
            cell.BackColor = Color.Transparent;
            cell.Margin = new Padding(0, 0, 0, EffectRowGap);
            cell.Padding = new Padding(0);
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            // 效果内容 : 效果代码 = 1 : 2，代码块通常比卡面文字长得多。中间的
            // 简写提供预览默认 0 高：只有真的带 `*Shorthand` 的效果才占地方。
            cell.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));
            cell.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            cell.RowStyles.Add(new RowStyle(SizeType.Percent, 66.66F));
            row.Cell = cell;

            Label textCaption;
            row.Text = CreateField(true);
            Control textCell = WrapControl(string.Empty, row.Text,
                new Padding(0, 0, EffectRowRightMargin, 2), out textCaption);
            row.TextCaption = textCaption;
            row.TextCell = textCell;
            cell.Controls.Add(textCell, 0, 0);

            // 简写提供: the text a `*Shorthand` copies in, read-only right above
            // the 效果代码 it belongs to. It gets its own caption row — the same
            // 效果 N · … title the 内容 / 代码 boxes carry — and the box below it
            // spans the whole column, so the band is a normally stacked caption
            // plus field and not a two-column strip.
            row.Inherited = CreateField(true);
            row.Inherited.Input.ReadOnly = true;
            row.Inherited.Input.ForeColor = AppTheme.TextMuted;
            row.Inherited.Input.BackColor = AppTheme.InputBackground;

            Label inheritedCaption;
            Control inheritedCell = WrapControl(InheritedLabel, row.Inherited,
                new Padding(0, 0, EffectRowRightMargin, InheritedBandGap),
                out inheritedCaption);
            row.InheritedCaption = inheritedCaption;

            row.InheritedCell = inheritedCell;
            inheritedCell.Visible = false;
            cell.Controls.Add(inheritedCell, 0, 1);

            Label codeCaption;
            row.Code = CreateField(true);
            Control codeCell = WrapControl(string.Empty, row.Code,
                new Padding(0, 0, EffectRowRightMargin, 0), out codeCaption);
            row.CodeCaption = codeCaption;
            cell.Controls.Add(codeCell, 0, 2);
            return row;
        }

        /// <summary>
        /// Captions carry the 效果 number once there is more than one, so the
        /// player can tell which text belongs to which code block.
        /// </summary>
        private void RenumberEffectRows()
        {
            bool single = _effectRows.Count <= 1;
            for (int i = 0; i < _effectRows.Count; i++)
            {
                EffectRow row = _effectRows[i];
                string head = single ? "效果" : "效果 " + (i + 1) + " · ";
                row.TextCaption.Text = head + "内容（卡面上印的效果文字）";
                row.CodeCaption.Text = head + "代码（Act / Auto / Cont 与 { } 代码块）";

                // 简写提供 的标题跟 内容 / 代码 一样带编号；只有一条效果时它就是
                // 一行普通标题，不再缩成左边一列。
                row.InheritedCaption.Text = single
                    ? InheritedLabel
                    : "效果 " + (i + 1) + " · " + InheritedLabel;
            }

            if (_removeEffectButton != null)
                _removeEffectButton.Enabled = _effectRows.Count > 1;
        }

        /// <summary>
        /// One 效果 fills the column, so a card with a single effect keeps the
        /// roomy 1 : 2 split; several effects get a readable fixed height each
        /// and the column scrolls. A 效果 that carries `*Shorthand` text adds
        /// room for the 简写提供 band — its caption row, the text it shows and
        /// the gap to the 效果代码 below — so the band never overlaps the code.
        /// </summary>
        private void LayoutEffectRows()
        {
            if (_effectStack == null || _effectRows.Count == 0 ||
                _layingOutEffects)
                return;

            _layingOutEffects = true;
            try
            {
                int viewport = _effectScroll == null ? 0 : _effectScroll.ClientSize.Height;
                int baseHeight = _effectRows.Count <= 1
                    ? Math.Max(EffectRowHeight, viewport)
                    : EffectRowHeight;

                bool moved = false;
                int total = 0;
                for (int i = 0; i < _effectRows.Count && i < _effectStack.RowStyles.Count; i++)
                {
                    // A column resize re-wraps the 简写提供 text, so the band of
                    // every row is measured again before the rows are stacked.
                    RefreshInheritedHeight(_effectRows[i]);

                    int height = baseHeight + _effectRows[i].InheritedHeight;
                    if (Math.Abs(_effectStack.RowStyles[i].Height - height) > 0.5F)
                    {
                        _effectStack.RowStyles[i].Height = height;
                        moved = true;
                    }
                    total += height;
                }

                if (_effectStack.Height != total)
                {
                    _effectStack.Height = total;
                    moved = true;
                }

                if (moved)
                    _effectStack.PerformLayout();
            }
            finally
            {
                _layingOutEffects = false;
            }
        }

        private Control WrapField(string caption, FieldBox box)
        {
            return WrapControl(caption, box, new Padding(0, 0, 8, 2));
        }

        private Control WrapChips(string caption, ChipRow chips)
        {
            return WrapControl(caption, chips, new Padding(0, 0, 0, 2));
        }

        private Control WrapControl(string caption, Control content, Padding margin)
        {
            Label captionLabel;
            return WrapControl(caption, content, margin, out captionLabel);
        }

        private Control WrapControl(string caption, Control content, Padding margin,
            out Label captionLabel)
        {
            var wrapper = new TableLayoutPanel();
            wrapper.Dock = DockStyle.Fill;
            wrapper.ColumnCount = 1;
            wrapper.RowCount = 2;
            wrapper.BackColor = Color.Transparent;
            wrapper.Margin = margin;
            wrapper.Padding = new Padding(0);
            wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, CaptionHeight));
            wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var label = new Label();
            captionLabel = label;
            label.Text = caption ?? string.Empty;
            label.Dock = DockStyle.Fill;
            label.AutoEllipsis = true;
            label.Font = _captionFont;
            label.ForeColor = AppTheme.TextMuted;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(2, 0, 0, 0);
            wrapper.Controls.Add(label, 0, 0);

            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0);
            wrapper.Controls.Add(content, 0, 1);
            return wrapper;
        }

        private Control BuildReferenceColumn()
        {
            var column = new TableLayoutPanel();
            column.Dock = DockStyle.Fill;
            column.ColumnCount = 1;
            column.RowCount = 2;
            column.BackColor = Color.Transparent;
            column.Margin = new Padding(0);
            column.Padding = new Padding(0);
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            // Row 0 is 添加异画, folded to zero while it does not apply; the
            // reference panel below always keeps the rest of the column.
            column.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _altArtSection = BuildAltArtSection();
            _altArtSection.Visible = false;
            column.Controls.Add(_altArtSection, 0, 0);

            _referenceBody = new Panel();
            _referenceBody.Dock = DockStyle.Fill;
            _referenceBody.BackColor = Color.Transparent;
            _referenceBody.Margin = new Padding(0);

            _referenceSearchPanel = BuildReferenceSearchPanel();
            _referenceBody.Controls.Add(_referenceSearchPanel);

            _referenceDetailPanel = BuildReferenceDetailPanel();
            _referenceDetailPanel.Visible = false;
            _referenceBody.Controls.Add(_referenceDetailPanel);

            column.Controls.Add(_referenceBody, 0, 1);
            return column;
        }

        /// <summary>
        /// 添加异画: the queue of variant numbers plus the art that carries them.
        /// A variant lives only as an image below
        /// StreamingAssets\AlternateArtwork, so this block writes files and
        /// never touches CardData.txt. The height stays 168px and the pictures
        /// queue sideways instead of making the block taller. The bottom band
        /// splits left / right: the thumbnail queue on the left, the 保存 /
        /// 清空 pair stacked on the right, so the tiles get the row the buttons
        /// used to cross and the preview keeps the full 131px body row.
        /// </summary>
        private Control BuildAltArtSection()
        {
            var host = new TableLayoutPanel();
            host.Dock = DockStyle.Fill;
            host.ColumnCount = 2;
            host.RowCount = 2;
            host.BackColor = PanelSurface;
            host.Margin = new Padding(0, 0, 0, 8);
            host.Padding = new Padding(10, 6, 10, 6);
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, CaptionHeight));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            // Column 0 is the preview, column 1 the 卡号 / 提示 / 队列 + 按钮 stack.
            host.ColumnStyles.Add(new ColumnStyle(
                SizeType.Absolute, AltArtImageWidth));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _altArtCaption = CreateCaption("添加异画");
            _altArtCaption.ForeColor = AppTheme.TextSoft;
            host.Controls.Add(_altArtCaption, 0, 0);
            host.SetColumnSpan(_altArtCaption, 2);

            // The hint line under the number is one row of text; a tooltip
            // carries the full 写入 … sentence that rarely fits the column.
            _altToolTip = new ToolTip();

            var right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.RowCount = 3;
            right.BackColor = Color.Transparent;
            right.Margin = new Padding(0);
            right.Padding = new Padding(0);
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, AltArtHintHeight));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _altIdBox = CreateField(false);
            _altIdBox.Dock = DockStyle.Fill;
            _altIdBox.Input.TextChanged += AltIdBox_TextChanged;
            right.Controls.Add(_altIdBox, 0, 0);

            _altStatusLabel = new Label();
            _altStatusLabel.Text = string.Empty;
            _altStatusLabel.Dock = DockStyle.Fill;
            _altStatusLabel.AutoEllipsis = true;
            _altStatusLabel.Font = _captionFont;
            _altStatusLabel.ForeColor = AppTheme.TextMuted;
            _altStatusLabel.BackColor = Color.Transparent;
            _altStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _altStatusLabel.Margin = new Padding(2, 0, 0, 0);
            right.Controls.Add(_altStatusLabel, 0, 1);

            // The band under the hint splits left / right: the queue takes the
            // width, 保存 / 清空 keep a 60px column. Losing the old 30px button
            // row lets the tiles grow from ~35x49 to ~56x79.
            var band = new TableLayoutPanel();
            band.Dock = DockStyle.Fill;
            band.ColumnCount = 2;
            band.RowCount = 1;
            band.BackColor = Color.Transparent;
            band.Margin = new Padding(0);
            band.Padding = new Padding(0);
            band.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            band.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            band.ColumnStyles.Add(new ColumnStyle(
                SizeType.Absolute, AltArtActionWidth));

            _altQueueStrip = new AltArtQueueStrip();
            _altQueueStrip.Dock = DockStyle.Fill;
            _altQueueStrip.Margin = new Padding(0, 2, 8, 0);
            _altQueueStrip.BadgeFont = _captionFont;
            _altQueueStrip.SelectedColor = AppTheme.Accent;
            _altQueueStrip.ProblemColor = AppTheme.Danger;
            _altQueueStrip.SelectionChanged += AltQueueStrip_SelectionChanged;
            _altQueueStrip.RemoveRequested += AltQueueStrip_RemoveRequested;
            _altQueueStrip.AddRequested += delegate { BrowseForAltArtImages(); };
            band.Controls.Add(_altQueueStrip, 0, 0);

            var actions = new FlowLayoutPanel();
            // Top to bottom and pinned to the bottom of the band: 保存 sits
            // above 清空, and 清空 ends flush with the bottom right corner the
            // pair has always hugged.
            actions.Dock = DockStyle.Bottom;
            actions.Height = AltArtActionHeight * 2 + AltArtActionGap;
            actions.FlowDirection = FlowDirection.TopDown;
            actions.WrapContents = false;
            actions.BackColor = Color.Transparent;
            actions.Margin = new Padding(0);
            actions.Padding = new Padding(0);

            _altSaveButton = CreateActionButton("保存", ChipSurface,
                AppTheme.TextSoft, AltArtActionWidth);
            _altSaveButton.Margin = new Padding(0, 0, 0, AltArtActionGap);
            _altSaveButton.Click += delegate { RaiseAltArtSaveRequested(); };
            actions.Controls.Add(_altSaveButton);

            _altClearButton = CreateActionButton("清空", PanelSurface,
                AppTheme.TextMuted, AltArtActionWidth);
            _altClearButton.Margin = new Padding(0);
            _altClearButton.Click += delegate { ClearAltArtQueue(); };
            actions.Controls.Add(_altClearButton);

            band.Controls.Add(actions, 1, 0);
            right.Controls.Add(band, 0, 2);
            host.Controls.Add(right, 1, 1);

            _altImageBox = new CardImageDropBox();
            _altImageBox.Dock = DockStyle.Fill;
            _altImageBox.Margin = new Padding(0, 0, 8, 0);
            // No caption and a 6px frame instead of 12: the cell runs from the
            // caption all the way down to the bottom of the block (131px tall),
            // so it draws an ~85x119 card instead of the ~65x91 one it had
            // while the buttons crossed the whole block on a row of their own.
            _altImageBox.ShowCaption = false;
            _altImageBox.InnerPadding = 6;
            _altImageBox.HintFont = _captionFont;
            _altImageBox.SurfaceColor = PanelSurface;
            _altImageBox.BrowseRequested += delegate { BrowseForAltArtImages(); };
            _altImageBox.FileDropped += AltImageBox_FileDropped;
            host.Controls.Add(_altImageBox, 0, 1);

            return host;
        }

        private Panel BuildReferenceSearchPanel()
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 4;
            panel.BackColor = Color.Transparent;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(0);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, SearchRowHeight));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, SearchRowHeight));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _refNameBox = CreateField(false);
            _refNameBox.Input.TextChanged += delegate { RestartReferenceSearch(); };
            panel.Controls.Add(WrapField("卡名 / 卡号（全部卡表）", _refNameBox), 0, 0);

            _refTextBox = CreateField(false);
            _refTextBox.Input.TextChanged += delegate { RestartReferenceSearch(); };
            panel.Controls.Add(WrapField("效果文字 / 效果代码", _refTextBox), 0, 1);

            _refStatusLabel = new Label();
            _refStatusLabel.Text = "参考卡表：正在准备…";
            _refStatusLabel.Dock = DockStyle.Fill;
            _refStatusLabel.AutoEllipsis = true;
            _refStatusLabel.Font = _captionFont;
            _refStatusLabel.ForeColor = AppTheme.TextMuted;
            _refStatusLabel.BackColor = Color.Transparent;
            _refStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _refStatusLabel.Margin = new Padding(2, 0, 0, 0);
            panel.Controls.Add(_refStatusLabel, 0, 2);

            _referenceScroll = new DarkScrollPanel();
            _referenceScroll.Dock = DockStyle.Fill;
            _referenceScroll.BackColor = AppTheme.MainBackground;
            _referenceScroll.Margin = new Padding(0);
            _referenceScroll.ScrollbarReserve = 8;
            _referenceScroll.Resize += delegate
            {
                if (_referenceGrid != null)
                    _referenceGrid.PageViewportHeight = _referenceScroll.ClientSize.Height;
            };

            _referenceGrid = new TileGridControl();
            _referenceGrid.ExpandToContent = true;
            _referenceGrid.Thumbnails = _thumbnails;
            _referenceGrid.BackColor = AppTheme.MainBackground;
            _referenceGrid.Margin = new Padding(0);
            _referenceGrid.ConfigureCards();
            _referenceGrid.TileWidth = 118;
            _referenceGrid.TileHeight = 214;
            _referenceGrid.ImageWidth = 112;
            _referenceGrid.ImageHeight = 157;
            _referenceGrid.GridPadding = 4;
            _referenceGrid.Gap = 6;
            _referenceGrid.EmptyText = "输入卡名 / 卡号，或效果文字";
            _referenceGrid.ItemClick += ReferenceGrid_ItemClick;
            _referenceGrid.ScrollToTopRequested += delegate
            {
                if (_referenceScroll != null)
                    _referenceScroll.ScrollToTop();
            };
            _referenceScroll.Content = _referenceGrid;
            panel.Controls.Add(_referenceScroll, 0, 3);
            return panel;
        }

        private Panel BuildReferenceDetailPanel()
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 6;
            panel.BackColor = Color.Transparent;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(0);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, CaptionHeight));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, CaptionHeight));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.BackColor = Color.Transparent;
            header.Margin = new Padding(0);
            header.Padding = new Padding(0);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _refDetailTitle = new Label();
            _refDetailTitle.Text = string.Empty;
            _refDetailTitle.Dock = DockStyle.Fill;
            _refDetailTitle.AutoEllipsis = true;
            _refDetailTitle.Font = _buttonFont;
            _refDetailTitle.ForeColor = AppTheme.Text;
            _refDetailTitle.BackColor = Color.Transparent;
            _refDetailTitle.TextAlign = ContentAlignment.MiddleLeft;
            _refDetailTitle.Margin = new Padding(2, 0, 0, 0);
            header.Controls.Add(_refDetailTitle, 0, 0);

            _refBackButton = CreateActionButton("← 返回搜索", ChipSurface,
                AppTheme.TextSoft, 84);
            _refBackButton.Margin = new Padding(0);
            _refBackButton.Click += delegate { ShowReferenceGrid(); };
            header.Controls.Add(_refBackButton, 1, 0);
            panel.Controls.Add(header, 0, 0);

            _refDetailSummary = new Label();
            _refDetailSummary.Text = string.Empty;
            _refDetailSummary.Dock = DockStyle.Fill;
            _refDetailSummary.AutoEllipsis = true;
            _refDetailSummary.Font = _captionFont;
            _refDetailSummary.ForeColor = AppTheme.TextMuted;
            _refDetailSummary.BackColor = Color.Transparent;
            _refDetailSummary.TextAlign = ContentAlignment.MiddleLeft;
            _refDetailSummary.Margin = new Padding(2, 0, 0, 0);
            panel.Controls.Add(_refDetailSummary, 0, 1);

            panel.Controls.Add(CreateCaption("效果内容（参考，可选中复制）"), 0, 2);

            _refTextBox2 = CreateField(true);
            _refTextBox2.Input.ReadOnly = true;
            // 不设 Dock 时 FieldBox 会保持默认 200×100 的左上角尺寸，
            // 点开参考卡时效果框看起来只有一小块，所以这里必须填满行。
            _refTextBox2.Dock = DockStyle.Fill;
            _refTextBox2.Margin = new Padding(0, 0, 0, 6);
            panel.Controls.Add(_refTextBox2, 0, 3);

            panel.Controls.Add(CreateCaption("效果代码（参考，可选中复制）"), 0, 4);

            _refCodeBox = CreateField(true);
            _refCodeBox.Input.ReadOnly = true;
            _refCodeBox.Dock = DockStyle.Fill;
            _refCodeBox.Margin = new Padding(0);
            panel.Controls.Add(_refCodeBox, 0, 5);
            return panel;
        }

        private Label CreateCaption(string text)
        {
            var label = new Label();
            label.Text = text ?? string.Empty;
            label.Dock = DockStyle.Fill;
            label.AutoEllipsis = true;
            label.Font = _captionFont;
            label.ForeColor = AppTheme.TextMuted;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(2, 0, 0, 0);
            return label;
        }

        private void LoadDraftFields(CardEditorDraft draft)
        {
            _titleLabel.Text = draft.IsNew ? "添加卡片" : "修改卡片";

            _idBox.Text = draft.Id ?? string.Empty;
            _nameBox.Text = draft.Name ?? string.Empty;
            _jpNameBox.Text = draft.JapaneseName ?? string.Empty;
            _imageIdBox.Text = draft.ImageId ?? string.Empty;
            _levelBox.Text = draft.Level.ToString();
            _costBox.Text = draft.Cost.ToString();
            _powerBox.Text = draft.Power.ToString();
            _soulBox.Text = draft.Soul.ToString();
            _backupBox.Text = draft.Backup.ToString();
            _triggerBox.Text = draft.Trigger == TriggerIcon.None
                ? string.Empty
                : TriggerName(draft.Trigger);
            _trait1Box.Text = draft.Trait1 ?? string.Empty;
            _trait2Box.Text = draft.Trait2 ?? string.Empty;
            _trait3Box.Text = draft.Trait3 ?? string.Empty;
            _comboBox.Text = draft.Combo ?? string.Empty;
            SetEffectRows(CardEffectSplitter.PairDraft(draft));

            _categoryRow.Select(draft.Category);
            _colorRow.Select(draft.Color);
            _powerBox.Input.Enabled = draft.Category == CardCategory.Character;

            UpdatePackTarget();
            ShowPreview(draft.PreviewImagePath);
            _subtitleLabel.Text = BuildSubtitle();
            RefreshAltArtSection();
        }

        /// <summary>
        /// Name shown in the 触发 field: the shared Chinese table first, then
        /// the English enum name so a card without a Chinese name still reads
        /// exactly like the card table.
        /// </summary>
        private string TriggerName(TriggerIcon trigger)
        {
            return Glossary == null ? trigger.ToString() : Glossary.GetName(trigger);
        }

        /// <summary>
        /// Fills 卡包 from the 卡号: 添加卡片 opens without a series, and the
        /// head of the number (AW/S18 for AW/S18-E101) names the pack the new
        /// block has to be written to. A prefix several packs declare (promo
        /// folders) is offered as chips; a prefix that matches nothing keeps
        /// the packs of the series the editor was opened from.
        /// </summary>
        private void UpdatePackTarget()
        {
            CardEditorDraft draft = _draft;
            if (draft == null || _folderRow == null)
                return;

            if (!draft.IsNew)
            {
                _packHint = null;
                _folderRow.ClearChips();
                SetFolderRowVisible(false);
                RefreshHeader();
                return;
            }

            string id = _idBox.Text.Trim();
            if (id.Length == 0)
            {
                // 添加卡片 opens with an empty 卡号: the number decides the pack,
                // so the row must not offer every pack of the series (it used
                // to stay filled after 修改卡片 and to list all chips whenever
                // the series has more than one pack).
                _packHint = draft.HasFolder ? null : "输入卡号后自动分配卡包";
                _folderRow.ClearChips();
                SetFolderRowVisible(false);
                RefreshHeader();
                return;
            }

            CardPackIndex index = EnsurePackIndex();
            IList<CardPackTarget> targets = index == null ? null : index.Find(id);

            if (targets != null && targets.Count > 0)
            {
                CardPackTarget chosen = index.Resolve(id);
                if (chosen == null)
                    chosen = targets[0];

                _packHint = null;
                _folderRow.ClearChips();
                if (targets.Count == 1)
                {
                    SetFolderRowVisible(false);
                }
                else
                {
                    for (int i = 0; i < targets.Count; i++)
                        _folderRow.AddChip(targets[i].DisplayLabel, targets[i]);
                    _folderRow.Select(chosen);
                    SetFolderRowVisible(true);
                }

                ApplyPackTarget(draft, chosen);
                RefreshHeader();
                return;
            }

            _packHint = "卡号前缀 " + CardPackIndex.CardIdHead(id) + " 没有匹配的卡包";

            if (_folders != null && _folders.Count > 0)
                ShowSeriesFolders(draft);
            else
                ClearPackTarget(draft);

            RefreshHeader();
        }

        /// <summary>
        /// Drops the pack of a draft whose 卡号 names none, so 保存 refuses with
        /// a reason instead of writing into the pack picked before.
        /// </summary>
        private void ClearPackTarget(CardEditorDraft draft)
        {
            _folderRow.ClearChips();
            SetFolderRowVisible(false);
            draft.SeriesName = string.Empty;
            draft.FolderPath = string.Empty;
            draft.FolderFullPath = string.Empty;
            draft.FileName = "CardData";
            draft.SetName = string.Empty;
        }

        /// <summary>
        /// Falls back to the packs of the series the editor was opened from.
        /// A blank 添加卡片 opened from the sidebar has nothing to fall back to.
        /// </summary>
        private void ShowSeriesFolders(CardEditorDraft draft)
        {
            _folderRow.ClearChips();
            if (_folders == null || _folders.Count == 0)
            {
                SetFolderRowVisible(false);
                return;
            }

            for (int i = 0; i < _folders.Count; i++)
                _folderRow.AddChip(_folders[i].ToString(), _folders[i]);

            SeriesFolder current = FindFolder(draft.FolderPath);
            if (current == null)
                current = _folders[0];
            _folderRow.Select(current);
            SetFolderRowVisible(_folders.Count > 1);
            CardEditorDraft.UseFolder(draft, current);
        }

        /// <summary>Points the draft at the pack a chip or the card number picked.</summary>
        private void ApplyPackTarget(CardEditorDraft draft, CardPackTarget target)
        {
            if (draft == null || target == null || target.Folder == null)
                return;

            if (string.IsNullOrEmpty(target.Folder.FullPath))
                target.Folder.FullPath = target.FolderFullPath;
            CardEditorDraft.UseFolder(draft, target.Folder);
        }

        /// <summary>Applies the chip the player clicked, whichever kind it holds.</summary>
        private void ApplyPackChoice()
        {
            if (_draft == null || _folderRow == null)
                return;

            if (_folderRow.SelectedValue is CardPackTarget)
                ApplyPackTarget(_draft, (CardPackTarget)_folderRow.SelectedValue);
            else if (_folderRow.SelectedValue is SeriesFolder)
                CardEditorDraft.UseFolder(_draft, (SeriesFolder)_folderRow.SelectedValue);
        }

        private void FolderRow_SelectionChanged(object sender, EventArgs e)
        {
            if (_draft == null)
                return;

            ApplyPackChoice();
            RefreshHeader();
        }

        private SeriesFolder FindFolder(string folderPath)
        {
            if (_folders == null)
                return null;

            for (int i = 0; i < _folders.Count; i++)
            {
                if (string.Equals(_folders[i].FolderPath, folderPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return _folders[i];
                }
            }
            return null;
        }

        private void SetFolderRowVisible(bool visible)
        {
            _packChoicesVisible = visible;
            if (_folderCell != null)
                _folderCell.Visible = visible;

            if (_infoContent != null)
            {
                _infoContent.RowStyles[4].Height = visible ? ChipRowHeight : 0F;
                _infoContent.Height = InfoTotalHeight(visible);
            }

            if (_infoScroll != null)
                _infoScroll.PerformLayout();
        }

        /// <summary>Card pack lookup of 添加卡片, built from the card tables once.</summary>
        private CardPackIndex EnsurePackIndex()
        {
            if (_packIndex == null && Database != null)
                _packIndex = CardPackIndex.Build(Database);
            return _packIndex;
        }

        /// <summary>
        /// Typing a 卡号 picks the 卡包 (and with it the series) right away, so
        /// the header and the chips always describe where 保存 will write.
        /// </summary>
        private void IdBox_TextChanged(object sender, EventArgs e)
        {
            if (_loadingDraft || _draft == null || !_draft.IsNew)
                return;

            UpdatePackTarget();
        }

        private void RefreshHeader()
        {
            if (_subtitleLabel != null)
                _subtitleLabel.Text = BuildSubtitle();

            EventHandler handler = TargetChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        /// <summary>Header line: 系列 · 卡包 · 卡号, plus the hint while unresolved.</summary>
        private string BuildSubtitle()
        {
            if (_draft == null)
                return string.Empty;

            string id = _draft.IsNew ? _idBox.Text.Trim() : _draft.Id;
            string text = _draft.TargetLabel;
            if (!string.IsNullOrEmpty(id))
                text += " · " + id;
            if (string.IsNullOrEmpty(_packHint))
                return text;
            return text + " · " + _packHint;
        }

        private void ShowPreview(string path)
        {
            DisposePreview();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    using (Image source = Image.FromFile(path))
                        _preview = new Bitmap(source);
                }
                catch (Exception)
                {
                    _preview = null;
                }
            }

            _imageBox.SetImage(_preview, _preview == null
                ? "拖入卡图，或点击选择文件"
                : "拖入新卡图可替换");
        }

        private void DisposePreview()
        {
            if (_preview == null)
                return;

            _preview.Dispose();
            _preview = null;
        }

        private void BrowseForImage()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择卡图";
                dialog.Filter =
                    "卡图 (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|" +
                    "*.png;*.jpg;*.jpeg;*.webp;*.bmp";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                    return;

                ApplyPickedImage(dialog.FileName);
            }
        }

        private void ImageBox_FileDropped(object sender, CardImageDroppedEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.Path))
                return;

            ApplyPickedImage(e.Path);
        }

        private void ApplyPickedImage(string path)
        {
            if (_draft == null || string.IsNullOrEmpty(path))
                return;

            if (!CardDataWriter.IsSupportedImage(path))
            {
                SetStatus("只支持 png / jpg / jpeg / webp / bmp 卡图", true);
                return;
            }

            _draft.ImageSourcePath = path;
            _draft.PreviewImagePath = path;
            ShowPreview(path);
            SetStatus("保存时会把卡图复制到卡包目录：" + Path.GetFileName(path), false);
        }

        /// <summary>
        /// Shows or folds the 添加异画 block. It only makes sense for a card
        /// that already has a CardData block and a pack with a Prefix line,
        /// because the game finds a variant through its image alone.
        /// </summary>
        private void RefreshAltArtSection()
        {
            bool visible = _draft != null && !_draft.IsNew &&
                !string.IsNullOrEmpty(AltArtBaseCardId) &&
                !string.IsNullOrEmpty(_draft.Prefix);
            SetAltArtVisible(visible);
            if (!visible)
                return;

            // The queue belongs to one base card: opening another card (or the
            // same one after an id edit) starts over instead of saving letters
            // that name the wrong card.
            if (!string.Equals(_altQueueBaseId, AltArtBaseCardId,
                StringComparison.OrdinalIgnoreCase))
            {
                _altQueueBaseId = AltArtBaseCardId ?? string.Empty;
                ClearAltArtQueue();
            }
            else
            {
                RefreshAltArtQueue(SelectedAltArtIndex);
            }
        }

        /// <summary>Row the number field and the preview belong to.</summary>
        private int SelectedAltArtIndex
        {
            get { return _altQueueStrip == null ? -1 : _altQueueStrip.SelectedIndex; }
        }

        /// <summary>
        /// Letters the queue already uses, so three images added in a row get
        /// a、b、c instead of all asking for the same free letter.
        /// </summary>
        private HashSet<char> ReservedAltArtSuffixes()
        {
            var reserved = new HashSet<char>();
            for (int i = 0; i < _altQueue.Count; i++)
                TrackAltArtSuffix(reserved, _altQueue[i].AltId);
            return reserved;
        }

        private static void TrackAltArtSuffix(HashSet<char> reserved, string altId)
        {
            string id = (altId ?? string.Empty).Trim();
            if (id.Length == 0)
                return;

            char suffix = id[id.Length - 1];
            if (AltArtworkNaming.IsUsableSuffix(suffix))
                reserved.Add(suffix);
        }

        /// <summary>Next free variant number, queue letters included.</summary>
        private string NextAltArtId(ISet<char> reserved)
        {
            if (_draft == null)
                return string.Empty;

            return AltArtworkWriter.SuggestAltId(AltArtBaseCardId, _draft.Prefix,
                _draft.FolderPath, AlternateArtworkRoot, reserved);
        }

        /// <summary>
        /// Adds dropped / picked images to the queue. Every image is numbered on
        /// its own, so nine pictures dropped at once become a … i in one go.
        /// </summary>
        private void AppendAltArtImages(IList<string> paths)
        {
            if (_draft == null || !_altArtVisible || paths == null)
                return;

            int added = 0;
            string skipped = null;
            HashSet<char> reserved = ReservedAltArtSuffixes();
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path))
                    continue;

                if (!CardDataWriter.IsSupportedImage(path))
                {
                    if (skipped == null)
                        skipped = Path.GetFileName(path);
                    continue;
                }

                var item = new AltArtQueueItem(path, NextAltArtId(reserved));
                item.LoadThumbnail();
                _altQueue.Add(item);
                TrackAltArtSuffix(reserved, item.AltId);
                added++;
            }

            if (added == 0)
            {
                SetAltArtStatus(skipped == null
                    ? "没有可加入的异画卡图"
                    : "异画只认 png / jpg：" + skipped, true);
                return;
            }

            RefreshAltArtQueue(_altQueue.Count - 1);
            SetAltArtStatus(skipped == null
                ? "已加入 " + added + " 张，点 保存 写入 AlternateArtwork"
                : "已加入 " + added + " 张；跳过 " + skipped + "（只认 png / jpg）",
                skipped != null);
        }

        private void SetAltArtVisible(bool visible)

        {
            _altArtVisible = visible;
            if (_altArtSection == null)
                return;

            _altArtSection.Visible = visible;
            var column = _altArtSection.Parent as TableLayoutPanel;
            if (column != null && column.RowStyles.Count > 0)
                column.RowStyles[0].Height = visible ? AltArtSectionHeight : 0F;
        }

        /// <summary>
        /// Pushes the queue into the strip and the selected row into the number
        /// field and the preview. -1 means an empty queue.
        /// </summary>
        private void RefreshAltArtQueue(int selected)
        {
            if (selected >= _altQueue.Count)
                selected = _altQueue.Count - 1;
            if (selected < 0 && _altQueue.Count > 0)
                selected = 0;

            if (_altQueueStrip != null)
                _altQueueStrip.SetItems(_altQueue, selected);

            _altSyncingId = true;
            try
            {
                if (_altIdBox == null)
                    return;

                _altIdBox.Text = selected >= 0 && selected < _altQueue.Count
                    ? _altQueue[selected].AltId
                    : SuggestAltArtId();
            }
            finally
            {
                _altSyncingId = false;
            }

            ShowAltArtPreview(selected);
            UpdateAltArtCaption();
            UpdateAltArtHint();
        }

        private void ShowAltArtPreview(int index)
        {
            if (_altImageBox == null)
                return;

            Image image = index >= 0 && index < _altQueue.Count
                ? _altQueue[index].Thumbnail
                : null;
            _altImageBox.SetImage(image, image == null
                ? "拖入异画卡图\r\n可多选，也可拖文件夹"
                : "继续拖入可追加");
        }

        /// <summary>标题跟着队列张数走；保存 / 清空 只是按钮，不再拉长。</summary>
        private void UpdateAltArtCaption()
        {
            int count = _altQueue.Count;
            if (_altArtCaption != null)
            {
                _altArtCaption.Text = count == 0
                    ? "添加异画 · 可一次拖入多张"
                    : string.Format("添加异画 · {0} 张待保存", count);
            }

            // 60px 放不下“保存 3 张异画”，张数只在标题里说一遍。
            if (_altSaveButton != null)
            {
                _altSaveButton.Text = "保存";
                _altSaveButton.Enabled = count > 0;
            }

            if (_altClearButton != null)
                _altClearButton.Enabled = count > 0;
        }

        private void AltQueueStrip_SelectionChanged(object sender, EventArgs e)
        {
            int index = SelectedAltArtIndex;
            _altSyncingId = true;
            try
            {
                if (_altIdBox != null && index >= 0 && index < _altQueue.Count)
                    _altIdBox.Text = _altQueue[index].AltId;
            }
            finally
            {
                _altSyncingId = false;
            }

            ShowAltArtPreview(index);
            UpdateAltArtHint();
        }

        private void AltQueueStrip_RemoveRequested(object sender, EventArgs e)
        {
            RemoveAltArtItem(SelectedAltArtIndex);
        }

        private void AltIdBox_TextChanged(object sender, EventArgs e)
        {
            if (_altSyncingId)
                return;

            int index = SelectedAltArtIndex;
            if (index >= 0 && index < _altQueue.Count)
                _altQueue[index].AltId = _altIdBox.Text.Trim();

            UpdateAltArtHint();
        }

        /// <summary>Drops one row and selects its neighbour.</summary>
        private void RemoveAltArtItem(int index)
        {
            if (index < 0 || index >= _altQueue.Count)
                return;

            AltArtQueueItem item = _altQueue[index];
            _altQueue.RemoveAt(index);
            item.Dispose();

            int next = _altQueue.Count == 0
                ? -1
                : Math.Min(index, _altQueue.Count - 1);
            RefreshAltArtQueue(next);
        }

        private void ClearAltArtQueue()
        {
            for (int i = 0; i < _altQueue.Count; i++)
                _altQueue[i].Dispose();
            _altQueue.Clear();
            _altSubmitted = null;
            RefreshAltArtQueue(-1);
        }

        /// <summary>Another row of the queue already claims this number.</summary>
        private string FindQueuedDuplicate(int index)
        {
            string id = (_altQueue[index].AltId ?? string.Empty).Trim();
            if (id.Length == 0)
                return null;

            for (int i = 0; i < _altQueue.Count; i++)
            {
                if (i == index)
                    continue;

                if (string.Equals((_altQueue[i].AltId ?? string.Empty).Trim(),
                    id, StringComparison.OrdinalIgnoreCase))
                {
                    return id;
                }
            }
            return null;
        }


        private string SuggestAltArtId()
        {
            if (_draft == null)
                return string.Empty;

            return AltArtworkWriter.SuggestAltId(AltArtBaseCardId,
                _draft.Prefix, _draft.FolderPath, AlternateArtworkRoot);
        }

        /// <summary>
        /// Mirrors the save rules while the player types: the line under the
        /// 异画卡号 field names the file that will be written, or says why the
        /// number cannot work.
        /// </summary>
        private void UpdateAltArtHint()
        {
            if (_altStatusLabel == null || _draft == null)
                return;

            string problem;
            string hint = DescribeAltArtTarget(out problem);
            int index = SelectedAltArtIndex;
            if (index >= 0 && index < _altQueue.Count)
            {
                string duplicate = problem == null
                    ? FindQueuedDuplicate(index)
                    : null;
                if (duplicate != null)
                {
                    problem = "duplicate";
                    hint = "本页已经用了这个卡号：" + duplicate;
                }

                // The strip paints this row with a red frame, so a refused
                // number is visible without reading the hint line.
                _altQueue[index].Problem = problem == null ? null : hint;
            }

            _altStatusLabel.Text = hint;
            _altStatusLabel.ForeColor = problem == null
                ? AppTheme.TextMuted
                : AppTheme.Danger;
            if (_altToolTip != null)
                _altToolTip.SetToolTip(_altStatusLabel, hint);
            if (_altQueueStrip != null)
                _altQueueStrip.Invalidate();
        }

        private string DescribeAltArtTarget(out string problem)
        {
            problem = null;
            string baseId = (AltArtBaseCardId ?? string.Empty).Trim();
            string altId = _altIdBox == null ? string.Empty : _altIdBox.Text.Trim();

            if (string.IsNullOrEmpty(_draft.Prefix))
            {
                problem = "prefix";
                return "这个卡包没有 Prefix，游戏读不到它的异画";
            }

            if (altId.Length == 0)
            {
                problem = "empty";
                return "例如 " + baseId + "a（基卡号 + 一个字母）";
            }

            if (!altId.StartsWith(baseId, StringComparison.OrdinalIgnoreCase))
            {
                problem = "base";
                return "异画卡号要在 " + baseId + " 后面加一个字母";
            }

            string tail = altId.Substring(baseId.Length);
            if (tail.Length != 1)
            {
                problem = "tail";
                return tail.Length == 0
                    ? "异画卡号要带上异画字母：" + baseId + "a"
                    : "只能比基卡号多一个字母，例如 " + baseId + "a";
            }

            if (!char.IsLetter(tail[0]))
            {
                problem = "digit";
                return "最后一位必须是字母，数字会被当成卡号的一部分";
            }

            if (!AltArtworkNaming.IsUsableSuffix(tail[0]))
            {
                problem = "E";
                return "不能以大写 E 结尾：E 是卡表的异画标记";
            }

            var folder = new SeriesFolder();
            folder.Prefix = _draft.Prefix;
            folder.FolderPath = _draft.FolderPath;
            string directory = AltArtworkNaming.ResolveDirectory(
                AlternateArtworkRoot, folder);
            string fileBase = AltArtworkNaming.GetFileBaseName(altId, tail[0]);
            string target = string.IsNullOrEmpty(directory)
                ? fileBase + ".png"
                : AltArtworkNaming.DisplayPath(AlternateArtworkRoot,
                    Path.Combine(directory, fileBase + ".png"));
            return "写入 " + target + "（png / jpg）· 游戏里显示 " + altId;
        }

        private void RaiseAltArtSaveRequested()
        {
            EventHandler handler = AltArtSaveRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void BrowseForAltArtImages()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择异画卡图（可多选）";
                // The game only reads .png / .jpg out of AlternateArtwork, so
                // the picker offers nothing else.
                dialog.Filter = "异画卡图 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
                dialog.CheckFileExists = true;
                dialog.Multiselect = true;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                    return;

                AppendAltArtImages(dialog.FileNames);
            }
        }

        private void AltImageBox_FileDropped(object sender,
            CardImageDroppedEventArgs e)
        {
            if (e == null)
                return;

            AppendAltArtImages(e.Paths);
        }

        /// <summary>Status line of the 添加异画 block.</summary>
        public void SetAltArtStatus(string text, bool error)
        {
            if (_altStatusLabel == null)
                return;

            _altStatusLabel.Text = text ?? string.Empty;
            _altStatusLabel.ForeColor = error
                ? AppTheme.Danger
                : AppTheme.TextMuted;
        }

        /// <summary>
        /// After a batch save: the images the writer accepted leave the queue,
        /// the refused ones stay with the writer's reason and a red frame, so a
        /// retry only needs a new letter for those rows.
        /// </summary>
        public void NotifyAltArtsSaved(AltArtBatchResult result)
        {
            if (result == null)
                return;

            SetStatus(result.Message, result.FailedCount > 0);
            List<AltArtQueueItem> submitted = _altSubmitted;
            _altSubmitted = null;

            if (submitted == null)
            {
                RefreshAltArtQueue(SelectedAltArtIndex);
                return;
            }

            int keep = -1;
            for (int i = 0; i < submitted.Count; i++)
            {
                AltArtQueueItem item = submitted[i];
                AltArtworkResult one = i < result.Results.Count
                    ? result.Results[i]
                    : null;

                if (one != null && one.Success)
                {
                    if (_altQueue.Remove(item))
                        item.Dispose();
                    continue;
                }

                int at = _altQueue.IndexOf(item);
                if (at < 0)
                    continue;

                item.Problem = one == null ? "保存失败" : one.Message;
                if (keep < 0)
                    keep = at;
            }

            RefreshAltArtQueue(keep >= 0
                ? keep
                : Math.Max(0, _altQueue.Count - 1));
        }

        /// <summary>
        /// Collects the 添加异画 queue into one request per image, in the order
        /// the images were added. Returns null and explains in the footer when
        /// the card cannot take variants or the queue is empty.
        /// </summary>
        public IList<AltArtworkRequest> ReadAltArts()
        {
            if (_draft == null || _draft.IsNew ||
                string.IsNullOrEmpty(AltArtBaseCardId) || !_altArtVisible)
            {
                SetStatus("先打开一张已有的卡，才能给它添加异画", true);
                return null;
            }

            if (_altQueue.Count == 0)
            {
                SetStatus("先把异画卡图拖进 添加异画 的图片框（可以多选）", true);
                if (_altImageBox != null)
                    _altImageBox.Focus();
                return null;
            }

            var requests = new List<AltArtworkRequest>(_altQueue.Count);
            for (int i = 0; i < _altQueue.Count; i++)
            {
                AltArtQueueItem item = _altQueue[i];
                string altId = (item.AltId ?? string.Empty).Trim();
                if (altId.Length == 0)
                {
                    SetStatus("第 " + (i + 1) + " 张还没有异画卡号", true);
                    RefreshAltArtQueue(i);
                    if (_altIdBox != null)
                        _altIdBox.Input.Focus();
                    return null;
                }

                if (string.IsNullOrEmpty(item.SourcePath) ||
                    !File.Exists(item.SourcePath))
                {
                    SetStatus("第 " + (i + 1) + " 张的卡图已经不在原处：" +
                        item.SourcePath, true);
                    RefreshAltArtQueue(i);
                    return null;
                }

                var request = new AltArtworkRequest();
                request.BaseCardId = AltArtBaseCardId;
                request.Prefix = _draft.Prefix;
                request.FolderPath = _draft.FolderPath;
                request.FolderFullPath = _draft.FolderFullPath;
                request.FileName = _draft.FileName;
                request.AlternateArtworkRoot = AlternateArtworkRoot;
                request.AltId = altId;
                request.ImageSourcePath = item.SourcePath;
                requests.Add(request);
            }

            _altSubmitted = new List<AltArtQueueItem>(_altQueue);
            return requests;
        }

        /// <summary>
        /// Copies the controls back into the draft. Returns null and shows the
        /// reason in the footer when a field cannot be used (empty 卡号, text in
        /// a number field, unknown 触发), so 保存 never writes a broken block.
        /// </summary>
        public CardEditorDraft ReadDraft()
        {
            if (_draft == null)
                return null;

            string id = _idBox.Text.Trim();
            if (id.Length == 0)
            {
                SetStatus("卡号不能为空", true);
                _idBox.Input.Focus();
                return null;
            }

            string error = null;
            int level = ParseNumber(_levelBox, "等级", _draft.Level, ref error);
            int cost = ParseNumber(_costBox, "COST", _draft.Cost, ref error);
            int power = ParseNumber(_powerBox, "战斗力", _draft.Power, ref error);
            int soul = ParseNumber(_soulBox, "灵魂", _draft.Soul, ref error);
            int backup = ParseNumber(_backupBox, "反击", _draft.Backup, ref error);

            TriggerIcon trigger = TriggerIcon.None;
            string triggerText = _triggerBox.Text.Trim();
            if (triggerText.Length > 0 &&
                !TryParseTrigger(triggerText, Glossary, out trigger))
            {
                if (error == null)
                {
                    error = "触发无法识别：" + triggerText +
                        "（例如 书 / Book / Gate，留空表示无）";
                }
            }

            if (error != null)
            {
                SetStatus(error, true);
                return null;
            }

            CardEditorDraft draft = _draft;
            draft.Id = id;
            draft.Name = _nameBox.Text.Trim();
            draft.JapaneseName = _jpNameBox.Text.Trim();
            draft.ImageId = _imageIdBox.Text.Trim();
            draft.Level = Math.Max(0, level);
            draft.Cost = Math.Max(0, cost);
            draft.Power = Math.Max(0, power);
            draft.Soul = Math.Max(0, soul);
            draft.Backup = Math.Max(0, backup);
            draft.Trigger = trigger;
            draft.Category = _categoryRow.SelectedValue is CardCategory
                ? (CardCategory)_categoryRow.SelectedValue
                : CardCategory.Character;
            draft.Color = _colorRow.SelectedValue is CardColor
                ? (CardColor)_colorRow.SelectedValue
                : CardColor.Undefined;
            draft.Trait1 = _trait1Box.Text.Trim();
            draft.Trait2 = _trait2Box.Text.Trim();
            draft.Trait3 = _trait3Box.Text.Trim();
            draft.Combo = _comboBox.Text.Trim();
            ReadEffectRows(draft);

            // 事件 / 高潮 have no power; the writer drops the line anyway.
            if (draft.Category != CardCategory.Character)
                draft.Power = 0;

            if (draft.IsNew)
            {
                // The chips hold either the packs the card number resolved to
                // or the packs of the series the editor was opened from; the
                // click already updated the draft, this keeps the last state.
                if (_packChoicesVisible)
                    ApplyPackChoice();
                else
                    UpdatePackTarget();

                if (!draft.HasFolder)
                {
                    string head = CardPackIndex.CardIdHead(id);
                    SetStatus(head.Length == 0
                        ? "卡号不能为空"
                        : "卡号前缀 " + head +
                            " 没有匹配的卡包，请检查卡号（例如 AW/S18-E101）",
                        true);
                    _idBox.Input.Focus();
                    return null;
                }
            }

            _subtitleLabel.Text = BuildSubtitle();
            return draft;
        }

        /// <summary>
        /// Reads the 效果 rows back into the draft: 效果内容 as one entry per
        /// row (a row may hold several lines), the 效果 block each entry
        /// belongs to, and 效果代码 as the ability blocks in row order. Keeping
        /// all three in row order is what lets the writer put every `Text` line
        /// back under its own block — including the row of a `*Shorthand`
        /// whose 效果内容 stays empty.
        /// The 简写提供 preview is not read: its text never becomes a `Text`
        /// line, the shorthand adds it again on load.
        /// </summary>
        private void ReadEffectRows(CardEditorDraft draft)
        {
            var texts = new List<string>();
            var codes = new List<string>();
            var blocks = new List<int>();
            int segmentsSeen = 0;
            for (int i = 0; i < _effectRows.Count; i++)
            {
                string text = NormalizeEffectValue(_effectRows[i].Text.Text);
                string code = NormalizeEffectValue(_effectRows[i].Code.Text);
                if (text.Length == 0 && code.Length == 0)
                    continue;

                texts.Add(text);
                if (code.Length > 0)
                {
                    codes.Add(code);
                    segmentsSeen += CardEffectSplitter.SplitCode(code).Count;
                }

                // 这一行是第几段代码：代码框空着的行仍算上一段自己的内容。
                blocks.Add(Math.Max(0, segmentsSeen - 1));
            }

            draft.Text = string.Join("\n", texts.ToArray());
            draft.EffectCode = string.Join("\n", codes.ToArray());
            draft.TextLines = texts.Count == 0 ? null : texts;

            // 玩家看到并改过的是每一行自己的效果内容：读回来的文字按行序
            // 重新对位，简写预览里的文字不算 `Text` 行。
            draft.TextBlockIndexes = blocks.Count == 0 ? null : blocks;
            draft.TextInherited = null;
            draft.OriginalTextLines = null;
        }

        /// <summary>
        /// CRLF from the native edit control back to the "\n" the parser and
        /// the writer use, without the blank lines a box picks up on the way.
        /// </summary>
        private static string NormalizeEffectValue(string text)
        {
            string[] lines = CardDataWriter.SplitLines(text);
            var parts = new List<string>();
            int first = 0;
            int last = lines.Length - 1;
            while (first <= last && lines[first].Trim().Length == 0)
                first++;
            while (last >= first && lines[last].Trim().Length == 0)
                last--;

            for (int i = first; i <= last; i++)
                parts.Add(lines[i].TrimEnd());
            return string.Join("\n", parts.ToArray());
        }

        /// <summary>
        /// A WinForms TextBox needs CRLF to start a new line: the parser hands
        /// us "\n" (and effect texts may hold any mix), so normalize before the
        /// text reaches the native edit control. The writer splits on every
        /// line ending again, so nothing depends on this form.
        /// </summary>
        private static string ToDisplayText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Replace("\n", "\r\n");
        }

        private static int ParseNumber(FieldBox box, string caption, int fallback,
            ref string error)
        {
            string text = box.Text.Trim();
            if (text.Length == 0)
                return fallback;

            int value;
            if (!int.TryParse(text, out value))
            {
                if (error == null)
                    error = caption + " 需要是整数：" + text;
                return fallback;
            }
            return value;
        }

        /// <summary>
        /// Parses the 触发 field: a number, an English enum name or a Chinese
        /// name from <see cref="TriggerGlossary"/>. The card table always keeps
        /// the English name, so what the user types only decides the enum.
        /// </summary>
        private static bool TryParseTrigger(string text, TriggerGlossary glossary,
            out TriggerIcon trigger)
        {
            return TriggerGlossary.TryParse(text, glossary, out trigger);
        }

        private void RestartReferenceSearch()
        {
            if (_searchTimer == null)
                return;

            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void ResetReference()
        {
            _referenceSelection = null;
            ShowReferenceGrid();
            if (_refNameBox != null)
                _refNameBox.Text = string.Empty;
            if (_refTextBox != null)
                _refTextBox.Text = string.Empty;
            if (_referenceGrid != null)
                _referenceGrid.SetItems(new List<TileGridItem>());

            SetReferenceStatus(_referenceIndex == null
                ? "参考卡表：正在建立索引…"
                : "参考卡表：" + _referenceIndex.Count +
                    " 张 · 输入卡名 / 卡号或效果文字");
        }

        /// <summary>
        /// Reads every CardData.txt once, on a background thread, so typing in
        /// the 参考 boxes searches all packs instead of only the open series.
        /// </summary>
        private void EnsureReferenceIndex()
        {
            if (Database == null)
            {
                SetReferenceStatus("参考卡表不可用：没有读到游戏卡表");
                return;
            }
            if (_referenceIndex != null || _referenceBuilding)
                return;

            _referenceBuilding = true;
            SetReferenceStatus("参考卡表：正在建立索引…");
            var worker = new Thread(ReferenceWorker);
            worker.IsBackground = true;
            worker.Start();
        }

        private void ReferenceWorker()
        {
            CardReferenceIndex index;
            try
            {
                index = CardReferenceIndex.Build(Database,
                    delegate(int done, int total)
                    {
                        if (done != total && done % 10 != 0)
                            return;
                        PostToUi(delegate
                        {
                            SetReferenceStatus(string.Format(
                                "参考卡表：正在读取 {0}/{1} 个卡包…", done, total));
                        });
                    },
                    delegate { return _referenceStop; });
            }
            catch (Exception)
            {
                index = CardReferenceIndex.Empty;
            }

            _referenceIndex = index;
            _referenceBuilding = false;
            PostToUi(delegate
            {
                if (_refNameBox == null || _refTextBox == null)
                    return;

                if (_refNameBox.Text.Trim().Length > 0 ||
                    _refTextBox.Text.Trim().Length > 0)
                {
                    RunReferenceSearch();
                }
                else
                {
                    SetReferenceStatus("参考卡表：" + index.Count +
                        " 张 · 输入卡名 / 卡号或效果文字");
                }
            });
        }

        private void PostToUi(Action action)
        {
            if (action == null)
                return;

            if (IsHandleCreated && InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (InvalidOperationException)
                {
                    // The page was disposed while the index was still loading.
                }
                return;
            }

            action();
        }

        private void RunReferenceSearch()
        {
            if (_referenceIndex == null)
            {
                SetReferenceStatus(_referenceBuilding
                    ? "参考卡表：正在建立索引…"
                    : "参考卡表：尚未就绪");
                return;
            }

            string name = _refNameBox.Text.Trim();
            string text = _refTextBox.Text.Trim();
            if (name.Length == 0 && text.Length == 0)
            {
                _referenceGrid.SetItems(new List<TileGridItem>());
                SetReferenceStatus("参考卡表：" + _referenceIndex.Count +
                    " 张 · 输入卡名 / 卡号或效果文字");
                return;
            }

            List<CardReferenceEntry> hits = _referenceIndex.Search(name, text,
                SearchResultLimit);
            var items = new List<TileGridItem>();
            for (int i = 0; i < hits.Count; i++)
            {
                CardReferenceEntry entry = hits[i];
                var item = new TileGridItem();
                item.Title = entry.DisplayName;
                item.Subtitle = entry.Id;
                item.Tag = entry;

                // Thumbnails are resolved while painting, so a search touching
                // a hundred packs only indexes the folders it actually shows.
                CardReferenceEntry captured = entry;
                item.ImagePathProvider = delegate
                {
                    return ResolveReferenceImage(captured);
                };
                items.Add(item);
            }

            _referenceGrid.SetItems(items);
            if (hits.Count == 0)
            {
                SetReferenceStatus("没有找到匹配的卡 · 换个卡名或效果文字试试");
                return;
            }

            SetReferenceStatus(hits.Count >= SearchResultLimit
                ? string.Format("命中 {0} 张以上（显示前 {1} 张）· 单击查看效果",
                    SearchResultLimit, SearchResultLimit)
                : string.Format("命中 {0} 张 · 单击查看效果代码", hits.Count));
        }

        private void ReferenceGrid_ItemClick(object sender, TileGridItemEventArgs e)
        {
            if (e == null || e.Item == null)
                return;

            var entry = e.Item.Tag as CardReferenceEntry;
            if (entry != null)
                ShowReferenceEntry(entry);
        }

        private void ShowReferenceEntry(CardReferenceEntry entry)
        {
            if (entry == null)
                return;

            _referenceSelection = entry;
            _refDetailTitle.Text = entry.Id + " · " + entry.DisplayName;
            string set = string.IsNullOrEmpty(entry.SetName)
                ? entry.FolderPath
                : entry.SetName;
            _refDetailSummary.Text = string.IsNullOrEmpty(set)
                ? entry.SeriesName + " · " + entry.Summary
                : entry.SeriesName + " / " + set + " · " + entry.Summary;
            _refTextBox2.Text = ToDisplayText(entry.Text);
            _refCodeBox.Text = ToDisplayText(entry.EffectCode);

            _referenceSearchPanel.Visible = false;
            _referenceDetailPanel.Visible = true;
        }

        private void ShowReferenceGrid()
        {
            if (_referenceSearchPanel == null || _referenceDetailPanel == null)
                return;

            _referenceDetailPanel.Visible = false;
            _referenceSearchPanel.Visible = true;
        }

        private string ResolveReferenceImage(CardReferenceEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.FolderFullPath))
                return null;

            Dictionary<string, string> index;
            if (!_folderImages.TryGetValue(entry.FolderFullPath, out index))
            {
                index = ImageLocator.BuildIndex(entry.FolderFullPath);
                _folderImages[entry.FolderFullPath] = index;
            }

            var probe = new WeissCard();
            probe.Id = entry.Id;
            probe.ImageId = entry.ImageId;
            ImageLocator.Resolve(probe, index);
            return probe.ImagePath;
        }

        private void SetReferenceStatus(string text)
        {
            if (_refStatusLabel != null)
                _refStatusLabel.Text = text ?? string.Empty;
        }
    }
}
