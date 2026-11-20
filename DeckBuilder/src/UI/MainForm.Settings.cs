using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    public sealed partial class MainForm
    {
        /// <summary>
        /// Width of the settings category column. It matches the second level
        /// sidebar of the series card list and of the deck detail page, so all
        /// three pages line up in the same root column.
        /// </summary>
        private const float SettingsSidebarWidth = 250F;

        private const int SettingsSidebarHeaderHeight = 104;
        private const int SettingsHeaderRowHeight = 30;
        private const int SettingsTitleRowHeight = 28;
        private const int SettingsSubtitleRowHeight = 22;
        private const int SettingsSidebarFooterHeight = 62;
        private const int SettingsCategoryCaptionHeight = 28;
        private const int SettingsCategoryRowHeight = 40;
        private const int SettingsCategoryWidth = 218;

        /// <summary>
        /// The body is one long scroll: the four categories are stacked top to
        /// bottom, a section header is what the sidebar jumps to, and a single
        /// setting is a title line over a hint line. Every height is absolute,
        /// so the list reports its own size without measuring any text.
        /// </summary>
        private const int SettingsSectionHeaderHeight = 62;
        private const int SettingsItemTitleHeight = 24;
        private const int SettingsItemHintHeight = 34;
        private const int SettingsItemHeight =
            SettingsItemTitleHeight + SettingsItemHintHeight;
        private const int SettingsCardPackHintHeight = 40;
        private const int SettingsCardPackButtonHeight = 36;
        private const int SettingsCardPackItemHeight =
            SettingsItemTitleHeight + SettingsCardPackHintHeight +
            SettingsCardPackButtonHeight;

        /// <summary>
        /// Height of the 主题 row: the title, the hint and the two buttons that
        /// pick the palette. Same metrics as the card pack row, so 外观 and
        /// 存储 line up.
        /// </summary>
        private const int SettingsThemeItemHeight =
            SettingsItemTitleHeight + SettingsItemHintHeight +
            SettingsCardPackButtonHeight;

        /// <summary>
        /// Hint under 主题. It is put back after a successful save, so a failed
        /// write does not leave its error sitting on the line for good.
        /// </summary>
        private const string ThemeHintText =
            "切换后立即生效，并写入 DeckBuilder\\settings.txt 供下次启动沿用；" +
            "删掉该文件即恢复深色主题。";
        private const int SettingsNoteHeight = 26;
        private const int SettingsSectionGapHeight = 20;
        private const int SettingsScrollbarReserve = 12;

        /// <summary>
        /// A jump leaves this many pixels above the section header, so the
        /// header does not sit flush against the top of the viewport.
        /// </summary>
        private const int SettingsScrollAnchorBias = 8;

        /// <summary>
        /// Index of the 存储 category. It is the only category with a working
        /// action so far: installing a card pack archive.
        /// </summary>
        private const int CardPackSettingsCategory = 2;

        /// <summary>
        /// Index of the 术语 category. It holds the editable 触发名称 table, the
        /// only working option outside 存储.
        /// </summary>
        private const int TerminologySettingsCategory = 1;

        /// <summary>
        /// Index of the 外观 category. It holds the only setting that writes to
        /// settings.txt: the 深色 / 浅色 theme switch.
        /// </summary>
        private const int AppearanceSettingsCategory = 0;

        // Category order of the second level settings sidebar. The buttons and
        // the body pages are built from these arrays, so adding a category
        // means one title, one subtitle and its placeholder cards below.
        private static readonly string[] SettingsCategoryTitles =
        {
            "外观", "术语", "存储", "软件信息"
        };

        private static readonly string[] SettingsCategorySubtitles =
        {
            "主题、界面密度与字号等外观选项",
            "卡牌术语与关键词的显示方式",
            "数据目录、缓存与卡组草稿",
            "版本、数据来源与许可信息"
        };

        private Panel _settingsSidebarHost;
        private DarkScrollPanel _settingsScroll;
        private TableLayoutPanel _settingsContent;
        private int _settingsContentHeight;
        private int[] _settingsSectionTops;
        private int _settingsScrollTarget;
        private bool _settingsScrollPending;
        private List<RoundedButton> _settingsCategoryButtons;
        private int _settingsCategory;

        /// <summary>The two 深色 / 浅色 buttons of the 主题 setting.</summary>
        private RoundedButton _themeDarkButton;
        private RoundedButton _themeLightButton;

        /// <summary>
        /// Hint line of the 主题 setting. It is reused to print the reason when
        /// the settings file cannot be written.
        /// </summary>
        private Label _themeHintLabel;


        /// <summary>
        /// Settings entry point: the main rail folds to its compact form and
        /// the category list takes over the second level column, mirroring the
        /// card list and deck pages. The body shows the category that was open
        /// last, so returning to 设置 keeps the previous place.
        /// </summary>
        private void ShowSettings()
        {
            _viewMode = ViewMode.Settings;

            // CloseDeckDetail restores the expanded rail and folds the filter
            // column, so it runs before the settings layout is applied.
            CloseDeckDetail();

            _navColumnStyle.Width = NavCompactWidth;
            _filterColumnStyle.Width = SettingsSidebarWidth;
            _filterHost.Visible = false;
            SetNavExpanded(false);
            SetDeckSubItemsVisible(false);
            SetActiveNavItem(null);

            if (_settingsSidebarHost != null)
            {
                _settingsSidebarHost.Visible = true;
                _settingsSidebarHost.BringToFront();
            }

            ShowSettingsCategory(_settingsCategory);
            ShowContentPage(_settingsPage);
            UpdateNewDeckStripVisibility();
            ExitCollectionMarkMode();
            UpdateCollectionChrome();
            ScheduleDarkChrome();
        }

        /// <summary>
        /// The settings body: every category stacked inside one scroll host, so
        /// the page slides from 外观 down to 软件信息 with a single bar. The
        /// categories used to be separate pages swapped by visibility; they are
        /// sections of the same list now and the sidebar only scrolls to the
        /// one it clicked.
        /// </summary>
        private Panel BuildSettingsPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.MainBackground;
            page.Visible = false;

            _settingsScroll = new DarkScrollPanel();
            _settingsScroll.Dock = DockStyle.Fill;
            _settingsScroll.Margin = new Padding(0);
            _settingsScroll.ScrollbarReserve = SettingsScrollbarReserve;

            _settingsContent = new TableLayoutPanel();
            _settingsContent.ColumnCount = 1;
            _settingsContent.BackColor = Color.Transparent;
            _settingsContent.Margin = new Padding(0);
            // The inset lives on the list instead of on every card, so the flat
            // rows line up on one left edge.
            _settingsContent.Padding = new Padding(20, 4, 16, 24);
            _settingsContent.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F));

            BuildSettingsSections();

            _settingsScroll.Content = _settingsContent;
            _settingsScroll.SizeChanged +=
                delegate { ApplySettingsScrollTarget(); };
            page.Controls.Add(_settingsScroll);

            ShowSettingsCategory(_settingsCategory);
            return page;
        }

        /// <summary>
        /// Stacks the four categories into the single list and remembers where
        /// each section starts. A section is its header, the working table of
        /// the category (if it has one), one flat row per setting, then the
        /// note that says what is still a placeholder.
        /// </summary>
        private void BuildSettingsSections()
        {
            _settingsSectionTops = new int[SettingsCategoryTitles.Length];
            _settingsContentHeight = 0;

            for (int category = 0; category < SettingsCategoryTitles.Length;
                category++)
            {
                _settingsSectionTops[category] = _settingsContentHeight;

                Label titleLabel;
                Label subtitleLabel;
                AppendSettingsRow(CreatePageHeader(
                    SettingsCategoryTitles[category],
                    SettingsCategorySubtitles[category],
                    out titleLabel, out subtitleLabel),
                    SettingsSectionHeaderHeight);

                if (category == TerminologySettingsCategory)
                    AppendTriggerGlossarySection();
                else if (category == CardPackSettingsCategory)
                    AppendSettingsRow(CreateCardPackItem(),
                        SettingsCardPackItemHeight);
                else if (category == AppearanceSettingsCategory)
                    AppendSettingsRow(CreateThemeItem(),
                        SettingsThemeItemHeight);

                List<string[]> items = SettingsCardsFor(category);
                for (int i = 0; i < items.Count; i++)
                    AppendSettingsRow(
                        CreateSettingsItem(items[i][0], items[i][1]),
                        SettingsItemHeight);

                AppendSettingsRow(CreateSettingsNote(SectionNoteFor(category)),
                    SettingsNoteHeight);

                if (category < SettingsCategoryTitles.Length - 1)
                    AppendSettingsRow(CreateSettingsSpacer(),
                        SettingsSectionGapHeight);
            }

            _settingsContent.Height = _settingsContentHeight;
        }

        /// <summary>
        /// Adds one fixed height row and grows the recorded total, so the list
        /// knows its own height before it is ever shown and the scroll host can
        /// clamp to it without measuring a single label.
        /// </summary>
        private void AppendSettingsRow(Control control, int height)
        {
            int row = _settingsContent.RowCount;
            _settingsContent.RowCount = row + 1;
            _settingsContent.RowStyles.Add(
                new RowStyle(SizeType.Absolute, height));
            _settingsContent.Controls.Add(control, 0, row);
            _settingsContentHeight += height;
        }

        /// <summary>
        /// Scrolls the body so the section of the clicked category starts at
        /// the top of the viewport. The anchors were recorded while the
        /// sections were built, so no control has to be laid out first.
        /// </summary>
        private void ScrollSettingsToSection(int index)
        {
            if (_settingsSectionTops == null || index < 0 ||
                index >= _settingsSectionTops.Length)
                return;

            _settingsScrollTarget = Math.Max(0,
                _settingsSectionTops[index] - SettingsScrollAnchorBias);
            _settingsScrollPending = _settingsScroll != null;
            ApplySettingsScrollTarget();
        }

        /// <summary>
        /// Applies (and forgets) the pending jump. The first jump runs before
        /// the page has a size, where the offset would clamp to zero, so the
        /// target stays pending until the host reports a real height once.
        /// </summary>
        private void ApplySettingsScrollTarget()
        {
            if (!_settingsScrollPending || _settingsScroll == null)
                return;

            if (_settingsScroll.ClientSize.Height <= 0)
                return;

            _settingsScrollPending = false;
            _settingsScroll.ScrollTo(_settingsScrollTarget);
        }

        /// <summary>
        /// One flat setting: the title line over a muted hint, painted straight
        /// on the page. Settings used to sit in rounded cards; the frame is
        /// gone, so nothing draws a border around a single option any more.
        /// </summary>
        private TableLayoutPanel CreateSettingsItem(string title,
            string description)
        {
            TableLayoutPanel item = CreateSettingsItemHost(2);
            item.RowStyles.Add(
                new RowStyle(SizeType.Absolute, SettingsItemTitleHeight));
            item.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            item.Controls.Add(CreateSettingsTitleLabel(title), 0, 0);
            item.Controls.Add(CreateSettingsHintLabel(description), 0, 1);
            return item;
        }

        /// <summary>
        /// Empty single column grid for a flat settings block. The caller adds
        /// the absolute row heights so the list total stays predictable.
        /// </summary>
        private TableLayoutPanel CreateSettingsItemHost(int rowCount)
        {
            var item = new TableLayoutPanel();
            item.Dock = DockStyle.Fill;
            item.ColumnCount = 1;
            item.RowCount = rowCount;
            item.BackColor = Color.Transparent;
            item.Margin = new Padding(0);
            item.Padding = new Padding(0);
            item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return item;
        }

        /// <summary>Bold title line of one flat setting.</summary>
        private Label CreateSettingsTitleLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.ForeColor = AppTheme.Text;
            label.BackColor = Color.Transparent;
            label.Font = _settingsTitleFont;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0);
            return label;
        }

        /// <summary>
        /// Muted hint under a title. The label is not auto sized, so a long
        /// sentence wraps inside its row instead of widening the list.
        /// </summary>
        private Label CreateSettingsHintLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.ForeColor = AppTheme.TextMuted;
            label.BackColor = Color.Transparent;
            label.Font = _settingsHintFont;
            label.TextAlign = ContentAlignment.TopLeft;
            label.Margin = new Padding(0);
            return label;
        }

        /// <summary>Closing line of a section: what is still a placeholder.</summary>
        private Control CreateSettingsNote(string text)
        {
            var note = new Label();
            note.Text = text;
            note.Dock = DockStyle.Fill;
            note.ForeColor = AppTheme.TextMuted;
            note.BackColor = Color.Transparent;
            note.Font = _settingsHintFont;
            note.TextAlign = ContentAlignment.TopLeft;
            note.Margin = new Padding(4, 0, 0, 0);
            return note;
        }

        /// <summary>Breathing room between two sections of the single list.</summary>
        private static Control CreateSettingsSpacer()
        {
            var spacer = new Panel();
            spacer.Dock = DockStyle.Fill;
            spacer.BackColor = Color.Transparent;
            spacer.Margin = new Padding(0);
            return spacer;
        }

        /// <summary>
        /// Note of one category, mirroring the placeholder text that category
        /// used to carry at the bottom of its own page.
        /// </summary>
        private static string SectionNoteFor(int category)
        {
            switch (category)
            {
                case CardPackSettingsCategory:
                    return "卡表压缩包可直接拖到窗口安装；以上其余设置项为占位。";
                case TerminologySettingsCategory:
                    return "触发名称只改界面显示，卡表与导出的卡组始终写英文名；以上其余设置项为占位。";
                default:
                    return category == AppearanceSettingsCategory
                        ? "主题已可切换并会写入 DeckBuilder\\settings.txt；以上其余设置项为占位，将在后续版本开放。"
                        : "以上设置项均为占位，具体选项将在后续版本开放。";
            }
        }

        /// <summary>
        /// Placeholder settings of one category: a title plus the one line hint
        /// under it. The order matches the sidebar entries built from
        /// <see cref="SettingsCategoryTitles"/>.
        /// </summary>
        private static List<string[]> SettingsCardsFor(int category)
        {
            var cards = new List<string[]>();

            switch (category)
            {
                case 0:
                    cards.Add(new[]
                    {
                        "界面密度",
                        "当前为宽松布局。紧凑的卡片间距与更小的缩略图开发中。"
                    });
                    cards.Add(new[]
                    {
                        "字号",
                        "正文使用 Microsoft YaHei UI 9pt，标题与字幕跟随主题固定。"
                    });
                    break;
                case 1:
                    cards.Add(new[]
                    {
                        "关键词提示",
                        "为卡牌效果中的常驻关键词添加悬浮说明的选项开发中。"
                    });
                    break;
                case 2:
                    cards.Add(new[]
                    {
                        "数据目录",
                        "卡牌数据与图片根目录的浏览、重新扫描功能开发中。"
                    });
                    cards.Add(new[]
                    {
                        "缩略图缓存",
                        "缓存上限当前固定为 120 张，手动清理入口尚未开放。"
                    });
                    cards.Add(new[]
                    {
                        "卡组草稿",
                        "编辑中的卡组会自动保存为草稿，导出与备份功能规划中。"
                    });
                    break;
                default:
                    cards.Add(new[]
                    {
                        "版本",
                        "Weiss Schwarz 卡牌检索 / Deck Builder · CH-0.6 · Made by xineist"
                    });
                    cards.Add(new[]
                    {
                        "数据来源",
                        "只读卡牌数据随程序目录发布，界面不写入游戏文件。"
                    });
                    cards.Add(new[]
                    {
                        "许可与致谢",
                        "卡牌图像与文本版权归原作者与原发行方所有。"
                    });
                    break;
            }

            return cards;
        }

        /// <summary>
        /// The one working setting of 外观: a 深色 and a 浅色 button. Pressing one
        /// repaints the whole window from the other palette and writes the choice
        /// to settings.txt, so the next start opens in the same theme.
        /// </summary>
        private TableLayoutPanel CreateThemeItem()
        {
            TableLayoutPanel item = CreateSettingsItemHost(3);
            item.RowStyles.Add(
                new RowStyle(SizeType.Absolute, SettingsItemTitleHeight));
            item.RowStyles.Add(
                new RowStyle(SizeType.Absolute, SettingsItemHintHeight));
            item.RowStyles.Add(
                new RowStyle(SizeType.Absolute, SettingsCardPackButtonHeight));

            // The row is flat like every other setting, so the two buttons
            // paint straight on the page colour instead of on a card.
            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.BackColor = AppTheme.MainBackground;
            buttons.Margin = new Padding(0);
            buttons.Padding = new Padding(0);

            _themeDarkButton = CreateThemeButton("深色", AppThemeMode.Dark);
            _themeLightButton = CreateThemeButton("浅色", AppThemeMode.Light);
            buttons.Controls.Add(_themeDarkButton);
            buttons.Controls.Add(_themeLightButton);

            _themeHintLabel = CreateSettingsHintLabel(ThemeHintText);

            item.Controls.Add(CreateSettingsTitleLabel("主题"), 0, 0);
            item.Controls.Add(_themeHintLabel, 0, 1);
            item.Controls.Add(buttons, 0, 2);
            return item;
        }

        /// <summary>
        /// One of the two theme buttons. The palette in use paints the active
        /// button in the accent colour through <see cref="RoundedButton.Selected"/>;
        /// the other one keeps the tile surface of a normal small button.
        /// </summary>
        private RoundedButton CreateThemeButton(string text, AppThemeMode mode)
        {
            var button = new RoundedButton();
            button.Text = text;
            button.AccessibleName = text + "主题";
            button.Tag = mode;
            button.Width = 96;
            button.Height = 32;
            button.CornerRadius = 8;
            button.Font = _settingsTitleFont;
            button.Margin = new Padding(0, 0, 8, 0);
            button.Cursor = Cursors.Hand;
            button.Click += ThemeButton_Click;
            ApplyThemeButtonState(button);
            return button;
        }

        /// <summary>
        /// Paints one theme button for the palette in use: the active one takes
        /// the accent fill with white text, the other one the tile surface with
        /// the soft text colour.
        /// </summary>
        private static void ApplyThemeButtonState(RoundedButton button)
        {
            if (button == null || !(button.Tag is AppThemeMode))
                return;

            bool active = (AppThemeMode)button.Tag == AppTheme.Mode;
            button.Selected = active;
            button.SurfaceColor = Color.Empty;
            button.NormalColor = active
                ? AppTheme.Accent
                : AppTheme.TileBackground;
            button.HoverColor = active ? AppTheme.AccentHover : AppTheme.Divider;
            button.PressedColor = active
                ? AppTheme.AccentPressed
                : AppTheme.Border;
            button.SelectedColor = active
                ? AppTheme.Accent
                : AppTheme.TileBackground;
            button.ForeColor = active
                ? AppTheme.ForegroundOn(AppTheme.Accent)
                : AppTheme.TextSoft;
            button.Invalidate();
        }

        private void ThemeButton_Click(object sender, EventArgs e)
        {
            RoundedButton button = sender as RoundedButton;
            if (button == null || !(button.Tag is AppThemeMode))
                return;

            ApplyThemeMode((AppThemeMode)button.Tag);
        }

        /// <summary>
        /// Switches the palette. The colour table is swapped first because every
        /// AppTheme getter reads it, then the live control tree is repainted from
        /// the old palette to the new one, and the choice is written to
        /// settings.txt. A failed write only shows up on the hint line: the
        /// new theme still stays for this session.
        /// </summary>
        private void ApplyThemeMode(AppThemeMode mode)
        {
            if (mode == AppTheme.Mode)
                return;

            AppTheme.Apply(mode);
            AppTheme.Recolor(this);
            ApplyDarkChrome();
            RefreshNavPaint();

            ApplyThemeButtonState(_themeDarkButton);
            ApplyThemeButtonState(_themeLightButton);

            _appSettings.Theme = mode;
            string error;
            if (_appSettings.Save(out error))
            {
                if (_themeHintLabel != null)
                    _themeHintLabel.Text = ThemeHintText;
            }
            else if (_themeHintLabel != null)
            {
                _themeHintLabel.Text = error;
            }
        }


        /// <summary>
        /// The one working setting of 存储: installs a card pack archive picked
        /// through the file dialog. Dropping a card-pack archive anywhere on
        /// the window does the same, so the description mentions both ways.
        /// The block itself is flat like every other setting: only the button
        /// keeps its own fill.
        /// </summary>
        private TableLayoutPanel CreateCardPackItem()
        {
            TableLayoutPanel item = CreateSettingsItemHost(3);
            item.RowStyles.Add(
                new RowStyle(SizeType.Absolute, SettingsItemTitleHeight));
            item.RowStyles.Add(
                new RowStyle(SizeType.Absolute, SettingsCardPackHintHeight));
            item.RowStyles.Add(
                new RowStyle(SizeType.Absolute, SettingsCardPackButtonHeight));

            var button = new RoundedButton();
            button.Text = "选择压缩包…";
            button.AccessibleName = "选择卡表压缩包";
            button.Dock = DockStyle.Left;
            button.Width = 148;
            button.CornerRadius = 8;
            button.SurfaceColor = AppTheme.TileBackground;
            button.NormalColor = AppTheme.Accent;
            button.HoverColor = AppTheme.AccentHover;
            button.PressedColor = AppTheme.AccentPressed;
            button.ForeColor = AppTheme.ForegroundOn(AppTheme.Accent);
            button.Font = _settingsTitleFont;
            button.Margin = new Padding(0, 0, 0, 6);
            button.Click += delegate { BrowseCardPack(); };

            item.Controls.Add(CreateSettingsTitleLabel("卡表压缩包"), 0, 0);
            item.Controls.Add(CreateSettingsHintLabel(
                "把 .zip、.rar 或 .7z 拖到窗口任意位置即可安装，也可以点击下方按钮选择文件。\r\n" +
                "仅接受包含 Cards\\<系列>\\SingleSetData.txt 的压缩包，安装后直接进入该系列的卡表。"), 0, 1);
            item.Controls.Add(button, 0, 2);
            return item;
        }
    }
}
