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
    public sealed partial class MainForm : Form
    {
        private const int NavBrandRowHeight = 86;
        private const int NavItemRowHeight = 52;
        private const int NavSubItemRowHeight = 44;
        private const int NavItemIndent = 10;
        private const int NavSubItemIndent = 26;
        private const int NavSettingsRowHeight = 46;
        // 启动游戏 sits directly above 设置 and shares its 46px row.
        private const int NavLaunchRowHeight = 46;
        private const int NavFooterRowHeight = 34;

        // The series search is pinned in the sidebar above the page buttons,
        // so it stays reachable from every page and folds away together with
        // the labels when the rail switches to its compact form.
        private const int NavSearchRowHeight = 140;
        private const float NavExpandedWidth = 180F;
        private const float NavCompactWidth = 72F;

        /// <summary>
        /// Width kept clear on the right edge of the series / card grid for the
        /// slim scrollbar the page host paints. Matches the deck overview so
        /// every list in the app scrolls the same way.
        /// </summary>
        private const int GridScrollbarGutter = 14;

        private static readonly Color NavSurfaceColor = Color.FromArgb(31, 41, 55);
        private static readonly Color NavItemHoverColor = Color.FromArgb(45, 57, 75);
        private static readonly Color NavItemPressedColor = Color.FromArgb(75, 85, 99);

        /// <summary>
        /// Top level pages reachable from the sidebar. New feature pages only
        /// need a value here plus a call to <see cref="AddNavItem"/>.
        /// </summary>
        private enum ViewMode
        {
            SeriesList,
            CardList,
            DeckNew,
            DeckEdit,
            Settings,

            /// <summary>第一级 of 我的收藏: the series covers, greyed per series.</summary>
            CollectionList,

            /// <summary>第二级 of 我的收藏: the card list of one series.</summary>
            CollectionCards,

            /// <summary>
            /// 第一级 of 编辑卡片: the series covers, the very same grid 系列卡表
            /// shows, used to pick the series whose cards get edited.
            /// </summary>
            CardEditList,

            /// <summary>
            /// 第二级 of 编辑卡片: the card list of one series. A click opens the
            /// editor instead of the card detail page.
            /// </summary>
            CardEditCards,

            /// <summary>
            /// The editor itself (修改卡片 / 添加卡片). It is a normal content
            /// page, not an overlay, so Esc / 取消 returns to the card list.
            /// </summary>
            CardEditor
        }

        private enum SeriesSortMode
        {
            Name = 0,
            Abbreviation = 1
        }

        /// <summary>
        /// Which sidebar section a sub entry belongs to. Only the sub entries of
        /// the section that is currently open are unfolded below their parent
        /// page; every other sub entry folds to zero height.
        /// </summary>
        private enum NavSection
        {
            None,
            Deck,
            CardEdit
        }

        private readonly CardDatabase _database;
        private readonly CardSearchService _searchService;
        private readonly ThumbnailCache _thumbnails;
        private readonly SeriesCoverLocator _coverLocator;
        private readonly Font _navTitleFont;
        private readonly Font _navTitleCompactFont;
        private readonly Font _navItemFont;
        private readonly Font _navSubItemFont;
        private readonly Font _settingsTitleFont;
        private readonly Font _settingsHintFont;
        private List<WeissCard> _results;

        private ViewMode _viewMode;
        private SeriesDefinition _activeSeries;
        private ColumnStyle _navColumnStyle;
        private ColumnStyle _filterColumnStyle;
        private ColumnStyle _deckSidebarColumnStyle;

        private Panel _filterHost;
        private Panel _deckSidebarHost;
        private DarkScrollPanel _filterBody;
        private FlowLayoutPanel _filterStack;
        private FlowLayoutPanel _filterFooter;
        private TableLayoutPanel _navLayout;
        private DarkScrollPanel _navScrollHost;
        private TableLayoutPanel _navItemsLayout;
        private RowStyle _navItemsRow;
        private Control _navSearchHost;
        private RowStyle _navSearchRow;
        private readonly List<NavItem> _navItems = new List<NavItem>();
        private bool _navExpanded = true;
        private Label _navTitle;
        private Label _navFooter;
        private RoundedButton _navSearchButton;
        private RoundedButton _navSeriesButton;
        private GearButton _navSettingsButton;
        private GearButton _navLaunchGameButton;
        private ToolTip _navToolTip;
        private Panel _contentHost;
        private Panel _browsePage;
        private Panel _settingsPage;
        private RoundedTextBox _seriesSearchText;
        private FlowLayoutPanel _seriesSortChecks;
        private Label _seriesSearchSummary;
        private SeriesSortMode _seriesSortMode;
        private bool _updatingSeriesControls;
        private LinkLabel _backButton;
        private Label _seriesTitleLabel;
        private Label _seriesFolderLabel;
        private Label _contentTitle;
        private Label _contentSubtitle;
        private Label _statusLabel;
        private TileGridControl _tileGrid;
        private DarkScrollPanel _tileScroll;
        private CardDetailOverlay _cardDetailOverlay;
        private DeckImportOverlay _deckImportOverlay;
        private readonly ThumbnailCache _detailThumbnails;

        private FlowLayoutPanel _sortChecks;
        private TableLayoutPanel _setChecks;
        private FlowLayoutPanel _levelChecks;
        private FlowLayoutPanel _costChecks;
        private FlowLayoutPanel _colorChecks;
        private FlowLayoutPanel _typeChecks;
        private RoundedTextBox _nameText;
        private FilterCheckBox _showAltCheck;
        private FilterCheckBox _onlyAltCheck;
        private RoundedButton _searchButton;
        private TableLayoutPanel _setFilterGroup;
        private int _sortMode;
        private bool _populatingCombos;
        private bool _updatingAltToggles;

        public MainForm(CardDatabase database)
        {
            if (database == null)
                throw new ArgumentNullException("database");

            _database = database;
            _searchService = new CardSearchService();
            _thumbnails = new ThumbnailCache(120);
            _detailThumbnails = new ThumbnailCache(32);
            _coverLocator = new SeriesCoverLocator(database.CardsRoot);
            _collection = new CollectionStore(database.DataRoot);
            _navTitleFont = new Font("Microsoft YaHei UI", 14F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _navTitleCompactFont = new Font("Microsoft YaHei UI", 10.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _navItemFont = new Font("Microsoft YaHei UI", 11.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _navSubItemFont = new Font("Microsoft YaHei UI", 10F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _settingsTitleFont = new Font("Microsoft YaHei UI", 10F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _settingsHintFont = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _navToolTip = new ToolTip();
            _results = new List<WeissCard>();
            _viewMode = ViewMode.SeriesList;

            BuildUi();
            PopulateFixedFilters();
            ShowSeriesList();

            // 卡表压缩包可以直接拖到窗口任意位置安装，安装完成后回到系列列表。
            EnableCardPackDrop();
        }

        private void BuildUi()
        {
            SuspendLayout();

            Text = "Weiss Schwarz 卡牌检索 / Deck Builder";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1100, 700);
            Size = new Size(1440, 900);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular,
                GraphicsUnit.Point, 134);
            BackColor = AppTheme.MainBackground;
            ForeColor = AppTheme.Text;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 4;
            root.RowCount = 1;
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);

            _navColumnStyle = new ColumnStyle(SizeType.Absolute, 180F);
            _filterColumnStyle = new ColumnStyle(SizeType.Absolute, 0F);
            _deckSidebarColumnStyle = new ColumnStyle(SizeType.Absolute, 0F);
            root.ColumnStyles.Add(_navColumnStyle);
            root.ColumnStyles.Add(_filterColumnStyle);
            root.ColumnStyles.Add(_deckSidebarColumnStyle);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            root.Controls.Add(BuildNavPanel(), 0, 0);

            _filterHost = BuildFilterHost();
            root.Controls.Add(_filterHost, 1, 0);

            // The settings page opens its category sidebar in the very same
            // second level column the series filter uses, so the main rail
            // collapses and the category list takes its place, exactly like on
            // the card list and deck pages. It stays hidden until 设置 is hit.
            _settingsSidebarHost = BuildSettingsSidebarHost();
            root.Controls.Add(_settingsSidebarHost, 1, 0);

            // The deck detail page reuses the second level slot of the series
            // filter, so both card pages share one sidebar layout.
            _deckSidebarHost = BuildDeckSidebarHost();
            root.Controls.Add(_deckSidebarHost, 2, 0);

            root.Controls.Add(BuildContentPanel(), 3, 0);

            ResumeLayout(true);
        }

        /// <summary>
        /// Sidebar frame: brand block, a growing list of top-level pages, the
        /// 启动游戏 and 设置 entries pinned above the footer and the version
        /// footer.
        /// </summary>
        private Control BuildNavPanel()
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = NavSurfaceColor;
            panel.Padding = new Padding(10, 12, 10, 10);

            _navLayout = new TableLayoutPanel();
            _navLayout.Dock = DockStyle.Fill;
            _navLayout.ColumnCount = 1;
            _navLayout.RowCount = 7;
            _navLayout.BackColor = NavSurfaceColor;
            _navLayout.Margin = new Padding(0);
            _navLayout.Padding = new Padding(0);
            _navLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, NavBrandRowHeight));
            _navSearchRow =
                new RowStyle(SizeType.Absolute, NavSearchRowHeight);
            _navLayout.RowStyles.Add(_navSearchRow);
            _navItemsRow = new RowStyle(SizeType.Absolute, NavItemRowHeight);
            _navLayout.RowStyles.Add(_navItemsRow);
            _navLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _navLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, NavLaunchRowHeight));
            _navLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, NavSettingsRowHeight));
            _navLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, NavFooterRowHeight));
            _navLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _navLayout.SizeChanged += delegate { UpdateNavLayoutMetrics(); };

            _navTitle = new Label();
            _navTitle.Text = "Weiss\nSchwarz";
            _navTitle.Dock = DockStyle.Fill;
            _navTitle.ForeColor = Color.White;
            _navTitle.Font = _navTitleFont;
            _navTitle.Margin = new Padding(4, 0, 4, 0);
            _navTitle.TextAlign = ContentAlignment.MiddleLeft;
            _navLayout.Controls.Add(_navTitle, 0, 0);

            // The series search sits directly below the brand and above the
            // page buttons, so it is available on every page yet still reads
            // as part of the sidebar. It collapses with the rail.
            _navSearchHost = BuildSeriesSearchPanel();
            _navSearchHost.Dock = DockStyle.Fill;
            _navSearchHost.Margin = new Padding(0);
            _navLayout.Controls.Add(_navSearchHost, 0, 1);

            // Page buttons live in a slim scroll host together with their
            // collapsible blocks (deck sub entries), so the pinned search,
            // settings row and footer never move when pages are added.
            _navScrollHost = new DarkScrollPanel();
            _navScrollHost.Dock = DockStyle.Fill;
            _navScrollHost.BackColor = NavSurfaceColor;
            _navScrollHost.Margin = new Padding(0);
            _navScrollHost.Padding = new Padding(0);

            _navItemsLayout = new TableLayoutPanel();
            _navItemsLayout.ColumnCount = 1;
            _navItemsLayout.RowCount = 0;
            _navItemsLayout.BackColor = NavSurfaceColor;
            _navItemsLayout.Margin = new Padding(0);
            _navItemsLayout.Padding = new Padding(0);
            _navItemsLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F));

            _navScrollHost.Content = _navItemsLayout;
            _navLayout.Controls.Add(_navScrollHost, 0, 2);

            var spacer = new Panel();
            spacer.Dock = DockStyle.Fill;
            spacer.BackColor = NavSurfaceColor;
            _navLayout.Controls.Add(spacer, 0, 3);

            // The series search / sort controls live in the sidebar block, so
            // the item stack only holds the page entries. The compact rail has
            // no room for that block, so it gets its own 搜索 entry at the top
            // of the stack; the row stays folded while the rail is expanded.
            _navSearchButton = AddNavItem("搜索", "搜索", "搜索系列",
                NavSearchButton_Click);
            SetNavItemRowVisible(_navSearchButton, false);

            _navSeriesButton = AddNavItem("系列卡表", "系列", "系列卡表",
                NavSeriesButton_Click);

            // Deck tools: the parent entry opens the functional editor page,
            // the indented sub entries act as its tabs and only show up while
            // the deck section is the active one.
            _navDeckButton = AddNavItem("卡组编辑", "卡组", "卡组编辑",
                NavDeckButton_Click);
            _navDeckNewButton = AddNavItem("编辑卡组", "编辑", "编辑卡组",
                NavDeckNewButton_Click, true, NavSection.Deck);
            _navDeckEditButton = AddNavItem("查看卡组", "查看", "查看卡组",
                NavDeckEditButton_Click, true, NavSection.Deck);

            // 我的收藏 mirrors the series page: the same cover grid, the same
            // card list, but coloured by what the player owns. It sits below
            // the deck tools, and the check box unfolded right below the entry
            // belongs to this section only.
            _navCollectionButton = AddNavItem("我的收藏", "收藏", "我的收藏",
                NavCollectionButton_Click);
            AddCollectionOwnedOption();

            // 编辑卡片 is the card authoring section: 修改卡片 browses a series
            // like 系列卡表 and opens the editor on a click, 添加卡片 starts a
            // blank card right away, 添加异画 opens the same editor with the
            // 添加异画 block of the clicked card ready. All sub entries only
            // unfold while the section is open, exactly like the deck tools.
            _navCardEditButton = AddNavItem("编辑卡片", "写卡", "编辑卡片",
                NavCardEditButton_Click);
            _navCardEditModifyButton = AddNavItem("修改卡片", "修改", "修改卡片",
                NavCardEditModifyButton_Click, true, NavSection.CardEdit);
            _navCardEditAddButton = AddNavItem("添加卡片", "添加", "添加卡片",
                NavCardEditAddButton_Click, true, NavSection.CardEdit);
            _navCardEditAltButton = AddNavItem("添加异画", "异画", "添加异画",
                NavCardEditAltButton_Click, true, NavSection.CardEdit);

            // Both pinned entries live at the bottom of the sidebar: 启动游戏
            // starts the game with the noUpdate argument (the same thing
            // 不热更启动游戏.lnk does), 设置 opens the settings page.
            _navLaunchGameButton = CreatePinnedNavButton("启动游戏",
                SidebarIcon.Play, "启动游戏（跳过自动更新）");
            _navLaunchGameButton.Click += NavLaunchGameButton_Click;
            _navLayout.Controls.Add(_navLaunchGameButton, 0, 4);

            // Grey the entry out when the data folder is not inside a game
            // install, so a missing exe shows up before the click.
            if (GameLauncher.FindGameRoot(
                _database == null ? null : _database.DataRoot) == null)
            {
                _navLaunchGameButton.Enabled = false;
                _navToolTip.SetToolTip(_navLaunchGameButton,
                    "找不到 " + GameLauncher.ExeName + "（用 --data 或环境变量 " +
                    GameLauncher.ExePathVariable + " 指定游戏位置）");
            }

            _navSettingsButton = CreatePinnedNavButton("设置",
                SidebarIcon.Gear, "设置");
            _navSettingsButton.Click += NavSettingsButton_Click;
            _navLayout.Controls.Add(_navSettingsButton, 0, 5);

            _navFooter = new Label();
            _navFooter.Text = "CH-0.6 · 本地数据";
            _navFooter.Dock = DockStyle.Fill;
            _navFooter.ForeColor = AppTheme.TextMuted;
            _navFooter.Font = new Font("Microsoft YaHei UI", 8F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _navFooter.TextAlign = ContentAlignment.BottomLeft;
            _navFooter.Margin = new Padding(4, 0, 4, 0);
            _navLayout.Controls.Add(_navFooter, 0, 6);

            panel.Controls.Add(_navLayout);
            return panel;
        }

        /// <summary>
        /// Builds one of the pinned bottom entries. They keep their label while
        /// the rail is expanded and fold to the icon alone at 72px, which is why
        /// they need the custom drawn icon instead of a text-only button.
        /// </summary>
        private GearButton CreatePinnedNavButton(string text, SidebarIcon icon,
            string toolTip)
        {
            var button = new GearButton();
            button.Text = text;
            button.AccessibleName = text;
            button.Icon = icon;
            button.Dock = DockStyle.Fill;
            button.CornerRadius = 10;
            button.IconSize = 20;
            button.SurfaceColor = NavSurfaceColor;
            button.NormalColor = Color.Transparent;
            button.HoverColor = NavItemHoverColor;
            button.PressedColor = NavItemPressedColor;
            button.ActiveColor = NavItemHoverColor;
            button.Font = _navItemFont;
            button.ForeColor = AppTheme.TextSoft;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(10, 0, 0, 0);
            button.Margin = new Padding(0, 0, 0, 6);
            _navToolTip.SetToolTip(button, toolTip);
            return button;
        }

        /// <summary>
        /// Appends a top level page button to the sidebar. Call this once per
        /// feature page; row height, compact label and active state are
        /// handled automatically.
        /// </summary>
        private RoundedButton AddNavItem(string text, string compactText,
            string toolTip, EventHandler onClick)
        {
            return AddNavItem(text, compactText, toolTip, onClick, false);
        }

        /// <summary>
        /// Appends a page button to the sidebar. Sub items get a narrower row,
        /// the smaller caption font and a deeper left indent so the hierarchy
        /// stays readable when one feature owns several pages.
        /// </summary>
        private RoundedButton AddNavItem(string text, string compactText,
            string toolTip, EventHandler onClick, bool subItem)
        {
            return AddNavItem(text, compactText, toolTip, onClick, subItem,
                NavSection.None);
        }

        /// <summary>
        /// Appends a page button to the sidebar. Sub items get a narrower row,
        /// the smaller caption font and a deeper left indent so the hierarchy
        /// stays readable when one feature owns several pages.
        /// </summary>
        private RoundedButton AddNavItem(string text, string compactText,
            string toolTip, EventHandler onClick, bool subItem,
            NavSection section)
        {
            int index = _navItemsLayout.RowStyles.Count;
            int rowHeight = subItem ? NavSubItemRowHeight : NavItemRowHeight;
            int indent = subItem ? NavSubItemIndent : NavItemIndent;

            var button = new RoundedButton();
            button.Text = text;
            button.AccessibleName = text;
            button.Dock = DockStyle.Fill;
            button.CornerRadius = 10;
            button.SurfaceColor = NavSurfaceColor;
            button.NormalColor = NavSurfaceColor;
            button.HoverColor = NavItemHoverColor;
            button.PressedColor = NavItemPressedColor;
            button.SelectedColor = NavItemHoverColor;
            button.ForeColor = AppTheme.TextSoft;
            button.Font = subItem ? _navSubItemFont : _navItemFont;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(indent, 0, 0, 0);
            button.Margin = new Padding(0, 0, 0, subItem ? 4 : 6);
            button.Cursor = Cursors.Hand;
            if (onClick != null)
                button.Click += onClick;

            var row = new RowStyle(SizeType.Absolute, rowHeight);
            _navItemsLayout.RowStyles.Add(row);
            _navItemsLayout.RowCount = _navItemsLayout.RowStyles.Count;
            _navItemsLayout.Controls.Add(button, 0, index);

            var item = new NavItem();
            item.Text = text;
            item.CompactText = compactText;
            item.Button = button;
            item.IsSubItem = subItem;
            item.Indent = indent;
            item.Row = row;
            item.Section = section;
            _navItems.Add(item);

            if (_navToolTip != null && !string.IsNullOrEmpty(toolTip))
                _navToolTip.SetToolTip(button, toolTip);

            UpdateNavLayoutMetrics();
            return button;
        }

        /// <summary>
        /// Grows the page button area with the number of registered pages and
        /// caps it at the space left between the pinned sidebar blocks (brand,
        /// series search, settings, footer); anything beyond that scrolls
        /// inside the host.
        /// </summary>
        private void UpdateNavLayoutMetrics()
        {
            if (_navLayout == null || _navItemsLayout == null)
                return;

            int itemsHeight = 0;
            for (int i = 0; i < _navItemsLayout.RowStyles.Count; i++)
                itemsHeight += (int)_navItemsLayout.RowStyles[i].Height;
            _navItemsLayout.Height = Math.Max(1, itemsHeight);

            int searchHeight = _navSearchRow == null
                ? 0
                : (int)_navSearchRow.Height;
            int reserved = NavBrandRowHeight + searchHeight +
                NavLaunchRowHeight + NavSettingsRowHeight + NavFooterRowHeight;
            int available = _navLayout.ClientSize.Height - reserved;
            int rowHeight = Math.Min(itemsHeight,
                Math.Max(NavItemRowHeight, available));

            if (_navItemsRow != null)
                _navItemsRow.Height = Math.Max(NavItemRowHeight, rowHeight);

            if (_navScrollHost != null)
                _navScrollHost.PerformLayout();
        }

        /// <summary>
        /// Highlights the page the user is currently on. Passing null marks
        /// the settings entry as active.
        /// </summary>
        private void SetActiveNavItem(RoundedButton selected)
        {
            for (int i = 0; i < _navItems.Count; i++)
            {
                RoundedButton button = _navItems[i].Button;
                bool active = ReferenceEquals(button, selected);
                if (button.Selected == active)
                    continue;

                button.Selected = active;
                button.ForeColor = active ? Color.White : AppTheme.TextSoft;
                button.Invalidate();
            }

            bool settingsActive = selected == null;
            if (_navSettingsButton != null &&
                _navSettingsButton.IsActive != settingsActive)
            {
                _navSettingsButton.IsActive = settingsActive;
                _navSettingsButton.Invalidate();
            }

            UpdateNavGroupStates(selected);
        }
        /// <summary>
        /// Series search block pinned in the sidebar between the brand and the
        /// page buttons. It stays mounted on every page and only disappears
        /// when the navigation rail collapses to its compact form.
        /// </summary>
        private Control BuildSeriesSearchPanel()
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 5;
            panel.BackColor = Color.Transparent;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(0, 2, 0, 10);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var caption = new Label();
            caption.Text = "系列搜索";
            caption.Dock = DockStyle.Fill;
            caption.ForeColor = AppTheme.Text;
            caption.BackColor = Color.Transparent;
            caption.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            caption.TextAlign = ContentAlignment.BottomLeft;
            caption.Margin = new Padding(1, 0, 0, 2);
            panel.Controls.Add(caption, 0, 0);

            _seriesSearchText = new RoundedTextBox();
            _seriesSearchText.Dock = DockStyle.Fill;
            _seriesSearchText.Margin = new Padding(0);
            _seriesSearchText.CornerRadius = 8;
            _seriesSearchText.AccessibleName = "系列搜索";
            _seriesSearchText.Input.AccessibleName = "系列搜索";
            _seriesSearchText.Input.TextChanged += SeriesSearchText_TextChanged;
            _seriesSearchText.Input.KeyDown += SeriesSearchInput_KeyDown;
            if (_navToolTip != null)
            {
                _navToolTip.SetToolTip(_seriesSearchText,
                    "名称 / 缩写 / 卡号头 · 回车查看结果");
            }
            panel.Controls.Add(_seriesSearchText, 0, 1);

            var sortCaption = new Label();
            sortCaption.Text = "排序";
            sortCaption.Dock = DockStyle.Fill;
            sortCaption.ForeColor = AppTheme.Text;
            sortCaption.BackColor = Color.Transparent;
            sortCaption.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            sortCaption.TextAlign = ContentAlignment.BottomLeft;
            sortCaption.Margin = new Padding(1, 6, 0, 2);
            panel.Controls.Add(sortCaption, 0, 2);

            _seriesSortChecks = new FlowLayoutPanel();
            _seriesSortChecks.Dock = DockStyle.Fill;
            _seriesSortChecks.FlowDirection = FlowDirection.LeftToRight;
            _seriesSortChecks.WrapContents = false;
            _seriesSortChecks.AutoScroll = false;
            _seriesSortChecks.BackColor = Color.Transparent;
            _seriesSortChecks.Margin = new Padding(0);
            _seriesSortChecks.Padding = new Padding(0, 1, 0, 0);
            panel.Controls.Add(_seriesSortChecks, 0, 3);

            AddSeriesSortOption("系列名", SeriesSortMode.Name);
            AddSeriesSortOption("缩写", SeriesSortMode.Abbreviation);
            if (_seriesSortChecks.Controls.Count > 0)
            {
                RadioButton first =
                    _seriesSortChecks.Controls[0] as RadioButton;
                if (first != null)
                {
                    _updatingSeriesControls = true;
                    try
                    {
                        first.Checked = true;
                    }
                    finally
                    {
                        _updatingSeriesControls = false;
                    }
                }
            }

            _seriesSearchSummary = new Label();
            _seriesSearchSummary.Text = string.Empty;
            _seriesSearchSummary.Dock = DockStyle.Fill;
            _seriesSearchSummary.AutoEllipsis = true;
            _seriesSearchSummary.ForeColor = AppTheme.TextMuted;
            _seriesSearchSummary.BackColor = Color.Transparent;
            _seriesSearchSummary.Font = new Font("Microsoft YaHei UI", 8F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _seriesSearchSummary.TextAlign = ContentAlignment.MiddleLeft;
            _seriesSearchSummary.Margin = new Padding(1, 0, 0, 0);
            panel.Controls.Add(_seriesSearchSummary, 0, 4);

            return panel;
        }

        /// <summary>
        /// Caption under the sidebar search: the total series count, or the
        /// number of matches while a query is active. The wording stays short
        /// so it fits the narrow rail.
        /// </summary>
        private void UpdateSeriesSearchSummary(int matchCount)
        {
            if (_seriesSearchSummary == null)
                return;

            int total = _database.SeriesNames.Count;
            string query = _seriesSearchText == null
                ? string.Empty
                : _seriesSearchText.Text.Trim();
            if (query.Length == 0)
            {
                _seriesSearchSummary.Text = string.Format(
                    "共 {0} 个系列", total);
                return;
            }

            _seriesSearchSummary.Text = matchCount == 0
                ? "没有匹配的系列"
                : string.Format("{0} / {1} 个系列", matchCount, total);
        }

        private Panel BuildFilterHost()
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            // The footer grows by one button on the collection card list, so
            // its height is kept in a field instead of being fixed here.
            _filterFooterRow = new RowStyle(SizeType.Absolute, FilterFooterHeight);
            layout.RowStyles.Add(_filterFooterRow);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
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

            _backButton = new LinkLabel();
            _backButton.Text = "←  系列列表";
            _backButton.Dock = DockStyle.Fill;
            _backButton.BackColor = Color.Transparent;
            _backButton.LinkColor = AppTheme.TextSoft;
            _backButton.ActiveLinkColor = Color.White;
            _backButton.VisitedLinkColor = AppTheme.TextSoft;
            _backButton.DisabledLinkColor = AppTheme.TextMuted;
            _backButton.LinkBehavior = LinkBehavior.HoverUnderline;
            _backButton.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _backButton.TextAlign = ContentAlignment.MiddleLeft;
            _backButton.Margin = new Padding(0);
            _backButton.Cursor = Cursors.Hand;
            _backButton.Click += BackButton_Click;
            header.Controls.Add(_backButton, 0, 0);

            _seriesTitleLabel = new Label();
            _seriesTitleLabel.Dock = DockStyle.Fill;
            _seriesTitleLabel.AutoEllipsis = true;
            _seriesTitleLabel.Font = new Font("Microsoft YaHei UI", 13F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _seriesTitleLabel.ForeColor = AppTheme.Text;
            _seriesTitleLabel.BackColor = Color.Transparent;
            _seriesTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _seriesTitleLabel.Margin = new Padding(0);
            header.Controls.Add(_seriesTitleLabel, 0, 1);

            _seriesFolderLabel = new Label();
            _seriesFolderLabel.Dock = DockStyle.Fill;
            _seriesFolderLabel.AutoEllipsis = true;
            _seriesFolderLabel.Font = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _seriesFolderLabel.ForeColor = AppTheme.TextMuted;
            _seriesFolderLabel.BackColor = Color.Transparent;
            _seriesFolderLabel.TextAlign = ContentAlignment.MiddleLeft;
            _seriesFolderLabel.Margin = new Padding(0);
            header.Controls.Add(_seriesFolderLabel, 0, 2);

            layout.Controls.Add(header, 0, 0);

            var separator = new Panel();
            separator.Dock = DockStyle.Fill;
            separator.BackColor = AppTheme.Divider;
            layout.Controls.Add(separator, 0, 1);

            layout.Controls.Add(BuildNameFilterPanel(), 0, 2);

            var separator2 = new Panel();
            separator2.Dock = DockStyle.Fill;
            separator2.BackColor = AppTheme.Divider;
            layout.Controls.Add(separator2, 0, 3);

            _filterBody = new DarkScrollPanel();
            _filterBody.Dock = DockStyle.Fill;
            _filterBody.BackColor = Color.Transparent;
            _filterBody.Margin = new Padding(0);

            _filterStack = new FlowLayoutPanel();
            _filterStack.Dock = DockStyle.None;
            _filterStack.FlowDirection = FlowDirection.TopDown;
            _filterStack.WrapContents = false;
            _filterStack.AutoSize = true;
            _filterStack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _filterStack.BackColor = Color.Transparent;
            _filterStack.Padding = new Padding(14, 10, 14, 16);
            _filterStack.Margin = new Padding(0);
            _filterBody.Content = _filterStack;
            layout.Controls.Add(_filterBody, 0, 4);

            _filterFooter = new FlowLayoutPanel();
            _filterFooter.Dock = DockStyle.Fill;
            _filterFooter.FlowDirection = FlowDirection.TopDown;
            _filterFooter.WrapContents = false;
            _filterFooter.BackColor = Color.Transparent;
            _filterFooter.Padding = new Padding(14, 8, 14, 10);
            _filterFooter.Margin = new Padding(0);
            layout.Controls.Add(_filterFooter, 0, 5);

            BuildFilterControls();
            return host;
        }

        private Control BuildNameFilterPanel()
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 2;
            panel.BackColor = Color.Transparent;
            panel.Padding = new Padding(14, 6, 14, 6);
            panel.Margin = new Padding(0);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var label = new Label();
            label.Text = "卡名搜索";
            label.Dock = DockStyle.Fill;
            label.ForeColor = AppTheme.Text;
            label.BackColor = Color.Transparent;
            label.Font = new Font("Microsoft YaHei UI", 10F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0);
            panel.Controls.Add(label, 0, 0);

            _nameText = new RoundedTextBox();
            _nameText.Dock = DockStyle.Fill;
            _nameText.Margin = new Padding(0);
            _nameText.Input.KeyDown += TextBox_KeyDown;
            panel.Controls.Add(_nameText, 0, 1);

            return panel;
        }


        private void BuildFilterControls()
        {
            var heading = new Label();
            heading.Text = "筛选条件 · 不勾选表示不限";
            heading.Width = 218;
            heading.Height = 24;
            heading.Margin = new Padding(0, 0, 0, 8);
            heading.ForeColor = AppTheme.TextMuted;
            heading.BackColor = Color.Transparent;
            heading.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            heading.TextAlign = ContentAlignment.MiddleLeft;
            _filterStack.Controls.Add(heading);

            _setChecks = CreateFilterSetList();
            _setFilterGroup = AddFilterGroup("套装", _setChecks, 52);

            _sortChecks = CreateFilterChecks(false);
            AddFilterGroup("排序", _sortChecks, 58);

            _levelChecks = CreateFilterChecks(false);
            AddFilterGroup("等级", _levelChecks, 58);

            _costChecks = CreateFilterChecks(false);
            AddFilterGroup("COST", _costChecks, 58);

            _colorChecks = CreateFilterChecks(true);
            AddFilterGroup("颜色", _colorChecks, 84);

            _typeChecks = CreateFilterChecks(false);
            AddFilterGroup("类型", _typeChecks, 58);

            var altPanel = new FlowLayoutPanel();
            altPanel.Dock = DockStyle.Fill;
            altPanel.FlowDirection = FlowDirection.TopDown;
            altPanel.WrapContents = false;
            altPanel.AutoSize = false;
            altPanel.BackColor = Color.Transparent;
            altPanel.Margin = new Padding(0);
            altPanel.Padding = new Padding(0, 2, 0, 0);

            _showAltCheck = CreateFilterCheck("显示异画", null, false);
            _showAltCheck.Width = 218;
            _showAltCheck.Height = 24;
            _showAltCheck.Margin = new Padding(0, 0, 0, 2);
            _showAltCheck.CheckedChanged += ShowAltCheck_CheckedChanged;
            altPanel.Controls.Add(_showAltCheck);

            _onlyAltCheck = CreateFilterCheck("只显示异画", null, false);
            _onlyAltCheck.Width = 218;
            _onlyAltCheck.Height = 24;
            _onlyAltCheck.Margin = new Padding(0);
            _onlyAltCheck.CheckedChanged += OnlyAltCheck_CheckedChanged;
            altPanel.Controls.Add(_onlyAltCheck);

            AddFilterGroup("异画", altPanel, 82);

            // 添加卡片 sits right above 搜索 in the same footer. It is only
            // unfolded on the card level of 我的收藏, where it starts the
            // marking gesture, and turns into 保存 while that mode is on.
            _collectionAddButton = CreateCollectionAddButton();
            _filterFooter.Controls.Add(_collectionAddButton);

            _searchButton = new RoundedButton();
            _searchButton.Text = "搜索";
            _searchButton.Width = 218;
            _searchButton.Height = 34;
            _searchButton.Margin = new Padding(0, 0, 0, 6);
            _searchButton.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _searchButton.Click += SearchButton_Click;
            _filterFooter.Controls.Add(_searchButton);

            _statusLabel = new Label();
            _statusLabel.Text = string.Empty;
            _statusLabel.AutoSize = true;
            _statusLabel.MaximumSize = new Size(218, 0);
            _statusLabel.Margin = new Padding(0, 0, 0, 0);
            _statusLabel.ForeColor = AppTheme.TextMuted;
            _statusLabel.BackColor = Color.Transparent;
            _statusLabel.Font = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _filterFooter.Controls.Add(_statusLabel);
        }


        private TableLayoutPanel AddFilterGroup(string caption, Control control)
        {
            return AddFilterGroup(caption, control, 58);
        }

        private TableLayoutPanel AddFilterGroup(
            string caption,
            Control control,
            int height)
        {
            var group = new TableLayoutPanel();
            group.Width = 218;
            group.Height = height;
            group.ColumnCount = 1;
            group.RowCount = 2;
            group.BackColor = Color.Transparent;
            group.Margin = new Padding(0, 0, 0, 9);
            group.Padding = new Padding(0);
            group.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
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
            _filterStack.Controls.Add(group);
            return group;
        }

        private static TableLayoutPanel CreateFilterSetList()
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 1;
            panel.Width = 218;
            panel.BackColor = Color.Transparent;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(0);
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return panel;
        }

        private static FlowLayoutPanel CreateFilterChecks(bool wrapContents)
        {
            var panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.LeftToRight;
            panel.WrapContents = wrapContents;
            panel.AutoScroll = false;
            panel.BackColor = Color.Transparent;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(0, 2, 0, 0);
            return panel;
        }

        private static FilterCheckBox CreateFilterCheck(
            string text,
            object tag,
            bool wrap)
        {
            var check = new FilterCheckBox();
            check.Text = text;
            check.Tag = tag;
            check.AutoSize = false;
            check.BackColor = Color.Transparent;
            check.ForeColor = AppTheme.Text;
            if (!wrap)
                check.Height = 24;
            return check;
        }

        private static FilterRadioButton CreateSortOption(string text, int mode)
        {
            var option = new FilterRadioButton();
            option.Text = text;
            option.Tag = mode;
            option.AutoSize = false;
            option.Width = 32;
            option.Height = 24;
            option.Margin = new Padding(0, 0, 8, 2);
            option.BackColor = Color.Transparent;
            option.ForeColor = AppTheme.Text;
            return option;
        }


        private Control BuildContentPanel()
        {
            _contentHost = new Panel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = AppTheme.MainBackground;
            _contentHost.Padding = new Padding(8, 8, 8, 8);

            _browsePage = BuildBrowsePage();
            _contentHost.Controls.Add(_browsePage);

            _settingsPage = BuildSettingsPage();
            _settingsPage.Visible = false;
            _contentHost.Controls.Add(_settingsPage);

            // 编辑卡组 reuses the browse page: its series picker is the cover
            // grid and its card level is the filter column plus the card grid.
            _deckEditPage = BuildDeckEditPage();
            _deckEditPage.Visible = false;
            _contentHost.Controls.Add(_deckEditPage);

            return _contentHost;
        }

        private Panel BuildBrowsePage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.MainBackground;

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.BackColor = AppTheme.MainBackground;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // The 编辑卡组 tray lives in the third row and is folded to zero
            // height on every other page that shares this layout.
            _newDeckRowStyle = new RowStyle(SizeType.Absolute, 0F);
            layout.RowStyles.Add(_newDeckRowStyle);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            layout.Controls.Add(CreatePageHeader("系列卡表", string.Empty,
                out _contentTitle, out _contentSubtitle), 0, 0);

            // The grid scrolls inside a host that paints the slim themed bar,
            // exactly like the deck overview: a native WinForms scrollbar here
            // would be twice as wide and take the system colours instead.
            _tileScroll = new DarkScrollPanel();
            _tileScroll.Dock = DockStyle.Fill;
            _tileScroll.BackColor = AppTheme.MainBackground;
            _tileScroll.Margin = new Padding(0);
            _tileScroll.ScrollbarReserve = GridScrollbarGutter;
            _tileScroll.Resize += TileScroll_Resize;

            _tileGrid = new TileGridControl();
            // The page around the grid scrolls for it, so the grid reports the
            // height its tiles need instead of keeping a scrollbar of its own.
            _tileGrid.ExpandToContent = true;
            _tileGrid.Thumbnails = _thumbnails;
            _tileGrid.BackColor = AppTheme.MainBackground;
            _tileGrid.Margin = new Padding(0);
            _tileGrid.ItemClick += TileGrid_ItemClick;
            _tileGrid.ItemLongPress += TileGrid_ItemLongPress;
            _tileGrid.ItemRightClick += TileGrid_ItemRightClick;
            _tileGrid.ScrollToTopRequested += TileGrid_ScrollToTopRequested;

            // 我的收藏 adds its own meaning to the same grid (mark instead of
            // details) without changing the handlers of the other pages.
            _tileGrid.ItemClick += TileGrid_CollectionItemClick;
            _tileGrid.ItemLongPress += TileGrid_CollectionItemLongPress;
            _tileGrid.ItemRightClick += TileGrid_CollectionItemRightClick;
            _tileScroll.Content = _tileGrid;
            layout.Controls.Add(_tileScroll, 0, 1);

            layout.Controls.Add(BuildNewDeckStrip(), 0, 2);

            page.Controls.Add(layout);
            return page;
        }

        /// <summary>
        /// Keeps the grid's idea of the page viewport in sync: an empty grid
        /// takes that height so its "没有找到" hint stays centered.
        /// </summary>
        private void TileScroll_Resize(object sender, EventArgs e)
        {
            if (_tileGrid == null || _tileScroll == null)
                return;

            _tileGrid.PageViewportHeight = _tileScroll.ClientSize.Height;
        }

        /// <summary>
        /// The grid asks for the top after a new result set; it cannot scroll
        /// itself because the host owns the bar.
        /// </summary>
        private void TileGrid_ScrollToTopRequested(object sender, EventArgs e)
        {
            if (_tileScroll != null)
                _tileScroll.ScrollToTop();
        }

        /// <summary>
        /// Builds the title / subtitle header shared by every content page.
        /// </summary>
        private static TableLayoutPanel CreatePageHeader(string title,
            string subtitle, out Label titleLabel, out Label subtitleLabel)
        {
            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 1;
            header.RowCount = 2;
            header.BackColor = AppTheme.MainBackground;
            header.Margin = new Padding(0);
            header.Padding = new Padding(0);
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            titleLabel = new Label();
            titleLabel.Text = title ?? string.Empty;
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.ForeColor = AppTheme.Text;
            titleLabel.Font = new Font("Microsoft YaHei UI", 16F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Margin = new Padding(0);
            header.Controls.Add(titleLabel, 0, 0);

            subtitleLabel = new Label();
            subtitleLabel.Text = subtitle ?? string.Empty;
            subtitleLabel.Dock = DockStyle.Fill;
            subtitleLabel.AutoEllipsis = true;
            subtitleLabel.ForeColor = AppTheme.TextMuted;
            subtitleLabel.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            subtitleLabel.Margin = new Padding(0);
            header.Controls.Add(subtitleLabel, 0, 1);

            return header;
        }

        /// <summary>
        /// Shows one content page and hides the others, so new pages only need
        /// their own panel added to <see cref="_contentHost"/>.
        /// </summary>
        private void ShowContentPage(Control page)
        {
            if (_contentHost == null || page == null)
                return;

            for (int i = 0; i < _contentHost.Controls.Count; i++)
            {
                Control child = _contentHost.Controls[i];
                child.Visible = ReferenceEquals(child, page);
            }

            page.BringToFront();

            // The settings category list shares the second level column of the
            // series filter, so every other page folds it away again.
            if (_settingsSidebarHost != null)
            {
                bool settingsActive = page == _settingsPage;
                if (_settingsSidebarHost.Visible != settingsActive)
                    _settingsSidebarHost.Visible = settingsActive;
            }
        }


        private void ShowSeriesList()
        {
            _viewMode = ViewMode.SeriesList;
            _activeSeries = null;
            _results.Clear();

            CloseDeckDetail();
            _navColumnStyle.Width = NavExpandedWidth;
            _filterColumnStyle.Width = 0F;
            _filterHost.Visible = false;
            _filterBody.ScrollToTop();
            SetNavExpanded(true);
            SetDeckSubItemsVisible(false);
            ShowContentPage(_browsePage);
            SetActiveNavItem(_navSeriesButton);
            UpdateNewDeckStripVisibility();

            // Leaving the collection discards unsaved marks and folds the
            // collection-only sidebar pieces away again.
            ExitCollectionMarkMode();
            UpdateCollectionChrome();

            _contentTitle.Text = "系列卡表";
            _contentSubtitle.Text = string.Format(
                "共 {0} 个系列 · 单击封面进入卡片列表",
                _database.SeriesNames.Count);

            _tileGrid.ConfigureSeries();
            RefreshSeriesItems();
        }

        /// <summary>
        /// Moves the caret into the sidebar search box. The focus change is
        /// queued so it runs after the click that asked for it has finished
        /// re-expanding the rail and laying the box out again.
        /// </summary>
        private void FocusSeriesSearchBox()
        {
            if (_seriesSearchText == null || !IsHandleCreated)
                return;

            BeginInvoke((Action)delegate
            {
                if (_seriesSearchText == null || !_seriesSearchText.Visible)
                    return;

                _seriesSearchText.Input.Focus();
                _seriesSearchText.Input.SelectAll();
            });
        }

        private void RefreshSeriesItems()
        {
            IList<SeriesDefinition> matches = SearchSeries();
            UpdateSeriesSearchSummary(matches.Count);

            if (_tileGrid == null || !IsSeriesOverviewVisible)
                return;

            bool collection = _viewMode == ViewMode.CollectionList;
            bool onlyOwned = collection && IsOnlyOwnedChecked;
            _tileGrid.EmptyText = collection
                ? (onlyOwned ? "还没有拥有的系列" : "没有找到系列")
                : "没有找到系列";
            _tileGrid.SetItems(BuildSeriesItems(matches));

            if (collection)
            {
                int ownedTypes;
                int ownedCopies;
                _collection.GetTotals(out ownedTypes, out ownedCopies);
                string countText = matches.Count == _database.SeriesNames.Count
                    ? string.Format("共 {0} 个系列", matches.Count)
                    : string.Format("找到 {0} 个系列（共 {1} 个）",
                        matches.Count, _database.SeriesNames.Count);
                _contentSubtitle.Text = string.Format(
                    "{0} · 已拥有 {1} 种 / {2} 张 · 单击封面查看",
                    countText, ownedTypes, ownedCopies);
                return;
            }

            bool deckPicker = _viewMode == ViewMode.DeckNew;
            bool cardEdit = _viewMode == ViewMode.CardEditList;
            string hint = deckPicker
                ? "选择系列后浏览卡片"
                : cardEdit
                    ? "选择系列后编辑卡片"
                    : "单击封面进入卡片列表";
            if (matches.Count == _database.SeriesNames.Count)
            {
                _contentSubtitle.Text = string.Format(
                    "共 {0} 个系列 · {1}", matches.Count, hint);
            }
            else
            {
                _contentSubtitle.Text = string.Format(
                    "找到 {0} 个系列（共 {1} 个） · {2}",
                    matches.Count, _database.SeriesNames.Count, hint);
            }
        }

        /// <summary>
        /// Applies the sidebar query and sort order to the series catalog. The
        /// matching rules stay in <see cref="SeriesSearchService"/>, so the
        /// search behaves exactly like the old sidebar search.
        /// </summary>
        private IList<SeriesDefinition> SearchSeries()
        {
            string query = _seriesSearchText == null
                ? string.Empty
                : _seriesSearchText.Text;
            var matches = new List<SeriesDefinition>();
            for (int i = 0; i < _database.SeriesNames.Count; i++)
            {
                SeriesDefinition series = _database.GetSeriesMetadata(
                    _database.SeriesNames[i]);
                if (series == null || !SeriesSearchService.Matches(series, query))
                    continue;
                matches.Add(series);
            }

            if (_seriesSortMode == SeriesSortMode.Abbreviation)
                matches.Sort(SeriesSearchService.CompareByAbbreviation);
            else
                matches.Sort(SeriesSearchService.CompareByName);

            // 只显示拥有 hides whole series without a single owned card. The
            // pack files are read lazily and cached by the store, so typing in
            // the search box only pays for the series that actually have data.
            if (_viewMode == ViewMode.CollectionList && IsOnlyOwnedChecked)
            {
                matches.RemoveAll(delegate(SeriesDefinition series)
                {
                    return !_collection.HasAnyOwned(series);
                });
            }
            return matches;
        }


        private List<TileGridItem> BuildSeriesItems(
            IList<SeriesDefinition> seriesList)
        {
            var items = new List<TileGridItem>();
            for (int i = 0; i < seriesList.Count; i++)
            {
                SeriesDefinition captured = seriesList[i];
                string abbreviation =
                    SeriesSearchService.FormatAbbreviations(captured);
                if (string.IsNullOrEmpty(abbreviation))
                    abbreviation = captured.FolderName ?? string.Empty;

                var item = new TileGridItem();
                item.Title = captured.Name;
                item.Subtitle = abbreviation;
                item.PreferredHeight =
                    _tileGrid.MeasureSeriesItemHeight(abbreviation);
                item.ImagePathProvider = delegate
                {
                    return _coverLocator.GetCoverPath(captured);
                };
                item.Tag = captured;

                // On the collection page a series without a single owned card
                // is drawn grey, exactly like an unowned card in its list.
                if (_viewMode == ViewMode.CollectionList)
                    item.Dimmed = !_collection.HasAnyOwned(captured);

                items.Add(item);
            }
            return items;
        }

        private void OpenSeries(SeriesDefinition metadata)
        {
            if (metadata == null)
                return;

            // The overview doubles as the series picker of the deck editor and
            // as the first level of 我的收藏, so the opened card list keeps the
            // page it was started from.
            bool deckPicker = _viewMode == ViewMode.DeckNew;
            bool collection = _viewMode == ViewMode.CollectionList;
            bool cardEdit = _viewMode == ViewMode.CardEditList;

            CloseDeckDetail();

            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                SeriesDefinition series = _database.GetSeries(metadata.Name);
                if (series == null)
                {
                    MessageBox.Show(this, "系列未找到：" + metadata.Name,
                        "打开系列", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _activeSeries = series;
                _viewMode = deckPicker
                    ? ViewMode.DeckNew
                    : collection
                        ? ViewMode.CollectionCards
                        : cardEdit
                            ? ViewMode.CardEditCards
                            : ViewMode.CardList;

                // Opening another series always starts outside the marking
                // mode; unsaved marks of the previous series are dropped.
                ExitCollectionMarkMode();

                // 编辑卡组 keeps one tray per series: reopening a series brings
                // back the cards it stashed, another series starts empty.
                if (deckPicker)
                    BeginDeckDraftForSeries(series);

                _navColumnStyle.Width = NavCompactWidth;
                _filterColumnStyle.Width = 250F;
                _filterHost.Visible = true;
                SetNavExpanded(false);
                SetDeckSubItemsVisible(false);
                ShowContentPage(_browsePage);
                // The card list of 写卡 folds its sub entries away (see above),
                // so the section label carries the highlight there: a hidden
                // sub entry cannot show the selected box.
                SetActiveNavItem(deckPicker
                    ? _navDeckButton
                    : collection
                        ? _navCollectionButton
                        : cardEdit
                            ? _navCardEditButton
                            : _navSeriesButton);
                ScheduleDarkChrome();

                string folderName = string.IsNullOrEmpty(series.FolderName)
                    ? series.Name
                    : series.FolderName;
                string abbreviation = series.Abbreviation;
                if (string.IsNullOrEmpty(abbreviation))
                    abbreviation = folderName;
                _backButton.Text = deckPicker || cardEdit
                    ? "←  选择系列"
                    : collection
                        ? "←  我的收藏"
                        : "←  系列列表";
                _seriesTitleLabel.Text = series.Name;
                _seriesFolderLabel.Text =
                    SeriesSearchService.FormatAbbreviations(series);
                _contentTitle.Text = deckPicker
                    ? "编辑卡组"
                    : cardEdit
                        ? (_cardEditIntent == CardEditIntent.AltArt
                            ? "添加异画"
                            : "修改卡片")
                        : series.Name;
                _contentSubtitle.Text = folderName + " · 正在加载...";

                PopulateSetFilter();
                ResetFilters();
                _filterBody.ScrollToTop();
                _tileGrid.ConfigureCards();
                RunSearch();
                UpdateNewDeckStripVisibility();
                UpdateCollectionChrome();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开系列失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowSeriesList();
            }
            finally
            {
                Cursor = previousCursor;
            }
        }

        private void SetNavExpanded(bool expanded)
        {
            _navExpanded = expanded;
            _navTitle.Text = expanded ? "Weiss\nSchwarz" : "WS";
            _navTitle.Font = expanded ? _navTitleFont : _navTitleCompactFont;
            _navFooter.Visible = expanded;

            // The series search only exists while the rail shows its labels;
            // the compact rail has no room for the input and folds it to zero
            // height so the page buttons move up next to the brand.
            if (_navSearchHost != null && _navSearchHost.Visible != expanded)
                _navSearchHost.Visible = expanded;
            if (_navSearchRow != null)
            {
                float searchHeight = expanded ? NavSearchRowHeight : 0F;
                if (_navSearchRow.Height != searchHeight)
                    _navSearchRow.Height = searchHeight;
            }

            // The compact rail trades the search block for a single 搜索 entry
            // at the top of the page stack, so the search stays one click away
            // even while the labels are hidden.
            SetNavItemRowVisible(_navSearchButton, !expanded);

            for (int i = 0; i < _navItems.Count; i++)
            {
                NavItem item = _navItems[i];
                item.Button.Text = expanded ? item.Text : item.CompactText;
                item.Button.TextAlign = expanded
                    ? ContentAlignment.MiddleLeft
                    : ContentAlignment.MiddleCenter;
                item.Button.Font = item.IsSubItem ? _navSubItemFont : _navItemFont;
                item.Button.Padding = expanded
                    ? new Padding(item.Indent, 0, 0, 0)
                    : new Padding(0);
            }

            if (_navSettingsButton != null)
            {
                _navSettingsButton.ShowText = expanded;
                _navSettingsButton.TextAlign = expanded
                    ? ContentAlignment.MiddleLeft
                    : ContentAlignment.MiddleCenter;
                _navSettingsButton.Padding = expanded
                    ? new Padding(10, 0, 0, 0)
                    : new Padding(0);
                _navSettingsButton.Invalidate();
            }

            // The launch entry folds the same way: icon only at 72px.
            if (_navLaunchGameButton != null)
            {
                _navLaunchGameButton.ShowText = expanded;
                _navLaunchGameButton.TextAlign = expanded
                    ? ContentAlignment.MiddleLeft
                    : ContentAlignment.MiddleCenter;
                _navLaunchGameButton.Padding = expanded
                    ? new Padding(10, 0, 0, 0)
                    : new Padding(0);
                _navLaunchGameButton.Invalidate();
            }

            UpdateNavLayoutMetrics();

            if (_navLayout != null)
                _navLayout.PerformLayout();

            // The 只显示拥有 row only fits the expanded rail of 我的收藏.
            UpdateCollectionChrome();
        }

        /// <summary>
        /// True while the series cover grid is the visible body: the 系列卡表
        /// page, 我的收藏 (whose first level is the same cover grid) or the
        /// series picker of the 编辑卡组 page.
        /// </summary>
        private bool IsSeriesOverviewVisible
        {
            get
            {
                return _viewMode == ViewMode.SeriesList ||
                    _viewMode == ViewMode.CollectionList ||
                    _viewMode == ViewMode.CardEditList ||
                    (_viewMode == ViewMode.DeckNew && _activeSeries == null);
            }
        }

        /// <summary>
        /// The 编辑卡组 / 查看卡组 sub entries belong to the 卡组编辑 section and
        /// collapse (row height 0) whenever another top level page is active.
        /// </summary>
        private void SetDeckSubItemsVisible(bool visible)
        {
            SetNavSubItemsVisible(visible ? NavSection.Deck : NavSection.None);
        }

        /// <summary>
        /// Unfolds the sub entries of one section and folds every other one, so
        /// the two sections that own sub entries (卡组编辑 and 编辑卡片) are never
        /// unfolded at the same time. <see cref="NavSection.None"/> folds all.
        /// </summary>
        private void SetNavSubItemsVisible(NavSection section)
        {
            for (int i = 0; i < _navItems.Count; i++)
            {
                NavItem item = _navItems[i];
                if (!item.IsSubItem)
                    continue;

                bool visible = section != NavSection.None &&
                    item.Section == section;

                if (item.Row != null)
                    item.Row.Height = visible ? NavSubItemRowHeight : 0F;
                if (item.Button.Visible != visible)
                    item.Button.Visible = visible;
            }

            UpdateNavLayoutMetrics();
            if (_navItemsLayout != null)
                _navItemsLayout.PerformLayout();
            RefreshNavPaint();
        }

        /// <summary>
        /// Shows or hides one page entry by folding its row to zero height. The
        /// row itself stays registered so the indices of the other entries and
        /// the row metrics keep working when the entry comes back.
        /// </summary>
        private void SetNavItemRowVisible(RoundedButton button, bool visible)
        {
            if (button == null)
                return;

            for (int i = 0; i < _navItems.Count; i++)
            {
                NavItem item = _navItems[i];
                if (!ReferenceEquals(item.Button, button))
                    continue;

                if (item.Row != null)
                {
                    item.Row.Height = visible
                        ? (item.IsSubItem
                            ? NavSubItemRowHeight
                            : NavItemRowHeight)
                        : 0F;
                }

                if (item.Button.Visible != visible)
                    item.Button.Visible = visible;
                break;
            }

            UpdateNavLayoutMetrics();
        }


        private void PopulateFixedFilters()
        {
            _populatingCombos = true;
            try
            {
                AddFilterChecks(_levelChecks,
                    new FilterOption("0", "0"),
                    new FilterOption("1", "1"),
                    new FilterOption("2", "2"),
                    new FilterOption("3", "3"));
                AddFilterChecks(_costChecks,
                    new FilterOption("0", "0"),
                    new FilterOption("1", "1"),
                    new FilterOption("2", "2"),
                    new FilterOption("3+", "3+"));
                AddFilterChecks(_colorChecks,
                    new FilterOption("黄", "Yellow"),
                    new FilterOption("绿", "Green"),
                    new FilterOption("红", "Red"),
                    new FilterOption("蓝", "Blue"),
                    new FilterOption("紫", "Purple"),
                    new FilterOption("未定义", "Undefined"));
                AddFilterChecks(_typeChecks,
                    new FilterOption("角色", "Character"),
                    new FilterOption("事件", "Event"),
                    new FilterOption("高潮", "Climax"));
                AddSortOptions();
            }
            finally
            {
                _populatingCombos = false;
            }
        }

        private void AddSortOptions()
        {
            _sortChecks.Controls.Clear();
            AddSortOption("默认", 0);
            AddSortOption("卡牌编号", 1);
            AddSortOption("卡名", 2);
            if (_sortChecks.Controls.Count > 0)
            {
                RadioButton first = _sortChecks.Controls[0] as RadioButton;
                if (first != null)
                    first.Checked = true;
            }
        }

        private void AddSortOption(string text, int mode)
        {
            FilterRadioButton option = CreateSortOption(text, mode);
            option.CheckedChanged += SortOption_CheckedChanged;
            _sortChecks.Controls.Add(option);
            option.Width = option.MeasureContentWidth();
            _sortChecks.PerformLayout();
        }

        private void AddSeriesSortOption(string text, SeriesSortMode mode)
        {
            FilterRadioButton option = CreateSortOption(text, (int)mode);
            option.Margin = new Padding(0, 0, 10, 0);
            option.CheckedChanged += SeriesSortOption_CheckedChanged;
            _seriesSortChecks.Controls.Add(option);
            option.Width = option.MeasureContentWidth();
            _seriesSortChecks.PerformLayout();
        }

        private void PopulateSetFilter()
        {
            IList<string> setNames = _activeSeries == null
                ? new List<string>()
                : _database.GetSetNames(_activeSeries.Name);

            _populatingCombos = true;
            try
            {
                _setChecks.SuspendLayout();
                _setChecks.Controls.Clear();
                _setChecks.RowStyles.Clear();

                int totalHeight = 0;
                int rowCount = Math.Max(1, setNames.Count);
                _setChecks.RowCount = rowCount;
                for (int i = 0; i < setNames.Count; i++)
                {
                    FilterCheckBox check = CreateFilterCheck(
                        setNames[i], setNames[i], true);
                    check.Margin = new Padding(0, 0, 0, 2);
                    check.Width = 218;
                    check.CheckedChanged += FilterCheck_CheckedChanged;
                    _setChecks.Controls.Add(check, 0, i);

                    int height = check.MeasureContentHeight(218);
                    check.Height = height;
                    totalHeight += height + 2;
                    _setChecks.RowStyles.Add(
                        new RowStyle(SizeType.Absolute, height + 2));
                }

                if (setNames.Count == 0)
                {
                    _setChecks.RowStyles.Add(
                        new RowStyle(SizeType.Absolute, 0F));
                }

                _setChecks.Height = totalHeight;
                _setChecks.ResumeLayout(true);

                if (_setFilterGroup != null)
                {
                    _setFilterGroup.Height = 26 + totalHeight + 2;
                    _filterStack.PerformLayout();
                    ScheduleDarkChrome();
                }
            }
            finally
            {
                _populatingCombos = false;
            }
        }

        private void ResetFilters()
        {
            _populatingCombos = true;
            try
            {
                ClearFilterChecks(_setChecks);
                ClearFilterChecks(_levelChecks);
                ClearFilterChecks(_costChecks);
                ClearFilterChecks(_colorChecks);
                ClearFilterChecks(_typeChecks);
                _sortMode = 0;
                if (_sortChecks.Controls.Count > 0)
                {
                    RadioButton first =
                        _sortChecks.Controls[0] as RadioButton;
                    if (first != null)
                        first.Checked = true;
                }
                _nameText.Text = string.Empty;
                _showAltCheck.Checked = false;
                _onlyAltCheck.Checked = false;
                _statusLabel.Text = string.Empty;
            }
            finally
            {
                _populatingCombos = false;
            }
        }

        private void AddFilterChecks(
            FlowLayoutPanel panel,
            params FilterOption[] options)
        {
            panel.Controls.Clear();
            for (int i = 0; i < options.Length; i++)
            {
                FilterOption option = options[i];
                FilterCheckBox check = CreateFilterCheck(
                    option.Text, option.Value, false);
                check.Margin = new Padding(0, 2, 8, 2);
                check.CheckedChanged += FilterCheck_CheckedChanged;
                panel.Controls.Add(check);
                check.Width = check.MeasureContentWidth();
            }
            panel.PerformLayout();
        }

        private static void ClearFilterChecks(Control container)
        {
            for (int i = 0; i < container.Controls.Count; i++)
            {
                CheckBox check = container.Controls[i] as CheckBox;
                if (check != null)
                    check.Checked = false;
            }
        }

        private sealed class NavItem
        {
            public string Text { get; set; }
            public string CompactText { get; set; }
            public RoundedButton Button { get; set; }

            /// <summary>Sub entries are indented below their parent page.</summary>
            public bool IsSubItem { get; set; }
            public int Indent { get; set; }

            /// <summary>Section the sub entry unfolds with; None for top pages.</summary>
            public NavSection Section { get; set; }

            /// <summary>Row inside the scrolling item stack, used to collapse it.</summary>
            public RowStyle Row { get; set; }
        }

        private sealed class FilterOption
        {
            public string Text { get; private set; }
            public string Value { get; private set; }

            public FilterOption(string text, string value)
            {
                Text = text;
                Value = value;
            }

            public override string ToString()
            {
                return Text;
            }
        }

        /// <summary>
        /// Windows delivers the wheel to the focused control, so after clicking
        /// a sidebar page the next gesture over the list still goes to that
        /// button and the page looks like it cannot scroll. Hand the notch to
        /// the scroll host the pointer actually sits on and let the focused
        /// control have it only when the host under the pointer cannot move.
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (e != null && e.Delta != 0)
            {
                DarkScrollPanel host = FindScrollHost(Cursor.Position);
                if (host != null && host.ScrollByWheel(e.Delta))
                    return;
            }

            base.OnMouseWheel(e);
        }

        /// <summary>
        /// Walks the control tree down from the pointer and then back up to the
        /// first <see cref="DarkScrollPanel"/> so wheel gestures reach the page
        /// that is under the cursor.
        /// </summary>
        private DarkScrollPanel FindScrollHost(Point screenPoint)
        {
            Control current = this;
            while (current != null)
            {
                Point local = current.PointToClient(screenPoint);
                Control child = current.GetChildAtPoint(local,
                    GetChildAtPointSkip.Invisible | GetChildAtPointSkip.Disabled);
                if (child == null)
                    break;
                current = child;
            }

            while (current != null && !(current is DarkScrollPanel))
                current = current.Parent;
            return current as DarkScrollPanel;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_deckImportOverlay != null)
                _deckImportOverlay.Clear();
            _thumbnails.Dispose();
            _detailThumbnails.Dispose();
            _navTitleFont.Dispose();
            _navTitleCompactFont.Dispose();
            _navItemFont.Dispose();
            _navSubItemFont.Dispose();
            _settingsTitleFont.Dispose();
            _settingsHintFont.Dispose();

            _navToolTip.Dispose();
            base.OnFormClosed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape &&
                _deckImportOverlay != null &&
                _deckImportOverlay.Visible)
            {
                // The layer handles Escape itself while it has the focus; when
                // the key reaches the window instead it still has to step back
                // from the pasted block before the layer is taken down.
                _deckImportOverlay.HandleEscape();
                return true;
            }

            if (keyData == Keys.Escape &&
                _cardDetailOverlay != null &&
                _cardDetailOverlay.Visible)
            {
                HideCardDetail();
                return true;
            }

            if (keyData == Keys.Escape &&
                _viewMode == ViewMode.DeckEdit &&
                _deckDetailPanel != null &&
                _deckDetailPanel.Visible)
            {
                // A key that means "back out" never deletes a deck: while 删除
                // is red the first Escape only takes that state off, the next
                // one leaves the page.
                if (_deckDeleteArmed)
                {
                    DisarmDeckDelete();
                    return true;
                }

                DeckDetailBack_Click(null, EventArgs.Empty);
                return true;
            }

            if (keyData == Keys.Escape && _viewMode == ViewMode.CardEditor)
            {
                // The editor page handles Esc itself; this is the fallback for
                // the case where the window and not the page holds the focus.
                CancelCardEditor();
                return true;
            }

            if (keyData == Keys.Escape && _viewMode == ViewMode.DeckNew)
            {
                // Inside the deck editor Esc first returns to its series
                // picker, then leaves the editor altogether.
                if (_activeSeries != null)
                    ShowDeckNew();
                else
                    ShowSeriesList();
                return true;
            }

            if (keyData == Keys.Escape &&
                (_viewMode == ViewMode.CollectionCards ||
                 _viewMode == ViewMode.CollectionList))
            {
                // Unsaved marks are dropped first, exactly like the red delete
                // state of the deck page: the next Esc leaves the series.
                if (_collectionMarkMode)
                {
                    ExitCollectionMarkMode();
                    RunSearch();
                    return true;
                }

                if (_viewMode == ViewMode.CollectionCards)
                    ShowCollection();
                else
                    ShowSeriesList();
                return true;
            }

            if (keyData == Keys.Escape &&
                (_viewMode == ViewMode.Settings ||
                 _viewMode == ViewMode.DeckEdit))
            {
                ShowSeriesList();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkModeHelper.Apply(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyDarkChrome();
            ScheduleDarkChrome();

            RefreshNavPaint();

            if (_tileGrid != null && _tileGrid.CanFocus)
                ActiveControl = _tileGrid;

            if (_viewMode == ViewMode.SeriesList)
                _tileGrid.ScrollToTop();
        }

        /// <summary>
        /// Repaints the whole sidebar including every child window. Rounded
        /// buttons keep no opaque background of their own, so a full repaint
        /// is required to clear pixels left behind by earlier layout passes.
        /// </summary>
        private void RefreshNavPaint()
        {
            if (_navLayout == null)
                return;

            InvalidateTree(_navLayout);
            Control parent = _navLayout.Parent;
            if (parent != null)
                InvalidateTree(parent);
        }

        private static void InvalidateTree(Control control)
        {
            if (control == null)
                return;

            control.Invalidate();
            control.Update();
            for (int i = 0; i < control.Controls.Count; i++)
                InvalidateTree(control.Controls[i]);
        }

        private void ScheduleDarkChrome()
        {
            if (!IsHandleCreated)
                return;
            BeginInvoke(new Action(ApplyDarkChrome));
        }

        private void ApplyDarkChrome()
        {
            DarkModeHelper.Apply(Handle);
            if (_tileGrid != null && _tileGrid.IsHandleCreated)
                DarkModeHelper.Apply(_tileGrid.Handle);
            if (_deckLocalGrid != null && _deckLocalGrid.IsHandleCreated)
                DarkModeHelper.Apply(_deckLocalGrid.Handle);
            if (_deckDefaultGrid != null && _deckDefaultGrid.IsHandleCreated)
                DarkModeHelper.Apply(_deckDefaultGrid.Handle);
            if (_deckCards != null && _deckCards.IsHandleCreated)
                DarkModeHelper.Apply(_deckCards.Handle);
            if (_filterBody != null && _filterBody.IsHandleCreated)
                DarkModeHelper.Apply(_filterBody.Handle);
            if (_filterStack != null && _filterStack.IsHandleCreated)
                DarkModeHelper.Apply(_filterStack.Handle);
        }
    }
}

