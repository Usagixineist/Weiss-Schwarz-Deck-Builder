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

        /// <summary>Height of the 添加异画 block above the reference panel.</summary>
        private const int AltArtSectionHeight = 146;

        /// <summary>Width of the 异画 art box inside that block.</summary>
        private const int AltArtImageWidth = 78;

        private static readonly Color PanelSurface = Color.FromArgb(23, 33, 48);
        private static readonly Color ChipSurface = Color.FromArgb(39, 51, 69);

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
        private FieldBox _altIdBox;
        private CardImageDropBox _altImageBox;
        private RoundedButton _altSaveButton;
        private Label _altStatusLabel;
        private string _altImagePath;
        private Image _altPreview;

        /// <summary>
        /// Whether 添加异画 applies to the open card. Kept next to
        /// <see cref="Control.Visible"/> because a page that was never shown
        /// reports every child as invisible.
        /// </summary>
        private bool _altArtVisible;

        public event EventHandler CloseRequested;
        public event EventHandler SaveRequested;

        /// <summary>
        /// Raised when 保存异画 is clicked: the page hands the request to
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
                DisposeAltArtPreview();
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
            ApplyAltArtImage(null);
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
            AddFieldCell(_infoContent, 1, 9, "触发 (Soul / Book / Gate)", out _triggerBox);
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

        private Control BuildEffectColumn()
        {
            var column = new TableLayoutPanel();
            column.Dock = DockStyle.Fill;
            column.ColumnCount = 1;
            column.RowCount = 2;
            column.BackColor = Color.Transparent;
            column.Margin = new Padding(0);
            column.Padding = new Padding(0);
            // 效果内容 : 效果代码 = 1 : 2，代码块通常比卡面文字长得多。
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 66.66F));
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _textBox = CreateField(true);
            Control textCell = WrapField("效果内容（卡面上印的效果文字）", _textBox);
            textCell.Margin = new Padding(0, 0, 0, 8);
            column.Controls.Add(textCell, 0, 0);

            _codeBox = CreateField(true);
            Control codeCell = WrapField(
                "效果代码（Act / Auto / Cont 与 { } 代码块）", _codeBox);
            codeCell.Margin = new Padding(0);
            column.Controls.Add(codeCell, 0, 1);
            return column;
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
        /// 添加异画: the card number of the variant plus the art that carries it.
        /// A variant lives only as an image below
        /// StreamingAssets\AlternateArtwork, so this block writes a file and
        /// never touches CardData.txt.
        /// </summary>
        private Control BuildAltArtSection()
        {
            var host = new TableLayoutPanel();
            host.Dock = DockStyle.Fill;
            host.ColumnCount = 1;
            host.RowCount = 3;
            host.BackColor = PanelSurface;
            host.Margin = new Padding(0, 0, 0, 8);
            host.Padding = new Padding(10, 6, 10, 6);
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, CaptionHeight));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var caption = new Label();
            caption.Text = "添加异画";
            caption.Dock = DockStyle.Fill;
            caption.AutoEllipsis = true;
            caption.Font = _captionFont;
            caption.ForeColor = AppTheme.TextSoft;
            caption.BackColor = Color.Transparent;
            caption.TextAlign = ContentAlignment.MiddleLeft;
            caption.Margin = new Padding(2, 0, 0, 0);
            host.Controls.Add(caption, 0, 0);

            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.BackColor = Color.Transparent;
            body.Margin = new Padding(0);
            body.Padding = new Padding(0);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(
                SizeType.Absolute, AltArtImageWidth));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var numberCell = new TableLayoutPanel();
            numberCell.Dock = DockStyle.Fill;
            numberCell.ColumnCount = 1;
            numberCell.RowCount = 3;
            numberCell.BackColor = Color.Transparent;
            numberCell.Margin = new Padding(0, 0, 8, 0);
            numberCell.Padding = new Padding(0);
            numberCell.RowStyles.Add(new RowStyle(SizeType.Absolute, CaptionHeight));
            numberCell.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            numberCell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            numberCell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            numberCell.Controls.Add(CreateCaption(
                "异画卡号（基卡号 + 一个字母）"), 0, 0);

            _altIdBox = CreateField(false);
            _altIdBox.Dock = DockStyle.Fill;
            _altIdBox.Input.TextChanged += delegate { UpdateAltArtHint(); };
            numberCell.Controls.Add(_altIdBox, 0, 1);

            _altStatusLabel = new Label();
            _altStatusLabel.Text = string.Empty;
            _altStatusLabel.Dock = DockStyle.Fill;
            _altStatusLabel.AutoEllipsis = true;
            _altStatusLabel.Font = _captionFont;
            _altStatusLabel.ForeColor = AppTheme.TextMuted;
            _altStatusLabel.BackColor = Color.Transparent;
            _altStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _altStatusLabel.Margin = new Padding(2, 0, 0, 0);
            numberCell.Controls.Add(_altStatusLabel, 0, 2);

            body.Controls.Add(numberCell, 0, 0);

            _altImageBox = new CardImageDropBox();
            _altImageBox.Dock = DockStyle.Fill;
            _altImageBox.HintFont = _captionFont;
            _altImageBox.SurfaceColor = PanelSurface;
            _altImageBox.BrowseRequested += delegate { BrowseForAltArtImage(); };
            _altImageBox.FileDropped += AltImageBox_FileDropped;
            body.Controls.Add(_altImageBox, 1, 0);

            host.Controls.Add(body, 0, 1);

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.BackColor = Color.Transparent;
            actions.Margin = new Padding(0);
            actions.Padding = new Padding(0, 2, 0, 0);

            _altSaveButton = CreateActionButton("保存异画", ChipSurface,
                AppTheme.TextSoft, 96);
            _altSaveButton.Margin = new Padding(0);
            _altSaveButton.Click += delegate { RaiseAltArtSaveRequested(); };
            actions.Controls.Add(_altSaveButton);
            host.Controls.Add(actions, 0, 2);

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
                : draft.Trigger.ToString();
            _trait1Box.Text = draft.Trait1 ?? string.Empty;
            _trait2Box.Text = draft.Trait2 ?? string.Empty;
            _trait3Box.Text = draft.Trait3 ?? string.Empty;
            _comboBox.Text = draft.Combo ?? string.Empty;
            _textBox.Text = ToDisplayText(draft.Text);
            _codeBox.Text = ToDisplayText(draft.EffectCode);

            _categoryRow.Select(draft.Category);
            _colorRow.Select(draft.Color);
            _powerBox.Input.Enabled = draft.Category == CardCategory.Character;

            UpdatePackTarget();
            ShowPreview(draft.PreviewImagePath);
            _subtitleLabel.Text = BuildSubtitle();
            RefreshAltArtSection();
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
            CardPackIndex index = id.Length == 0 ? null : EnsurePackIndex();
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

            bool hasSeriesChoice = _folders != null && _folders.Count > 1;
            _packHint = id.Length == 0
                ? (hasSeriesChoice
                    ? "选择卡包，或输入卡号自动分配"
                    : (draft.HasFolder ? null : "输入卡号后自动分配卡包"))
                : "卡号前缀 " + CardPackIndex.CardIdHead(id) + " 没有匹配的卡包";

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

            _altIdBox.Text = SuggestAltArtId();
            ApplyAltArtImage(null);
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

        /// <summary>Variant number the pack does not use yet, base card included.</summary>
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
            _altStatusLabel.Text = hint;
            _altStatusLabel.ForeColor = problem == null
                ? AppTheme.TextMuted
                : AppTheme.Danger;
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

        private void BrowseForAltArtImage()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择异画卡图";
                // The game only reads .png / .jpg out of AlternateArtwork, so
                // the picker offers nothing else.
                dialog.Filter = "异画卡图 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                    return;

                ApplyAltArtImage(dialog.FileName);
            }
        }

        private void AltImageBox_FileDropped(object sender,
            CardImageDroppedEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.Path))
                return;

            ApplyAltArtImage(e.Path);
        }

        private void ApplyAltArtImage(string path)
        {
            _altImagePath = path;
            DisposeAltArtPreview();

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    using (Image source = Image.FromFile(path))
                        _altPreview = new Bitmap(source);
                }
                catch (Exception)
                {
                    _altPreview = null;
                }
            }

            if (_altImageBox != null)
            {
                _altImageBox.SetImage(_altPreview, _altPreview == null
                    ? "拖入异画卡图"
                    : "拖入可替换");
            }

            UpdateAltArtHint();
        }

        private void DisposeAltArtPreview()
        {
            if (_altPreview == null)
                return;

            _altPreview.Dispose();
            _altPreview = null;
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
        /// After a successful save: the picked art is used up and the next free
        /// letter is offered, so a second variant needs no retyping.
        /// </summary>
        public void NotifyAltArtSaved(string message)
        {
            SetStatus(message, false);
            ApplyAltArtImage(null);
            if (_altIdBox != null)
                _altIdBox.Text = SuggestAltArtId();
            UpdateAltArtHint();
        }

        /// <summary>
        /// Collects 添加异画 into a request. Returns null and explains in the
        /// footer when the number or the art is missing.
        /// </summary>
        public AltArtworkRequest ReadAltArt()
        {
            if (_draft == null || _draft.IsNew ||
                string.IsNullOrEmpty(AltArtBaseCardId) || !_altArtVisible)
            {
                SetStatus("先打开一张已有的卡，才能给它添加异画", true);
                return null;
            }

            string altId = _altIdBox.Text.Trim();
            if (altId.Length == 0)
            {
                SetStatus("异画卡号不能为空，例如 " + AltArtBaseCardId + "a", true);
                _altIdBox.Input.Focus();
                return null;
            }

            if (string.IsNullOrEmpty(_altImagePath) || !File.Exists(_altImagePath))
            {
                SetStatus("先把异画卡图拖进 添加异画 的图片框", true);
                _altImageBox.Focus();
                return null;
            }

            var request = new AltArtworkRequest();
            request.BaseCardId = AltArtBaseCardId;
            request.Prefix = _draft.Prefix;
            request.FolderPath = _draft.FolderPath;
            request.AlternateArtworkRoot = AlternateArtworkRoot;
            request.AltId = altId;
            request.ImageSourcePath = _altImagePath;
            return request;
        }

        /// <summary>Puts the cursor in the 异画卡号 field of 添加异画.</summary>
        public void FocusAltArtSection()
        {
            if (!_altArtVisible || _altIdBox == null)
                return;

            _altIdBox.Input.Focus();
            _altIdBox.Input.SelectAll();
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
            if (triggerText.Length > 0 && !TryParseTrigger(triggerText, out trigger))
            {
                if (error == null)
                {
                    error = "触发无法识别：" + triggerText +
                        "（例如 Soul / Book / Gate，留空表示无）";
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
            draft.Text = _textBox.Text;
            draft.EffectCode = _codeBox.Text;

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

        private static bool TryParseTrigger(string text, out TriggerIcon trigger)
        {
            int number;
            if (int.TryParse(text, out number))
            {
                trigger = (TriggerIcon)number;
                return number >= (int)TriggerIcon.None &&
                    number <= (int)TriggerIcon.CustomIcon6;
            }

            return Enum.TryParse(text, true, out trigger);
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
