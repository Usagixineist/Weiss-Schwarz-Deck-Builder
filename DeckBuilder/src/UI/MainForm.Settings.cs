using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

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
        private const int SettingsCardRowHeight = 100;

        /// <summary>
        /// Index of the 存储 category. It is the only category with a working
        /// action so far: installing a card pack archive.
        /// </summary>
        private const int CardPackSettingsCategory = 2;

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
        private Panel[] _settingsCategoryPages;
        private List<RoundedButton> _settingsCategoryButtons;
        private int _settingsCategory;

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
        /// Container of the four category pages. The sidebar only swaps which
        /// child is visible, so every category is built once and keeps its
        /// state while the user jumps between them.
        /// </summary>
        private Panel BuildSettingsPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.MainBackground;
            page.Visible = false;

            _settingsCategoryPages = new Panel[SettingsCategoryTitles.Length];
            for (int i = 0; i < SettingsCategoryTitles.Length; i++)
            {
                Panel categoryPage = BuildSettingsCategoryPage(i);
                categoryPage.Dock = DockStyle.Fill;
                categoryPage.Visible = i == 0;
                page.Controls.Add(categoryPage);
                _settingsCategoryPages[i] = categoryPage;
            }

            ShowSettingsCategory(_settingsCategory);
            return page;
        }

        /// <summary>
        /// One category of the settings body: the shared page header plus the
        /// placeholder cards of that category. Real options will replace the
        /// cards one by one without touching the surrounding layout.
        /// </summary>
        private Panel BuildSettingsCategoryPage(int category)
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.MainBackground;
            page.Margin = new Padding(0);

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

            Label titleLabel;
            Label subtitleLabel;
            layout.Controls.Add(CreatePageHeader(
                SettingsCategoryTitles[category],
                SettingsCategorySubtitles[category],
                out titleLabel, out subtitleLabel), 0, 0);

            List<string[]> cards = SettingsCardsFor(category);

            // 存储页第一行是真实可用的入口，其余分类仍然是占位卡片。
            int firstRow = 0;
            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 1;
            body.RowCount = cards.Count + 1;
            body.BackColor = AppTheme.MainBackground;
            body.Margin = new Padding(0);
            body.Padding = new Padding(0);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            if (category == CardPackSettingsCategory)
            {
                body.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
                body.Controls.Add(CreateCardPackCard(), 0, 0);
                firstRow = 1;
            }
            body.RowCount = cards.Count + firstRow + 1;

            for (int i = 0; i < cards.Count; i++)
            {
                body.RowStyles.Add(
                    new RowStyle(SizeType.Absolute, SettingsCardRowHeight));
                body.Controls.Add(
                    CreatePlaceholderCard(cards[i][0], cards[i][1]), 0, i + firstRow);
            }

            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var note = new Label();
            note.Text = firstRow == 0
                ? "以上设置项均为占位，具体选项将在后续版本开放。"
                : "卡表压缩包可直接拖到窗口安装；以上其余设置项为占位。";
            note.Dock = DockStyle.Fill;
            note.ForeColor = AppTheme.TextMuted;
            note.BackColor = Color.Transparent;
            note.Font = _settingsHintFont;
            note.TextAlign = ContentAlignment.TopLeft;
            note.Margin = new Padding(2, 0, 0, 0);
            body.Controls.Add(note, 0, cards.Count + firstRow);

            layout.Controls.Add(body, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        /// <summary>
        /// Placeholder cards of one category: a title plus the one line hint
        /// underneath. The order matches the sidebar entries built from
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
                        "主题",
                        "深色主题固定启用；浅色与跟随系统主题将在后续版本开放。"
                    });
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
                        "术语对照",
                        "费用、灵魂、力量、触发等术语的中日对照表将在此展示。"
                    });
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
        /// The one working card of 存储: installs a card pack archive picked
        /// through the file dialog. Dropping a card-pack archive anywhere on the window
        /// does the same, so the description mentions both ways.
        /// </summary>
        private Panel CreateCardPackCard()
        {
            var card = new Panel();
            card.Dock = DockStyle.Fill;
            card.BackColor = AppTheme.TileBackground;
            card.Margin = new Padding(0, 0, 0, 12);
            card.Padding = new Padding(16, 10, 16, 12);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 2;
            layout.BackColor = Color.Transparent;
            layout.Margin = new Padding(0);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var titleLabel = new Label();
            titleLabel.Text = "卡表压缩包";
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.ForeColor = AppTheme.Text;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Font = _settingsTitleFont;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;

            var descriptionLabel = new Label();
            descriptionLabel.Text =
                "把 .zip、.rar 或 .7z 拖到窗口任意位置即可安装，也可以点击右侧按钮选择文件。\r\n" +
                "仅接受包含 Cards\\<系列>\\SingleSetData.txt 的压缩包，安装后直接进入该系列的卡表。";
            descriptionLabel.Dock = DockStyle.Fill;
            descriptionLabel.ForeColor = AppTheme.TextMuted;
            descriptionLabel.BackColor = Color.Transparent;
            descriptionLabel.Font = _settingsHintFont;
            descriptionLabel.TextAlign = ContentAlignment.TopLeft;

            var button = new RoundedButton();
            button.Text = "选择压缩包…";
            button.AccessibleName = "选择卡表压缩包";
            button.Dock = DockStyle.Fill;
            button.CornerRadius = 8;
            button.SurfaceColor = AppTheme.TileBackground;
            button.NormalColor = AppTheme.Accent;
            button.HoverColor = AppTheme.AccentHover;
            button.PressedColor = AppTheme.AccentPressed;
            button.ForeColor = Color.White;
            button.Font = _settingsTitleFont;
            button.Margin = new Padding(0, 0, 0, 6);
            button.Click += delegate { BrowseCardPack(); };

            layout.Controls.Add(titleLabel, 0, 0);
            layout.Controls.Add(button, 1, 0);
            layout.Controls.Add(descriptionLabel, 0, 1);
            layout.SetColumnSpan(descriptionLabel, 2);

            card.Controls.Add(layout);
            card.SizeChanged += delegate { ApplyRoundedRegion(card, 10); };
            return card;
        }

        /// <summary>
        /// Rounded card with a title and a description. Shared by every
        /// placeholder page so new pages stay visually consistent.
        /// </summary>
        private Panel CreatePlaceholderCard(string title, string description)
        {
            var card = new Panel();
            card.Dock = DockStyle.Fill;
            card.BackColor = AppTheme.TileBackground;
            card.Margin = new Padding(0, 0, 0, 12);
            card.Padding = new Padding(16, 10, 16, 12);

            var titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 24;
            titleLabel.ForeColor = AppTheme.Text;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Font = _settingsTitleFont;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;

            var descriptionLabel = new Label();
            descriptionLabel.Text = description;
            descriptionLabel.Dock = DockStyle.Fill;
            descriptionLabel.ForeColor = AppTheme.TextMuted;
            descriptionLabel.BackColor = Color.Transparent;
            descriptionLabel.Font = _settingsHintFont;
            descriptionLabel.TextAlign = ContentAlignment.TopLeft;

            card.Controls.Add(descriptionLabel);
            card.Controls.Add(titleLabel);
            card.SizeChanged += delegate { ApplyRoundedRegion(card, 10); };
            return card;
        }

        private static void ApplyRoundedRegion(Control control, int radius)
        {
            if (control == null || control.Width <= 0 || control.Height <= 0)
                return;

            using (GraphicsPath path = AppTheme.CreateRoundedRectangle(
                new Rectangle(0, 0, control.Width, control.Height), radius))
            {
                Region previous = control.Region;
                control.Region = new Region(path);
                if (previous != null)
                    previous.Dispose();
            }
        }
    }
}
