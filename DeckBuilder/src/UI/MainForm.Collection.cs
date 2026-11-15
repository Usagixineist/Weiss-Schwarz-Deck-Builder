using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 我的收藏: the series/card pages of the collection section plus the
    /// marking gesture that edits the player's own card counts. Card data
    /// itself stays read-only; only StreamingAssets\MyCollocation is written.
    /// </summary>
    public sealed partial class MainForm
    {
        /// <summary>Height of the filter footer without the collection button.</summary>
        private const int FilterFooterHeight = 82;

        /// <summary>Footer height while 添加卡片 sits above 搜索.</summary>
        private const int FilterFooterCollectionHeight = 132;

        /// <summary>Row of the 只显示拥有 check box under the 我的收藏 entry.</summary>
        private const int NavCollectionOwnedRowHeight = 30;

        private CollectionStore _collection;
        private RoundedButton _navCollectionButton;
        private FilterCheckBox _collectionOwnedCheck;
        private RowStyle _collectionOwnedRow;
        private RoundedButton _collectionAddButton;
        private RowStyle _filterFooterRow;

        /// <summary>
        /// True while the player marks counts. Clicks then change the draft
        /// instead of opening the detail page, and the footer button reads 保存.
        /// </summary>
        private bool _collectionMarkMode;

        /// <summary>
        /// Unsaved counts of the open series, keyed by pack. Editing a copy
        /// keeps the store clean until 保存, so leaving the page (or Esc) simply
        /// drops the draft.
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, int>>
            _collectionDraft =
                new Dictionary<string, Dictionary<string, int>>(
                    StringComparer.OrdinalIgnoreCase);

        private bool IsCollectionSection
        {
            get
            {
                return _viewMode == ViewMode.CollectionList ||
                    _viewMode == ViewMode.CollectionCards;
            }
        }

        private bool IsOnlyOwnedChecked
        {
            get { return _collectionOwnedCheck != null && _collectionOwnedCheck.Checked; }
        }

        /// <summary>
        /// The 只显示拥有 row lives directly under the nav entry. Sidebar
        /// measurements, the collection button and the nav repaint all follow
        /// the current page, so every page switch calls this one method.
        /// </summary>
        private void UpdateCollectionChrome()
        {
            UpdateCollectionOwnedOption();
            UpdateCollectionAddButton();
            RefreshNavPaint();
        }

        /// <summary>
        /// Unfolds the check box only while the collection section is open in
        /// the expanded rail; the compact rail has no room for it.
        /// </summary>
        private void UpdateCollectionOwnedOption()
        {
            bool visible = _navExpanded && IsCollectionSection;
            if (_collectionOwnedRow != null)
            {
                float height = visible ? NavCollectionOwnedRowHeight : 0F;
                if (_collectionOwnedRow.Height != height)
                    _collectionOwnedRow.Height = height;
            }
            if (_collectionOwnedCheck != null &&
                _collectionOwnedCheck.Visible != visible)
            {
                _collectionOwnedCheck.Visible = visible;
            }
            if (_navItemsLayout != null)
                _navItemsLayout.PerformLayout();
            UpdateNavLayoutMetrics();
        }

        /// <summary>
        /// Adds the 只显示拥有 check box to the nav item stack. The row is
        /// registered right after the 我的收藏 entry and stays collapsed until
        /// that section is opened, so the other pages keep their old spacing.
        /// </summary>
        private void AddCollectionOwnedOption()
        {
            int index = _navItemsLayout.RowStyles.Count;
            _collectionOwnedRow = new RowStyle(SizeType.Absolute, 0F);
            _navItemsLayout.RowStyles.Add(_collectionOwnedRow);
            _navItemsLayout.RowCount = _navItemsLayout.RowStyles.Count;

            _collectionOwnedCheck = new FilterCheckBox();
            _collectionOwnedCheck.Text = "只显示拥有";
            _collectionOwnedCheck.AccessibleName = "只显示拥有";
            _collectionOwnedCheck.Dock = DockStyle.Fill;
            _collectionOwnedCheck.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _collectionOwnedCheck.ForeColor = AppTheme.TextSoft;
            _collectionOwnedCheck.Margin = new Padding(18, 0, 0, 6);
            _collectionOwnedCheck.Visible = false;
            _collectionOwnedCheck.CheckedChanged +=
                CollectionOwnedCheck_CheckedChanged;
            _navItemsLayout.Controls.Add(_collectionOwnedCheck, 0, index);

            if (_navToolTip != null)
            {
                _navToolTip.SetToolTip(_collectionOwnedCheck,
                    "只显示已经拥有的系列与卡片");
            }
        }

        private void CollectionOwnedCheck_CheckedChanged(object sender,
            EventArgs e)
        {
            // The option belongs to 我的收藏: on its cover level it hides whole
            // series, on the card level it hides the cards that are not owned.
            if (_viewMode == ViewMode.CollectionList)
                RefreshSeriesItems();
            else if (_viewMode == ViewMode.CollectionCards)
                RunSearch();
        }

        private void NavCollectionButton_Click(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.CollectionList)
                ShowCollection();
        }

        /// <summary>
        /// First level of 我的收藏: the same covers as 系列卡表, with every
        /// series the player owns nothing from drawn grey.
        /// </summary>
        private void ShowCollection()
        {
            _viewMode = ViewMode.CollectionList;
            _activeSeries = null;
            _results.Clear();
            ExitCollectionMarkMode();

            CloseDeckDetail();
            _navColumnStyle.Width = NavExpandedWidth;
            _filterColumnStyle.Width = 0F;
            _filterHost.Visible = false;
            _filterBody.ScrollToTop();
            SetNavExpanded(true);
            SetDeckSubItemsVisible(false);
            ShowContentPage(_browsePage);
            SetActiveNavItem(_navCollectionButton);
            UpdateNewDeckStripVisibility();
            UpdateCollectionChrome();

            _contentTitle.Text = "我的收藏";
            _tileGrid.ConfigureSeries();
            RefreshSeriesItems();
            _tileGrid.ScrollToTop();
            ScheduleDarkChrome();
        }

        /// <summary>
        /// 添加卡片 / 保存 button of the filter footer. It only unfolds on the
        /// card level of 我的收藏, where the marking gesture is available.
        /// </summary>
        private RoundedButton CreateCollectionAddButton()
        {
            var button = new RoundedButton();
            button.Text = "添加卡片";
            button.AccessibleName = "添加卡片";
            button.Width = 218;
            button.Height = 32;
            button.CornerRadius = 8;
            button.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Margin = new Padding(0, 0, 0, 6);
            button.Visible = false;
            button.Click += CollectionAddButton_Click;
            PaintCollectionAddButton(button, false);
            return button;
        }

        /// <summary>
        /// A quiet dark button while 添加卡片 waits for a click, the blue
        /// primary button once it turned into 保存.
        /// </summary>
        private static void PaintCollectionAddButton(RoundedButton button,
            bool saving)
        {
            if (button == null)
                return;

            button.NormalColor = saving ? AppTheme.Accent : AppTheme.Divider;
            button.HoverColor = saving
                ? AppTheme.AccentHover
                : AppTheme.Border;
            button.PressedColor = saving
                ? AppTheme.AccentPressed
                : AppTheme.Placeholder;
            button.ForeColor = saving ? Color.White : AppTheme.Text;
            button.Invalidate();
        }

        /// <summary>
        /// Shows the footer button on the collection card level and keeps the
        /// footer row tall enough for it; every other page folds both away.
        /// </summary>
        private void UpdateCollectionAddButton()
        {
            bool visible = _viewMode == ViewMode.CollectionCards;
            if (_collectionAddButton != null)
            {
                string text = _collectionMarkMode ? "保存" : "添加卡片";
                if (_collectionAddButton.Text != text)
                    _collectionAddButton.Text = text;
                _collectionAddButton.AccessibleName = text;
                if (_collectionAddButton.Visible != visible)
                    _collectionAddButton.Visible = visible;
                PaintCollectionAddButton(_collectionAddButton,
                    _collectionMarkMode);
            }

            if (_filterFooterRow != null)
            {
                float height = visible
                    ? FilterFooterCollectionHeight
                    : FilterFooterHeight;
                if (_filterFooterRow.Height != height)
                    _filterFooterRow.Height = height;
            }
            if (_filterFooter != null)
                _filterFooter.PerformLayout();
        }

        private void CollectionAddButton_Click(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.CollectionCards || _activeSeries == null)
                return;

            if (_collectionMarkMode)
                SaveCollectionMarkMode();
            else
                BeginCollectionMarkMode();
        }

        /// <summary>
        /// Starts marking: the current counts of the open series are copied
        /// into the draft so clicks can change them without touching the files.
        /// </summary>
        private void BeginCollectionMarkMode()
        {
            if (_activeSeries == null)
                return;

            _collectionMarkMode = true;
            _collectionDraft.Clear();
            for (int i = 0; i < _activeSeries.Folders.Count; i++)
            {
                SeriesFolder folder = _activeSeries.Folders[i];
                var copy = new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, int> entry in
                    _collection.GetPack(folder.FolderPath))
                {
                    copy[entry.Key] = entry.Value;
                }
                _collectionDraft[CollectionStore.GetPackKey(folder.FolderPath)] =
                    copy;
            }

            UpdateCollectionAddButton();
            UpdateCollectionMarkStatus();
        }

        /// <summary>Drops the draft and leaves marking mode without writing.</summary>
        private void ExitCollectionMarkMode()
        {
            if (!_collectionMarkMode && _collectionDraft.Count == 0)
                return;

            _collectionMarkMode = false;
            _collectionDraft.Clear();
            UpdateCollectionAddButton();
        }

        /// <summary>
        /// Writes every pack of the open series. Each pack is sorted and saved
        /// on its own file; a pack the player emptied loses its file, so
        /// MyCollocation only ever holds packs with real counts.
        /// </summary>
        private void SaveCollectionMarkMode()
        {
            if (_activeSeries == null)
            {
                ExitCollectionMarkMode();
                return;
            }

            string error = null;
            for (int i = 0; i < _activeSeries.Folders.Count && error == null; i++)
            {
                SeriesFolder folder = _activeSeries.Folders[i];
                Dictionary<string, int> counts;
                if (!_collectionDraft.TryGetValue(
                    CollectionStore.GetPackKey(folder.FolderPath),
                    out counts))
                {
                    counts = new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase);
                }

                string path;
                string message;
                if (!_collection.SavePack(folder.FolderPath, counts,
                    out path, out message))
                {
                    error = message ?? "未知错误";
                }
            }

            if (error != null)
            {
                MessageBox.Show(this,
                    "保存收藏失败：\r\n\r\n" + error,
                    "保存收藏", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int copies;
            int types = _collection.CountSeries(_activeSeries, out copies);
            ExitCollectionMarkMode();
            RunSearch();
            _statusLabel.Text = string.Format(
                "收藏已保存 · 本系列 {0} 种 / {1} 张", types, copies);
        }

        /// <summary>
        /// Owned copies of a card, read from the draft while marking and from
        /// the store otherwise, so the grid always shows what a save would write.
        /// </summary>
        private int GetCollectionCount(WeissCard card)
        {
            if (card == null)
                return 0;

            if (_collectionMarkMode)
            {
                Dictionary<string, int> pack;
                if (!_collectionDraft.TryGetValue(
                    CollectionStore.GetPackKey(card.FolderPath), out pack))
                {
                    return 0;
                }

                int count;
                return pack.TryGetValue(CollectionStore.NormalizeCode(card.Id),
                    out count)
                    ? count
                    : 0;
            }

            return _collection.GetCount(card.FolderPath, card.Id);
        }

        private void SetCollectionCount(WeissCard card, int count)
        {
            if (card == null || !_collectionMarkMode)
                return;

            string code = CollectionStore.NormalizeCode(card.Id);
            if (code.Length == 0)
                return;

            string key = CollectionStore.GetPackKey(card.FolderPath);
            Dictionary<string, int> pack;
            if (!_collectionDraft.TryGetValue(key, out pack))
            {
                pack = new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);
                _collectionDraft[key] = pack;
            }

            if (count <= 0)
                pack.Remove(code);
            else
                pack[code] = Math.Min(CollectionStore.MaxCount, count);
        }

        /// <summary>
        /// Distinct owned cards and their copies of one series. Marking mode
        /// reads the draft, so the header follows the unsaved clicks.
        /// </summary>
        private int CountOwnedInSeries(SeriesDefinition series, out int copies)
        {
            copies = 0;
            if (series == null)
                return 0;

            int types = 0;
            for (int i = 0; i < series.Folders.Count; i++)
            {
                SeriesFolder folder = series.Folders[i];
                IDictionary<string, int> pack;
                if (_collectionMarkMode)
                {
                    Dictionary<string, int> draft;
                    if (!_collectionDraft.TryGetValue(
                        CollectionStore.GetPackKey(folder.FolderPath), out draft))
                    {
                        continue;
                    }
                    pack = draft;
                }
                else
                {
                    pack = _collection.GetPack(folder.FolderPath);
                }

                types += pack.Count;
                foreach (KeyValuePair<string, int> entry in pack)
                    copies += entry.Value;
            }
            return types;
        }

        /// <summary>Status line while marking: what the clicks do and how much is marked.</summary>
        private void UpdateCollectionMarkStatus()
        {
            if (_statusLabel == null || _activeSeries == null)
                return;

            int copies;
            int types = CountOwnedInSeries(_activeSeries, out copies);
            _statusLabel.Text = string.Format(
                "标记中：{0} 种 / {1} 张 · 左键+1 / 右键-1（上限 {2}）",
                types, copies, CollectionStore.MaxCount);
        }

        /// <summary>
        /// Left click of the collection card level. 添加卡片 turns the click
        /// into a mark (0 → 1 → … → 99 → 0) so the same grid both browses and
        /// edits the collection; without marking it opens the detail page.
        /// </summary>
        private void TileGrid_CollectionItemClick(object sender,
            TileGridItemEventArgs e)
        {
            if (_viewMode != ViewMode.CollectionCards ||
                e == null || e.Item == null)
            {
                return;
            }

            WeissCard card = e.Item.Tag as WeissCard;
            if (card == null)
                return;

            if (!_collectionMarkMode)
            {
                ShowCardDetail(card);
                return;
            }

            int current = GetCollectionCount(card);
            int next = current >= CollectionStore.MaxCount
                ? 0
                : current + 1;
            ApplyCollectionCount(e.Item, card, next);
        }

        private void TileGrid_CollectionItemRightClick(object sender,
            TileGridItemEventArgs e)
        {
            if (_viewMode != ViewMode.CollectionCards || !_collectionMarkMode ||
                e == null || e.Item == null)
            {
                return;
            }

            WeissCard card = e.Item.Tag as WeissCard;
            if (card == null)
                return;

            int current = GetCollectionCount(card);
            if (current <= 0)
                return;

            ApplyCollectionCount(e.Item, card, current - 1);
        }

        /// <summary>
        /// Long press keeps the detail page reachable while marking, exactly
        /// like the deck editor where the plain click has another meaning.
        /// </summary>
        private void TileGrid_CollectionItemLongPress(object sender,
            TileGridItemEventArgs e)
        {
            if (_viewMode != ViewMode.CollectionCards ||
                e == null || e.Item == null)
            {
                return;
            }

            ShowCardDetail(e.Item.Tag as WeissCard);
        }

        /// <summary>
        /// Mirrors one draft change on the tile: colour returns and the red
        /// count sticker appears as soon as the card counts as owned.
        /// </summary>
        private void ApplyCollectionCount(TileGridItem item, WeissCard card,
            int count)
        {
            SetCollectionCount(card, count);
            item.OwnedCount = count;
            item.Dimmed = count <= 0;
            if (_tileGrid != null)
                _tileGrid.Invalidate();
            UpdateCollectionMarkStatus();
        }
    }
}
