using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// Deck tools in the sidebar: a placeholder page for 新建卡组 and a grid
    /// overview of every deck file found in Decks and DefaultDecks.
    /// </summary>
    public sealed partial class MainForm
    {
        // 展开 packs every copy into a tight 10 x 5 block, 缩略 spreads the
        // distinct cards over a 7 x 4 block with a roomier gap.
        private const int DeckExpandedColumns = 10;
        private const int DeckCompactColumns = 7;
        private const int DeckCompactRows = 4;
        private const int DeckExpandedGap = 2;
        private const int DeckCompactGap = 15;
        private const int DeckSidebarViewRowHeight = 70;
        private const int DeckSidebarGroupCaptionHeight = 26;
        private const float DeckDetailSidebarWidth = 250F;

        // Caption line above each block of the 查看卡组 page.
        private const int DeckListCaptionHeight = 30;

        // The two blocks share one scrolling page, so they are separated by a
        // gap and kept clear of the slim scrollbar the host paints on the right.
        private const int DeckListBlockGap = 18;
        private const int DeckListScrollbarGutter = 14;

        // 删除 rests as red text on the sidebar surface and only fills with
        // red once it is armed, so the second click can never be a reflex.
        private static readonly Color DeckDeleteRestFill = AppTheme.TileBackground;
        private static readonly Color DeckDeleteRestHoverFill =
            Color.FromArgb(127, 29, 29);
        private static readonly Color DeckDeleteArmedFill =
            Color.FromArgb(220, 38, 38);
        private static readonly Color DeckDeleteArmedHoverFill =
            Color.FromArgb(239, 68, 68);
        private static readonly Color DeckDeleteArmedPressedFill =
            Color.FromArgb(185, 28, 28);

        private RoundedButton _navDeckButton;
        private RoundedButton _navDeckNewButton;
        private RoundedButton _navDeckEditButton;

        private Panel _deckEditPage;
        private Label _deckEditTitle;
        private Label _deckEditSubtitle;
        private Label _deckSelectionLabel;
        private RoundedButton _deckRefreshButton;

        // 查看卡组 splits its page in two blocks: 我的卡组 (StreamingAssets\
        // Decks) and 预设卡组 (DefaultDecks). Both blocks sit in one scrolling
        // page with a single scrollbar, and each caption pins to the top edge
        // while its block is on screen.
        private DarkScrollPanel _deckListScroll;
        private Panel _deckListContent;
        private Label _deckLocalCaption;
        private Label _deckDefaultCaption;
        private TileGridControl _deckLocalGrid;
        private TileGridControl _deckDefaultGrid;
        private int _deckLocalCaptionTop;
        private int _deckDefaultCaptionTop;

        private Panel _deckOverviewPanel;
        private Panel _deckDetailPanel;
        private Label _deckDetailName;
        private Label _deckDetailSeries;
        private DeckStatsPanel _deckStats;
        private TableLayoutPanel _deckStatsGroup;
        private LinkLabel _deckDetailBackLink;
        private Label _deckDetailHint;
        private Label _deckExportStatus;
        private RoundedButton _deckDetailEditButton;
        private RoundedButton _deckDetailDeleteButton;

        /// <summary>
        /// 删除 was clicked once and waits for the second click. Any other
        /// gesture on the page (opening another deck, 编辑, Esc, 返回) takes the
        /// red state back, so a delete always needs two clicks in a row.
        /// </summary>
        private bool _deckDeleteArmed;
        private FlowLayoutPanel _deckModeOptions;
        private FilterRadioButton _deckExpandOption;
        private FilterRadioButton _deckCompactOption;
        private FilterCheckBox _deckAltArtOption;
        private DeckCardGrid _deckCards;
        private DeckDefinition _activeDeck;
        private bool _deckCompactView;
        private bool _updatingDeckOptions;

        private DeckLibrary _deckLibrary;
        private DeckCoverLocator _deckCoverLocator;
        private DeckCardResolver _deckCardResolver;
        private TableLayoutPanel _deckEditLayout;
        private Control _deckEditHeader;

        /// <summary>
        /// Emphasises the 卡组编辑 section label while one of its sub entries is
        /// active. The label itself never draws the selected box so only one
        /// page at a time looks active.
        /// </summary>
        private void UpdateNavGroupStates(RoundedButton selected)
        {
            if (_navDeckButton == null)
                return;

            bool sectionActive = ReferenceEquals(selected, _navDeckNewButton) ||
                ReferenceEquals(selected, _navDeckEditButton) ||
                ReferenceEquals(selected, _navDeckButton);
            Color color = sectionActive ? Color.White : AppTheme.TextSoft;
            if (!_navDeckButton.Selected && _navDeckButton.ForeColor == color)
                return;

            _navDeckButton.Selected = false;
            _navDeckButton.ForeColor = color;
            _navDeckButton.Invalidate();
        }

        private void NavDeckButton_Click(object sender, EventArgs e)
        {
            // The parent entry opens the working page of this section, which is
            // the deck editor; 查看卡组 stays reachable as a sub entry.
            if (_viewMode != ViewMode.DeckEdit && _viewMode != ViewMode.DeckNew)
            {
                ShowDeckNew();
                return;
            }

            // 编辑卡组 keeps its own series picker, so on the card list of an
            // opened series this entry steps back to that picker first.
            if (_viewMode == ViewMode.DeckNew)
            {
                if (_activeSeries != null)
                    ShowDeckNew();
                return;
            }

            // While a deck is open this entry acts like the 系列 entry on the
            // card list page: it returns to the list of decks.
            if (_viewMode == ViewMode.DeckEdit &&
                _deckDetailPanel != null && _deckDetailPanel.Visible)
            {
                CloseDeckDetail();
                SetActiveNavItem(_navDeckEditButton);
            }
        }

        private void NavDeckNewButton_Click(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.DeckNew)
                ShowDeckNew();
        }

        private void NavDeckEditButton_Click(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.DeckEdit)
                ShowDeckEdit();
        }

        /// <summary>
        /// 编辑卡组 page. Its body is the series card list: the cover grid acts
        /// as the series picker and opening a cover shows that series in the
        /// filter column plus the card grid.
        /// </summary>
        private void ShowDeckNew()
        {
            _viewMode = ViewMode.DeckNew;
            _activeSeries = null;

            CloseDeckDetail();
            _navColumnStyle.Width = NavExpandedWidth;
            _filterColumnStyle.Width = 0F;
            _filterHost.Visible = false;
            SetNavExpanded(true);
            SetDeckSubItemsVisible(true);
            ShowContentPage(_browsePage);
            SetActiveNavItem(_navDeckNewButton);
            UpdateNewDeckStripVisibility();

            _backButton.Text = "←  系列列表";
            _contentTitle.Text = "编辑卡组";
            _contentSubtitle.Text = string.Format(
                "共 {0} 个系列 · 选择系列后浏览卡片",
                _database.SeriesNames.Count);

            _tileGrid.EmptyText = "没有找到系列";
            _tileGrid.ConfigureSeries();
            RefreshSeriesItems();
            _tileGrid.ScrollToTop();
            ExitCollectionMarkMode();
            UpdateCollectionChrome();
            ScheduleDarkChrome();
        }

        /// <summary>
        /// Shows every deck file found in Decks and DefaultDecks as a tile
        /// grid. The list is re-read each time the page is opened so a deck
        /// saved by the game shows up without restarting.
        /// </summary>
        private void ShowDeckEdit()
        {
            _viewMode = ViewMode.DeckEdit;

            _navColumnStyle.Width = NavExpandedWidth;
            _filterColumnStyle.Width = 0F;
            _filterHost.Visible = false;
            SetNavExpanded(true);
            SetDeckSubItemsVisible(true);
            ShowContentPage(_deckEditPage);
            SetActiveNavItem(_navDeckEditButton);
            CloseDeckDetail();
            UpdateNewDeckStripVisibility();
            RefreshDeckItems(true);
            ExitCollectionMarkMode();
            UpdateCollectionChrome();
            ScheduleDarkChrome();
        }

        private Panel BuildDeckEditPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.MainBackground;
            page.Visible = false;

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.BackColor = AppTheme.MainBackground;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            layout.Controls.Add(CreatePageHeader("查看卡组",
                "读取 StreamingAssets\\Decks 与 DefaultDecks 中的卡组文件",
                out _deckEditTitle, out _deckEditSubtitle), 0, 0);

            _deckEditLayout = layout;
            _deckEditHeader = layout.GetControlFromPosition(0, 0);

            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.BackColor = AppTheme.MainBackground;
            host.Margin = new Padding(0);

            _deckOverviewPanel = BuildDeckOverview();
            host.Controls.Add(_deckOverviewPanel);

            _deckDetailPanel = BuildDeckDetail();
            host.Controls.Add(_deckDetailPanel);

            layout.Controls.Add(host, 0, 1);

            page.Controls.Add(layout);
            return page;
        }

        /// <summary>
        /// Deck list: the toolbar plus two titled tile grids — 我的卡组 from
        /// StreamingAssets\Decks on top, the bundled 预设卡组 from DefaultDecks
        /// below. Both blocks live in one scrolling page with a single
        /// scrollbar on the right; a caption scrolls with its block and pins to
        /// the top edge until the next caption pushes it away.
        /// </summary>
        private Panel BuildDeckOverview()
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = AppTheme.MainBackground;
            panel.Margin = new Padding(0);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.BackColor = AppTheme.MainBackground;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            layout.Controls.Add(BuildDeckToolbar(), 0, 0);

            // One page, one scrollbar: the content panel stacks both blocks
            // and the scroll host moves it as a whole.
            _deckListScroll = new DarkScrollPanel();
            _deckListScroll.Dock = DockStyle.Fill;
            _deckListScroll.BackColor = AppTheme.MainBackground;
            _deckListScroll.Margin = new Padding(0);
            // The bar is painted by the host below its children, so the page
            // keeps the same gutter clear: otherwise the tiles scroll over the
            // bar and the list looks like it has no scrollbar at all.
            _deckListScroll.ScrollbarReserve = DeckListScrollbarGutter;

            _deckListContent = new Panel();
            _deckListContent.BackColor = AppTheme.MainBackground;
            _deckListContent.Margin = new Padding(0);

            _deckLocalGrid = CreateDeckGrid(
                "还没有我的卡组 · 在 编辑卡组 里保存后出现在这里");
            _deckDefaultGrid = CreateDeckGrid(
                "DefaultDecks 目录中没有预设卡组");
            _deckListContent.Controls.Add(_deckLocalGrid);
            _deckListContent.Controls.Add(_deckDefaultGrid);
            _deckListScroll.Content = _deckListContent;

            // The captions are siblings of the content, not part of it, so they
            // can stay on the top edge while the tiles scroll underneath.
            _deckLocalCaption = CreateDeckListCaption("我的卡组");
            _deckDefaultCaption = CreateDeckListCaption("预设卡组");
            _deckListScroll.Controls.Add(_deckLocalCaption);
            _deckListScroll.Controls.Add(_deckDefaultCaption);
            _deckLocalCaption.BringToFront();
            _deckDefaultCaption.BringToFront();

            _deckListScroll.ScrollOffsetChanged += DeckListScroll_ScrollOffsetChanged;
            _deckListScroll.Resize += DeckListScroll_Resize;

            layout.Controls.Add(_deckListScroll, 0, 1);

            panel.Controls.Add(layout);
            return panel;
        }

        private TileGridControl CreateDeckGrid(string emptyText)
        {
            var grid = new TileGridControl();
            // Both blocks are stacked in the page scroll host, so a block never
            // scrolls on its own: it reports the height its tiles need and the
            // page scrolls instead.
            grid.ExpandToContent = true;
            grid.Dock = DockStyle.None;
            grid.Thumbnails = _thumbnails;
            grid.BackColor = AppTheme.MainBackground;
            grid.Margin = new Padding(0);
            grid.ConfigureSeries();
            grid.EmptyText = emptyText;
            grid.ItemClick += DeckGrid_ItemClick;
            return grid;
        }

        private static Label CreateDeckListCaption(string text)
        {
            var label = new Label();
            label.Text = text;
            label.AutoEllipsis = true;
            label.ForeColor = AppTheme.Text;
            // Opaque, because the pinned caption has to hide the tiles that
            // scroll behind it.
            label.BackColor = AppTheme.MainBackground;
            label.Font = new Font("Microsoft YaHei UI", 10.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Padding = new Padding(6, 0, 0, 0);
            label.Margin = new Padding(0);
            return label;
        }

        /// <summary>
        /// Deck detail: the card image grid alone. The deck name, series and
        /// view mode live in the second level sidebar that shares the layout of
        /// the series filter column (see <see cref="BuildDeckSidebarHost"/>).
        /// </summary>
        private Panel BuildDeckDetail()
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = AppTheme.MainBackground;
            panel.Margin = new Padding(0);
            panel.Visible = false;

            _deckCards = new DeckCardGrid();
            _deckCards.Dock = DockStyle.Fill;
            _deckCards.Thumbnails = _thumbnails;
            _deckCards.BackColor = AppTheme.MainBackground;
            _deckCards.Margin = new Padding(0);
            _deckCards.Columns = DeckExpandedColumns;
            _deckCards.Gap = DeckExpandedGap;
            _deckCards.EmptyText = "卡组文件中没有卡片";
            _deckCards.ItemClick += DeckCards_ItemClick;

            panel.Controls.Add(_deckCards);
            return panel;
        }

        /// <summary>
        /// Second level sidebar of the deck detail page. It occupies the same
        /// root column as the series filter and repeats its structure — a
        /// header with back link, title and subtitle, a divider, a scrollable
        /// stack of option groups and a pinned footer — so the series card list
        /// and the deck detail page share one look.
        /// </summary>
        private Panel BuildDeckSidebarHost()
        {
            var host = new GradientPanel();
            host.Dock = DockStyle.Fill;
            host.StartColor = AppTheme.Navigation;
            host.EndColor = AppTheme.MainBackground;
            host.Visible = false;
            host.Margin = new Padding(0);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.BackColor = Color.Transparent;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // Header, divider, pinned 视图 block, divider, scrolling 卡组信息
            // block and the footer with the export actions and deck buttons.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,
                DeckSidebarViewRowHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 142F));
            host.Controls.Add(layout);

            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 1;
            header.RowCount = 3;
            header.BackColor = Color.Transparent;
            header.Padding = new Padding(14, 9, 14, 6);
            header.Margin = new Padding(0);
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _deckDetailBackLink = new LinkLabel();
            _deckDetailBackLink.Text = "←  卡组列表";
            _deckDetailBackLink.Dock = DockStyle.Fill;
            _deckDetailBackLink.BackColor = Color.Transparent;
            _deckDetailBackLink.LinkColor = AppTheme.TextSoft;
            _deckDetailBackLink.ActiveLinkColor = Color.White;
            _deckDetailBackLink.VisitedLinkColor = AppTheme.TextSoft;
            _deckDetailBackLink.DisabledLinkColor = AppTheme.TextMuted;
            _deckDetailBackLink.LinkBehavior = LinkBehavior.HoverUnderline;
            _deckDetailBackLink.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _deckDetailBackLink.TextAlign = ContentAlignment.MiddleLeft;
            _deckDetailBackLink.Margin = new Padding(0);
            _deckDetailBackLink.Cursor = Cursors.Hand;
            _deckDetailBackLink.Click += DeckDetailBack_Click;
            header.Controls.Add(_deckDetailBackLink, 0, 0);

            _deckDetailName = CreateDeckSidebarLabel(13F, FontStyle.Bold,
                AppTheme.Text, ContentAlignment.MiddleLeft);
            _deckDetailName.AutoEllipsis = true;
            header.Controls.Add(_deckDetailName, 0, 1);

            _deckDetailSeries = CreateDeckSidebarLabel(8.5F, FontStyle.Regular,
                AppTheme.TextMuted, ContentAlignment.MiddleLeft);
            _deckDetailSeries.AutoEllipsis = true;
            header.Controls.Add(_deckDetailSeries, 0, 2);

            layout.Controls.Add(header, 0, 0);

            var separator = new Panel();
            separator.Dock = DockStyle.Fill;
            separator.BackColor = AppTheme.Divider;
            separator.Margin = new Padding(0);
            layout.Controls.Add(separator, 0, 1);

            _deckModeOptions = CreateFilterChecks(false);

            _deckExpandOption = CreateSortOption("展开", 0);
            _deckExpandOption.Margin = new Padding(0, 0, 10, 2);
            _deckExpandOption.Width = _deckExpandOption.MeasureContentWidth();
            _deckExpandOption.CheckedChanged += DeckViewMode_CheckedChanged;
            _deckModeOptions.Controls.Add(_deckExpandOption);

            _deckCompactOption = CreateSortOption("缩略", 1);
            _deckCompactOption.Width = _deckCompactOption.MeasureContentWidth();
            _deckCompactOption.CheckedChanged += DeckViewMode_CheckedChanged;
            _deckModeOptions.Controls.Add(_deckCompactOption);

            // 异画 sits right behind the two view radios and only shows up in
            // the 缩略 view, where keeping the art variants apart is meaningful.
            _deckAltArtOption = CreateFilterCheck("异画", "art", false);
            _deckAltArtOption.Margin = new Padding(4, 0, 0, 2);
            _deckAltArtOption.Width = _deckAltArtOption.MeasureContentWidth();
            _deckAltArtOption.Visible = false;
            _deckAltArtOption.CheckedChanged += DeckAltArt_CheckedChanged;
            _deckModeOptions.Controls.Add(_deckAltArtOption);

            // 视图 and its options keep their own row above a divider, so
            // switching the view never forces a scroll through the numbers
            // of the 卡组信息 block below.
            var viewHost = new Panel();
            viewHost.Dock = DockStyle.Fill;
            viewHost.BackColor = Color.Transparent;
            viewHost.Margin = new Padding(0);
            viewHost.Padding = new Padding(14, 6, 14, 6);

            TableLayoutPanel viewGroup =
                CreateDeckSidebarGroup("视图", _deckModeOptions, 58);
            viewGroup.Dock = DockStyle.Fill;
            viewGroup.Margin = new Padding(0);
            viewHost.Controls.Add(viewGroup);
            layout.Controls.Add(viewHost, 0, 2);

            var viewSeparator = new Panel();
            viewSeparator.Dock = DockStyle.Fill;
            viewSeparator.BackColor = AppTheme.Divider;
            viewSeparator.Margin = new Padding(0);
            layout.Controls.Add(viewSeparator, 0, 3);

            var body = new DarkScrollPanel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Color.Transparent;
            body.Margin = new Padding(0);

            var stack = new FlowLayoutPanel();
            stack.Dock = DockStyle.None;
            stack.FlowDirection = FlowDirection.TopDown;
            stack.WrapContents = false;
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.BackColor = Color.Transparent;
            stack.Padding = new Padding(14, 12, 14, 16);
            stack.Margin = new Padding(0);
            body.Content = stack;
            layout.Controls.Add(body, 0, 4);

            // 卡组信息: the type / colour matrix, the level histogram and the
            // soul icons. The panel measures itself, so the group follows.
            _deckStats = new DeckStatsPanel();
            _deckStatsGroup = CreateDeckSidebarGroup("卡组信息", _deckStats,
                DeckSidebarGroupCaptionHeight +
                DeckStatsPanel.MeasureHeight(null));
            stack.Controls.Add(_deckStatsGroup);

            // 导出: four actions of the open deck, pinned directly above the
            // 编辑 / 删除 pair in the footer. The files always land in the
            // Output folder next to DeckBuilder; the status line below 编辑
            // names the result instead of opening Explorer over the game.
            var exportGrid = new TableLayoutPanel();
            exportGrid.Dock = DockStyle.Fill;
            exportGrid.ColumnCount = 2;
            exportGrid.RowCount = 2;
            exportGrid.BackColor = Color.Transparent;
            exportGrid.Margin = new Padding(0);
            exportGrid.Padding = new Padding(0);
            exportGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            exportGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            exportGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            exportGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

            string[] exportTexts = { "复制代码", "导出展开图",
                "导出缩略图", "导出PDF" };
            string[] exportKinds = { "codes", "expanded", "compact", "pdf" };
            for (int i = 0; i < exportTexts.Length; i++)
            {
                RoundedButton button = CreateDeckFooterButton(exportTexts[i],
                    AppTheme.TileBackground, AppTheme.Border, AppTheme.Divider,
                    AppTheme.TextSoft);
                button.Tag = exportKinds[i];
                button.Font = new Font("Microsoft YaHei UI", 8.5F,
                    FontStyle.Bold, GraphicsUnit.Point, 134);
                button.Margin = new Padding(i % 2 == 0 ? 0 : 4,
                    i < 2 ? 0 : 4, i % 2 == 0 ? 4 : 0, 0);
                button.Click += DeckDetailExport_Click;
                exportGrid.Controls.Add(button, i % 2, i / 2);
            }

            // Footer: the four export actions sit right above the two deck
            // actions, and the export status follows below 编辑. 编辑 hands the
            // deck back to 编辑卡组, 删除 is limited to 我的卡组 and always needs
            // that second, red click. The hint line no longer carries the ESC
            // reminder; it stays available for read-only and delete feedback.
            var footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 1;
            footer.RowCount = 4;
            footer.BackColor = Color.Transparent;
            footer.Padding = new Padding(14, 8, 14, 10);
            footer.Margin = new Padding(0);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            footer.Controls.Add(exportGrid, 0, 0);

            var actions = new TableLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.ColumnCount = 2;
            actions.RowCount = 1;
            actions.BackColor = Color.Transparent;
            actions.Margin = new Padding(0, 4, 0, 0);
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _deckDetailEditButton = CreateDeckFooterButton("编辑",
                AppTheme.Accent, AppTheme.AccentHover, AppTheme.AccentPressed,
                Color.White);
            _deckDetailEditButton.Margin = new Padding(0, 0, 4, 0);
            _deckDetailEditButton.Click += DeckDetailEdit_Click;
            actions.Controls.Add(_deckDetailEditButton, 0, 0);

            _deckDetailDeleteButton = CreateDeckFooterButton("删除",
                DeckDeleteRestFill, DeckDeleteRestHoverFill,
                DeckDeleteRestHoverFill, AppTheme.Danger);
            _deckDetailDeleteButton.Margin = new Padding(4, 0, 0, 0);
            _deckDetailDeleteButton.Click += DeckDetailDelete_Click;
            actions.Controls.Add(_deckDetailDeleteButton, 1, 0);

            footer.Controls.Add(actions, 0, 1);

            _deckExportStatus = CreateDeckSidebarLabel(8F, FontStyle.Regular,
                AppTheme.TextMuted, ContentAlignment.MiddleLeft);
            _deckExportStatus.Text = "写入 Output 文件夹";
            _deckExportStatus.AutoEllipsis = true;
            _deckExportStatus.Margin = new Padding(0);
            footer.Controls.Add(_deckExportStatus, 0, 2);

            _deckDetailHint = new Label();
            _deckDetailHint.Text = string.Empty;
            _deckDetailHint.Visible = false;
            _deckDetailHint.Dock = DockStyle.Fill;
            _deckDetailHint.AutoEllipsis = true;
            _deckDetailHint.ForeColor = AppTheme.TextMuted;
            _deckDetailHint.BackColor = Color.Transparent;
            _deckDetailHint.Font = new Font("Microsoft YaHei UI", 8F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _deckDetailHint.TextAlign = ContentAlignment.MiddleLeft;
            _deckDetailHint.Margin = new Padding(0);
            footer.Controls.Add(_deckDetailHint, 0, 3);

            layout.Controls.Add(footer, 0, 5);

            return host;
        }

        /// <summary>
        /// Builds one captioned option block for the second level sidebar.
        /// Metrics match the filter groups of the series card list so both
        /// sidebars line up pixel for pixel.
        /// </summary>
        private static TableLayoutPanel CreateDeckSidebarGroup(string caption,
            Control control, int height)
        {
            var group = new TableLayoutPanel();
            group.Width = 218;
            group.Height = height;
            group.ColumnCount = 1;
            group.RowCount = 2;
            group.BackColor = Color.Transparent;
            group.Margin = new Padding(0, 0, 0, 9);
            group.Padding = new Padding(0);
            group.RowStyles.Add(new RowStyle(SizeType.Absolute,
                DeckSidebarGroupCaptionHeight));
            group.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var label = new Label();
            label.Text = caption;
            label.Dock = DockStyle.Fill;
            label.ForeColor = AppTheme.Text;
            label.BackColor = Color.Transparent;
            label.Font = new Font("Microsoft YaHei UI", 10.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0);
            group.Controls.Add(label, 0, 0);

            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0);
            group.Controls.Add(control, 0, 1);

            return group;
        }

        private static Label CreateDeckSidebarLabel(float size, FontStyle style,
            Color color, ContentAlignment align)
        {
            var label = new Label();
            label.Dock = DockStyle.Fill;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Font = new Font("Microsoft YaHei UI", size, style,
                GraphicsUnit.Point, 134);
            label.TextAlign = align;
            label.Margin = new Padding(0);
            return label;
        }

        /// <summary>
        /// One of the two footer buttons of the deck detail page. It fills the
        /// rounded shape with the page colour, so the corners stay crisp on the
        /// gradient sidebar and only the fill colour changes with the state.
        /// </summary>
        private static RoundedButton CreateDeckFooterButton(string text,
            Color normal, Color hover, Color pressed, Color foreColor)
        {
            var button = new RoundedButton();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.CornerRadius = 8;
            button.SurfaceColor = AppTheme.MainBackground;
            button.NormalColor = normal;
            button.HoverColor = hover;
            button.PressedColor = pressed;
            button.ForeColor = foreColor;
            button.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            button.Margin = new Padding(0);
            return button;
        }

        private Control BuildDeckToolbar()
        {
            var bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 2;
            bar.RowCount = 1;
            bar.BackColor = AppTheme.MainBackground;
            bar.Margin = new Padding(0);
            bar.Padding = new Padding(0, 2, 0, 8);
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _deckSelectionLabel = new Label();
            _deckSelectionLabel.Text = "单击卡组查看摘要";
            _deckSelectionLabel.Dock = DockStyle.Fill;
            _deckSelectionLabel.AutoEllipsis = true;
            _deckSelectionLabel.ForeColor = AppTheme.TextMuted;
            _deckSelectionLabel.BackColor = Color.Transparent;
            _deckSelectionLabel.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _deckSelectionLabel.TextAlign = ContentAlignment.MiddleLeft;
            _deckSelectionLabel.Margin = new Padding(0);
            bar.Controls.Add(_deckSelectionLabel, 0, 0);

            _deckRefreshButton = new RoundedButton();
            _deckRefreshButton.Text = "刷新";
            _deckRefreshButton.AccessibleName = "刷新卡组";
            _deckRefreshButton.Dock = DockStyle.Fill;
            _deckRefreshButton.CornerRadius = 8;
            _deckRefreshButton.SurfaceColor = AppTheme.MainBackground;
            _deckRefreshButton.NormalColor = AppTheme.Accent;
            _deckRefreshButton.HoverColor = AppTheme.AccentHover;
            _deckRefreshButton.PressedColor = AppTheme.AccentPressed;
            _deckRefreshButton.ForeColor = Color.White;
            _deckRefreshButton.Font = _settingsTitleFont;
            _deckRefreshButton.Margin = new Padding(0);
            _deckRefreshButton.Click += DeckRefreshButton_Click;
            bar.Controls.Add(_deckRefreshButton, 1, 0);

            return bar;
        }

        /// <summary>
        /// Re-reads both deck folders and rebuilds the two blocks. Parsing is
        /// limited to the card codes, so this stays fast even with many decks.
        /// </summary>
        private void RefreshDeckItems(bool reload)
        {
            if (_deckLocalGrid == null || _deckDefaultGrid == null)
                return;

            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (_deckLibrary == null || reload)
                    _deckLibrary = DeckLibrary.Load(_database.DataRoot);
                if (_deckCoverLocator == null)
                    _deckCoverLocator = new DeckCoverLocator(_database);
                if (_deckCardResolver == null)
                {
                    _deckCardResolver = new DeckCardResolver(
                        _database, _deckCoverLocator);
                }

                _deckLocalGrid.SetItems(BuildDeckItems(DeckSource.Local));
                _deckDefaultGrid.SetItems(BuildDeckItems(DeckSource.Default));
                UpdateDeckListCaptions();
                LayoutDeckBlocks();

                // Entering the page or reloading starts the shared page at the
                // top; a refresh after 删除 keeps the player where they were.
                if (reload && _deckListScroll != null)
                    _deckListScroll.ScrollToTop();

                int total = _deckLibrary.Decks.Count;
                if (_deckEditSubtitle != null)
                {
                    _deckEditSubtitle.Text = string.Format(
                        "我的卡组 {0} 个 · 预设卡组 {1} 个 · 共 {2} 个 · 来源 Decks / DefaultDecks",
                        _deckLibrary.LocalCount, _deckLibrary.DefaultCount, total);
                }

                if (_deckSelectionLabel != null)
                {
                    _deckSelectionLabel.Text = total == 0
                        ? "未找到卡组文件 · 请检查 StreamingAssets\\Decks 与 DefaultDecks"
                        : "单击卡组查看摘要";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "读取卡组失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (_deckSelectionLabel != null)
                    _deckSelectionLabel.Text = "读取卡组失败";
            }
            finally
            {
                Cursor = previousCursor;
            }
        }

        /// <summary>
        /// Captions of the two blocks repeat the counts of the page header, so
        /// an empty block is recognisable without reading the small subtitle.
        /// </summary>
        private void UpdateDeckListCaptions()
        {
            if (_deckLibrary == null)
                return;

            if (_deckLocalCaption != null)
            {
                _deckLocalCaption.Text = _deckLibrary.LocalCount == 0
                    ? "我的卡组 · 0 个"
                    : string.Format("我的卡组 · {0} 个",
                        _deckLibrary.LocalCount);
            }

            if (_deckDefaultCaption != null)
            {
                _deckDefaultCaption.Text = _deckLibrary.DefaultCount == 0
                    ? "预设卡组 · 0 个"
                    : string.Format("预设卡组 · {0} 个",
                        _deckLibrary.DefaultCount);
            }
        }

        /// <summary>
        /// Stacks the two blocks inside the page: caption, tiles, caption,
        /// tiles. Each grid is exactly as tall as its tiles at the current
        /// width, so the page scroll covers both blocks and no block scrolls
        /// on its own.
        /// </summary>
        private void LayoutDeckBlocks()
        {
            if (_deckListScroll == null || _deckListContent == null ||
                _deckLocalGrid == null || _deckDefaultGrid == null)
                return;

            int width = _deckListScroll.ClientSize.Width;
            if (width <= 0)
                return;

            // The page is one gutter narrower than the host: that strip is the
            // scrollbar the host paints, so no block is allowed to cover it.
            int blockWidth = Math.Max(1, width - DeckListScrollbarGutter);
            _deckListContent.Width = blockWidth;

            // Width first: the column count, and with it the needed height,
            // follows the width.
            _deckLocalGrid.Width = blockWidth;
            _deckDefaultGrid.Width = blockWidth;

            int top = 0;
            _deckLocalCaptionTop = top;
            top += DeckListCaptionHeight;

            _deckLocalGrid.Top = top;
            _deckLocalGrid.Height = _deckLocalGrid.ContentHeight;
            top += _deckLocalGrid.Height + DeckListBlockGap;

            _deckDefaultCaptionTop = top;
            top += DeckListCaptionHeight;

            _deckDefaultGrid.Top = top;
            _deckDefaultGrid.Height = _deckDefaultGrid.ContentHeight;
            top += _deckDefaultGrid.Height + DeckListBlockGap;

            _deckListContent.Height = top;
            ApplyDeckListCaptions();
        }

        /// <summary>
        /// Keeps the caption of the block under the viewport pinned to the top
        /// edge; it scrolls away with its block only when the next caption has
        /// scrolled up to take its place.
        /// </summary>
        private void ApplyDeckListCaptions()
        {
            if (_deckListScroll == null || _deckLocalCaption == null ||
                _deckDefaultCaption == null)
                return;

            int width = Math.Max(1,
                _deckListScroll.ClientSize.Width - DeckListScrollbarGutter);
            int offset = _deckListScroll.ScrollOffset;
            _deckLocalCaption.SetBounds(0,
                Math.Max(_deckLocalCaptionTop - offset, 0),
                width, DeckListCaptionHeight);
            _deckDefaultCaption.SetBounds(0,
                Math.Max(_deckDefaultCaptionTop - offset, 0),
                width, DeckListCaptionHeight);
        }

        private void DeckListScroll_ScrollOffsetChanged(object sender, EventArgs e)
        {
            ApplyDeckListCaptions();
        }

        private void DeckListScroll_Resize(object sender, EventArgs e)
        {
            LayoutDeckBlocks();
        }

        private List<TileGridItem> BuildDeckItems(DeckSource source)
        {
            var items = new List<TileGridItem>();
            if (_deckLibrary == null)
                return items;

            IList<DeckDefinition> decks = source == DeckSource.Default
                ? _deckLibrary.DefaultDecks
                : _deckLibrary.LocalDecks;
            TileGridControl grid = source == DeckSource.Default
                ? _deckDefaultGrid
                : _deckLocalGrid;

            for (int i = 0; i < decks.Count; i++)
            {
                DeckDefinition captured = decks[i];
                string subtitle = BuildDeckSubtitle(captured);

                var item = new TileGridItem();
                item.Title = captured.Name;
                item.Subtitle = subtitle;
                if (grid != null)
                    item.PreferredHeight = grid.MeasureSeriesItemHeight(subtitle);
                item.ImagePathProvider = delegate
                {
                    return _deckCoverLocator == null
                        ? null
                        : _deckCoverLocator.GetCoverPath(captured);
                };
                item.Tag = captured;
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Tile subtitle of the deck list: the deck series and its set numbers
        /// on two lines, replacing the old source / card count caption.
        /// </summary>
        private string BuildDeckSubtitle(DeckDefinition deck)
        {
            if (deck == null)
                return string.Empty;

            string codes = deck.AbbreviationText;
            if (codes.Length == 0)
                codes = "未知编号";

            return ResolveDeckSeriesText(deck) + "\n" + codes;
        }

        /// <summary>
        /// Series names of a deck, resolved from the card prefixes. The deck
        /// list and the detail sidebar both show this text.
        /// </summary>
        private string ResolveDeckSeriesText(DeckDefinition deck)
        {
            if (deck == null)
                return string.Empty;

            IList<SeriesDefinition> seriesList = _deckCoverLocator == null
                ? new List<SeriesDefinition>()
                : _deckCoverLocator.ResolveSeries(deck);
            var names = new List<string>();
            for (int i = 0; i < seriesList.Count; i++)
            {
                string name = seriesList[i].Name;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }

            return names.Count == 0
                ? "未知系列"
                : string.Join(" / ", names.ToArray());
        }

        private void DeckGrid_ItemClick(object sender, TileGridItemEventArgs e)
        {
            if (e == null || e.Item == null)
                return;

            OpenDeck(e.Item.Tag as DeckDefinition);
        }

        /// <summary>
        /// Opens the detail view of one deck: the sidebar shows the deck name,
        /// its series and the view mode, the right side shows every card.
        /// </summary>
        private void OpenDeck(DeckDefinition deck)
        {
            if (deck == null)
                return;

            _activeDeck = deck;
            _deckCompactView = false;

            // A fresh page always starts unarmed: a red 删除 from the previous
            // deck must never apply to this one.
            ResetDeckDetailActions();
            UpdateDeckDetailActions(deck);

            // Warm the per-deck card index once so the grid paints with the real
            // images instead of blocking on the first card file read.
            if (_deckCardResolver != null && deck.CardCount > 0)
            {
                Cursor previous = Cursor;
                Cursor = Cursors.WaitCursor;
                try
                {
                    _deckCardResolver.Resolve(deck, deck.CardCodes[0]);
                }
                finally
                {
                    Cursor = previous;
                }
            }

            // The name matcher behind the related-card strip works on one
            // series, so the deck detail points it at the deck's first series.
            IList<SeriesDefinition> deckSeries = _deckCoverLocator == null
                ? null
                : _deckCoverLocator.ResolveSeries(deck);
            _activeSeries = deckSeries != null && deckSeries.Count > 0
                ? _database.GetSeries(deckSeries[0].Name)
                : null;

            if (_deckDetailName != null)
                _deckDetailName.Text = deck.Name;

            // 预设卡组 are game files, so the hint says so before the greyed
            // 删除 button is clicked. A local deck clears the line again.
            if (deck.Source == DeckSource.Default)
            {
                SetDeckDetailHint("预设卡组 · 只读，不能删除",
                    AppTheme.TextMuted);
            }
            else
            {
                SetDeckDetailHint(null, AppTheme.TextMuted);
            }

            // The header mirrors the series column: one muted subtitle line
            // holding the series names and the set numbers.
            if (_deckDetailSeries != null)
            {
                string codes = deck.AbbreviationText.Length == 0
                    ? "未知编号"
                    : deck.AbbreviationText;
                _deckDetailSeries.Text =
                    ResolveDeckSeriesText(deck) + " · " + codes;
            }

            UpdateDeckStatistics(deck);

            if (_deckExportStatus != null)
            {
                _deckExportStatus.Text = "写入 Output 文件夹";
                _deckExportStatus.ForeColor = AppTheme.TextMuted;
            }

            _updatingDeckOptions = true;
            try
            {
                if (_deckExpandOption != null)
                    _deckExpandOption.Checked = true;
                if (_deckCompactOption != null)
                    _deckCompactOption.Checked = false;
                if (_deckAltArtOption != null)
                    _deckAltArtOption.Checked = false;
            }
            finally
            {
                _updatingDeckOptions = false;
            }

            ApplyDeckViewMode();
            ShowDeckDetail(true);
            SelectDeckTile(deck);

            if (_deckSelectionLabel != null)
            {
                _deckSelectionLabel.Text = string.Format(
                    "{0} · {1} · {2} 张 · {3}",
                    deck.Name, deck.SourceDisplay, deck.CardCount,
                    deck.AbbreviationText.Length == 0
                        ? "未知系列"
                        : deck.AbbreviationText);
            }
        }

        private void CloseDeckDetail()
        {
            ResetDeckDetailActions();
            UpdateDeckDetailActions(null);
            _activeDeck = null;
            _activeSeries = null;
            ShowDeckDetail(false);
            UpdateDeckStatistics(null);
            if (_deckCards != null)
                _deckCards.SetItems(null);
            if (_deckSelectionLabel != null)
                _deckSelectionLabel.Text = "单击卡组查看摘要";
        }

        private void ShowDeckDetail(bool detail)
        {
            if (_deckOverviewPanel != null)
                _deckOverviewPanel.Visible = !detail;
            if (_deckDetailPanel != null)
                _deckDetailPanel.Visible = detail;

            // The detail page keeps its own sidebar, so the big "查看卡组"
            // title bar is collapsed and the card grid gets the whole height.
            if (_deckEditHeader != null)
                _deckEditHeader.Visible = !detail;
            if (_deckEditLayout != null && _deckEditLayout.RowStyles.Count > 0)
            {
                _deckEditLayout.RowStyles[0].SizeType = SizeType.Absolute;
                _deckEditLayout.RowStyles[0].Height = detail ? 0F : 62F;
            }

            ApplyDeckSidebarLayout(detail);
            ScheduleDarkChrome();
        }

        /// <summary>
        /// The deck detail page borrows the root column of the series filter
        /// for its second level sidebar and collapses the main sidebar to the
        /// compact icon rail, exactly like the series card list does.
        /// </summary>
        private void ApplyDeckSidebarLayout(bool detail)
        {
            if (_deckSidebarHost != null)
                _deckSidebarHost.Visible = detail;
            if (_deckSidebarColumnStyle != null)
                _deckSidebarColumnStyle.Width =
                    detail ? DeckDetailSidebarWidth : 0F;

            // The series filter never shares the screen with the deck sidebar.
            if (_filterHost != null)
                _filterHost.Visible = false;
            if (_filterColumnStyle != null)
                _filterColumnStyle.Width = 0F;

            if (_navColumnStyle != null)
                _navColumnStyle.Width = detail ? NavCompactWidth : NavExpandedWidth;

            if (detail)
            {
                SetDeckSubItemsVisible(false);
                SetNavExpanded(false);
                SetActiveNavItem(_navDeckButton);
            }
            else
            {
                SetNavExpanded(true);
                SetDeckSubItemsVisible(true);
            }
        }

        private void DeckDetailBack_Click(object sender, EventArgs e)
        {
            // The list keeps a highlight on the deck that was open, so the
            // player finds the row again after reading it.
            DeckDefinition previous = _activeDeck;
            CloseDeckDetail();

            // The detail page highlighted the 卡组编辑 section only, so the
            // sub entry has to take the highlight back on the list page.
            SetActiveNavItem(_navDeckEditButton);
            SelectDeckTile(previous);
        }

        /// <summary>
        /// 导出 buttons of the deck detail page. The clipboard action needs no
        /// image lookup; the three file actions resolve each card to its image
        /// with the same resolver the card grid uses, so a missing image turns
        /// into a placeholder tile instead of failing the whole sheet.
        /// </summary>
        private void DeckDetailExport_Click(object sender, EventArgs e)
        {
            var button = sender as RoundedButton;
            DeckDefinition deck = _activeDeck;
            if (button == null || deck == null)
                return;

            // Exporting is another operation, so it also clears a pending red
            // 删除.
            DisarmDeckDelete();

            string kind = button.Tag as string ?? string.Empty;
            DeckExportResult result = null;
            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (kind == "codes")
                {
                    result = DeckExportService.CopyCardCodes(deck);
                }
                else if (kind == "expanded")
                {
                    IList<string> codes =
                        DeckComposition.OrderByCode(deck.CardCodes);
                    result = DeckExportService.ExportExpandedPng(deck, codes,
                        ResolveDeckExportImages(deck, codes),
                        BuildDeckExportSubtitle(deck, "展开"));
                }
                else if (kind == "compact")
                {
                    bool altArt = _deckAltArtOption != null &&
                        _deckAltArtOption.Checked;
                    IList<DeckCardCount> groups = altArt
                        ? DeckComposition.GroupByCode(deck.CardCodes)
                        : DeckComposition.GroupByBaseCode(deck.CardCodes);
                    var codes = new List<string>(groups.Count);
                    for (int i = 0; i < groups.Count; i++)
                        codes.Add(groups[i].Code);

                    result = DeckExportService.ExportCompactPng(deck, groups,
                        ResolveDeckExportImages(deck, codes),
                        BuildDeckExportSubtitle(deck, "缩略"));
                }
                else if (kind == "pdf")
                {
                    IList<string> codes =
                        DeckComposition.OrderByCode(deck.CardCodes);
                    result = DeckExportService.ExportPdf(deck, codes,
                        ResolveDeckExportImages(deck, codes));
                }
            }
            finally
            {
                Cursor = previous;
            }

            ShowDeckExportResult(kind, result);
        }

        private string BuildDeckExportSubtitle(DeckDefinition deck, string view)
        {
            string subtitle = string.Format("{0} · 共 {1} 张", view,
                deck.CardCount);
            if (!string.IsNullOrEmpty(deck.AbbreviationText))
                subtitle += " · " + deck.AbbreviationText;
            return subtitle;
        }

        private IList<string> ResolveDeckExportImages(DeckDefinition deck,
            IList<string> cardCodes)
        {
            var paths = new List<string>(cardCodes == null
                ? 0
                : cardCodes.Count);
            if (cardCodes == null)
                return paths;

            for (int i = 0; i < cardCodes.Count; i++)
                paths.Add(ResolveDeckExportImage(deck, cardCodes[i]));
            return paths;
        }

        private string ResolveDeckExportImage(DeckDefinition deck, string code)
        {
            if (_deckCardResolver != null)
            {
                string resolved = _deckCardResolver.GetImagePath(deck, code);
                if (!string.IsNullOrEmpty(resolved))
                    return resolved;
            }

            if (_deckCoverLocator == null)
                _deckCoverLocator = new DeckCoverLocator(_database);
            return _deckCoverLocator.GetCardImagePath(code);
        }

        private void ShowDeckExportResult(string kind, DeckExportResult result)
        {
            if (_deckExportStatus == null || result == null)
                return;

            if (!result.Success)
            {
                _deckExportStatus.Text = result.Error;
                _deckExportStatus.ForeColor = AppTheme.Danger;
                return;
            }

            _deckExportStatus.ForeColor = AppTheme.Success;
            if (kind == "codes")
            {
                _deckExportStatus.Text = string.Format("已复制 {0} 个卡号",
                    _activeDeck == null ? 0 : _activeDeck.CardCount);
                return;
            }

            string file = Path.GetFileName(result.FilePath);
            _deckExportStatus.Text = kind == "pdf"
                ? string.Format("已导出 Output\\{0}（{1} 页）", file,
                    result.PageCount)
                : "已导出 Output\\" + file;
        }

        /// <summary>
        /// 编辑: hands the open deck back to 编辑卡组. The deck keeps its name
        /// and every card code; codes of another series cannot be resolved by
        /// the picker, so the status line counts them and the tray shows the
        /// placeholder instead of dropping the card.
        /// </summary>
        private void DeckDetailEdit_Click(object sender, EventArgs e)
        {
            DeckDefinition deck = _activeDeck;
            if (deck == null)
                return;

            DisarmDeckDelete();

            if (_deckCoverLocator == null)
                _deckCoverLocator = new DeckCoverLocator(_database);

            IList<SeriesDefinition> seriesList =
                _deckCoverLocator.ResolveSeries(deck);
            if (seriesList == null || seriesList.Count == 0)
            {
                SetDeckDetailHint("找不到该卡组的系列，无法编辑",
                    AppTheme.Danger);
                return;
            }

            // ShowDeckNew / OpenSeries rebuild the tray, so the codes have to be
            // handed over after the series is open.
            ShowDeckNew();
            OpenSeries(seriesList[0]);
            if (_activeSeries == null || _viewMode != ViewMode.DeckNew)
            {
                SetDeckDraftStatus("打开系列失败，未载入 " + deck.Name);
                return;
            }

            DeckDraft draft = Draft;
            draft.Clear();
            draft.Name = deck.Name;

            int unresolved = 0;
            for (int i = 0; i < deck.CardCodes.Count; i++)
            {
                string code = deck.CardCodes[i];
                if (draft.Add(code) && ResolveDeckDraftCard(code) == null)
                    unresolved++;
            }

            _deckDraftSlot = 0;
            _deckDraftSaved = false;
            RefreshNewDeckStrip();

            // An edit always pulls the tray up, so the handed over cards are
            // visible right away instead of behind a folded tab.
            if (_newDeckStrip != null)
                _newDeckStrip.Expanded = true;

            SetDeckDraftStatus(string.Format("已载入 {0} · {1} 张{2}",
                deck.Name, draft.CardCount,
                unresolved > 0
                    ? string.Format("（{0} 张不在 {1}，显示为占位）",
                        unresolved, seriesList[0].Name)
                    : string.Empty));
        }

        /// <summary>
        /// 删除 mirrors the two click gesture of 导入卡组: the first click only
        /// turns the button red and asks for the second one, every other
        /// gesture on the page takes the red state back.
        /// </summary>
        private void DeckDetailDelete_Click(object sender, EventArgs e)
        {
            DeckDefinition deck = _activeDeck;
            if (deck == null)
                return;

            if (deck.Source != DeckSource.Local)
            {
                ResetDeckDetailActions();
                SetDeckDetailHint("预设卡组属于游戏文件，不能删除",
                    AppTheme.TextMuted);
                return;
            }

            if (!_deckDeleteArmed)
            {
                ArmDeckDelete();
                return;
            }

            ResetDeckDetailActions();
            string error = null;
            bool deleted = _deckLibrary != null &&
                _deckLibrary.DeleteDeck(deck, out error);
            if (!deleted)
            {
                SetDeckDetailHint("删除失败：" + (error ?? string.Empty),
                    AppTheme.Danger);
                RefreshDeckItems(false);

                // A file removed from outside (game or sync plugin) is gone
                // from the list too, so the stale detail page is closed.
                if (!File.Exists(deck.FilePath))
                {
                    DeckDetailBack_Click(null, EventArgs.Empty);
                    if (_deckSelectionLabel != null)
                        _deckSelectionLabel.Text = "卡组文件已不存在，已从列表移除";
                }
                return;
            }

            // The file is gone. Also remove the matching PlayerPrefs values:
            // otherwise the sync plug-in sees the game deck on the next launch
            // and writes the file back.
            bool removedFromGame;
            string registryWarning;
            GameDeckRegistry.DeleteDeck(
                deck.Name, out removedFromGame, out registryWarning);

            // The page returns to the list and re-reads the folder through the
            // library that just reloaded itself.
            DeckDetailBack_Click(null, EventArgs.Empty);
            RefreshDeckItems(false);
            if (_deckSelectionLabel != null)
            {
                if (!string.IsNullOrEmpty(registryWarning))
                {
                    _deckSelectionLabel.Text = "已删除 " + deck.Name +
                        "；游戏卡组未同步移除：" + registryWarning;
                }
                else if (removedFromGame)
                {
                    _deckSelectionLabel.Text = "已删除 " + deck.Name +
                        "（游戏卡组已同步移除）";
                }
                else
                {
                    _deckSelectionLabel.Text = "已删除 " + deck.Name;
                }
            }
        }

        private void ArmDeckDelete()
        {
            _deckDeleteArmed = true;
            if (_deckDetailDeleteButton != null)
            {
                _deckDetailDeleteButton.Text = "确认删除";
                _deckDetailDeleteButton.NormalColor = DeckDeleteArmedFill;
                _deckDetailDeleteButton.HoverColor = DeckDeleteArmedHoverFill;
                _deckDetailDeleteButton.PressedColor = DeckDeleteArmedPressedFill;
                _deckDetailDeleteButton.ForeColor = Color.White;
                _deckDetailDeleteButton.Invalidate();
            }
            SetDeckDetailHint("再次点击“删除”确认 · 其他操作会取消",
                AppTheme.Danger);
        }

        /// <summary>
        /// Keeps the feedback line under the export status in sync with the
        /// current state. Empty text hides the row again; the footer grows
        /// only while a read-only or delete message actually needs it.
        /// </summary>
        private void SetDeckDetailHint(string text, Color color)
        {
            if (_deckDetailHint == null)
                return;

            bool visible = !string.IsNullOrEmpty(text);
            _deckDetailHint.Text = text ?? string.Empty;
            _deckDetailHint.ForeColor = color;
            _deckDetailHint.Visible = visible;

            TableLayoutPanel footer =
                _deckDetailHint.Parent as TableLayoutPanel;
            if (footer == null || footer.RowStyles.Count <= 3)
                return;

            footer.RowStyles[3].Height = visible ? 22F : 0F;
            TableLayoutPanel layout = footer.Parent as TableLayoutPanel;
            if (layout == null)
                return;

            int footerRow = layout.GetRow(footer);
            if (footerRow >= 0 && footerRow < layout.RowStyles.Count)
                layout.RowStyles[footerRow].Height = visible ? 164F : 142F;
        }

        private void DisarmDeckDelete()
        {
            if (_deckDeleteArmed)
                ResetDeckDetailActions();
        }

        /// <summary>Takes the red state off again; nothing is deleted.</summary>
        private void ResetDeckDetailActions()
        {
            _deckDeleteArmed = false;
            ResetDeckDeleteButton();
            SetDeckDetailHint(null, AppTheme.TextMuted);
        }

        private void ResetDeckDeleteButton()
        {
            if (_deckDetailDeleteButton == null)
                return;

            _deckDetailDeleteButton.Text = "删除";
            _deckDetailDeleteButton.NormalColor = DeckDeleteRestFill;
            _deckDetailDeleteButton.HoverColor = DeckDeleteRestHoverFill;
            _deckDetailDeleteButton.PressedColor = DeckDeleteRestHoverFill;
            _deckDetailDeleteButton.ForeColor = AppTheme.Danger;
            _deckDetailDeleteButton.Invalidate();
        }

        /// <summary>预设卡组 stay read only, so 删除 is greyed for them.</summary>
        private void UpdateDeckDetailActions(DeckDefinition deck)
        {
            bool deletable = deck != null && deck.Source == DeckSource.Local;
            if (_deckDetailDeleteButton == null ||
                _deckDetailDeleteButton.Enabled == deletable)
            {
                return;
            }

            _deckDetailDeleteButton.Enabled = deletable;
            _deckDetailDeleteButton.Cursor = deletable
                ? Cursors.Hand
                : Cursors.Default;
        }

        /// <summary>
        /// Highlights the tile of one deck in its own block and clears the other
        /// block, so the list never shows two selected decks at once.
        /// </summary>
        private void SelectDeckTile(DeckDefinition deck)
        {
            if (_deckLocalGrid == null || _deckDefaultGrid == null)
                return;

            TileGridControl grid = deck != null &&
                deck.Source == DeckSource.Default
                ? _deckDefaultGrid
                : _deckLocalGrid;
            TileGridControl other = ReferenceEquals(grid, _deckLocalGrid)
                ? _deckDefaultGrid
                : _deckLocalGrid;

            other.SelectedIndex = -1;
            if (deck == null)
            {
                grid.SelectedIndex = -1;
                return;
            }

            IList<TileGridItem> items = grid.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i].Tag, deck))
                {
                    grid.SelectedIndex = i;
                    return;
                }
            }

            grid.SelectedIndex = -1;
        }

        private void DeckViewMode_CheckedChanged(object sender, EventArgs e)
        {
            if (_updatingDeckOptions)
                return;

            FilterRadioButton option = sender as FilterRadioButton;
            if (option == null || !option.Checked)
                return;

            // Switching the view is another operation, so it takes a pending
            // red 删除 back as well.
            DisarmDeckDelete();

            object mode = option.Tag;
            _deckCompactView = mode is int && (int)mode == 1;
            ApplyDeckViewMode();
        }

        private void DeckAltArt_CheckedChanged(object sender, EventArgs e)
        {
            if (_updatingDeckOptions)
                return;

            DisarmDeckDelete();
            ApplyDeckViewMode();
        }

        /// <summary>
        /// 异画 is a 缩略 only switch, so it is hidden as soon as the grid shows
        /// every copy separately. The checked state survives the switch, so
        /// returning to 缩略 keeps the previous choice.
        /// </summary>
        private void UpdateDeckAltArtState()
        {
            if (_deckAltArtOption == null)
                return;

            bool visible = _deckCompactView;
            if (_deckAltArtOption.Visible != visible)
                _deckAltArtOption.Visible = visible;
        }

        /// <summary>
        /// 展开 keeps every copy in a 10 wide grid with a tight 2 px gap, so a
        /// 50 card deck fills a 10 x 5 block like a physical deck. 缩略 collapses
        /// duplicate codes into one 7 x 4 sized grid with a count badge and, by
        /// default, merges every art variant; checking 异画 keeps them apart.
        /// </summary>
        private void ApplyDeckViewMode()
        {
            if (_activeDeck == null || _deckCards == null)
                return;

            UpdateDeckAltArtState();

            bool altArt = _deckAltArtOption != null &&
                _deckAltArtOption.Checked;

            var tiles = new List<DeckCardTile>();
            if (_deckCompactView)
            {
                IList<DeckCardCount> groups = altArt
                    ? DeckComposition.GroupByCode(_activeDeck.CardCodes)
                    : DeckComposition.GroupByBaseCode(_activeDeck.CardCodes);
                for (int i = 0; i < groups.Count; i++)
                    tiles.Add(CreateDeckCardTile(groups[i].Code, groups[i].Count));

                _deckCards.Columns = DeckCompactColumns;
                _deckCards.TargetRows = DeckCompactRows;
                _deckCards.Gap = DeckCompactGap;
                _deckCards.ShowCounts = true;
                // 缩略 view: the badge is a small sticker, the count keeps
                // its font size so it fills the smaller circle.
                _deckCards.CountBadgeScale = 0.7F;
            }
            else
            {
                IList<string> ordered =
                    DeckComposition.OrderByCode(_activeDeck.CardCodes);
                for (int i = 0; i < ordered.Count; i++)
                    tiles.Add(CreateDeckCardTile(ordered[i], 1));

                _deckCards.Columns = DeckExpandedColumns;
                _deckCards.TargetRows = 0;
                _deckCards.Gap = DeckExpandedGap;
                _deckCards.ShowCounts = false;
                _deckCards.CountBadgeScale = 1F;
            }

            _deckCards.SetItems(tiles);
            ScheduleDarkChrome();
        }

        private DeckCardTile CreateDeckCardTile(string code, int count)
        {
            var tile = new DeckCardTile();
            tile.Code = code;
            tile.Count = count;
            DeckDefinition deck = _activeDeck;
            string captured = code;
            tile.ImagePathProvider = delegate
            {
                if (_deckCardResolver != null)
                {
                    string resolved = _deckCardResolver.GetImagePath(deck, captured);
                    if (!string.IsNullOrEmpty(resolved))
                        return resolved;
                }

                return _deckCoverLocator == null
                    ? null
                    : _deckCoverLocator.GetCardImagePath(captured);
            };
            return tile;
        }

        /// <summary>
        /// A card of the open deck is shown on the same detail page as a card
        /// from the search results.
        /// </summary>
        private void DeckCards_ItemClick(object sender, DeckCardTileEventArgs e)
        {
            if (e == null || e.Tile == null || _deckCardResolver == null)
                return;

            // Opening a card is another operation, so it also clears a pending
            // red 删除.
            DisarmDeckDelete();

            WeissCard card = _deckCardResolver.Resolve(_activeDeck, e.Tile.Code);
            if (card != null)
                ShowCardDetail(card);
        }

        /// <summary>
        /// Fills the 卡组信息 block with the numbers of one deck. Every code is
        /// resolved through the shared <see cref="DeckCardResolver"/> index, so
        /// the pass costs one dictionary lookup per card.
        /// </summary>
        private void UpdateDeckStatistics(DeckDefinition deck)
        {
            if (_deckStats == null)
                return;

            DeckStatistics statistics = DeckStatistics.Compute(
                deck == null ? null : deck.CardCodes,
                delegate(string code)
                {
                    return deck == null || _deckCardResolver == null
                        ? null
                        : _deckCardResolver.Resolve(deck, code);
                });

            _deckStats.SetStatistics(statistics);

            if (_deckStatsGroup != null)
            {
                int height = _deckStats.MeasureHeight() +
                    DeckSidebarGroupCaptionHeight;
                if (_deckStatsGroup.Height != height)
                    _deckStatsGroup.Height = height;
            }
        }

        private void DeckRefreshButton_Click(object sender, EventArgs e)
        {
            if (_deckCoverLocator != null)
                _deckCoverLocator.Clear();
            if (_deckCardResolver != null)
                _deckCardResolver.Clear();
            RefreshDeckItems(true);
        }
    }
}
