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
    /// opens the editor page on a click, 添加卡片 starts a blank card. 添加异画
    /// has no entry of its own any more: the block sits in the editor of every
    /// existing card, so a variant is added right where the card is edited.
    /// The editor is a normal page of the content host (not a popup) and saving
    /// goes through <see cref="CardDataWriter"/>, which rewrites the lines the
    /// editor owns in the card pack's CardData.txt and keeps a .bak next to it;
    /// an 异画 is written by <see cref="AltArtworkWriter"/> into
    /// StreamingAssets\AlternateArtwork.
    /// </summary>
    public sealed partial class MainForm
    {
        private RoundedButton _navCardEditButton;
        private RoundedButton _navCardEditModifyButton;
        private RoundedButton _navCardEditAddButton;
        private CardEditorPage _cardEditorPage;

        /// <summary>
        /// Base card the open editor writes an 异画 for: the card itself, or the
        /// card a clone was copied from. Empty hides the 添加异画 block.
        /// </summary>
        private string _cardEditorBaseCardId = string.Empty;

        /// <summary>True once 保存异画 wrote a file, so the grid reloads on exit.</summary>
        private bool _cardAltArtWritten;

        /// <summary>
        /// The card list the 取消 button returns to. 修改卡片 sets it to the
        /// series of the card that was opened; 添加卡片 sets it only when a
        /// series was already open (its packs are the fallback when the 卡号
        /// names none). Null means 取消 returns to the series covers.
        /// </summary>
        private SeriesDefinition _cardEditorReturnSeries;

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
            ShowCardEditHome();
        }

        /// <summary>
        /// 添加卡片: the 卡号 decides which card pack the new block is written
        /// to, so the editor opens right away instead of asking for a series
        /// first. The series that happens to be open only serves as a fallback
        /// for a 卡号 whose prefix the card tables do not know, and as the page
        /// 取消 returns to.
        /// </summary>
        private void ShowCardAdd()
        {
            ShowCardAddEditor(IsCardEditSection ? _activeSeries : null);
        }

        private void ShowCardEditHome()
        {
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
            SetActiveNavItem(_navCardEditModifyButton);
            UpdateNewDeckStripVisibility();

            _backButton.Text = "←  系列列表";
            _contentTitle.Text = "修改卡片";
            _contentSubtitle.Text = string.Format(
                "共 {0} 个系列 · 选择系列后编辑卡片",
                _database.SeriesNames.Count);

            _tileGrid.EmptyText = "没有找到系列";
            _tileGrid.ConfigureSeries();
            RefreshSeriesItems();
            _tileGrid.ScrollToTop();
            ExitCollectionMarkMode();
            UpdateCollectionChrome();
            ScheduleDarkChrome();
        }

        private void ShowCardEditor(WeissCard card)
        {
            if (card == null || _activeSeries == null)
                return;

            SeriesFolder folder = FindCardFolder(card.FolderPath);
            CardEditorDraft draft = CardEditorDraft.FromCard(card, folder);
            if (draft == null)
                return;

            // An 异画 clone has no block of its own: the variant of that card
            // belongs to the card it was copied from.
            WeissCard baseCard = card;
            while (baseCard.CloneOf != null &&
                !ReferenceEquals(baseCard.CloneOf, baseCard))
            {
                baseCard = baseCard.CloneOf;
            }
            _cardEditorBaseCardId = baseCard.Id ?? string.Empty;

            ShowCardEditorPage(draft, null, _activeSeries);
        }

        /// <summary>
        /// Opens an empty editor. <paramref name="series"/> may be null: the
        /// editor then starts without a series, and the 卡号 fills in the card
        /// pack while the player types (see CardEditorPage.UpdatePackTarget).
        /// </summary>
        private void ShowCardAddEditor(SeriesDefinition series)
        {
            SeriesDefinition loaded = null;
            IList<SeriesFolder> folders = null;
            SeriesFolder folder = null;

            if (series != null)
            {
                loaded = _database.GetSeries(series.Name);
                if (loaded != null)
                {
                    _activeSeries = loaded;
                    folders = loaded.Folders;

                    // A single pack leaves no choice, so it is prefilled; a
                    // multi-pack series waits for the 卡号 to pick one.
                    if (loaded.Folders.Count == 1)
                        folder = loaded.Folders[0];
                }
            }

            CardEditorDraft draft = CardEditorDraft.CreateBlank(loaded, folder);
            draft.PreviewImagePath = string.Empty;
            ShowCardEditorPage(draft, folders, loaded);
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
            _cardEditorPage.Glossary = _triggerGlossary;
            _cardEditorPage.CloseRequested += CardEditorPage_CloseRequested;
            _cardEditorPage.SaveRequested += CardEditorPage_SaveRequested;
            _cardEditorPage.AltArtSaveRequested += CardEditorPage_AltArtSaveRequested;
            _cardEditorPage.Visible = false;
            _cardEditorPage.TargetChanged += CardEditorPage_TargetChanged;
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
            _cardAltArtWritten = false;
            _cardEditorPage.AltArtBaseCardId = draft.IsNew
                ? string.Empty
                : _cardEditorBaseCardId;
            _cardEditorPage.AlternateArtworkRoot =
                _database == null ? string.Empty : _database.AlternateArtworkRoot;

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
        /// The editor resolves 系列 / 卡包 from the 卡号 while the player types;
        /// the header above the page mirrors it, so both always name the pack
        /// 保存 writes into.
        /// </summary>
        private void CardEditorPage_TargetChanged(object sender, EventArgs e)
        {
            if (_cardEditorPage == null || _cardEditorPage.Draft == null)
                return;

            _contentSubtitle.Text = _cardEditorPage.Subtitle;
        }

        /// <summary>
        /// 取消: back to the card list the editor was opened from, or to the
        /// cover grid when there is no series to return to.
        /// </summary>
        private void CancelCardEditor()
        {
            bool hasSeries = _cardEditorReturnSeries != null && _activeSeries != null;
            string seriesName = hasSeries ? _cardEditorReturnSeries.Name : string.Empty;
            // 保存异画 only wrote a file: the card list has to be rebuilt from
            // the reloaded card table so the new variant shows up right away.
            bool refresh = _cardAltArtWritten;
            CloseCardEditor();

            if (!hasSeries)
            {
                ShowCardEditHome();
                return;
            }

            if (refresh)
            {
                SeriesDefinition reloaded = _database.GetSeries(seriesName);
                if (reloaded != null)
                {
                    // OpenSeries keeps the 写卡 page it was started from.
                    _viewMode = ViewMode.CardEditList;
                    OpenSeries(reloaded);
                    return;
                }
            }

            _viewMode = ViewMode.CardEditCards;
            _navColumnStyle.Width = NavCompactWidth;
            _filterColumnStyle.Width = 250F;
            _filterHost.Visible = true;
            SetNavExpanded(false);
            // The card list is the working page of 写卡, where its sub entries
            // fold away: the section label carries the highlight then.
            SetNavSubItemsVisible(NavSection.None);
            SetActiveNavItem(_navCardEditButton);
            _backButton.Text = "←  选择系列";
            _contentTitle.Text = "修改卡片";
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

            if (_cardAltArtWritten)
            {
                // The variant exists on disk only; dropping the cached series
                // makes the next card list read it back.
                _cardAltArtWritten = false;
                _database.Reload();
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

        /// <summary>
        /// 保存异画: writes every queued image into
        /// StreamingAssets\AlternateArtwork through <see cref="AltArtworkWriter"/>
        /// and keeps the editor open with the refused rows still in the queue,
        /// so one bad number does not throw away the rest of the batch. The
        /// card list is reloaded when the editor closes.
        /// </summary>
        private void CardEditorPage_AltArtSaveRequested(object sender, EventArgs e)
        {
            if (_cardEditorPage == null)
                return;

            IList<AltArtworkRequest> requests = _cardEditorPage.ReadAltArts();
            if (requests == null)
                return;

            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            AltArtBatchResult result;
            try
            {
                result = AltArtworkWriter.SaveAll(requests);
            }
            finally
            {
                Cursor = previousCursor;
            }

            if (result.SavedCount == 0)
            {
                _cardEditorPage.SetStatus(result.Message, true);
                _cardEditorPage.SetAltArtStatus(result.Message, true);
                return;
            }

            _cardAltArtWritten = true;
            _contentSubtitle.Text = _cardEditorPage.Subtitle;
            _cardEditorPage.NotifyAltArtsSaved(result);
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
