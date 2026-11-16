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
    /// opens the card editor on a click, 添加卡片 starts a blank card. Saving
    /// goes through <see cref="CardDataWriter"/>, which rewrites the matching
    /// block of the card pack's CardData.txt and keeps a .bak next to it.
    /// </summary>
    public sealed partial class MainForm
    {
        private RoundedButton _navCardEditButton;
        private RoundedButton _navCardEditModifyButton;
        private RoundedButton _navCardEditAddButton;
        private CardEditorOverlay _cardEditorOverlay;

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
                    _viewMode == ViewMode.CardEditCards;
            }
        }

        private void NavCardEditButton_Click(object sender, EventArgs e)
        {
            // The section label opens 修改卡片; clicking it again on the card
            // list steps back to the series covers, like 系列卡表.
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

            // Let the card list finish laying out before the editor covers it.
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

            ShowCardEditorOverlay(draft, null);
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
            ShowCardEditorOverlay(draft, loaded.Folders);
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

        private void EnsureCardEditorOverlay()
        {
            if (_cardEditorOverlay != null)
                return;

            _cardEditorOverlay = new CardEditorOverlay();
            _cardEditorOverlay.Dock = DockStyle.Fill;
            _cardEditorOverlay.CloseRequested += CardEditorOverlay_CloseRequested;
            _cardEditorOverlay.SaveRequested += CardEditorOverlay_SaveRequested;
            Controls.Add(_cardEditorOverlay);
            _cardEditorOverlay.Visible = false;
        }

        private void ShowCardEditorOverlay(CardEditorDraft draft,
            IList<SeriesFolder> folders)
        {
            if (draft == null)
                return;

            EnsureCardEditorOverlay();
            if (_cardEditorOverlay.Visible)
                return;

            _cardEditorOverlay.Bounds = ClientRectangle;
            Bitmap backdrop = CaptureDetailBackdrop();
            _cardEditorOverlay.ShowEditor(draft, backdrop, folders);
            _cardEditorOverlay.Visible = true;
            _cardEditorOverlay.BringToFront();
        }

        private void CardEditorOverlay_CloseRequested(object sender, EventArgs e)
        {
            HideCardEditor();
        }

        private void HideCardEditor()
        {
            if (_cardEditorOverlay == null || !_cardEditorOverlay.Visible)
                return;

            _cardEditorOverlay.HideEditor();
            if (_tileGrid != null && _tileGrid.CanFocus)
                _tileGrid.Focus();
        }

        /// <summary>
        /// 保存: the overlay hands back the edited draft, the writer updates the
        /// card pack file and the page reloads that series so the grid shows
        /// the new state without restarting the app.
        /// </summary>
        private void CardEditorOverlay_SaveRequested(object sender, EventArgs e)
        {
            if (_cardEditorOverlay == null)
                return;

            CardEditorDraft draft = _cardEditorOverlay.ReadDraft();
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
                _cardEditorOverlay.SetStatus(result.Message, true);
                return;
            }

            string seriesName = draft.SeriesName;
            HideCardEditor();
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
