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

        public event EventHandler CloseRequested;
        public event EventHandler SaveRequested;

        /// <summary>Card table the reference search reads; null disables it.</summary>
        public CardDatabase Database { get; set; }

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

            _searchTimer = new System.Windows.Forms.Timer();
            _searchTimer.Interval = 260;
            _searchTimer.Tick += delegate
            {
                _searchTimer.Stop();
                RunReferenceSearch();
            };

            BuildPage();
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
            }
            base.Dispose(disposing);
        }

        public void LoadDraft(CardEditorDraft draft, IList<SeriesFolder> folders)
        {
            if (draft == null)
                return;

            _draft = draft;
            _folders = folders;
            LoadDraftFields(draft);
            ResetReference();
            EnsureReferenceIndex();

            SetStatus(draft.IsNew
                ? "填写卡号与卡名，把卡图拖进左上角的框，然后保存"
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
            AddFieldCell(_infoContent, 0, 3, "组合 (Combo)", out _comboBox, true);

            _folderRow = CreateChipRow(true);
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
            AddFieldCell(_infoContent, 0, 9, "应援", out _backupBox);
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
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
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
            column.RowCount = 1;
            column.BackColor = Color.Transparent;
            column.Margin = new Padding(0);
            column.Padding = new Padding(0);
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _referenceBody = new Panel();
            _referenceBody.Dock = DockStyle.Fill;
            _referenceBody.BackColor = Color.Transparent;
            _referenceBody.Margin = new Padding(0);

            _referenceSearchPanel = BuildReferenceSearchPanel();
            _referenceBody.Controls.Add(_referenceSearchPanel);

            _referenceDetailPanel = BuildReferenceDetailPanel();
            _referenceDetailPanel.Visible = false;
            _referenceBody.Controls.Add(_referenceDetailPanel);

            column.Controls.Add(_referenceBody, 0, 0);
            return column;
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
            _refTextBox2.Margin = new Padding(0, 0, 0, 6);
            panel.Controls.Add(_refTextBox2, 0, 3);

            panel.Controls.Add(CreateCaption("效果代码（参考，可选中复制）"), 0, 4);

            _refCodeBox = CreateField(true);
            _refCodeBox.Input.ReadOnly = true;
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
            _subtitleLabel.Text = draft.TargetLabel +
                (string.IsNullOrEmpty(draft.Id) ? string.Empty : " · " + draft.Id);

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

            LoadFolderChips(draft);
            ShowPreview(draft.PreviewImagePath);
        }

        private void LoadFolderChips(CardEditorDraft draft)
        {
            _folderRow.ClearChips();
            bool show = draft.IsNew && _folders != null && _folders.Count > 1;
            if (show)
            {
                for (int i = 0; i < _folders.Count; i++)
                {
                    SeriesFolder folder = _folders[i];
                    string label = string.IsNullOrEmpty(folder.SetName)
                        ? folder.FolderPath
                        : folder.SetName;
                    _folderRow.AddChip(label, folder);
                }
                _folderRow.Select(FindFolder(draft.FolderPath));
            }

            SetFolderRowVisible(show);
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
            int backup = ParseNumber(_backupBox, "应援", _draft.Backup, ref error);

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

            if (_folderRow.Visible && _folderRow.SelectedValue is SeriesFolder)
            {
                CardEditorDraft.UseFolder(draft,
                    (SeriesFolder)_folderRow.SelectedValue);
                _subtitleLabel.Text = draft.TargetLabel + " · " + draft.Id;
            }

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
