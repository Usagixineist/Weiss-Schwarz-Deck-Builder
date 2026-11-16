using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 编辑卡片 layer: the left column holds the card art box above the base
    /// fields, the right column holds 效果内容 above 效果代码. It edits a
    /// <see cref="CardEditorDraft"/> and only raises 保存 / 关闭; the file itself
    /// is written by <see cref="CardDataWriter"/> behind MainForm.
    /// </summary>
    internal sealed class CardEditorOverlay : UserControl
    {
        private const int PopupMaxWidth = 1160;
        private const int PopupMaxHeight = 800;
        private const int PopupMargin = 30;
        private const int PopupCornerRadius = 18;
        private const int HeaderHeight = 64;
        private const int FooterHeight = 58;
        private const int CaptionHeight = 17;
        private const int ChipRowHeight = 48;
        private const int InfoRowHeight = 48;
        private const int ColumnGap = 16;
        private const int BodyPadding = 22;

        private static readonly Color PopupSurface = Color.FromArgb(23, 33, 48);
        private static readonly Color ChipSurface = Color.FromArgb(39, 51, 69);

        private readonly Font _titleFont;
        private readonly Font _subtitleFont;
        private readonly Font _captionFont;
        private readonly Font _bodyFont;
        private readonly Font _chipFont;
        private readonly Font _buttonFont;
        private readonly Font _statusFont;

        private Bitmap _backdrop;
        private Image _preview;
        private Panel _popup;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _statusLabel;
        private CardImageDropBox _imageBox;
        private FieldBox _idBox;
        private FieldBox _nameBox;
        private FieldBox _jpNameBox;
        private FieldBox _imageIdBox;
        private FieldBox _levelBox;
        private FieldBox _costBox;
        private FieldBox _powerBox;
        private FieldBox _soulBox;
        private FieldBox _backupBox;
        private FieldBox _trait1Box;
        private FieldBox _trait2Box;
        private FieldBox _trait3Box;
        private FieldBox _textBox;
        private FieldBox _codeBox;
        private ChipRow _categoryRow;
        private ChipRow _colorRow;
        private ChipRow _folderRow;
        private Control _folderCell;
        private TableLayoutPanel _infoContent;
        private DarkScrollPanel _infoScroll;
        private FieldBox _triggerBox;
        private RoundedButton _saveButton;
        private RoundedButton _cancelButton;
        private RoundedButton _closeButton;
        private CardEditorDraft _draft;
        private IList<SeriesFolder> _folders;

        public event EventHandler CloseRequested;
        public event EventHandler SaveRequested;

        public CardEditorOverlay()
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
                _titleFont.Dispose();
                _subtitleFont.Dispose();
                _captionFont.Dispose();
                _bodyFont.Dispose();
                _chipFont.Dispose();
                _buttonFont.Dispose();
                _statusFont.Dispose();
                DisposePreview();
                if (_backdrop != null)
                {
                    _backdrop.Dispose();
                    _backdrop = null;
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>Draft currently shown, updated by <see cref="ReadDraft"/>.</summary>
        public CardEditorDraft Draft
        {
            get { return _draft; }
        }

        public void ShowEditor(CardEditorDraft draft, Bitmap backdrop,
            IList<SeriesFolder> folders)
        {
            if (draft == null)
                return;

            EnsurePopup();
            _draft = draft;
            _folders = folders;

            if (_backdrop != null)
                _backdrop.Dispose();
            _backdrop = backdrop;

            LoadDraft(draft);
            SetStatus(draft.IsNew
                ? "填写卡号与卡名，把卡图拖进左上角的框，然后保存"
                : "修改后点击保存，卡片会写回 CardData.txt（自动备份 .bak）",
                false);

            _popup.Visible = true;
            LayoutPopup();
            Visible = true;
            BringToFront();
            Focus();

            // 添加卡片 starts at the empty 卡号, an open card at its 卡名 so a
            // click into the editor does not offer to overwrite the card number.
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

        /// <summary>Folds the editor away and drops the captured backdrop.</summary>
        public void HideEditor()
        {
            if (_popup != null)
                _popup.Visible = false;
            Visible = false;
            _draft = null;
            _folders = null;
            DisposePreview();
            if (_backdrop != null)
            {
                _backdrop.Dispose();
                _backdrop = null;
            }
        }

        public void SetStatus(string text, bool error)
        {
            if (_statusLabel == null)
                return;

            _statusLabel.Text = text ?? string.Empty;
            _statusLabel.ForeColor = error ? AppTheme.Danger : AppTheme.TextMuted;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (_backdrop != null)
            {
                e.Graphics.DrawImage(_backdrop, ClientRectangle);
            }
            else
            {
                using (var brush = new SolidBrush(AppTheme.MainBackground))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            using (var dim = new SolidBrush(Color.FromArgb(168, 8, 15, 27)))
                e.Graphics.FillRectangle(dim, ClientRectangle);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutPopup();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // A click on the dimmed page only takes the focus back: an editor
            // is closed with 取消 / ✕ / Esc so a stray click keeps the edits.
            base.OnMouseDown(e);
            Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                RaiseCloseRequested();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void EnsurePopup()
        {
            if (_popup != null)
                return;

            _popup = new Panel();
            _popup.BackColor = PopupSurface;
            _popup.Visible = false;
            _popup.Margin = new Padding(0);
            Controls.Add(_popup);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.BackColor = Color.Transparent;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.Controls.Add(BuildHeader(), 0, 0);
            layout.Controls.Add(BuildBody(), 0, 1);
            layout.Controls.Add(BuildFooter(), 0, 2);
            _popup.Controls.Add(layout);
        }

        private void LayoutPopup()
        {
            if (_popup == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            int width = Math.Min(PopupMaxWidth,
                Math.Max(760, ClientSize.Width - 2 * PopupMargin));
            int height = Math.Min(PopupMaxHeight,
                Math.Max(560, ClientSize.Height - 2 * PopupMargin));
            width = Math.Min(width, Math.Max(1, ClientSize.Width));
            height = Math.Min(height, Math.Max(1, ClientSize.Height));

            var bounds = new Rectangle(
                (ClientSize.Width - width) / 2,
                (ClientSize.Height - height) / 2,
                width, height);

            if (_popup.Bounds != bounds)
                _popup.Bounds = bounds;

            Region old = _popup.Region;
            using (GraphicsPath path = AppTheme.CreateRoundedRectangle(
                new Rectangle(0, 0, width, height), PopupCornerRadius))
            {
                _popup.Region = new Region(path);
            }
            if (old != null)
                old.Dispose();
        }

        private void DisposePreview()
        {
            if (_preview == null)
                return;

            _preview.Dispose();
            _preview = null;
        }

        private void RaiseCloseRequested()
        {
            EventHandler handler = CloseRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private Control BuildHeader()
        {
            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 2;
            header.RowCount = 2;
            header.BackColor = Color.Transparent;
            header.Margin = new Padding(0);
            header.Padding = new Padding(BodyPadding, 12, 14, 6);
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));

            _titleLabel = new Label();
            _titleLabel.Text = "修改卡片";
            _titleLabel.Dock = DockStyle.Fill;
            _titleLabel.AutoEllipsis = true;
            _titleLabel.Font = _titleFont;
            _titleLabel.ForeColor = AppTheme.Text;
            _titleLabel.BackColor = Color.Transparent;
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _titleLabel.Margin = new Padding(0);
            header.Controls.Add(_titleLabel, 0, 0);

            _subtitleLabel = new Label();
            _subtitleLabel.Text = string.Empty;
            _subtitleLabel.Dock = DockStyle.Fill;
            _subtitleLabel.AutoEllipsis = true;
            _subtitleLabel.Font = _subtitleFont;
            _subtitleLabel.ForeColor = AppTheme.TextMuted;
            _subtitleLabel.BackColor = Color.Transparent;
            _subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _subtitleLabel.Margin = new Padding(0);
            header.Controls.Add(_subtitleLabel, 0, 1);

            _closeButton = CreateFlatButton("✕", 30);
            _closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _closeButton.Margin = new Padding(0, 2, 0, 0);
            _closeButton.Click += delegate { RaiseCloseRequested(); };
            header.Controls.Add(_closeButton, 1, 0);

            return header;
        }

        private Control BuildFooter()
        {
            var footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 2;
            footer.RowCount = 1;
            footer.BackColor = Color.Transparent;
            footer.Margin = new Padding(0);
            footer.Padding = new Padding(BodyPadding, 8, BodyPadding, 12);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 216F));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _statusLabel = new Label();
            _statusLabel.Text = string.Empty;
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Font = _statusFont;
            _statusLabel.ForeColor = AppTheme.TextMuted;
            _statusLabel.BackColor = Color.Transparent;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Margin = new Padding(0);
            footer.Controls.Add(_statusLabel, 0, 0);

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.BackColor = Color.Transparent;
            actions.Margin = new Padding(0);
            actions.Padding = new Padding(0);

            _saveButton = CreateActionButton("保存", AppTheme.Accent, Color.White, 96);
            _saveButton.Click += SaveButton_Click;
            actions.Controls.Add(_saveButton);

            _cancelButton = CreateActionButton("取消", ChipSurface,
                AppTheme.TextSoft, 96);
            _cancelButton.Click += delegate { RaiseCloseRequested(); };
            actions.Controls.Add(_cancelButton);

            footer.Controls.Add(actions, 1, 0);
            return footer;
        }

        private Control BuildBody()
        {
            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.BackColor = Color.Transparent;
            body.Margin = new Padding(0);
            body.Padding = new Padding(BodyPadding, 4, BodyPadding, 6);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Control left = BuildLeftColumn();
            left.Margin = new Padding(0, 0, ColumnGap / 2, 0);
            body.Controls.Add(left, 0, 0);

            Control right = BuildRightColumn();
            right.Margin = new Padding(ColumnGap / 2, 0, 0, 0);
            body.Controls.Add(right, 1, 0);
            return body;
        }

        private RoundedButton CreateFlatButton(string text, int width)
        {
            var button = new RoundedButton();
            button.Text = text;
            button.AutoSize = false;
            button.Size = new Size(width, 30);
            button.CornerRadius = 8;
            button.SurfaceColor = PopupSurface;
            button.NormalColor = ChipSurface;
            button.HoverColor = AppTheme.Divider;
            button.PressedColor = AppTheme.Border;
            button.SelectedColor = ChipSurface;
            button.ForeColor = AppTheme.TextSoft;
            button.Font = _chipFont;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0);
            return button;
        }

        private RoundedButton CreateActionButton(string text, Color fill,
            Color fore, int width)
        {
            var button = new RoundedButton();
            button.Text = text;
            button.AutoSize = false;
            button.Size = new Size(width, 32);
            button.CornerRadius = 9;
            button.SurfaceColor = PopupSurface;
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
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 68F));
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _imageBox = new CardImageDropBox();
            _imageBox.Dock = DockStyle.Fill;
            _imageBox.Margin = new Padding(0, 0, 0, 8);
            _imageBox.CaptionFont = _captionFont;
            _imageBox.HintFont = _subtitleFont;
            _imageBox.SurfaceColor = PopupSurface;
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
            _infoContent.RowCount = 10;
            _infoContent.AutoSize = false;
            _infoContent.BackColor = Color.Transparent;
            _infoContent.Margin = new Padding(0);
            _infoContent.Padding = new Padding(0);
            _infoContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _infoContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            float[] heights =
            {
                InfoRowHeight, InfoRowHeight, ChipRowHeight, ChipRowHeight,
                ChipRowHeight, InfoRowHeight, InfoRowHeight, InfoRowHeight,
                InfoRowHeight, InfoRowHeight
            };
            for (int i = 0; i < heights.Length; i++)
                _infoContent.RowStyles.Add(new RowStyle(SizeType.Absolute, heights[i]));

            AddFieldCell(_infoContent, 0, 0, "卡号 (Id)", out _idBox, false);
            AddFieldCell(_infoContent, 1, 0, "卡名", out _nameBox, false);
            AddFieldCell(_infoContent, 0, 1, "日文名", out _jpNameBox, false);
            AddFieldCell(_infoContent, 1, 1, "图片 (Image)", out _imageIdBox, false);

            _folderRow = CreateChipRow(true);
            _folderCell = WrapChips("卡包（新卡写入的位置）", _folderRow);
            _infoContent.Controls.Add(_folderCell, 0, 2);
            _infoContent.SetColumnSpan(_folderCell, 2);

            _categoryRow = CreateChipRow(false);
            _categoryRow.AddChip("角色", CardCategory.Character);
            _categoryRow.AddChip("事件", CardCategory.Event);
            _categoryRow.AddChip("高潮", CardCategory.Climax);
            AddChipCell(_infoContent, 3, "类别", _categoryRow);

            _colorRow = CreateChipRow(false);
            _colorRow.AddChip("黄", CardColor.Yellow);
            _colorRow.AddChip("绿", CardColor.Green);
            _colorRow.AddChip("红", CardColor.Red);
            _colorRow.AddChip("蓝", CardColor.Blue);
            _colorRow.AddChip("紫", CardColor.Purple);
            AddChipCell(_infoContent, 4, "颜色", _colorRow);

            AddFieldCell(_infoContent, 0, 5, "等级", out _levelBox, false);
            AddFieldCell(_infoContent, 1, 5, "COST", out _costBox, false);
            AddFieldCell(_infoContent, 0, 6, "战斗力", out _powerBox, false);
            AddFieldCell(_infoContent, 1, 6, "灵魂", out _soulBox, false);
            AddFieldCell(_infoContent, 0, 7, "应援", out _backupBox, false);
            AddFieldCell(_infoContent, 1, 7, "触发（Soul / Book / Gate）",
                out _triggerBox, false);
            AddFieldCell(_infoContent, 0, 8, "特征 1", out _trait1Box, false);
            AddFieldCell(_infoContent, 1, 8, "特征 2", out _trait2Box, false);
            AddFieldCell(_infoContent, 0, 9, "特征 3", out _trait3Box, false);

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
            int total = 7 * InfoRowHeight + 2 * ChipRowHeight;
            if (withFolder)
                total += ChipRowHeight;
            return total;
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

        private Control WrapField(string caption, FieldBox box)
        {
            TableLayoutPanel wrapper = CreateCaptionCell(caption);
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0);
            wrapper.Controls.Add(box, 0, 1);
            return wrapper;
        }

        private Control WrapChips(string caption, ChipRow chips)
        {
            TableLayoutPanel wrapper = CreateCaptionCell(caption);
            chips.Dock = DockStyle.Fill;
            chips.Margin = new Padding(0);
            wrapper.Controls.Add(chips, 0, 1);
            return wrapper;
        }

        /// <summary>Caption row above a field, shared by inputs and chip rows.</summary>
        private TableLayoutPanel CreateCaptionCell(string caption)
        {
            var wrapper = new TableLayoutPanel();
            wrapper.Dock = DockStyle.Fill;
            wrapper.ColumnCount = 1;
            wrapper.RowCount = 2;
            wrapper.BackColor = Color.Transparent;
            wrapper.Margin = new Padding(0, 0, 8, 2);
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
            return wrapper;
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

        private Control BuildRightColumn()
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
            Control codeCell = WrapField("效果代码（Act / Auto 与 { } 代码块）", _codeBox);
            codeCell.Margin = new Padding(0);
            column.Controls.Add(codeCell, 0, 1);
            return column;
        }

        private void LoadDraft(CardEditorDraft draft)
        {
            _titleLabel.Text = draft.IsNew ? "添加卡片" : "修改卡片";
            _subtitleLabel.Text = draft.TargetLabel;

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
                _infoContent.RowStyles[2].Height = visible ? ChipRowHeight : 0F;
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
            draft.Text = _textBox.Text;
            draft.EffectCode = _codeBox.Text;

            // 事件 / 高潮 have no power; the parser ignores it for them anyway.
            if (draft.Category != CardCategory.Character)
                draft.Power = 0;

            if (_folderRow.Visible && _folderRow.SelectedValue is SeriesFolder)
            {
                CardEditorDraft.UseFolder(draft,
                    (SeriesFolder)_folderRow.SelectedValue);
                _subtitleLabel.Text = draft.TargetLabel;
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
