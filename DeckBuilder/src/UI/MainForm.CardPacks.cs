using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using WeissDeckBuilder.Data;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// Drag and drop import of card pack archives. Dropping a compliant ZIP,
    /// RAR or 7z on the window installs it into StreamingAssets\Cards, reloads
    /// the series list and opens the imported series; every other file reports
    /// 添加失败.
    /// </summary>
    public sealed partial class MainForm
    {
        private Panel _cardPackHint;
        private Label _cardPackHintTitle;
        private Label _cardPackHintText;
        private bool _cardPackInstalling;

        /// <summary>
        /// Makes the whole window a drop target. Called once after the UI has
        /// been built, so the hint overlay is in place before the handlers are
        /// attached to every child control.
        /// </summary>
        private void EnableCardPackDrop()
        {
            BuildCardPackHint();
            Controls.Add(_cardPackHint);
            _cardPackHint.BringToFront();
            ApplyCardPackDropHandlers(this);
        }

        /// <summary>
        /// OLE drops land on the control under the cursor, so every control
        /// gets the handlers instead of the form alone. Controls created later
        /// (pages, overlays, grids) are picked up through ControlAdded.
        /// </summary>
        private void ApplyCardPackDropHandlers(Control control)
        {
            // 卡图 / 异画 boxes answer file drops themselves. Attaching the
            // archive handlers to them as well would make a dropped png report
            // 请一次拖入一个卡表压缩包 instead of being used as the art, so
            // those controls are skipped (their children still get the hook).
            if (!CardPackDropScope.Accepts(control))
            {
                for (int i = 0; i < control.Controls.Count; i++)
                    ApplyCardPackDropHandlers(control.Controls[i]);
                return;
            }

            control.AllowDrop = true;
            control.DragEnter += CardPack_DragEnter;
            control.DragOver += CardPack_DragOver;
            control.DragLeave += CardPack_DragLeave;
            control.DragDrop += CardPack_DragDrop;
            control.ControlAdded += CardPack_ControlAdded;

            for (int i = 0; i < control.Controls.Count; i++)
                ApplyCardPackDropHandlers(control.Controls[i]);
        }

        private void CardPack_ControlAdded(object sender, ControlEventArgs e)
        {
            if (e.Control != null)
                ApplyCardPackDropHandlers(e.Control);
        }

        /// <summary>
        /// Full window overlay shown while a pack hovers the window, so the
        /// player sees where the drop goes before releasing the mouse.
        /// </summary>
        private void BuildCardPackHint()
        {
            _cardPackHint = new Panel();
            _cardPackHint.Dock = DockStyle.Fill;
            _cardPackHint.BackColor = AppTheme.InputBackground;
            _cardPackHint.Visible = false;

            var border = new Panel();
            border.Dock = DockStyle.Fill;
            border.BackColor = Color.Transparent;
            border.Padding = new Padding(30);
            border.Paint += CardPackHintBorder_Paint;

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.BackColor = Color.Transparent;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _cardPackHintTitle = new Label();
            _cardPackHintTitle.Text = "释放鼠标，安装卡表";
            _cardPackHintTitle.Dock = DockStyle.Fill;
            _cardPackHintTitle.BackColor = Color.Transparent;
            _cardPackHintTitle.ForeColor = AppTheme.Text;
            _cardPackHintTitle.Font = new Font("Microsoft YaHei UI", 20F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _cardPackHintTitle.TextAlign = ContentAlignment.BottomCenter;
            _cardPackHintTitle.Margin = new Padding(0);

            _cardPackHintText = new Label();
            _cardPackHintText.Text = string.Empty;
            _cardPackHintText.Dock = DockStyle.Fill;
            _cardPackHintText.BackColor = Color.Transparent;
            _cardPackHintText.ForeColor = AppTheme.TextSoft;
            _cardPackHintText.Font = new Font("Microsoft YaHei UI", 10.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _cardPackHintText.TextAlign = ContentAlignment.TopCenter;
            _cardPackHintText.Margin = new Padding(0, 10, 0, 0);

            layout.Controls.Add(_cardPackHintTitle, 0, 0);
            layout.Controls.Add(_cardPackHintText, 0, 1);
            border.Controls.Add(layout);
            _cardPackHint.Controls.Add(border);
        }

        private void CardPackHintBorder_Paint(object sender, PaintEventArgs e)
        {
            Control panel = sender as Control;
            if (panel == null || panel.Width < 8 || panel.Height < 8)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(1, 1, panel.Width - 3, panel.Height - 3);
            using (GraphicsPath path = AppTheme.CreateRoundedRectangle(bounds, 18))
            {
                using (var pen = new Pen(AppTheme.Accent, 2F))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }

        private void ShowCardPackHint(string archivePath)
        {
            if (_cardPackHint == null)
                return;

            string fileName = string.Empty;
            try
            {
                fileName = Path.GetFileName(archivePath);
            }
            catch (ArgumentException)
            {
            }

            _cardPackHintTitle.Text = "释放鼠标，安装卡表";
            _cardPackHintText.Text =
                (fileName.Length > 0 ? fileName + "\r\n" : string.Empty) +
                "将解压到 StreamingAssets\\Cards，并立即打开该系列";
            if (!_cardPackHint.Visible)
            {
                _cardPackHint.Visible = true;
                _cardPackHint.BringToFront();
            }
        }

        private void HideCardPackHint()
        {
            if (_cardPackHint != null && _cardPackHint.Visible)
                _cardPackHint.Visible = false;
        }

        private void CardPack_DragEnter(object sender, DragEventArgs e)
        {
            string archivePath = GetSingleArchivePath(e.Data);
            if (archivePath == null || _cardPackInstalling)
            {
                e.Effect = DragDropEffects.None;
                if (!_cardPackInstalling)
                    HideCardPackHint();
                return;
            }

            e.Effect = DragDropEffects.Copy;
            ShowCardPackHint(archivePath);
        }

        private void CardPack_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = !_cardPackInstalling && GetSingleArchivePath(e.Data) != null
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void CardPack_DragLeave(object sender, EventArgs e)
        {
            // Moving between child controls raises DragLeave as well; the hint
            // only folds away once the cursor really left the window.
            if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
                HideCardPackHint();
        }

        private void CardPack_DragDrop(object sender, DragEventArgs e)
        {
            HideCardPackHint();
            e.Effect = DragDropEffects.None;

            string[] files = GetDroppedFiles(e.Data);
            if (files == null || files.Length == 0)
                return;

            if (files.Length != 1 || !IsSupportedArchive(files[0]))
            {
                MessageBox.Show(this,
                    "添加失败：\r\n\r\n请一次拖入一个卡表压缩包（.zip、.rar 或 .7z）。",
                    "添加卡表压缩包", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_cardPackInstalling)
                return;

            e.Effect = DragDropEffects.Copy;
            BeginCardPackInstall(files[0]);
        }

        private static string[] GetDroppedFiles(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
                return null;

            return data.GetData(DataFormats.FileDrop) as string[];
        }

        private static string GetSingleArchivePath(IDataObject data)
        {
            string[] files = GetDroppedFiles(data);
            if (files == null || files.Length != 1)
                return null;

            return IsSupportedArchive(files[0]) ? files[0] : null;
        }

        private static bool IsSupportedArchive(string path)
        {
            return CardPackInstaller.IsSupportedArchive(path);
        }

        /// <summary>
        /// Starts the install after the drop has been answered: the OLE drag
        /// loop must not block on disk I/O while the mouse button is released.
        /// </summary>
        private void BeginCardPackInstall(string archivePath)
        {
            if (_cardPackInstalling || string.IsNullOrEmpty(archivePath))
                return;

            _cardPackInstalling = true;
            try
            {
                BeginInvoke((Action)delegate { InstallCardPack(archivePath); });
            }
            catch (InvalidOperationException)
            {
                InstallCardPack(archivePath);
            }
        }

        private void InstallCardPack(string archivePath)
        {
            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                CardPackInstallResult result =
                    CardPackInstaller.Install(archivePath, _database.DataRoot);

                if (!result.Success)
                {
                    _statusLabel.Text = "添加失败";
                    MessageBox.Show(this,
                        "添加失败：\r\n\r\n" + result.ErrorMessage,
                        "添加卡表压缩包", MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                _database.Reload();
                if (_coverLocator != null)
                    _coverLocator.Clear();
                ShowSeriesList();

                // For a normal pack the archive holds the raw card
                // definitions; alternate artwork can add cards on top, so the
                // series itself reports the number the player will actually
                // see. Supplement packs without metadata have no series to
                // open, so they fall back to the file-count summary below.
                SeriesDefinition series =
                    _database.GetSeries(result.SeriesName);
                int cardCount = series != null
                    ? series.CardCount
                    : result.CardCount;

                string summary;
                if (result.SeriesCount > 1)
                {
                    summary = cardCount > 0
                        ? string.Format("已添加 {0} 个系列 · {1} 张卡",
                            result.SeriesCount, cardCount)
                        : string.Format("已添加 {0} 个系列的补充文件 · {1} 个文件",
                            result.SeriesCount, result.ExtractedFileCount);
                }
                else if (!string.IsNullOrEmpty(result.SeriesName))
                {
                    summary = cardCount > 0
                        ? string.Format("已添加「{0}」· {1} 张卡",
                            result.SeriesName, cardCount)
                        : string.Format("已添加「{0}」的补充文件 · {1} 个文件",
                            result.SeriesName, result.ExtractedFileCount);
                }
                else
                {
                    summary = string.Format("已添加补充文件 · {0} 个文件",
                        result.ExtractedFileCount);
                }
                if (result.ReplacedExistingFolder)
                    summary += "（覆盖同名系列）";

                // Dropping a pack jumps straight into the imported card list:
                // the status bar carries the install summary, so no extra
                // dialog stands between the drop and the cards themselves.
                // A metadata-only or image-only supplement has nothing to
                // open, so keep the series list and show the file summary.
                if (series != null && cardCount > 0)
                    OpenSeries(series);
                else
                    _contentSubtitle.Text = summary;

                _statusLabel.Text = summary;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "添加失败";
                MessageBox.Show(this,
                    "添加失败：\r\n\r\n" + ex.Message,
                    "添加卡表压缩包", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = previousCursor;
                _cardPackInstalling = false;
            }
        }

        /// <summary>
        /// Settings entry point: pick a pack with the file dialog instead of
        /// dragging it onto the window.
        /// </summary>
        private void BrowseCardPack()
        {
            if (_cardPackInstalling)
                return;

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择卡表压缩包";
                dialog.Filter =
                "卡表压缩包 (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|" +
                "ZIP 压缩包 (*.zip)|*.zip|" +
                "RAR 压缩包 (*.rar)|*.rar|" +
                "7z 压缩包 (*.7z)|*.7z|" +
                "所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                BeginCardPackInstall(dialog.FileName);
            }
        }
    }
}
