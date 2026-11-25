using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Infrastructure;
using WeissDeckBuilder.Models;
using WeissDeckBuilder.Search;

namespace WeissDeckBuilder.UI
{
    public sealed partial class MainForm
    {
        private void NavSeriesButton_Click(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.SeriesList)
                ShowSeriesList();
        }

        /// <summary>
        /// The WS brand label doubles as the only way back to 今日运势; Home
        /// deliberately has no sidebar row of its own.
        /// </summary>
        private void NavHomeButton_Click(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.Home)
                ShowHome();
        }

        /// <summary>
        /// Compact rail counterpart of the sidebar search block: the rail is
        /// too narrow for the input, so this entry returns to the first level
        /// (which expands the rail again) and hands the caret to the search
        /// box, ready for typing.
        /// </summary>
        private void NavSearchButton_Click(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.SeriesList)
                ShowSeriesList();

            FocusSeriesSearchBox();
        }

        private void NavSettingsButton_Click(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.Settings)
                ShowSettings();
        }

        /// <summary>
        /// 附加功能: the first click scans DeckBuilder\extra, later clicks reuse
        /// the pages that are already alive (so a plugin keeps its state).
        /// </summary>
        private void NavExtrasButton_Click(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.Extras)
                ShowExtras();
        }

        /// <summary>
        /// 启动游戏 hands the exe the `noUpdate` argument, exactly like
        /// 不热更启动游戏.lnk, so the bundled updater never rewrites the card
        /// table this app edits. The DeckBuilder window stays open and the game
        /// window comes up on top of it.
        /// </summary>
        private void NavLaunchGameButton_Click(object sender, EventArgs e)
        {
            if (GameLauncher.IsRunning())
            {
                MessageBox.Show(this, "游戏已经在运行。", "启动游戏",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string error;
            GameLaunchTarget target = GameLauncher.Resolve(
                _database == null ? null : _database.DataRoot, out error);
            if (target == null)
            {
                MessageBox.Show(this, error, "启动游戏",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!GameLauncher.TryLaunch(target, out error))
            {
                MessageBox.Show(this, error, "启动游戏",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BackButton_Click(object sender, EventArgs e)
        {
            // The deck editor keeps its own series picker, so its back link
            // returns there instead of leaving the editor; the collection card
            // list returns to the collection covers.
            if (_viewMode == ViewMode.DeckNew)
                ShowDeckNew();
            else if (_viewMode == ViewMode.CollectionCards)
                ShowCollection();
            else if (_viewMode == ViewMode.CardEditCards)
                ShowCardEdit();
            else
                ShowSeriesList();
        }

        private void TileGrid_ItemClick(object sender, TileGridItemEventArgs e)
        {
            if (e.Item == null)
                return;

            if (_viewMode == ViewMode.SeriesList ||
                _viewMode == ViewMode.CollectionList ||
                _viewMode == ViewMode.CardEditList ||
                (_viewMode == ViewMode.DeckNew && _activeSeries == null))
            {
                SeriesDefinition series = e.Item.Tag as SeriesDefinition;
                if (series != null)
                    OpenSeries(series);
                return;
            }

            if (_viewMode == ViewMode.CardList)
            {
                WeissCard card = e.Item.Tag as WeissCard;
                if (card != null)
                    ShowCardDetail(card);
                return;
            }

            if (_viewMode == ViewMode.CardEditCards)
            {
                // 修改卡片 opens the editor instead of the read-only detail
                // page; the card itself stays the single source of the fields.
                WeissCard card = e.Item.Tag as WeissCard;
                if (card != null)
                    ShowCardEditor(card);
                return;
            }

            if (IsDeckDraftLevel)
            {
                // 编辑卡组 collects cards with a plain click; the details stay
                // on a long press so the deck can be assembled without ever
                // leaving the card grid.
                AddCardToDeckDraft(e.Item.Tag as WeissCard);
            }
        }

        private void ShowCardDetail(WeissCard card)
        {
            if (card == null)
                return;

            EnsureCardDetailOverlay();
            if (_cardDetailOverlay.Visible)
                return;

            _cardDetailOverlay.Bounds = ClientRectangle;
            Bitmap backdrop = CaptureDetailBackdrop();
            _cardDetailOverlay.ShowCard(card, backdrop);
            _cardDetailOverlay.Visible = true;
            _cardDetailOverlay.BringToFront();
            _cardDetailOverlay.Focus();
        }

        private void EnsureCardDetailOverlay()
        {
            if (_cardDetailOverlay != null)
                return;

            _cardDetailOverlay = new CardDetailOverlay(_detailThumbnails);
            _cardDetailOverlay.NameResolver = ResolveCardsByName;
            _cardDetailOverlay.CardNavigation = GetAdjacentDetailCard;
            _cardDetailOverlay.Glossary = _triggerGlossary;
            _cardDetailOverlay.Dock = DockStyle.Fill;
            _cardDetailOverlay.CloseRequested +=
                CardDetailOverlay_CloseRequested;
            Controls.Add(_cardDetailOverlay);
            _cardDetailOverlay.Visible = false;
        }

        private void CardDetailOverlay_CloseRequested(
            object sender, EventArgs e)
        {
            HideCardDetail();
        }

        /// <summary>
        /// Finds the cards of the open series that carry a name written inside
        /// 「」 (or “” / "" / 『』) in an effect text. Alternate-art clones are
        /// skipped so the related-card strip shows one entry per card, and the
        /// matches are ordered from exact to loose.
        /// </summary>
        private IList<WeissCard> ResolveCardsByName(
            string name, string preferredFolder)
        {
            if (_activeSeries == null || string.IsNullOrEmpty(name))
                return new List<WeissCard>();

            return CardNameMatcher.Resolve(
                _activeSeries.EnumerateCards(), name.Trim(),
                CardNameMatcher.DefaultMaxResults, preferredFolder);
        }

        /// <summary>
        /// Previous/next card for the detail page. The open deck and the cards
        /// currently shown by the tile grid define the order and both stop at
        /// their ends; only a card that is not part of them (a related card
        /// opened from the bottom strip) falls back to the order of its own set
        /// so navigation still works there.
        /// </summary>
        private WeissCard GetAdjacentDetailCard(WeissCard card, int step)
        {
            if (card == null || step == 0)
                return null;

            if (_viewMode == ViewMode.DeckEdit && _activeDeck != null &&
                _deckCardResolver != null)
            {
                bool inDeck;
                WeissCard neighbour = GetAdjacentDeckCard(card, step, out inDeck);
                // The first and the last card of the deck answer null, and
                // that null is returned as is: 查看卡组 must not continue in
                // the series the deck was built from.
                if (inDeck)
                    return neighbour;
            }

            if ((_viewMode == ViewMode.CardList ||
                 _viewMode == ViewMode.CollectionCards ||
                 _viewMode == ViewMode.DeckNew) &&
                _tileGrid != null)
            {
                int index = _tileGrid.IndexOfItem(card);
                if (index >= 0)
                    return _tileGrid.GetItemTag(index + step) as WeissCard;
            }

            if (_activeSeries == null)
                return null;

            for (int i = 0; i < _activeSeries.Folders.Count; i++)
            {
                List<WeissCard> cards = _activeSeries.Folders[i].Cards;
                int index = cards.IndexOf(card);
                if (index < 0)
                    continue;

                int target = index + step;
                return target >= 0 && target < cards.Count
                    ? cards[target]
                    : null;
            }
            return null;
        }

        /// <summary>
        /// Previous/next card inside the open deck. Cards are walked in code
        /// order like the 缩略 grid: duplicate copies share one entry, so the
        /// arrows never stop on the same card twice and always move on.
        /// <paramref name="inDeck"/> reports whether the card belongs to the
        /// deck at all, so the caller can tell "end of the deck" apart from
        /// "this card is not in the deck".
        /// </summary>
        private WeissCard GetAdjacentDeckCard(WeissCard card, int step,
            out bool inDeck)
        {
            inDeck = false;
            IList<string> codes =
                DeckComposition.DistinctCards(_activeDeck.CardCodes);
            var cards = new List<WeissCard>(codes.Count);
            int index = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                WeissCard resolved =
                    _deckCardResolver.Resolve(_activeDeck, codes[i]);
                if (resolved == null)
                    continue;

                if (index < 0 && IsSameCard(resolved, card))
                    index = cards.Count;

                cards.Add(resolved);
            }

            if (index < 0)
                return null;

            inDeck = true;
            int target = index + step;
            return target >= 0 && target < cards.Count
                ? cards[target]
                : null;
        }

        /// <summary>
        /// Matches the card of the detail page with a card of the deck. The
        /// resolver hands out one instance per code, but the overlay can also be
        /// opened from a series list, so the codes are compared as well.
        /// </summary>
        private static bool IsSameCard(WeissCard left, WeissCard right)
        {
            if (left == null || right == null)
                return false;
            if (ReferenceEquals(left, right))
                return true;

            return string.Equals(
                CardCodeMatcher.Canonical(left.Id),
                CardCodeMatcher.Canonical(right.Id),
                StringComparison.OrdinalIgnoreCase);
        }

        private void HideCardDetail()
        {
            if (_cardDetailOverlay == null)
                return;

            _cardDetailOverlay.Visible = false;
            _cardDetailOverlay.Clear();
            if (_tileGrid != null && _tileGrid.CanFocus)
                _tileGrid.Focus();
        }

        private Bitmap CaptureDetailBackdrop()
        {
            Bitmap bitmap = null;
            try
            {
                int width = Math.Max(1, ClientSize.Width);
                int height = Math.Max(1, ClientSize.Height);
                bitmap = new Bitmap(width, height);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        PointToScreen(Point.Empty),
                        Point.Empty,
                        new Size(width, height),
                        CopyPixelOperation.SourceCopy);
                }
                return bitmap;
            }
            catch
            {
                if (bitmap != null)
                    bitmap.Dispose();
                return null;
            }
        }

        private void SortOption_CheckedChanged(object sender, EventArgs e)
        {
            if (_populatingCombos)
                return;

            RadioButton option = sender as RadioButton;
            if (option == null || !option.Checked)
                return;

            _sortMode = option.Tag is int ? (int)option.Tag : 0;
            RunSearch();
        }

        private void SeriesSearchText_TextChanged(object sender, EventArgs e)
        {
            if (_updatingSeriesControls)
                return;
            RefreshSeriesItems();
        }

        private void SeriesSearchInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            SubmitSeriesSearch();
        }

        /// <summary>
        /// Enter in the sidebar search opens the series covers with the current
        /// query applied. Inside the deck editor it returns to that page's own
        /// series picker instead of leaving the editor.
        /// </summary>
        private void SubmitSeriesSearch()
        {
            if (_seriesSearchText == null ||
                _seriesSearchText.Text.Trim().Length == 0)
            {
                return;
            }

            if (_viewMode == ViewMode.DeckNew)
            {
                if (_activeSeries == null)
                    RefreshSeriesItems();
                else
                    ShowDeckNew();
                return;
            }

            if (_viewMode == ViewMode.SeriesList ||
                _viewMode == ViewMode.CollectionList)
            {
                RefreshSeriesItems();
            }
            else if (_viewMode == ViewMode.CollectionCards)
            {
                ShowCollection();
            }
            else
            {
                ShowSeriesList();
            }
        }

        private void SeriesSortOption_CheckedChanged(object sender, EventArgs e)
        {
            if (_updatingSeriesControls)
                return;

            RadioButton option = sender as RadioButton;
            if (option == null || !option.Checked || !(option.Tag is int))
                return;

            _seriesSortMode = (SeriesSortMode)(int)option.Tag;
            RefreshSeriesItems();
        }

        private void FilterCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (_populatingCombos)
                return;

            RunSearch();
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            RunSearch();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                RunSearch();
            }
        }

        private void ShowAltCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (_populatingCombos || _updatingAltToggles)
                return;

            if (!_showAltCheck.Checked && _onlyAltCheck.Checked)
            {
                _updatingAltToggles = true;
                try
                {
                    _onlyAltCheck.Checked = false;
                }
                finally
                {
                    _updatingAltToggles = false;
                }
            }

            RunSearch();
        }

        private void OnlyAltCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (_populatingCombos || _updatingAltToggles)
                return;

            if (_onlyAltCheck.Checked && !_showAltCheck.Checked)
            {
                _updatingAltToggles = true;
                try
                {
                    _showAltCheck.Checked = true;
                }
                finally
                {
                    _updatingAltToggles = false;
                }
            }

            RunSearch();
        }

        private void RunSearch()
        {
            bool browsing = _viewMode == ViewMode.CardList ||
                _viewMode == ViewMode.DeckNew ||
                _viewMode == ViewMode.CollectionCards ||
                _viewMode == ViewMode.CardEditCards;
            if (_populatingCombos || !browsing || _activeSeries == null)
            {
                return;
            }

            _statusLabel.Text = "搜索中...";
            _statusLabel.Refresh();
            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;

            try
            {
                SearchQuery query = BuildQuery();
                using (LoadProfiler.Measure("RunSearch", "Search"))
                    _results = _searchService.Search(_activeSeries, query);

                // On the collection card list the grid also carries the owned
                // count: coloured tiles with a red sticker for cards the player
                // has, greyed tiles for the rest. 只显示拥有 drops the latter.
                bool collection = _viewMode == ViewMode.CollectionCards;
                bool onlyOwned = collection && IsOnlyOwnedChecked;
                var items = new List<TileGridItem>();
                using (LoadProfiler.Measure("RunSearch", "BuildItems"))
                {
                    for (int i = 0; i < _results.Count; i++)
                    {
                        WeissCard card = _results[i];
                        int owned = collection ? GetCollectionCount(card) : 0;
                        if (onlyOwned && owned <= 0)
                            continue;

                        string title = string.IsNullOrEmpty(card.JapaneseName)
                            ? card.Name
                            : card.JapaneseName;
                        var item = new TileGridItem();
                        item.Title = title;
                        item.Subtitle = card.Id;
                        item.ImagePath = card.ImagePath;
                        item.OwnedCount = owned;
                        item.Dimmed = collection && owned <= 0;
                        item.Tag = card;
                        items.Add(item);
                    }
                }

                _tileGrid.EmptyText = onlyOwned
                    ? "没有拥有的卡片"
                    : "没有找到卡片";
                using (LoadProfiler.Measure("RunSearch", "SetItems"))
                    _tileGrid.SetItems(items);
                ScheduleDarkChrome();

                string summary = string.Format("找到 {0} 张 · 卡池 {1} 张",
                    items.Count, _activeSeries.CardCount);
                if (collection)
                {
                    int ownedCopies;
                    int ownedTypes = CountOwnedInSeries(_activeSeries,
                        out ownedCopies);
                    summary += string.Format(" · 已拥有 {0} 种 / {1} 张",
                        ownedTypes, ownedCopies);
                }
                _statusLabel.Text = summary;
                string folderName = string.IsNullOrEmpty(_activeSeries.FolderName)
                    ? _activeSeries.Name
                    : _activeSeries.FolderName;

                // The editor keeps its page title, so the series name moves to
                // the subtitle to keep both pages distinguishable.
                string seriesLabel = _viewMode == ViewMode.DeckNew
                    ? _activeSeries.Name
                    : folderName;
                _contentSubtitle.Text = seriesLabel + " · " + summary;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "搜索失败";
                MessageBox.Show(this, ex.Message, "搜索失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = previousCursor;
            }
        }

        private SearchQuery BuildQuery()
        {
            var query = new SearchQuery();
            if (_activeSeries != null)
                query.SeriesName = _activeSeries.Name;
            query.CardName = _nameText.Text;
            AddCheckedValues(_setChecks, query.SetNames);
            AddCheckedValues(_levelChecks, query.Levels);
            AddCheckedValues(_costChecks, query.Costs);
            AddCheckedValues(_colorChecks, query.Colors);
            AddCheckedValues(_typeChecks, query.Types);
            query.ShowAltArt = _showAltCheck.Checked;
            query.OnlyAltArt = _onlyAltCheck.Checked;
            query.SortMode = (CardSortMode)Math.Max(0, _sortMode);
            return query;
        }

        private static void AddCheckedValues(
            Control container,
            List<string> target)
        {
            for (int i = 0; i < container.Controls.Count; i++)
            {
                CheckBox check = container.Controls[i] as CheckBox;
                if (check != null && check.Checked)
                {
                    string value = check.Tag as string;
                    target.Add(value ?? check.Text);
                }
            }
        }
    }
}
