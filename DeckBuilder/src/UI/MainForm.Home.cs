using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    public partial class MainForm
    {
        // The landing page is one compact block — title, card, 今日运势 column
        // and footer — centred in the content area. Every row is absolute so a
        // taller window grows the outer margin instead of pulling the title and
        // the footer out to the window edges.
        private const int HomeHeaderHeight = 58;
        private const int HomeHeaderGap = 12;
        private const int HomeContentHeight = 468;
        private const int HomeFooterGap = 10;
        private const int HomeFooterHeight = 20;
        private const int HomeStageHeight = HomeHeaderHeight + HomeHeaderGap +
            HomeContentHeight + HomeFooterGap + HomeFooterHeight;
        private const int HomeMinimumVerticalMargin = 12;

        private Panel _homePage;
        private TodayCardView _homeCardView;
        private Label _homeDateLabel;
        private Label _homeLevelLabel;
        private Label _homeScoreLabel;
        private FortuneScoreBar _homeScoreBar;
        private Label _homeGoodCaption;
        private Label _homeBadCaption;
        private FlowLayoutPanel _homeGoodTags;
        private FlowLayoutPanel _homeBadTags;
        private Label _homeCardName;
        private Label _homeCardMeta;
        private Label _homeCardHint;
        private Label _homeFooter;
        private TableLayoutPanel _homeRoot;
        private WeissCard _homeCard;
        private FortuneReport _homeFortune;
        private DateTime _homeDate;
        private bool _homeCardResolved;
        private bool _homeCardLoadScheduled;
        private Timer _homeDayTimer;

        /// <summary>
        /// The landing page: one deterministic card on the left and the daily
        /// 吉凶 / 今日人品 / 宜忌 panel on the right. It is built once with the
        /// other content pages and only its labels change afterwards.
        /// </summary>
        private Panel BuildHomePage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.MainBackground;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 5;
            root.RowCount = 5;
            root.BackColor = AppTheme.MainBackground;
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HomeHeaderHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HomeHeaderGap));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HomeContentHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HomeFooterGap));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HomeFooterHeight));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            Label titleLabel;
            Label subtitleLabel;
            Control header = CreatePageHeader("今日运势",
                "停一停，看看今天抽到的卡片和手气", out titleLabel,
                out subtitleLabel);
            titleLabel.TextAlign = ContentAlignment.MiddleCenter;
            subtitleLabel.TextAlign = ContentAlignment.MiddleCenter;
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 5);

            Control cardPanel = BuildHomeCardPanel();
            root.Controls.Add(cardPanel, 1, 2);
            Control fortunePanel = BuildHomeFortunePanel();
            root.Controls.Add(fortunePanel, 3, 2);

            // The footer belongs to the same centred stack as the title, so
            // tall windows do not leave it pinned against the bottom edge.
            _homeFooter = new Label();
            _homeFooter.Text = "同一天固定 · 跨天自动刷新";
            _homeFooter.Dock = DockStyle.Fill;
            _homeFooter.ForeColor = AppTheme.TextMuted;
            _homeFooter.Font = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _homeFooter.TextAlign = ContentAlignment.MiddleCenter;
            _homeFooter.Margin = new Padding(0);
            root.Controls.Add(_homeFooter, 0, 4);
            root.SetColumnSpan(_homeFooter, 5);

            _homeRoot = root;
            page.Resize += delegate { UpdateHomeVerticalPadding(); };

            page.Controls.Add(root);
            UpdateHomeVerticalPadding();
            return page;
        }

        /// <summary>
        /// Keeps the title, card and footer together in the vertical centre: the
        /// stage has a fixed height, so a taller window splits the remaining
        /// space into equal top and bottom margin instead of stretching the gaps
        /// between the title, the card and the footer.
        /// </summary>
        private void UpdateHomeVerticalPadding()
        {
            if (_homeRoot == null || _homePage == null)
                return;

            int height = _homePage.ClientSize.Height;
            if (height <= 0)
                height = _homeRoot.ClientSize.Height;
            if (height <= 0)
                return;

            int margin = (height - HomeStageHeight) / 2;
            if (margin < HomeMinimumVerticalMargin)
                margin = HomeMinimumVerticalMargin;

            var padding = new Padding(0, margin, 0, margin);
            if (_homeRoot.Padding != padding)
                _homeRoot.Padding = padding;
        }

        private Control BuildHomeCardPanel()
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 5;
            panel.BackColor = AppTheme.MainBackground;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(0);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));

            var heading = new Label();
            heading.Text = "今日卡片";
            heading.Dock = DockStyle.Fill;
            heading.ForeColor = AppTheme.TextMuted;
            heading.Font = new Font("Microsoft YaHei UI", 9.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            heading.TextAlign = ContentAlignment.MiddleCenter;
            heading.Margin = new Padding(0);
            panel.Controls.Add(heading, 0, 0);

            _homeCardView = new TodayCardView(_thumbnails);
            _homeCardView.Dock = DockStyle.Fill;
            _homeCardView.Margin = new Padding(0, 2, 0, 2);
            _homeCardView.Click += HomeCard_Click;
            panel.Controls.Add(_homeCardView, 0, 1);

            _homeCardName = new Label();
            _homeCardName.Dock = DockStyle.Fill;
            _homeCardName.ForeColor = AppTheme.Text;
            _homeCardName.Font = new Font("Microsoft YaHei UI", 13F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _homeCardName.TextAlign = ContentAlignment.MiddleCenter;
            _homeCardName.AutoEllipsis = true;
            _homeCardName.Margin = new Padding(0);
            _homeCardName.Click += HomeCard_Click;
            panel.Controls.Add(_homeCardName, 0, 2);

            _homeCardMeta = new Label();
            _homeCardMeta.Dock = DockStyle.Fill;
            _homeCardMeta.ForeColor = AppTheme.TextMuted;
            _homeCardMeta.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _homeCardMeta.TextAlign = ContentAlignment.MiddleCenter;
            _homeCardMeta.AutoEllipsis = true;
            _homeCardMeta.Margin = new Padding(0);
            panel.Controls.Add(_homeCardMeta, 0, 3);

            _homeCardHint = new Label();
            _homeCardHint.Dock = DockStyle.Fill;
            _homeCardHint.ForeColor = AppTheme.TextMuted;
            _homeCardHint.Font = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _homeCardHint.TextAlign = ContentAlignment.MiddleCenter;
            _homeCardHint.Margin = new Padding(0);
            panel.Controls.Add(_homeCardHint, 0, 4);

            return panel;
        }

        private Control BuildHomeFortunePanel()
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 3;
            panel.BackColor = AppTheme.MainBackground;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(40, 0, 24, 0);
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 320F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            var content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 1;
            content.RowCount = 11;
            content.BackColor = AppTheme.MainBackground;
            content.Margin = new Padding(0);
            content.Padding = new Padding(0);
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 94F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _homeDateLabel = new Label();
            _homeDateLabel.Dock = DockStyle.Fill;
            _homeDateLabel.ForeColor = AppTheme.TextSoft;
            _homeDateLabel.Font = new Font("Microsoft YaHei UI", 9.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _homeDateLabel.TextAlign = ContentAlignment.MiddleLeft;
            _homeDateLabel.Margin = new Padding(0);
            content.Controls.Add(_homeDateLabel, 0, 0);

            _homeLevelLabel = new Label();
            _homeLevelLabel.Dock = DockStyle.Fill;
            _homeLevelLabel.ForeColor = AppTheme.Text;
            _homeLevelLabel.Font = new Font("Microsoft YaHei UI", 54F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _homeLevelLabel.TextAlign = ContentAlignment.MiddleLeft;
            _homeLevelLabel.Margin = new Padding(0);
            content.Controls.Add(_homeLevelLabel, 0, 1);

            var scoreRow = new TableLayoutPanel();
            scoreRow.Dock = DockStyle.Fill;
            scoreRow.ColumnCount = 2;
            scoreRow.RowCount = 1;
            scoreRow.BackColor = AppTheme.MainBackground;
            scoreRow.Margin = new Padding(0);
            scoreRow.Padding = new Padding(0);
            scoreRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            scoreRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            scoreRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var scoreCaption = new Label();
            scoreCaption.Text = "今日人品";
            scoreCaption.Dock = DockStyle.Fill;
            scoreCaption.ForeColor = AppTheme.TextMuted;
            scoreCaption.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            scoreCaption.TextAlign = ContentAlignment.MiddleLeft;
            scoreCaption.Margin = new Padding(0);
            scoreRow.Controls.Add(scoreCaption, 0, 0);

            _homeScoreLabel = new Label();
            _homeScoreLabel.Text = "-- / 100";
            _homeScoreLabel.AutoSize = true;
            _homeScoreLabel.Anchor = AnchorStyles.Right;
            _homeScoreLabel.ForeColor = AppTheme.Text;
            _homeScoreLabel.Font = new Font("Microsoft YaHei UI", 10F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _homeScoreLabel.TextAlign = ContentAlignment.MiddleRight;
            _homeScoreLabel.Margin = new Padding(0);
            scoreRow.Controls.Add(_homeScoreLabel, 1, 0);
            content.Controls.Add(scoreRow, 0, 2);

            _homeScoreBar = new FortuneScoreBar();
            _homeScoreBar.Dock = DockStyle.Fill;
            _homeScoreBar.Margin = new Padding(0, 4, 0, 4);
            content.Controls.Add(_homeScoreBar, 0, 3);


            _homeGoodCaption = CreateHomeCaption("宜", AppTheme.Success);
            content.Controls.Add(_homeGoodCaption, 0, 5);

            _homeGoodTags = CreateHomeTagRow();
            content.Controls.Add(_homeGoodTags, 0, 6);

            _homeBadCaption = CreateHomeCaption("忌", AppTheme.Danger);
            content.Controls.Add(_homeBadCaption, 0, 8);

            _homeBadTags = CreateHomeTagRow();
            content.Controls.Add(_homeBadTags, 0, 9);

            panel.Controls.Add(content, 0, 1);

            return panel;
        }

        private static Label CreateHomeCaption(string text, Color color)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.ForeColor = color;
            label.Font = new Font("Microsoft YaHei UI", 10F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0);
            return label;
        }

        private FlowLayoutPanel CreateHomeTagRow()
        {
            var row = new FlowLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = false;
            row.AutoScroll = false;
            row.BackColor = AppTheme.MainBackground;
            row.Margin = new Padding(0);
            row.Padding = new Padding(0, 2, 0, 0);
            return row;
        }


        /// <summary>
        /// Switches to the landing page without adding a sidebar entry. The
        /// brand label at the top of the rail is the second way back here.
        /// </summary>
        private void ShowHome()
        {
            if (_homePage == null)
                return;

            _viewMode = ViewMode.Home;
            _activeSeries = null;
            _results.Clear();

            CloseDeckDetail();
            _navColumnStyle.Width = NavExpandedWidth;
            _filterColumnStyle.Width = 0F;
            _filterHost.Visible = false;
            _filterBody.ScrollToTop();
            SetNavExpanded(true);
            SetDeckSubItemsVisible(false);
            ShowContentPage(_homePage);
            UpdateHomeVerticalPadding();
            ClearActiveNavItem();
            UpdateNewDeckStripVisibility();
            ExitCollectionMarkMode();
            UpdateCollectionChrome();

            EnsureHomeData();
            ScheduleDarkChrome();
        }

        /// <summary>
        /// Home has no sidebar button, so the normal highlight helper would
        /// mark 设置 as active when passed null. Clear that too.
        /// </summary>
        private void ClearActiveNavItem()
        {
            SetActiveNavItem(null);
            if (_navSettingsButton != null && _navSettingsButton.IsActive)
            {
                _navSettingsButton.IsActive = false;
                _navSettingsButton.Invalidate();
            }
        }

        /// <summary>
        /// Keeps the date, fortune and card in sync with the local calendar.
        /// Called when the page opens, when the app is first shown, and by the
        /// minute timer so a window left open overnight rolls over by itself.
        /// </summary>
        private void EnsureHomeData()
        {
            if (_homeDateLabel == null || _homeCardView == null)
                return;

            DateTime today = DateTime.Today;
            if (_homeDate != today)
            {
                _homeDate = today;
                _homeFortune = DailyFortune.ForDate(today);
                _homeCard = null;
                _homeCardResolved = false;
                ShowHomeFortune(_homeFortune);
                ShowHomeCard(null, true);
            }

            if (!_homeCardResolved)
                ScheduleDailyCardLoad();
        }

        /// <summary>
        /// Defers the one-time series read until after the first frame, so the
        /// window and the fortune panel appear without waiting for card images.
        /// </summary>
        private void ScheduleDailyCardLoad()
        {
            if (_homeCardResolved || _homeCardLoadScheduled ||
                _database == null)
            {
                return;
            }

            if (!IsHandleCreated || IsDisposed)
                return;

            _homeCardLoadScheduled = true;
            try
            {
                BeginInvoke(new MethodInvoker(LoadDailyCard));
            }
            catch (InvalidOperationException)
            {
                _homeCardLoadScheduled = false;
            }
        }

        private void LoadDailyCard()
        {
            _homeCardLoadScheduled = false;
            if (IsDisposed)
                return;

            DateTime day = _homeDate;
            if (day == DateTime.MinValue)
                return;

            bool showWait = _viewMode == ViewMode.Home;
            WeissCard card = null;
            try
            {
                if (showWait)
                    UseWaitCursor = true;
                card = DailyCardPicker.Pick(_database, day);
            }
            catch (Exception)
            {
                card = null;
            }
            finally
            {
                if (showWait && !IsDisposed)
                    UseWaitCursor = false;
            }

            if (IsDisposed)
                return;

            if (_homeDate != day)
            {
                // Midnight passed while the series was loading; the new day
                // needs its own draw.
                _homeCardResolved = false;
                ScheduleDailyCardLoad();
                return;
            }

            _homeCard = card;
            _homeCardResolved = true;
            ShowHomeCard(card, false);
        }


        private void HomeDayTimer_Tick(object sender, EventArgs e)
        {
            if (_viewMode != ViewMode.Home)
                return;
            if (_homeDate != DateTime.Today)
                EnsureHomeData();
        }

        /// <summary>
        /// The 吉凶 colours are chosen per theme (the light theme needs darker
        /// green / red than the fixed status colours), and AppTheme.Recolor only
        /// maps palette slots. Repainting the labels and tags after a theme
        /// switch keeps them readable in both modes.
        /// </summary>
        private void ApplyHomeTheme()
        {
            if (_homeFortune != null)
                ShowHomeFortune(_homeFortune);
            if (_homeCardName != null)
                ShowHomeCard(_homeCard, !_homeCardResolved);
        }

        private void ShowHomeFortune(FortuneReport report)
        {
            if (report == null || _homeDateLabel == null)
                return;

            FortuneTone tone = HomeToneForScore(report.Score);
            _homeDateLabel.Text = FormatHomeDate(report.Date);
            _homeLevelLabel.Text = report.Level;
            _homeLevelLabel.ForeColor = HomeToneColor(tone);
            _homeScoreLabel.Text = string.Format("{0} / 100", report.Score);
            _homeScoreLabel.ForeColor = AppTheme.Text;
            _homeScoreBar.Score = report.Score;
            _homeScoreBar.Tone = tone;

            _homeGoodCaption.ForeColor = HomeToneColor(FortuneTone.Good);
            _homeBadCaption.ForeColor = HomeToneColor(FortuneTone.Bad);
            FillHomeTags(_homeGoodTags, report.Good, FortuneTone.Good);
            FillHomeTags(_homeBadTags, report.Bad, FortuneTone.Bad);
        }

        private static void FillHomeTags(FlowLayoutPanel row,
            IList<string> tags, FortuneTone tone)
        {
            if (row == null)
                return;

            row.SuspendLayout();
            for (int i = row.Controls.Count - 1; i >= 0; i--)
            {
                Control child = row.Controls[i];
                row.Controls.RemoveAt(i);
                child.Dispose();
            }

            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    var tag = new FortuneTagLabel(tags[i], tone);
                    tag.Margin = new Padding(0, 0, 8, 0);
                    row.Controls.Add(tag);
                }
            }
            row.ResumeLayout();
        }

        private void ShowHomeCard(WeissCard card, bool loading)
        {
            if (_homeCardView == null)
                return;

            _homeCardView.SetCard(card, loading);
            if (card == null)
            {
                _homeCardName.Text = loading
                    ? "正在抽取今日卡片…" : "今天没有可用的卡片";
                _homeCardMeta.Text = loading
                    ? "首次抽卡需要读取一个系列"
                    : "请检查卡牌数据是否已经安装";
                _homeCardHint.Text = string.Empty;
                _homeCardName.ForeColor = AppTheme.TextSoft;
                _homeCardName.Cursor = Cursors.Default;
            }
            else
            {
                string name = string.IsNullOrEmpty(card.Name)
                    ? card.Id : card.Name;
                if (string.IsNullOrEmpty(name))
                    name = "未命名卡片";
                string series = card.SeriesName ?? string.Empty;
                string id = card.Id ?? string.Empty;
                string meta;
                if (series.Length > 0 && id.Length > 0)
                    meta = series + " · " + id;
                else
                    meta = series.Length > 0 ? series : id;

                _homeCardName.Text = name;
                _homeCardMeta.Text = meta;
                _homeCardHint.Text = "单击卡片查看详情";
                _homeCardName.ForeColor = AppTheme.Text;
                _homeCardName.Cursor = Cursors.Hand;
            }

            _homeCardMeta.ForeColor = AppTheme.TextMuted;
            _homeCardHint.ForeColor = AppTheme.TextMuted;
            _homeCardName.Invalidate();
        }

        private void HomeCard_Click(object sender, EventArgs e)
        {
            if (_homeCard != null)
                ShowCardDetail(_homeCard);
        }

        private static FortuneTone HomeToneForScore(int score)
        {
            if (score >= 75)
                return FortuneTone.Great;
            if (score >= 45)
                return FortuneTone.Good;
            if (score >= 30)
                return FortuneTone.Neutral;
            return FortuneTone.Bad;
        }

        private static Color HomeToneColor(FortuneTone tone)
        {
            if (tone == FortuneTone.Great)
                return AppTheme.FortuneGold;
            if (tone == FortuneTone.Bad)
            {
                return AppTheme.IsDark
                    ? AppTheme.Danger
                    : Color.FromArgb(185, 28, 28);
            }
            if (tone == FortuneTone.Neutral)
                return AppTheme.TextSoft;
            return AppTheme.IsDark
                ? AppTheme.Success
                : Color.FromArgb(21, 128, 61);
        }

        private static string FormatHomeDate(DateTime date)
        {
            string[] weekdays = { "日", "一", "二", "三", "四", "五", "六" };
            return date.ToString("yyyy年M月d日") + " · 星期" +
                weekdays[(int)date.DayOfWeek];
        }

        /// <summary>Minute heartbeat used only for the midnight rollover.</summary>
        private void InitializeHomeTimer()
        {
            _homeDayTimer = new Timer();
            _homeDayTimer.Interval = 60000;
            _homeDayTimer.Tick += HomeDayTimer_Tick;
        }

        private void DisposeHomeTimer()
        {
            if (_homeDayTimer == null)
                return;
            _homeDayTimer.Stop();
            _homeDayTimer.Dispose();
            _homeDayTimer = null;
        }
    }
}

