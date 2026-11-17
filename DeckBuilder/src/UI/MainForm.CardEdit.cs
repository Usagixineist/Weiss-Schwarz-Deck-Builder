using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 编辑卡片 (写卡): 修改卡片 browses a series exactly like 系列卡表 and
    /// opens the editor page on a click, 添加卡片 starts a blank card. The
    /// editor is a normal page of the content host (not a popup) and saving
    /// goes through <see cref="CardDataWriter"/>, which rewrites the lines the
    /// editor owns in the card pack's CardData.txt and keeps a .bak next to it.
    /// </summary>
    public sealed partial class MainForm
    {
        private RoundedButton _navCardEditButton;
        private RoundedButton _navCardEditModifyButton;
        private RoundedButton _navCardEditAddButton;
        private CardEditorPage _cardEditorPage;

        /// <summary>
        /// The card list the 取消 button returns to: the series whose cards are
        /// being edited. Null when 添加卡片 was started from the cover grid, in
        /// which case 取消 goes back to the covers.
        /// </summary>
        private SeriesDefinition _cardEditorReturnSeries;

        /// <summary>True when 取消 returns to 添加卡片 rather than 修改卡片.</summary>
        private bool _cardEditorReturnNew;

        /// <summary>
        /// 添加卡片 was clicked without an open series: the next cover click
        /// lands in the editor instead of the plain card list.
        /// </summary>
        private bool _cardEditAddPending;

        private bool IsCardEditSection
        {
            get
            {
                return _viewMode == ViewMode.CardEditList ||
                    _viewMode == ViewMode.CardEditCards ||
                    _viewMode == ViewMode.CardEditor;
            }
        }

        private void NavCardEditButton_Click(object sender, EventArgs e)
        {
            // The section label opens 修改卡片; clicking it again on the card
            // list or in the editor steps back to the series covers.
            if (_viewMode != ViewMode.CardEditList)
                ShowCardEdit();
        }

        private void NavCardEditModifyButton_Click(object sender, EventArgs e)
        {
            if (!IsCardEditSection)
                ShowCardEdit();
        }

        private void NavCardEditAddButton_Click(object sender, EventArgs e)
        {
            ShowCardAdd();
        }

        /// <summary>修改卡片: the series cover grid of 系列卡表.</summary>
        private void ShowCardEdit()
        {
            ShowCardEditHome(false);
        }

        /// <summary>
        /// 添加卡片: with a series already open the editor starts right away;
        /// otherwise the cover grid picks the series and the click lands in the
        /// editor (see <see cref="OnCardEditSeriesOpened"/>).
        /// </summary>
        private void ShowCardAdd()
        {
            if (_activeSeries != null && IsCardEditSection)
            {
                SetNavSubItemsVisible(NavSection.CardEdit);
                SetActiveNavItem(_navCardEditAddButton);
                _contentTitle.Text = "添加卡片";
                _cardEditAddPending = false;
                ShowCardAddEditor(_activeSeries);
                return;
            }

            ShowCardEditHome(true);
        }

        private void ShowCardEditHome(bool addMode)
        {
            _cardEditAddPending = addMode;
            _viewMode = ViewMode.CardEditList;
            _activeSeries = null;
            _results.Clear();
            CloseCardEditor();

            CloseDeckDetail();
            _navColumnStyle.Width = NavExpandedWidth;
            _filterColumnStyle.Width = 0F;
            _filterHost.Visible = false;
            _filterBody.ScrollToTop();
            SetNavExpanded(true);
            SetNavSubItemsVisible(NavSection.CardEdit);
            ShowContentPage(_browsePage);
            SetActiveNavItem(addMode
                ? _navCardEditAddButton
                : _navCardEditModifyButton);
            UpdateNewDeckStripVisibility();

            _backButton.Text = "←  系列列表";
            _contentTitle.Text = addMode ? "添加卡片" : "修改卡片";
            _contentSubtitle.Text = string.Format(
                "共 {0} 个系列 · {1}",
                _database.SeriesNames.Count,
                addMode ? "选择系列后进入编辑界面" : "选择系列后编辑卡片");

            _tileGrid.EmptyText = "没有找到系列";
            _tileGrid.ConfigureSeries();
            RefreshSeriesItems();
            _tileGrid.ScrollToTop();
            ExitCollectionMarkMode();
            UpdateCollectionChrome();
            ScheduleDarkChrome();
        }

        /// <summary>
        /// Called at the end of <see cref="OpenSeries"/> for the 编辑卡片
        /// section. 添加卡片 continues into the editor; 修改卡片 just keeps the
        /// card list, whose clicks open the editor.
        /// </summary>
        private void OnCardEditSeriesOpened(SeriesDefinition series)
        {
            bool add = _cardEditAddPending;
            _cardEditAddPending = false;
            _contentTitle.Text = add ? "添加卡片" : "修改卡片";

            if (!add || series == null)
                return;

            if (!IsHandleCreated)
            {
                ShowCardAddEditor(series);
                return;
            }

            // Let the card list finish laying out before the editor replaces it.
            BeginInvoke((Action)delegate { ShowCardAddEditor(series); });
        }

        private void ShowCardEditor(WeissCard card)
        {
            if (card == null || _activeSeries == null)
                return;

            SeriesFolder folder = FindCardFolder(card.FolderPath);
            CardEditorDraft draft = CardEditorDraft.FromCard(card, folder);
            if (draft == null)
                return;

            ShowCardEditorPage(draft, null, _activeSeries);
        }

        private void ShowCardAddEditor(SeriesDefinition series)
        {
            if (series == null)
                return;

            SeriesDefinition loaded = _database.GetSeries(series.Name);
            if (loaded == null)
            {
                _statusLabel.Text = "系列未找到：" + series.Name;
                return;
            }

            _activeSeries = loaded;
            SeriesFolder folder = loaded.Folders.Count > 0
                ? loaded.Folders[0]
                : null;

            CardEditorDraft draft = CardEditorDraft.CreateBlank(loaded, folder);
            draft.SeriesName = loaded.Name;
            draft.PreviewImagePath = string.Empty;
            ShowCardEditorPage(draft, loaded.Folders, loaded);
        }

        /// <summary>
        /// The folder a card belongs to. Alternate-art clones share the folder
        /// of the base card, and a series with a single folder falls back to it
        /// so an odd FolderPath never blocks editing.
        /// </summary>
        private SeriesFolder FindCardFolder(string folderPath)
        {
            if (_activeSeries == null)
                return null;

            for (int i = 0; i < _activeSeries.Folders.Count; i++)
            {
                SeriesFolder folder = _activeSeries.Folders[i];
                if (string.Equals(folder.FolderPath, folderPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return folder;
                }
            }

            return _activeSeries.Folders.Count == 1
                ? _activeSeries.Folders[0]
                : null;
        }

        /// <summary>
        /// The editor page lives next to the other content pages, so 编辑卡片
        /// reads as a page instead of a dialog covering the card list.
        /// </summary>
        private void EnsureCardEditorPage()
        {
            if (_cardEditorPage != null)
                return;

            _cardEditorPage = new CardEditorPage();
            _cardEditorPage.Dock = DockStyle.Fill;
            _cardEditorPage.Database = _database;
            _cardEditorPage.Thumbnails = _thumbnails;
            _cardEditorPage.CloseRequested += CardEditorPage_CloseRequested;
            _cardEditorPage.SaveRequested += CardEditorPage_SaveRequested;
            _cardEditorPage.Visible = false;
            _contentHost.Controls.Add(_cardEditorPage);
        }

        private void ShowCardEditorPage(CardEditorDraft draft,
            IList<SeriesFolder> folders, SeriesDefinition series)
        {
            if (draft == null)
                return;

            EnsureCardEditorPage();
            _viewMode = ViewMode.CardEditor;
            _cardEditorReturnSeries = series;
            _cardEditorReturnNew = draft.IsNew;

            // The editor needs the width: the filter column (250px) belongs to
            // the card list and folds away while the page is open.
            _navColumnStyle.Width = NavCompactWidth;
            _filterColumnStyle.Width = 0F;
            _filterHost.Visible = false;
            SetNavExpanded(false);
            SetNavSubItemsVisible(NavSection.CardEdit);
            SetActiveNavItem(draft.IsNew
                ? _navCardEditAddButton
                : _navCardEditModifyButton);

            _contentTitle.Text = draft.IsNew ? "添加卡片" : "修改卡片";
            _contentSubtitle.Text = draft.TargetLabel;
            ShowContentPage(_cardEditorPage);
            _cardEditorPage.LoadDraft(draft, folders);
            ScheduleDarkChrome();
        }

        private void CardEditorPage_CloseRequested(object sender, EventArgs e)
        {
            CancelCardEditor();
        }

        /// <summary>
        /// 取消: back to the card list the editor was opened from, or to the
        /// cover grid when there is no series to return to.
        /// </summary>
        private void CancelCardEditor()
        {
            bool hasSeries = _cardEditorReturnSeries != null && _activeSeries != null;
            bool wasNew = _cardEditorReturnNew;
            CloseCardEditor();

            if (!hasSeries)
            {
                ShowCardEditHome(wasNew);
                return;
            }

            _viewMode = ViewMode.CardEditCards;
            _navColumnStyle.Width = NavCompactWidth;
            _filterColumnStyle.Width = 250F;
            _filterHost.Visible = true;
            SetNavExpanded(false);
            SetActiveNavItem(wasNew
                ? _navCardEditAddButton
                : _navCardEditModifyButton);
            _backButton.Text = "←  选择系列";
            _contentTitle.Text = wasNew ? "添加卡片" : "修改卡片";
            ShowContentPage(_browsePage);
            if (_tileGrid != null && _tileGrid.CanFocus)
                _tileGrid.Focus();
            ScheduleDarkChrome();
        }

        /// <summary>Unloads the draft and hides the page; it never navigates.</summary>
        private void CloseCardEditor()
        {
            if (_cardEditorPage != null && _cardEditorPage.Visible)
            {
                _cardEditorPage.UnloadDraft();
                _cardEditorPage.Visible = false;
            }
        }

        /// <summary>
        /// 保存: the page hands back the edited draft, the writer updates the
        /// card pack file and the card list reloads that series so the grid
        /// shows the new state without restarting the app.
        /// </summary>
        private void CardEditorPage_SaveRequested(object sender, EventArgs e)
        {
            if (_cardEditorPage == null)
                return;

            CardEditorDraft draft = _cardEditorPage.ReadDraft();
            if (draft == null)
                return;

            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            CardWriteResult result;
            try
            {
                result = CardDataWriter.Save(draft);
            }
            finally
            {
                Cursor = previousCursor;
            }

            if (!result.Success)
            {
                _cardEditorPage.SetStatus(result.Message, true);
                return;
            }

            string seriesName = draft.SeriesName;
            _cardEditorPage.InvalidateReference();
            CloseCardEditor();
            ReloadAfterCardSave(seriesName, result);
        }

        private void ReloadAfterCardSave(string seriesName, CardWriteResult result)
        {
            _database.Reload();

            SeriesDefinition series = _database.GetSeries(seriesName);
            if (series == null)
            {
                ShowCardEdit();
                _statusLabel.Text = result.Message;
                return;
            }

            // OpenSeries keeps the card-edit page (the series picker already
            // switched to the card list) and reruns the search against the file
            // that was just written.
            _viewMode = ViewMode.CardEditList;
            OpenSeries(series);
            _statusLabel.Text = result.Message;
        }
    }
}
