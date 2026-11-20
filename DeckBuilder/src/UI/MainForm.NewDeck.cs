using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 编辑卡组 workbench: the card grid feeds the new deck tray pinned under
    /// it. A left click adds the card, a long press opens the card detail and
    /// a right click takes one copy out again. The tray owns the deck name, the
    /// 导入 / 清空 / 整理 / 暂存·保存 band and the pulled up 卡组信息 pane; its
    /// own cards answer to the same long press and right click.
    /// </summary>
    public sealed partial class MainForm
    {
        // The tab keeps the name clear of the card grid above the tray.
        private const int NewDeckStripTopGap = 8;

        /// <summary>
        /// Cards kept by 暂存, one entry per series. Switching series always
        /// starts a new tray and only the series itself can read its entry back,
        /// so a deck never mixes cards of two series.
        /// </summary>
        private sealed class DeckDraftSnapshot
        {
            public string Name { get; set; }
            public List<string> Codes { get; set; }
        }

        private readonly Dictionary<string, string> _deckDraftImages =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DeckDraftSnapshot> _deckDraftCache =
            new Dictionary<string, DeckDraftSnapshot>(
                StringComparer.OrdinalIgnoreCase);

        private DeckDraft _deckDraft;
        private NewDeckStrip _newDeckStrip;
        private RowStyle _newDeckRowStyle;
        private string _deckDraftSeriesKey;
        private bool _deckDraftSaved;
        private Dictionary<string, WeissCard> _deckDraftCardIndex;

        /// <summary>Staged deck files of 暂存, created on first use.</summary>
        private DeckDraftStore _deckDraftStore;

        /// <summary>
        /// Slot the open tray was imported from, 0 when it is a fresh deck.
        /// 暂存 keeps writing back into that slot instead of using a new one.
        /// </summary>
        private int _deckDraftSlot;

        /// <summary>
        /// The deck assembled on this page. It belongs to the series that is
        /// open: opening another series starts with a new tray.
        /// </summary>
        private DeckDraft Draft
        {
            get
            {
                if (_deckDraft == null)
                    _deckDraft = new DeckDraft();
                return _deckDraft;
            }
        }

        /// <summary>True on the card level of 编辑卡组, where cards can be picked.</summary>
        private bool IsDeckDraftLevel
        {
            get { return _viewMode == ViewMode.DeckNew && _activeSeries != null; }
        }

        /// <summary>
        /// Files of the 暂存卡组 slots. They live next to Decks, so the game
        /// never sees a deck that was only staged.
        /// </summary>
        private DeckDraftStore DraftStore
        {
            get
            {
                if (_deckDraftStore == null)
                    _deckDraftStore = new DeckDraftStore(_database.DataRoot);
                return _deckDraftStore;
            }
        }

        /// <summary>Height the tray row currently asks for.</summary>
        private int NewDeckStripRowHeight
        {
            get
            {
                int height = _newDeckStrip != null
                    ? _newDeckStrip.DesiredHeight
                    : NewDeckStrip.TotalHeight;
                return height + NewDeckStripTopGap;
            }
        }

        /// <summary>
        /// The tray row of the browse page. It exists on every page that
        /// shares the browse layout and is folded to zero height outside the
        /// card level of 编辑卡组.
        /// </summary>
        private NewDeckStrip BuildNewDeckStrip()
        {
            _newDeckStrip = new NewDeckStrip();
            _newDeckStrip.Dock = DockStyle.Fill;
            _newDeckStrip.Margin = new Padding(0, NewDeckStripTopGap, 0, 0);
            _newDeckStrip.Thumbnails = _thumbnails;
            _newDeckStrip.Glossary = _triggerGlossary;
            _newDeckStrip.ImagePathProvider = delegate(string code)
            {
                string path;
                if (_deckDraftImages.TryGetValue(code, out path) &&
                    !string.IsNullOrEmpty(path))
                {
                    return path;
                }

                WeissCard card = ResolveDeckDraftCard(code);
                if (card == null)
                    return null;
                return string.IsNullOrEmpty(card.ImagePath)
                    ? card.AltImagePath
                    : card.ImagePath;
            };
            _newDeckStrip.CardRemoveRequested +=
                NewDeckStrip_CardRemoveRequested;
            _newDeckStrip.CardAddRequested +=
                NewDeckStrip_CardAddRequested;
            _newDeckStrip.CardDetailRequested +=
                NewDeckStrip_CardDetailRequested;
            _newDeckStrip.ActionRequested += NewDeckStrip_ActionRequested;
            _newDeckStrip.DeckNameChanged += NewDeckStrip_DeckNameChanged;
            _newDeckStrip.ExpandedChanged += NewDeckStrip_ExpandedChanged;
            _newDeckStrip.Visible = false;
            return _newDeckStrip;
        }

        /// <summary>
        /// Shows the tray only on the card level of 编辑卡组. Folding the row
        /// to zero instead of removing the control keeps the browse page
        /// layout intact for the plain series card list.
        /// </summary>
        private void UpdateNewDeckStripVisibility()
        {
            if (_newDeckStrip == null || _newDeckRowStyle == null)
                return;

            bool visible = IsDeckDraftLevel;
            if (!visible)
                LeaveDeckDraftSeries();

            if (_newDeckStrip.Visible != visible)
                _newDeckStrip.Visible = visible;

            ApplyNewDeckStripRowHeight();

            if (visible)
                RefreshNewDeckStrip();
        }

        /// <summary>
        /// Keeps the height of the tray row in step with the folded / pulled up
        /// state of the strip.
        /// </summary>
        private void ApplyNewDeckStripRowHeight()
        {
            if (_newDeckStrip == null || _newDeckRowStyle == null)
                return;

            float height = _newDeckStrip.Visible
                ? NewDeckStripRowHeight
                : 0F;
            if (_newDeckRowStyle.Height != height)
                _newDeckRowStyle.Height = height;

            Control owner = _newDeckStrip.Parent;
            if (owner != null)
                owner.PerformLayout();
        }

        private void RefreshNewDeckStrip()
        {
            if (_newDeckStrip == null)
                return;

            DeckDraft draft = Draft;
            _newDeckStrip.SetCards(draft.Name, draft.Cards,
                draft.CardCount, DeckDraft.MaxCards);
            _newDeckStrip.SetStatistics(BuildDeckDraftStatistics());
        }

        private void NewDeckStrip_ExpandedChanged(object sender, EventArgs e)
        {
            ApplyNewDeckStripRowHeight();
            SetDeckDraftStatus(_newDeckStrip.Expanded
                ? "新卡组栏已拉起 · 点击上方箭头收起"
                : "新卡组栏已收起");
        }


        /// <summary>
        /// Starts the tray of one series. A series that was stashed before
        /// restores its own cards; every other series starts with a fresh
        /// 新建卡组 tab, so cards never leak between series.
        /// </summary>
        private void BeginDeckDraftForSeries(SeriesDefinition series)
        {
            if (series == null)
            {
                LeaveDeckDraftSeries();
                return;
            }

            string key = DeckDraftSeriesKey(series);
            if (!string.Equals(key, _deckDraftSeriesKey,
                StringComparison.OrdinalIgnoreCase))
            {
                LeaveDeckDraftSeries();
            }

            _deckDraftSeriesKey = key;
            _deckDraft = new DeckDraft();
            _deckDraftSaved = false;
            _deckDraftSlot = 0;
            _deckDraftImages.Clear();
            BuildDeckDraftCardIndex(series);

            DeckDraftSnapshot snapshot;
            if (_deckDraftCache.TryGetValue(key, out snapshot))
            {
                if (!string.IsNullOrEmpty(snapshot.Name))
                    _deckDraft.Name = snapshot.Name;
                for (int i = 0; i < snapshot.Codes.Count; i++)
                    _deckDraft.Add(snapshot.Codes[i]);
            }

            if (_newDeckStrip != null)
                _newDeckStrip.Expanded = false;
        }

        /// <summary>Stable cache key of a series inside the deck editor.</summary>
        private static string DeckDraftSeriesKey(SeriesDefinition series)
        {
            if (series == null)
                return string.Empty;
            return string.IsNullOrEmpty(series.FolderName)
                ? series.Name
                : series.FolderName;
        }

        /// <summary>
        /// Caches the tray of the open series. Called by 暂存 and whenever the
        /// page leaves the card level, so switching series never loses work and
        /// never carries cards into another series.
        /// </summary>
        private void StashDeckDraft()
        {
            if (_deckDraftSeriesKey == null || _deckDraft == null)
                return;

            string key = _deckDraftSeriesKey;
            if (_deckDraft.IsEmpty || _deckDraftSaved)
            {
                _deckDraftCache.Remove(key);
                return;
            }

            var snapshot = new DeckDraftSnapshot();
            snapshot.Name = _deckDraft.Name;
            snapshot.Codes = new List<string>(_deckDraft.ToCardCodes());
            _deckDraftCache[key] = snapshot;
        }

        private void LeaveDeckDraftSeries()
        {
            if (_deckDraftSeriesKey == null)
                return;

            StashDeckDraft();
            _deckDraftSeriesKey = null;
        }

        /// <summary>
        /// Card lookup of the open series, used by the image provider, the card
        /// detail and the 卡组信息 pane. The codes of a stashed draft are
        /// resolved back through this index.
        /// </summary>
        private void BuildDeckDraftCardIndex(SeriesDefinition series)
        {
            var index = new Dictionary<string, WeissCard>(
                StringComparer.OrdinalIgnoreCase);
            if (series != null)
            {
                foreach (WeissCard card in series.EnumerateCards())
                {
                    string canonical = CardCodeMatcher.Canonical(card.Id);
                    if (canonical.Length == 0 || index.ContainsKey(canonical))
                        continue;
                    index[canonical] = card;
                }
            }

            _deckDraftCardIndex = index;
        }

        private WeissCard ResolveDeckDraftCard(string code)
        {
            if (_deckDraftCardIndex == null || string.IsNullOrEmpty(code))
                return null;

            WeissCard card;
            return _deckDraftCardIndex.TryGetValue(
                CardCodeMatcher.Canonical(code), out card)
                ? card
                : null;
        }

        /// <summary>
        /// Numbers of the divided pane: the same tables 查看卡组 prints, built
        /// from the codes of the tray.
        /// </summary>
        private DeckStatistics BuildDeckDraftStatistics()
        {
            return DeckStatistics.Compute(Draft.ToCardCodes(),
                ResolveDeckDraftCard);
        }


        /// <summary>
        /// Left click on a card of the 编辑卡组 grid drops one copy into the
        /// new deck. The copy count of a code collapses into one tray image;
        /// 50 张 is only a target, so adding past it is allowed and reported.
        /// </summary>
        private void AddCardToDeckDraft(WeissCard card)
        {
            if (card == null)
                return;

            AddCardCodeToDeckDraft(card.Id, card.ImagePath, card);
        }

        /// <summary>
        /// Adds one copy of a code. The tray only knows codes, so a card that
        /// was imported from another series (or whose series entry is gone)
        /// still gains copies; the image is reused when the page knows one.
        /// </summary>
        private void AddCardCodeToDeckDraft(string code, string imagePath,
            WeissCard card)
        {
            if (string.IsNullOrEmpty(code) || !IsDeckDraftLevel)
                return;

            DeckDraft draft = Draft;
            if (!draft.Add(code))
            {
                SetDeckDraftStatus("卡片编号为空，无法加入新卡组");
                return;
            }

            _deckDraftSaved = false;
            if (!string.IsNullOrEmpty(imagePath))
                _deckDraftImages[code] = imagePath;
            if (card != null && _deckDraftCardIndex != null)
            {
                _deckDraftCardIndex[CardCodeMatcher.Canonical(card.Id)] = card;
            }

            RefreshNewDeckStrip();
            SetDeckDraftStatus(string.Format("已加入 {0} · {1} {2}/{3} 张{4}",
                code, draft.Name, draft.CardCount, DeckDraft.MaxCards,
                draft.CardCount > DeckDraft.MaxCards ? "（已超出标准张数）" : ""));
        }

        /// <summary>
        /// Left click on a card of the tray: one more copy of that card joins
        /// the deck. The code is resolved through the series index only to
        /// pick its image; a placeholder gains copies all the same.
        /// </summary>
        private void NewDeckStrip_CardAddRequested(object sender,
            NewDeckCardEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.Code))
                return;

            WeissCard card = ResolveDeckDraftCard(e.Code);
            string imagePath = null;
            if (card != null)
            {
                imagePath = string.IsNullOrEmpty(card.ImagePath)
                    ? card.AltImagePath
                    : card.ImagePath;
            }

            AddCardCodeToDeckDraft(e.Code, imagePath, card);
        }

        /// <summary>
        /// Right click on a card of the grid, or on its image in the tray,
        /// takes one copy out of the new deck.
        /// </summary>
        private void RemoveCardFromDeckDraft(string code)
        {
            if (string.IsNullOrEmpty(code) || !IsDeckDraftLevel)
                return;

            DeckDraft draft = Draft;
            if (!draft.Remove(code))
            {
                SetDeckDraftStatus("新卡组里没有 " + code);
                return;
            }

            _deckDraftSaved = false;
            RefreshNewDeckStrip();
            SetDeckDraftStatus(string.Format("已移除 {0} · {1} {2}/{3} 张",
                code, draft.Name, draft.CardCount, DeckDraft.MaxCards));
        }

        private void NewDeckStrip_ActionRequested(object sender,
            NewDeckActionEventArgs e)
        {
            if (e == null)
                return;

            switch (e.Action)
            {
                case NewDeckAction.Import:
                    ImportDeckDraft();
                    break;
                case NewDeckAction.Clear:
                    ClearDeckDraft();
                    break;
                case NewDeckAction.Tidy:
                    TidyDeckDraft();
                    break;
                case NewDeckAction.Stash:
                    StashDeckDraftFromButton();
                    break;
                case NewDeckAction.Save:
                    SaveDeckDraft();
                    break;
            }
        }

        private void NewDeckStrip_DeckNameChanged(object sender,
            NewDeckNameEventArgs e)
        {
            if (e == null || e.Name.Length == 0)
                return;

            Draft.Name = e.Name;
            _deckDraftSaved = false;
            RefreshNewDeckStrip();
            SetDeckDraftStatus("卡组名称已改为 " + e.Name);
        }

        /// <summary>清空: empties the tray but keeps the name.</summary>
        private void ClearDeckDraft()
        {
            DeckDraft draft = Draft;
            draft.Clear();
            _deckDraftSaved = false;
            _deckDraftSlot = 0;
            RefreshNewDeckStrip();
            SetDeckDraftStatus("已清空新卡组 · " + draft.Name);
        }

        /// <summary>整理: sorts the tray and the deck file by card code.</summary>
        private void TidyDeckDraft()
        {
            DeckDraft draft = Draft;
            draft.Sort();
            _deckDraftSaved = false;
            RefreshNewDeckStrip();
            SetDeckDraftStatus(string.Format("已整理卡组 · {0} {1} 张",
                draft.Name, draft.CardCount));
        }

        /// <summary>
        /// 暂存: writes the tray into a slot of StreamingAssets\DeckDrafts, the
        /// folder next to Decks. A tray that was imported from a slot writes
        /// back into that slot; every other tray takes the first free one, so a
        /// stage never overwrites another staged deck.
        /// </summary>
        private void StashDeckDraftFromButton()
        {
            DeckDraft draft = Draft;
            if (draft.IsEmpty)
            {
                SetDeckDraftStatus("新卡组里还没有卡片，无法暂存");
                return;
            }

            DeckDraftStore store = DraftStore;
            int slot = _deckDraftSlot;
            if (slot <= 0)
            {
                slot = DeckDraftStore.FirstFreeSlot(store.Load());
                if (slot <= 0)
                {
                    SetDeckDraftStatus(string.Format(
                        "暂存卡组已满（{0} 个）· 在导入窗口右键两次删除一个再试",
                        DeckDraftStore.SlotCount));
                    return;
                }
            }

            string path;
            string error;
            if (!store.Save(slot, draft.Name, draft.ToCardCodes(), out path,
                out error))
            {
                SetDeckDraftStatus("暂存失败：" + (error ?? string.Empty));
                return;
            }

            _deckDraftSlot = slot;
            StashDeckDraft();
            SetDeckDraftStatus(string.Format(
                "已暂存 {0} · 暂存卡组 {1} · {2} 张 · 切回该系列可继续编辑",
                draft.Name, slot, draft.CardCount));
        }

            /// <summary>
            /// 导入: opens the 导入卡组 layer over the page. A pasted 卡组代码
            /// block and a staged deck both are whole decks, so both replace
            /// the tray outright: the layer only warns in its footer line, it
            /// never asks a second time.
            /// </summary>
            private void ImportDeckDraft()
            {
                if (!IsDeckDraftLevel)
                    return;

                if (_deckCoverLocator == null)
                    _deckCoverLocator = new DeckCoverLocator(_database);

                EnsureDeckImportOverlay();
                _deckImportOverlay.TrayCardCount = Draft.CardCount;
                _deckImportOverlay.Bounds = ClientRectangle;
                _deckImportOverlay.ShowImport(CaptureDetailBackdrop());
                _deckImportOverlay.Visible = true;
                _deckImportOverlay.BringToFront();
                _deckImportOverlay.Focus();
            }

            /// <summary>
            /// Creates the 导入卡组 layer once and keeps it docked over the whole
            /// page, the same way the card detail layer lives.
            /// </summary>
            private void EnsureDeckImportOverlay()
            {
                if (_deckImportOverlay != null)
                    return;

                _deckImportOverlay = new DeckImportOverlay(DraftStore,
                    _deckCoverLocator.GetCoverPath, _thumbnails);
                _deckImportOverlay.Dock = DockStyle.Fill;
                _deckImportOverlay.ImportRequested +=
                    DeckImportOverlay_ImportRequested;
                _deckImportOverlay.CloseRequested +=
                    DeckImportOverlay_CloseRequested;
                Controls.Add(_deckImportOverlay);
                _deckImportOverlay.Visible = false;
            }

            private void DeckImportOverlay_CloseRequested(object sender,
                EventArgs e)
            {
                HideDeckImportOverlay();
            }

            private void HideDeckImportOverlay()
            {
                if (_deckImportOverlay == null)
                    return;

                _deckImportOverlay.Visible = false;
                _deckImportOverlay.Clear();
                if (_tileGrid != null && _tileGrid.CanFocus)
                    _tileGrid.Focus();
            }

            private void DeckImportOverlay_ImportRequested(object sender,
                DeckImportRequestEventArgs e)
            {
                if (e == null || e.CardCodes == null || e.CardCodes.Count == 0)
                {
                    HideDeckImportOverlay();
                    return;
                }

                HideDeckImportOverlay();
                ApplyImportedDeck(e.CardCodes, e.DeckName, e.SlotIndex);
            }

            /// <summary>
            /// Replaces the tray with the imported cards. The 导入卡组 layer
            /// hands them over on the click itself, so the tray is rebuilt here
            /// without asking again.
            /// </summary>
            private void ApplyImportedDeck(IList<string> codes, string deckName,
                int slot)
            {
                DeckDraft draft = Draft;
                draft.Clear();
                int added = 0;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (draft.Add(codes[i]))
                        added++;
                }

                if (!string.IsNullOrEmpty(deckName))
                    draft.Name = deckName;

                _deckDraftSlot = slot;
                _deckDraftSaved = false;

                DeckStatistics statistics = BuildDeckDraftStatistics();
                RefreshNewDeckStrip();
                SetDeckDraftStatus(string.Format("已导入 {0} 张 · {1} {2}/{3} 张{4}",
                    added, draft.Name, draft.CardCount, DeckDraft.MaxCards,
                    statistics.Unresolved > 0
                        ? string.Format("（{0} 张不在当前系列，显示为占位）",
                            statistics.Unresolved)
                        : string.Empty));
            }


        /// <summary>
        /// 保存: tidies the deck once, writes it into StreamingAssets\Decks as a
        /// plain code list and forgets the stashed copy of this series.
        /// </summary>
        private void SaveDeckDraft()
        {
            DeckDraft draft = Draft;
            if (draft.IsEmpty)
            {
                SetDeckDraftStatus("新卡组里还没有卡片，无法保存");
                return;
            }

            string name = draft.Name;

            if (_deckLibrary == null)
                _deckLibrary = DeckLibrary.Load(_database.DataRoot);

            // 保存前整理一次：文件里的卡片和托盘都按编号排序。
            draft.Sort();
            draft.Name = name;

            string path = _deckLibrary.GetDeckPath(name);
            if (path == null)
            {
                SetDeckDraftStatus("卡组名称不能为空");
                return;
            }

            if (File.Exists(path))
            {
                DialogResult answer = MessageBox.Show(this,
                    "已存在同名卡组“" + name + "”，是否覆盖？", "保存卡组",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    SetDeckDraftStatus("已取消保存");
                    RefreshNewDeckStrip();
                    return;
                }
            }

            string savedPath;
            string error;
            bool saved = _deckLibrary.SaveDeck(name, draft.ToCardCodes(), true,
                out savedPath, out error);
            RefreshNewDeckStrip();

            if (!saved)
            {
                SetDeckDraftStatus("保存失败：" + (error ?? string.Empty));
                return;
            }

            _deckDraftSaved = true;
            _deckDraftCache.Remove(_deckDraftSeriesKey ?? string.Empty);

            // 保存 promotes the tray into Decks, so the staged copy of this
            // deck is no longer needed.
            if (_deckDraftSlot > 0)
            {
                DraftStore.Delete(_deckDraftSlot);
                _deckDraftSlot = 0;
            }

            SetDeckDraftStatus(string.Format("已保存 {0} · {1} 张 · {2}",
                name, draft.CardCount, savedPath));
        }

        private void SetDeckDraftStatus(string text)
        {
            if (_statusLabel != null)
                _statusLabel.Text = text;
        }

        private void NewDeckStrip_CardRemoveRequested(object sender,
            NewDeckCardEventArgs e)
        {
            if (e != null)
                RemoveCardFromDeckDraft(e.Code);
        }

        /// <summary>
        /// Long press on a card of the tray opens the same detail layer the
        /// grid uses; the code is resolved back through the series index.
        /// </summary>
        private void NewDeckStrip_CardDetailRequested(object sender,
            NewDeckCardEventArgs e)
        {
            if (e == null)
                return;

            ShowCardDetail(ResolveDeckDraftCard(e.Code));
        }

        /// <summary>
        /// Long press stands in for the card detail on the 编辑卡组 grid,
        /// where the plain click already means "add to the new deck".
        /// </summary>
        private void TileGrid_ItemLongPress(object sender, TileGridItemEventArgs e)
        {
            if (!IsDeckDraftLevel || e == null || e.Item == null)
                return;

            ShowCardDetail(e.Item.Tag as WeissCard);
        }

        private void TileGrid_ItemRightClick(object sender, TileGridItemEventArgs e)
        {
            if (!IsDeckDraftLevel || e == null || e.Item == null)
                return;

            WeissCard card = e.Item.Tag as WeissCard;
            if (card != null)
                RemoveCardFromDeckDraft(card.Id);
        }
    }
}
