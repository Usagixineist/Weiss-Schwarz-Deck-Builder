using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Extras;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 附加功能：第四个一级入口。主栏收成 72px 的图标栏，第二级栏列出
    /// extra\ 里的插件（一个插件一行），内容区显示选中的插件页面。
    ///
    /// 布局刻意和 设置 页面一模一样（同样的列宽、页头、分隔线、滚动列表、
    /// 底部提示），所以从 设置 切到 附加功能 时界面不会跳一下。
    /// </summary>
    public sealed partial class MainForm
    {
        private const int ExtrasSidebarHeaderHeight = 104;
        private const int ExtrasSidebarCaptionRowHeight = 28;
        private const int ExtrasSidebarTitleRowHeight = 28;
        private const int ExtrasSidebarSubtitleRowHeight = 22;
        private const int ExtrasSidebarFooterHeight = 70;
        private const int ExtrasSidebarFooterHintHeight = 30;
        private const int ExtrasSidebarFooterLinkHeight = 24;
        private const int ExtrasCategoryCaptionHeight = 28;
        private const int ExtrasItemHeight = 40;
        private const int ExtrasItemWidth = 218;

        /// <summary>页头固定占一条 62px 的带子，和 系列卡表 / 设置 的页头一致。</summary>
        private const int ExtrasPageHeaderHeight = 62;

        /// <summary>底部提示：插件放哪儿、怎么卸载。</summary>
        private const string ExtrasFolderHint =
            "插件放在 DeckBuilder\\extra，一个文件夹一个插件\r\n" +
            "删掉文件夹即卸载；运行中替换 dll 需重启程序";

        /// <summary>内容页壳子：一次只显示一个子页面（空页 / 插件页 / 错误页）。</summary>
        private Panel _extrasPage;

        /// <summary>第二级栏：插件列表。</summary>
        private Panel _extrasSidebarHost;

        private DarkScrollPanel _extrasScroll;
        private FlowLayoutPanel _extrasStack;
        private Label _extrasSubtitle;
        private ToolTip _extrasToolTip;

        /// <summary>一级入口按钮，用来画选中态。</summary>
        private RoundedButton _navExtrasButton;

        /// <summary>扫描结果；null 表示还没扫描过。</summary>
        private IList<ExtraEntry> _extrasEntries;

        private readonly List<RoundedButton> _extrasButtons =
            new List<RoundedButton>();

        /// <summary>插件 id → 已创建好的插件页（切换回来不重建）。</summary>
        private readonly Dictionary<string, Panel> _extrasFrames =
            new Dictionary<string, Panel>(StringComparer.Ordinal);

        /// <summary>一个插件都没有时显示的空页。</summary>
        private Panel _extrasEmptyPage;

        private Control _extrasActiveChild;
        private Control _extrasActivePluginContent;
        private string _activeExtraId;

        /// <summary>
        /// 插件按卡号打开的卡片缓存：同一个卡号点第二次直接复用第一次找到的
        /// 那张卡，不再扫一遍系列。
        /// </summary>
        private readonly Dictionary<string, WeissCard> _extraCardLookup =
            new Dictionary<string, WeissCard>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 第二级栏：插件列表。结构和 设置 的分类栏相同 —— 页头三段
        /// （分类 / 标题 / 数量）、分隔线、可滚动的按钮列表、底部路径提示。
        /// </summary>
        private Panel BuildExtrasSidebarHost()
        {
            var host = new GradientPanel();
            host.Dock = DockStyle.Fill;
            host.StartColor = AppTheme.Navigation;
            host.EndColor = AppTheme.MainBackground;
            host.Visible = false;
            host.Margin = new Padding(0);

            _extrasToolTip = new ToolTip();

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.BackColor = Color.Transparent;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, ExtrasSidebarHeaderHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, ExtrasSidebarFooterHeight));
            host.Controls.Add(layout);

            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 1;
            header.RowCount = 3;
            header.BackColor = Color.Transparent;
            header.Padding = new Padding(14, 9, 14, 6);
            header.Margin = new Padding(0);
            header.RowStyles.Add(new RowStyle(SizeType.Absolute,
                ExtrasSidebarCaptionRowHeight));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute,
                ExtrasSidebarTitleRowHeight));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute,
                ExtrasSidebarSubtitleRowHeight));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            Label caption = CreateDeckSidebarLabel(8.5F, FontStyle.Regular,
                AppTheme.TextMuted, ContentAlignment.MiddleLeft);
            caption.Text = "附加功能 · 已安装插件";
            caption.AutoEllipsis = true;
            header.Controls.Add(caption, 0, 0);

            Label title = CreateDeckSidebarLabel(13F, FontStyle.Bold,
                AppTheme.Text, ContentAlignment.MiddleLeft);
            title.Text = "插件列表";
            title.AutoEllipsis = true;
            header.Controls.Add(title, 0, 1);

            _extrasSubtitle = CreateDeckSidebarLabel(8.5F, FontStyle.Regular,
                AppTheme.TextMuted, ContentAlignment.MiddleLeft);
            _extrasSubtitle.Text = "未扫描";
            _extrasSubtitle.AutoEllipsis = true;
            header.Controls.Add(_extrasSubtitle, 0, 2);
            layout.Controls.Add(header, 0, 0);

            var separator = new Panel();
            separator.Dock = DockStyle.Fill;
            separator.BackColor = AppTheme.Divider;
            separator.Margin = new Padding(0);
            layout.Controls.Add(separator, 0, 1);

            _extrasScroll = new DarkScrollPanel();
            _extrasScroll.Dock = DockStyle.Fill;
            _extrasScroll.BackColor = Color.Transparent;
            _extrasScroll.Margin = new Padding(0);

            _extrasStack = new FlowLayoutPanel();
            _extrasStack.FlowDirection = FlowDirection.TopDown;
            _extrasStack.WrapContents = false;
            _extrasStack.AutoSize = true;
            _extrasStack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _extrasStack.BackColor = Color.Transparent;
            _extrasStack.Padding = new Padding(14, 12, 14, 16);
            _extrasStack.Margin = new Padding(0);

            Label listCaption = new Label();
            listCaption.Text = "插件";
            listCaption.Width = ExtrasItemWidth;
            listCaption.Height = ExtrasCategoryCaptionHeight;
            listCaption.ForeColor = AppTheme.Text;
            listCaption.BackColor = Color.Transparent;
            listCaption.Font = new Font("Microsoft YaHei UI", 10.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            listCaption.TextAlign = ContentAlignment.MiddleLeft;
            listCaption.Margin = new Padding(0, 0, 0, 6);
            _extrasStack.Controls.Add(listCaption);

            _extrasScroll.Content = _extrasStack;
            layout.Controls.Add(_extrasScroll, 0, 2);

            var footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 1;
            footer.RowCount = 2;
            footer.BackColor = Color.Transparent;
            footer.Margin = new Padding(0);
            footer.Padding = new Padding(14, 8, 14, 8);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute,
                ExtrasSidebarFooterHintHeight));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute,
                ExtrasSidebarFooterLinkHeight));

            Label hint = new Label();
            hint.Text = ExtrasFolderHint;
            hint.Dock = DockStyle.Fill;
            hint.ForeColor = AppTheme.TextMuted;
            hint.BackColor = Color.Transparent;
            hint.Font = new Font("Microsoft YaHei UI", 8F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            hint.TextAlign = ContentAlignment.TopLeft;
            hint.Margin = new Padding(0);
            footer.Controls.Add(hint, 0, 0);

            FlowLayoutPanel links = new FlowLayoutPanel();
            links.Dock = DockStyle.Fill;
            links.FlowDirection = FlowDirection.LeftToRight;
            links.WrapContents = false;
            links.BackColor = Color.Transparent;
            links.Margin = new Padding(0);
            links.Padding = new Padding(0);

            LinkLabel rescan = CreateExtrasSidebarLink("重新扫描");
            rescan.LinkClicked += delegate { ReloadExtras(true); };
            links.Controls.Add(rescan);

            LinkLabel open = CreateExtrasSidebarLink("打开目录");
            open.LinkClicked += delegate { OpenExtrasFolder(); };
            links.Controls.Add(open);
            footer.Controls.Add(links, 0, 1);

            layout.Controls.Add(footer, 0, 3);
            return host;
        }

        /// <summary>底部两个链接（重新扫描 / 打开目录）。</summary>
        private static LinkLabel CreateExtrasSidebarLink(string text)
        {
            var link = new LinkLabel();
            link.Text = text;
            link.AutoSize = true;
            link.BackColor = Color.Transparent;
            link.LinkColor = AppTheme.TextSoft;
            link.ActiveLinkColor = AppTheme.Text;
            link.VisitedLinkColor = AppTheme.TextSoft;
            link.DisabledLinkColor = AppTheme.TextMuted;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.Font = new Font("Microsoft YaHei UI", 8.5F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            link.Margin = new Padding(0, 0, 14, 0);
            link.Cursor = Cursors.Hand;
            return link;
        }

        /// <summary>
        /// 内容页壳子。子页面（空页 / 各插件页）都由 <see cref="ShowExtrasChild"/>
        /// 一次只显示一个，插件页创建后一直留在里面，切走再切回来不重建。
        /// </summary>
        private Panel BuildExtrasPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.MainBackground;
            page.Visible = false;
            page.Margin = new Padding(0);

            _extrasEmptyPage = BuildExtrasEmptyPage();
            page.Controls.Add(_extrasEmptyPage);
            return page;
        }

        /// <summary>
        /// 一个插件都没有时显示的说明页：插件目录在哪、怎么装、怎么卸。
        /// 两个按钮和底部的链接做的是同一件事，只是新用户不一定看得到底部。
        /// </summary>
        private Panel BuildExtrasEmptyPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.MainBackground;
            page.Margin = new Padding(0);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.BackColor = Color.Transparent;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, ExtrasPageHeaderHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.Controls.Add(layout);

            Label titleLabel;
            Label subtitleLabel;
            layout.Controls.Add(CreatePageHeader("附加功能",
                "插件的页面会显示在这里，左侧列表用来切换插件",
                out titleLabel, out subtitleLabel), 0, 0);

            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 1;
            body.RowCount = 6;
            body.BackColor = Color.Transparent;
            body.Margin = new Padding(0);
            body.Padding = new Padding(20, 14, 16, 24);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(body, 0, 1);

            Label heading = CreateSettingsTitleLabel("还没有安装插件");
            heading.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold,
                GraphicsUnit.Point, 134);
            body.Controls.Add(heading, 0, 0);

            body.Controls.Add(CreateSettingsHintLabel("插件目录（把插件文件夹整个复制进来）："),
                0, 1);

            string root = ExtraPaths.RootDirectory();
            var path = new Label();
            path.Text = string.IsNullOrEmpty(root) ? "（无法确定程序目录）" : root;
            path.Dock = DockStyle.Fill;
            path.ForeColor = AppTheme.TextSoft;
            path.BackColor = Color.Transparent;
            path.Font = new Font("Consolas", 9F, FontStyle.Regular,
                GraphicsUnit.Point, 134);
            path.TextAlign = ContentAlignment.MiddleLeft;
            path.AutoEllipsis = true;
            path.Margin = new Padding(0);
            body.Controls.Add(path, 0, 2);

            body.Controls.Add(CreateSettingsHintLabel(
                "一个文件夹一个插件，里面要有 plugin.txt 和对应的 dll。\r\n" +
                "装好后点 重新扫描；删除文件夹即卸载。程序运行中替换 dll 需要重启。"),
                0, 3);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.BackColor = Color.Transparent;
            buttons.Margin = new Padding(0);
            buttons.Padding = new Padding(0);

            RoundedButton openButton = CreateExtrasActionButton("打开 extra 文件夹",
                true, 150);
            openButton.Click += delegate { OpenExtrasFolder(); };
            buttons.Controls.Add(openButton);

            RoundedButton rescanButton = CreateExtrasActionButton("重新扫描",
                false, 108);
            rescanButton.Click += delegate { ReloadExtras(true); };
            buttons.Controls.Add(rescanButton);
            body.Controls.Add(buttons, 0, 4);

            return page;
        }

        /// <summary>
        /// 附加功能页上的按钮。accent 为 true 时用蓝色强调填充（白字），
        /// 否则和 设置 里的小按钮一样是瓷砖底色。
        /// </summary>
        private RoundedButton CreateExtrasActionButton(string text, bool accent,
            int width)
        {
            var button = new RoundedButton();
            button.Text = text;
            button.AccessibleName = text;
            button.Width = width;
            button.Height = 32;
            button.CornerRadius = 8;
            button.SurfaceColor = accent
                ? AppTheme.Accent
                : AppTheme.TileBackground;
            button.NormalColor = accent
                ? AppTheme.Accent
                : AppTheme.TileBackground;
            button.HoverColor = accent
                ? AppTheme.AccentHover
                : AppTheme.Divider;
            button.PressedColor = accent
                ? AppTheme.AccentPressed
                : AppTheme.Border;
            button.ForeColor = accent
                ? AppTheme.ForegroundOn(AppTheme.Accent)
                : AppTheme.TextSoft;
            button.Font = _settingsTitleFont;
            button.Margin = new Padding(0, 6, 8, 0);
            button.Cursor = Cursors.Hand;
            return button;
        }

        /// <summary>
        /// 一个插件的页面：页头 62px + 插件自己的内容。插件内容创建一次就
        /// 留着，切换插件只是换可见性；创建失败或插件本身加载失败时，内容区
        /// 换成错误页，把原因和日志入口直接摆在用户面前。
        /// </summary>
        private Panel BuildExtraFrame(ExtraEntry entry)
        {
            var frame = new TableLayoutPanel();
            frame.Dock = DockStyle.Fill;
            frame.ColumnCount = 1;
            frame.RowCount = 2;
            frame.BackColor = AppTheme.MainBackground;
            frame.Margin = new Padding(0);
            frame.Padding = new Padding(0);
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            frame.RowStyles.Add(
                new RowStyle(SizeType.Absolute, ExtrasPageHeaderHeight));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            string subtitle = entry.Info.Description;
            if (string.IsNullOrEmpty(subtitle))
                subtitle = "附加功能插件";
            if (!string.IsNullOrEmpty(entry.Info.Version))
                subtitle = subtitle + " · v" + entry.Info.Version;

            Label titleLabel;
            Label subtitleLabel;
            frame.Controls.Add(CreatePageHeader(entry.Info.Name, subtitle,
                out titleLabel, out subtitleLabel), 0, 0);

            var body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = AppTheme.MainBackground;
            body.Margin = new Padding(0);
            body.Padding = new Padding(0, 4, 16, 24);
            frame.Controls.Add(body, 0, 1);

            Control content = null;
            if (!entry.Failed && entry.Plugin != null)
            {
                try
                {
                    content = entry.Plugin.CreateContent(entry.Host);
                }
                catch (Exception ex)
                {
                    entry.Error = "插件页面创建失败：" + ex.Message;
                    AppendExtrasLog("extra/" + entry.Manifest.Id + "：" +
                        entry.Error);
                    content = null;
                }
            }

            if (content == null)
                content = BuildExtraErrorBody(entry);

            content.Dock = DockStyle.Fill;
            body.Controls.Add(content);
            frame.Tag = content;
            return frame;
        }

        /// <summary>
        /// 插件的错误页。文字说明为什么没加载起来，三个按钮都指向排查用的
        /// 地方：插件自己的文件夹、extras.log、重新扫描（装好新文件后不用重启
        /// 就能看到新插件）。
        /// </summary>
        private Panel BuildExtraErrorBody(ExtraEntry entry)
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = AppTheme.MainBackground;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(4, 10, 10, 10);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.BackColor = Color.Transparent;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(6, 0, 0, 0);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.Controls.Add(layout);

            Label heading = CreateSettingsTitleLabel("插件加载失败");
            heading.ForeColor = AppTheme.Danger;
            heading.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold,
                GraphicsUnit.Point, 134);
            layout.Controls.Add(heading, 0, 0);

            string reason = entry.Failed
                ? entry.Error
                : "插件没有返回页面（CreateContent 返回了 null）。";
            if (string.IsNullOrEmpty(reason))
                reason = "未知错误。";

            var message = new Label();
            message.Text = reason + "\r\n" + entry.Info.FolderPath;
            message.Dock = DockStyle.Fill;
            message.ForeColor = AppTheme.TextSoft;
            message.BackColor = Color.Transparent;
            message.Font = _settingsHintFont;
            message.TextAlign = ContentAlignment.TopLeft;
            message.Margin = new Padding(0);
            layout.Controls.Add(message, 0, 1);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.BackColor = Color.Transparent;
            buttons.Margin = new Padding(0);
            buttons.Padding = new Padding(0);

            string folder = entry.Info.FolderPath;
            RoundedButton folderButton = CreateExtrasActionButton("打开插件目录",
                false, 126);
            folderButton.Click += delegate { OpenFolderInShell(folder); };
            buttons.Controls.Add(folderButton);

            RoundedButton logButton = CreateExtrasActionButton("打开日志",
                false, 104);
            logButton.Click += delegate { OpenExtrasLog(); };
            buttons.Controls.Add(logButton);

            RoundedButton rescanButton = CreateExtrasActionButton("重新扫描",
                false, 108);
            rescanButton.Click += delegate { ReloadExtras(true); };
            buttons.Controls.Add(rescanButton);
            layout.Controls.Add(buttons, 0, 2);

            return panel;
        }

        /// <summary>
        /// 进入附加功能：主栏收成图标栏、第二级栏换成插件列表、内容区显示
        /// 上次选中的插件（第一次进入时选列表里的第一个）。流程和
        /// <c>ShowSettings</c> 一一对应，包括退出卡组详情和多选模式。
        /// </summary>
        private void ShowExtras()
        {
            _viewMode = ViewMode.Extras;
            CloseDeckDetail();
            _navColumnStyle.Width = NavCompactWidth;
            _filterColumnStyle.Width = SettingsSidebarWidth;
            _filterHost.Visible = false;
            SetNavExpanded(false);
            SetDeckSubItemsVisible(false);
            SetActiveNavItem(_navExtrasButton);

            if (_extrasSidebarHost != null)
            {
                _extrasSidebarHost.Visible = true;
                _extrasSidebarHost.BringToFront();
            }

            EnsureExtrasLoaded();
            ShowExtrasPage(FindExtraEntry(_activeExtraId));
            ShowContentPage(_extrasPage);
            UpdateNewDeckStripVisibility();
            ExitCollectionMarkMode();
            UpdateCollectionChrome();
            ScheduleDarkChrome();
        }

        /// <summary>
        /// 显示一个插件（null 表示一个都没有）。插件内容是懒创建的，
        /// 所以这里才真正执行插件的 <see cref="IExtraPlugin.CreateContent"/>。
        /// </summary>
        private void ShowExtrasPage(ExtraEntry entry)
        {
            if (entry == null)
            {
                _activeExtraId = null;
                ShowExtrasChild(_extrasEmptyPage, null);
            }
            else
            {
                _activeExtraId = entry.Manifest.Id;
                Panel frame = GetOrCreateExtraFrame(entry);
                ShowExtrasChild(frame, frame.Tag as Control);
            }

            HighlightExtraButton();
        }

        /// <summary>
        /// 在内容页里切换子页面，并通知前后两个插件页面激活 / 离开。
        /// 回调是第三方代码，异常只进日志，不打断切换。
        /// </summary>
        private void ShowExtrasChild(Control child, Control pluginContent)
        {
            if (_extrasPage == null || child == null)
                return;

            if (ReferenceEquals(_extrasActiveChild, child))
            {
                child.Visible = true;
                child.BringToFront();
                return;
            }

            NotifyExtraPage(_extrasActivePluginContent, false);
            for (int i = 0; i < _extrasPage.Controls.Count; i++)
            {
                Control sibling = _extrasPage.Controls[i];
                sibling.Visible = ReferenceEquals(sibling, child);
            }

            if (!_extrasPage.Controls.Contains(child))
                _extrasPage.Controls.Add(child);

            child.Visible = true;
            child.BringToFront();
            _extrasActiveChild = child;
            _extrasActivePluginContent = pluginContent;
            NotifyExtraPage(pluginContent, true);
        }

        /// <summary>
        /// 离开附加功能（切到系列 / 设置 / 卡组……）时通知当前插件页，并清掉
        /// “当前子页”的记录：下次进来 <see cref="ShowExtrasChild"/> 会重新
        /// 走一遍显示流程，插件的 OnActivated 因此每次都会收到。控件本身保留，
        /// 状态不会丢。
        /// </summary>
        private void DeactivateExtrasChild()
        {
            if (_extrasActivePluginContent == null)
                return;

            NotifyExtraPage(_extrasActivePluginContent, false);
            _extrasActiveChild = null;
            _extrasActivePluginContent = null;
        }

        /// <summary>插件页的可选生命周期回调，出错只写日志。</summary>
        private void NotifyExtraPage(Control content, bool activated)
        {
            IExtraPage page = content as IExtraPage;
            if (page == null)
                return;

            try
            {
                if (activated)
                    page.OnActivated();
                else
                    page.OnDeactivated();
            }
            catch (Exception ex)
            {
                AppendExtrasLog("插件页面回调 " +
                    (activated ? "OnActivated" : "OnDeactivated") +
                    " 失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 第一次进入附加功能时才扫描磁盘：没有插件的用户不会为这个页面
        /// 付任何启动时间。
        /// </summary>
        private void EnsureExtrasLoaded()
        {
            if (_extrasEntries != null)
                return;

            ReloadExtras(false);
        }

        /// <summary>
        /// 扫描 extra\ 并合并到已有列表。已经在列表里的插件（按 id）不重新
        /// 加载 —— dll 已经被 LoadFrom 锁定，重扫的意义只是发现新文件夹；
        /// 这样也不会把插件的状态（页面、缓存）弄丢。
        /// </summary>
        private void ReloadExtras(bool interactive)
        {
            string root = ExtraPaths.RootDirectory();
            IList<ExtraEntry> found = ExtraLoader.Discover(root, AppendExtrasLog);
            for (int i = 0; i < found.Count; i++)
            {
                ExtraEntry entry = found[i];
                entry.Host = new ExtraHost(this, entry.Info,
                    ExtraPaths.LogFilePath());
            }

            if (_extrasEntries == null)
            {
                _extrasEntries = found;
            }
            else
            {
                var merged = new List<ExtraEntry>(_extrasEntries);
                for (int i = 0; i < found.Count; i++)
                {
                    ExtraEntry entry = found[i];
                    if (FindExtraEntryIn(merged, entry.Manifest.Id) != null)
                    {
                        AppendExtrasLog("重新扫描：跳过已加载的插件 " +
                            entry.Manifest.Id);
                        continue;
                    }

                    merged.Add(entry);
                }

                merged.Sort(delegate(ExtraEntry left, ExtraEntry right)
                {
                    return ExtraCatalog.Compare(left.Manifest, right.Manifest);
                });
                _extrasEntries = merged;
            }

            RebuildExtrasSidebar();

            if (interactive)
                ShowExtrasPage(FindExtraEntry(_activeExtraId));
            else if (FindExtraEntry(_activeExtraId) == null &&
                _extrasEntries.Count > 0)
            {
                _activeExtraId = _extrasEntries[0].Manifest.Id;
            }
        }

        private ExtraEntry FindExtraEntry(string id)
        {
            if (_extrasEntries == null)
                return null;

            return FindExtraEntryIn(_extrasEntries, id);
        }

        private static ExtraEntry FindExtraEntryIn(IList<ExtraEntry> entries,
            string id)
        {
            if (entries == null || string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Manifest.Id, id,
                    StringComparison.Ordinal))
                {
                    return entries[i];
                }
            }

            return null;
        }

        /// <summary>插件页按需创建，之后一直复用（切换插件只改可见性）。</summary>
        private Panel GetOrCreateExtraFrame(ExtraEntry entry)
        {
            Panel frame;
            if (_extrasFrames.TryGetValue(entry.Manifest.Id, out frame) &&
                frame != null)
            {
                return frame;
            }

            frame = BuildExtraFrame(entry);
            _extrasFrames[entry.Manifest.Id] = frame;
            return frame;
        }

        /// <summary>
        /// 重画第二级栏的插件按钮。第 0 个控件是"插件"小标题，不参与重建；
        /// 列表为空时放一段说明文字，而不是留一片空白让用户以为坏了。
        /// </summary>
        private void RebuildExtrasSidebar()
        {
            if (_extrasStack == null)
                return;

            for (int i = _extrasStack.Controls.Count - 1; i >= 1; i--)
            {
                Control child = _extrasStack.Controls[i];
                _extrasStack.Controls.RemoveAt(i);
                child.Dispose();
            }

            _extrasButtons.Clear();

            if (_extrasEntries == null || _extrasEntries.Count == 0)
            {
                var empty = new Label();
                empty.Text = "还没有插件。\r\n把插件文件夹复制到\r\n" +
                    "DeckBuilder\\extra 后\r\n点底部 重新扫描。";
                empty.Width = ExtrasItemWidth;
                empty.Height = 82;
                empty.ForeColor = AppTheme.TextMuted;
                empty.BackColor = Color.Transparent;
                empty.Font = new Font("Microsoft YaHei UI", 8.5F,
                    FontStyle.Regular, GraphicsUnit.Point, 134);
                empty.TextAlign = ContentAlignment.TopLeft;
                empty.Margin = new Padding(0, 0, 0, 6);
                _extrasStack.Controls.Add(empty);
            }
            else
            {
                for (int i = 0; i < _extrasEntries.Count; i++)
                {
                    RoundedButton button = CreateExtraButton(_extrasEntries[i]);
                    _extrasStack.Controls.Add(button);
                    _extrasButtons.Add(button);
                }
            }

            UpdateExtrasHeader();
            HighlightExtraButton();
        }

        /// <summary>
        /// 侧边栏里的一行插件。加载失败的插件仍然列出来，用错误色和
        /// "（加载失败）"的后缀提示，点开就是错误页。
        /// </summary>
        private RoundedButton CreateExtraButton(ExtraEntry entry)
        {
            bool failed = entry.Failed;
            string text = entry.Info.Name;
            if (string.IsNullOrEmpty(text))
                text = entry.Manifest.Id;
            if (failed)
                text = text + "（加载失败）";

            var button = new RoundedButton();
            button.Text = text;
            button.AccessibleName = text;
            button.Tag = entry.Manifest.Id;
            button.Width = ExtrasItemWidth;
            button.Height = ExtrasItemHeight;
            button.CornerRadius = 10;
            button.SurfaceColor = Color.Empty;
            button.NormalColor = Color.Transparent;
            button.HoverColor = AppTheme.Divider;
            button.PressedColor = AppTheme.Border;
            button.SelectedColor = AppTheme.Divider;
            button.ForeColor = failed ? AppTheme.Danger : AppTheme.TextSoft;
            button.Font = _navSubItemFont;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(12, 0, 0, 0);
            button.Margin = new Padding(0, 0, 0, 6);
            button.Cursor = Cursors.Hand;
            button.Click += ExtraButton_Click;

            if (_extrasToolTip != null)
            {
                string tip = entry.Info.Description;
                if (string.IsNullOrEmpty(tip))
                    tip = entry.Info.Name;
                if (!string.IsNullOrEmpty(entry.Info.Version))
                    tip = tip + " · v" + entry.Info.Version;
                if (failed)
                {
                    tip = "加载失败：" + entry.Error + "\r\n" +
                        entry.Info.FolderPath;
                }

                _extrasToolTip.SetToolTip(button, tip);
            }

            return button;
        }

        private void ExtraButton_Click(object sender, EventArgs e)
        {
            RoundedButton button = sender as RoundedButton;
            if (button == null)
                return;

            string id = button.Tag as string;
            if (string.IsNullOrEmpty(id))
                return;

            _activeExtraId = id;
            ShowExtrasPage(FindExtraEntry(id));
        }

        /// <summary>把当前插件那一行画成选中态（其余行回落到普通色）。</summary>
        private void HighlightExtraButton()
        {
            if (_extrasEntries == null)
                return;

            for (int i = 0; i < _extrasButtons.Count && i < _extrasEntries.Count;
                i++)
            {
                ExtraEntry entry = _extrasEntries[i];
                RoundedButton button = _extrasButtons[i];
                button.Selected = string.Equals(entry.Manifest.Id,
                    _activeExtraId, StringComparison.Ordinal);
                button.ForeColor = entry.Failed
                    ? AppTheme.Danger
                    : AppTheme.TextSoft;
                button.Invalidate();
            }
        }

        /// <summary>页头第二行的"共 N 个 · extra"，鼠标悬停给出完整路径。</summary>
        private void UpdateExtrasHeader()
        {
            if (_extrasSubtitle == null)
                return;

            int count = _extrasEntries == null ? 0 : _extrasEntries.Count;
            if (count == 0)
            {
                _extrasSubtitle.Text = "未安装插件 · " + ExtraPaths.FolderName;
            }
            else
            {
                int failed = 0;
                for (int i = 0; i < _extrasEntries.Count; i++)
                {
                    if (_extrasEntries[i].Failed)
                        failed++;
                }

                _extrasSubtitle.Text = failed == 0
                    ? "共 " + count + " 个 · " + ExtraPaths.FolderName
                    : "共 " + count + " 个 · " + failed + " 个加载失败";
            }

            if (_extrasToolTip != null)
            {
                _extrasToolTip.SetToolTip(_extrasSubtitle,
                    ExtraPaths.RootDirectory());
            }
        }

        /// <summary>
        /// 往 <c>extras.log</c> 追加一行（时间戳 + 内容）。日志写失败只能
        /// 放弃：插件加载失败的提示已经在界面上，日志只是给回帖用的附件。
        /// </summary>
        private void AppendExtrasLog(string message)
        {
            string path = ExtraPaths.LogFilePath();
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                File.AppendAllText(path, DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        /// <summary>打开插件根目录；目录不存在就先建出来，用户可以直接往里拖。</summary>
        private void OpenExtrasFolder()
        {
            string root = ExtraPaths.RootDirectory();
            if (string.IsNullOrEmpty(root))
            {
                MessageBox.Show(this, "无法确定程序目录，插件目录不可用。",
                    "附加功能", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (!Directory.Exists(root))
                    Directory.CreateDirectory(root);
            }
            catch (IOException ex)
            {
                ShowExtrasOpenError(root, ex);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                ShowExtrasOpenError(root, ex);
                return;
            }

            OpenFolderInShell(root);
        }

        /// <summary>打开某个插件的文件夹；文件夹已被删掉时只提示。</summary>
        private void OpenFolderInShell(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "插件目录已经不存在：" + folder,
                    "附加功能", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex)
            {
                ShowExtrasOpenError(folder, ex);
            }
        }

        /// <summary>用记事本打开 extras.log；还没有日志时提示写日志的时机。</summary>
        private void OpenExtrasLog()
        {
            string path = ExtraPaths.LogFilePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show(this,
                    "还没有 extras.log：插件加载失败或清单有警告时才会写入。",
                    "附加功能", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                ShowExtrasOpenError(path, ex);
            }
        }

        private void ShowExtrasOpenError(string path, Exception error)
        {
            MessageBox.Show(this, "无法打开：" + path + "\r\n" + error.Message,
                "附加功能", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// 换主题后通知每个插件重新取色。插件自绘的颜色不在
        /// <see cref="AppTheme.Recolor"/> 的覆盖范围里，只能由插件自己处理。
        /// </summary>
        private void RaiseExtrasThemeChanged()
        {
            if (_extrasEntries == null)
                return;

            for (int i = 0; i < _extrasEntries.Count; i++)
            {
                ExtraHost host = _extrasEntries[i].Host as ExtraHost;
                if (host != null)
                    host.RaiseThemeChanged();
            }
        }

        /// <summary>插件要求刷新卡组列表时调用；卡组页还没打开过就什么都不用做。</summary>
        private void ReloadDeckLibraryFromExtras()
        {
            if (_deckLibrary == null)
                return;

            _deckLibrary.Reload();
        }

        /// <summary>
        /// 插件点了某张卡：只给一个卡号，先按卡号前缀在 SingleSetData 元数据里
        /// 反查系列（这一步不解析任何 CardData.txt），再让 CardDatabase 按它
        /// 自己的缓存加载这个系列，所以详情层里的卡图、日文名、异画和卡表里
        /// 看到的完全一致。找到就打开详情层返回 true，卡表里没有这张卡返回 false。
        /// </summary>
        private bool ShowCardDetailByCode(string cardCode)
        {
            // 第一次点某个系列时要现读它的 CardData.txt，鼠标换成等待，
            // 卡片加载完再弹详情层，界面不会像卡死一样。
            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            WeissCard card;
            try
            {
                card = FindCardByCode(cardCode);
            }
            finally
            {
                Cursor = previousCursor;
            }

            if (card == null)
            {
                if (!string.IsNullOrEmpty(cardCode))
                    AppendExtrasLog("找不到卡片：" + cardCode.Trim());
                return false;
            }

            ShowCardDetail(card);
            return true;
        }

        private WeissCard FindCardByCode(string cardCode)
        {
            if (_database == null || string.IsNullOrEmpty(cardCode))
                return null;

            string code = cardCode.Trim();
            if (code.Length == 0)
                return null;

            WeissCard cached;
            if (_extraCardLookup.TryGetValue(code, out cached))
                return cached;

            SeriesDefinition metadata = CardCodeLocator.FindSeries(_database, code);
            if (metadata == null)
                return null;

            SeriesDefinition series;
            try
            {
                series = _database.GetSeries(metadata.Name);
            }
            catch (Exception ex)
            {
                AppendExtrasLog("加载系列失败：" + metadata.Name + "（" +
                    ex.Message + "）");
                return null;
            }

            WeissCard found = CardCodeLocator.FindCard(series, code);
            if (found != null)
                _extraCardLookup[code] = found;
            return found;
        }

        /// <summary>
        /// 交给插件的主程序接口。每次进入附加功能页时按插件各建一个，
        /// 所以 <see cref="Info"/> 天然是"自己那一份"，插件之间互不干扰。
        ///
        /// 除了这个接口，插件不需要引用主程序的任何内部类型；卡表、触发名、
        /// 卡组都是现成的只读入口，写文件仍然走插件自己的目录。
        /// </summary>
        private sealed class ExtraHost : IExtraHost
        {
            private readonly MainForm _form;
            private readonly ExtraInfo _info;
            private readonly string _logPath;

            public ExtraHost(MainForm form, ExtraInfo info, string logPath)
            {
                _form = form;
                _info = info ?? new ExtraInfo();
                _logPath = logPath ?? string.Empty;
            }

            public ExtraInfo Info
            {
                get { return _info; }
            }

            public Form Owner
            {
                get { return _form; }
            }

            public string DataRoot
            {
                get
                {
                    return _form._database == null
                        ? string.Empty
                        : _form._database.DataRoot;
                }
            }

            public CardDatabase Database
            {
                get { return _form._database; }
            }

            public TriggerGlossary Glossary
            {
                get { return _form._triggerGlossary; }
            }

            public AppThemeMode Theme
            {
                get { return AppTheme.Mode; }
            }

            public string LogPath
            {
                get { return _logPath; }
            }

            public event EventHandler ThemeChanged;

            public DeckLibrary LoadDecks()
            {
                string root = DataRoot;
                return string.IsNullOrEmpty(root)
                    ? null
                    : DeckLibrary.Load(root);
            }

            public void ReloadDeckLibrary()
            {
                _form.ReloadDeckLibraryFromExtras();
            }

            public void Log(string message)
            {
                string prefix = string.IsNullOrEmpty(_info.Id)
                    ? string.Empty
                    : _info.Id + "：";
                _form.AppendExtrasLog(prefix + (message ?? string.Empty));
            }

            public void ShowMessage(string message)
            {
                MessageBox.Show(_form, message ?? string.Empty, _info.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            public void ShowError(string message)
            {
                MessageBox.Show(_form, message ?? string.Empty, _info.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            public bool ShowCard(string cardCode)
            {
                return _form.ShowCardDetailByCode(cardCode);
            }

            /// <summary>主程序换完主题后转告插件。</summary>
            public void RaiseThemeChanged()
            {
                EventHandler handler = ThemeChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }
    }
}
