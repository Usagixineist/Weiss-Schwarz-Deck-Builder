using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// Second level sidebar of the settings page: the category list that takes
    /// over the slot of the series filter while 设置 is open.
    /// </summary>
    public sealed partial class MainForm
    {
        /// <summary>
        /// Settings sidebar frame. It fills the same root column as the series
        /// filter and the deck sidebar and repeats their structure — header
        /// with title and subtitle, divider, scrollable category list and a
        /// footer hint — so all three pages look alike.
        /// </summary>
        private Panel BuildSettingsSidebarHost()
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
            layout.RowCount = 4;
            layout.BackColor = Color.Transparent;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,
                SettingsSidebarHeaderHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,
                SettingsSidebarFooterHeight));
            host.Controls.Add(layout);

            // Header: breadcrumb line, title and a one line summary of the
            // categories, matching the deck sidebar header metrics.
            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 1;
            header.RowCount = 3;
            header.BackColor = Color.Transparent;
            header.Padding = new Padding(14, 9, 14, 6);
            header.Margin = new Padding(0);
            header.RowStyles.Add(new RowStyle(SizeType.Absolute,
                SettingsHeaderRowHeight));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute,
                SettingsTitleRowHeight));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute,
                SettingsSubtitleRowHeight));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // The breadcrumb doubles as the way back, exactly like the back
            // link of the deck sidebar header.
            var backLink = new LinkLabel();
            backLink.Text = "←  卡片检索";
            backLink.Dock = DockStyle.Fill;
            backLink.BackColor = Color.Transparent;
            backLink.LinkColor = AppTheme.TextSoft;
            backLink.ActiveLinkColor = Color.White;
            backLink.VisitedLinkColor = AppTheme.TextSoft;
            backLink.DisabledLinkColor = AppTheme.TextMuted;
            backLink.LinkBehavior = LinkBehavior.HoverUnderline;
            backLink.Font = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            backLink.TextAlign = ContentAlignment.MiddleLeft;
            backLink.Margin = new Padding(0);
            backLink.Cursor = Cursors.Hand;
            backLink.Click += BackButton_Click;
            header.Controls.Add(backLink, 0, 0);

            Label title = CreateDeckSidebarLabel(13F, FontStyle.Bold,
                AppTheme.Text, ContentAlignment.MiddleLeft);
            title.Text = "偏好设置";
            title.AutoEllipsis = true;
            header.Controls.Add(title, 0, 1);

            Label subtitle = CreateDeckSidebarLabel(8.5F, FontStyle.Regular,
                AppTheme.TextMuted, ContentAlignment.MiddleLeft);
            subtitle.Text = "外观 · 术语 · 存储 · 软件信息";
            subtitle.AutoEllipsis = true;
            header.Controls.Add(subtitle, 0, 2);
            layout.Controls.Add(header, 0, 0);

            var separator = new Panel();
            separator.Dock = DockStyle.Fill;
            separator.BackColor = AppTheme.Divider;
            separator.Margin = new Padding(0);
            layout.Controls.Add(separator, 0, 1);

            // Category list: a slim scroll host keeps the list usable when
            // more categories arrive.
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

            var caption = new Label();
            caption.Text = "设置类目";
            caption.Width = SettingsCategoryWidth;
            caption.Height = SettingsCategoryCaptionHeight;
            caption.ForeColor = AppTheme.Text;
            caption.BackColor = Color.Transparent;
            caption.Font = new Font("Microsoft YaHei UI", 10.5F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            caption.TextAlign = ContentAlignment.MiddleLeft;
            caption.Margin = new Padding(0, 0, 0, 6);
            stack.Controls.Add(caption);

            _settingsCategoryButtons = new List<RoundedButton>();
            for (int i = 0; i < SettingsCategoryTitles.Length; i++)
            {
                RoundedButton button =
                    CreateSettingsCategoryButton(SettingsCategoryTitles[i], i);
                stack.Controls.Add(button);
                _settingsCategoryButtons.Add(button);
            }

            body.Content = stack;
            layout.Controls.Add(body, 0, 2);

            var footer = new Label();
            footer.Text = "设置项暂为占位 · 关闭程序后不保留";
            footer.Dock = DockStyle.Fill;
            footer.ForeColor = AppTheme.TextMuted;
            footer.BackColor = Color.Transparent;
            footer.Font = new Font("Microsoft YaHei UI", 8F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            footer.TextAlign = ContentAlignment.TopLeft;
            footer.Padding = new Padding(14, 8, 14, 10);
            footer.Margin = new Padding(0);
            layout.Controls.Add(footer, 0, 3);

            return host;
        }

        /// <summary>
        /// One entry of the category list. The sidebar paints a gradient, so
        /// the button keeps an empty surface and a clear resting fill; only
        /// hover and the active category paint the rounded item.
        /// </summary>
        private RoundedButton CreateSettingsCategoryButton(string text, int index)
        {
            var button = new RoundedButton();
            button.Text = text;
            button.AccessibleName = text;
            button.Tag = index;
            button.Width = SettingsCategoryWidth;
            button.Height = SettingsCategoryRowHeight;
            button.CornerRadius = 10;
            button.NormalColor = Color.Transparent;
            button.HoverColor = NavItemHoverColor;
            button.PressedColor = NavItemPressedColor;
            button.SelectedColor = NavItemHoverColor;
            button.ForeColor = AppTheme.TextSoft;
            button.Font = _navSubItemFont;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(12, 0, 0, 0);
            button.Margin = new Padding(0, 0, 0, 6);
            button.Cursor = Cursors.Hand;
            button.Click += SettingsCategory_Click;
            return button;
        }

        /// <summary>
        /// Shows one category page and highlights its entry. The pages are
        /// built once, so switching only toggles visibility.
        /// </summary>
        private void ShowSettingsCategory(int index)
        {
            int count = _settingsCategoryPages == null
                ? 0
                : _settingsCategoryPages.Length;
            if (count == 0)
            {
                _settingsCategory = Math.Max(0, index);
                return;
            }

            int clamped = Math.Max(0, Math.Min(count - 1, index));
            _settingsCategory = clamped;

            for (int i = 0; i < count; i++)
            {
                Panel page = _settingsCategoryPages[i];
                if (page != null)
                    page.Visible = i == clamped;
            }

            if (_settingsCategoryButtons != null)
            {
                for (int i = 0; i < _settingsCategoryButtons.Count; i++)
                {
                    RoundedButton button = _settingsCategoryButtons[i];
                    if (button == null)
                        continue;

                    bool active = i == clamped;
                    button.Selected = active;
                    button.ForeColor = active ? Color.White : AppTheme.TextSoft;
                    button.Invalidate();
                }
            }
        }

        private void SettingsCategory_Click(object sender, EventArgs e)
        {
            RoundedButton button = sender as RoundedButton;
            if (button == null || !(button.Tag is int))
                return;

            ShowSettingsCategory((int)button.Tag);
        }
    }
}
