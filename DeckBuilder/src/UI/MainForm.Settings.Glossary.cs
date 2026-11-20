using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 术语 page: the editable 触发名称 table. The card table always keeps the
    /// English trigger names (Soul / Book / Gate …), this table only decides
    /// what the interface calls them, so edits never touch game data.
    /// </summary>
    public sealed partial class MainForm
    {
        private const int TriggerGlossaryRowHeight = 26;
        private const int TriggerGlossaryHeaderHeight = 22;
        private const int TriggerGlossaryHintHeight = 40;
        private const int TriggerGlossaryStatusHeight = 24;

        /// <summary>
        /// Two 英文名 + 显示名称 pairs share one row, so the 23 editable icons
        /// need 12 rows instead of 23 and the section stays short enough to sit
        /// in the page wide scroll instead of a nested one.
        /// </summary>
        private const int TriggerGlossaryPairsPerRow = 2;

        private static int TriggerGlossaryRowCount
        {
            get
            {
                return (TriggerGlossary.EditableIcons.Length +
                    TriggerGlossaryPairsPerRow - 1) / TriggerGlossaryPairsPerRow;
            }
        }

        private static int TriggerGlossaryTableHeight
        {
            get
            {
                return TriggerGlossaryHeaderHeight +
                    TriggerGlossaryRowCount * TriggerGlossaryRowHeight;
            }
        }

        private Label _triggerGlossaryStatusLabel;
        private bool _updatingTriggerGlossaryBoxes;
        private readonly Dictionary<TriggerIcon, RoundedTextBox> _triggerGlossaryBoxes =
            new Dictionary<TriggerIcon, RoundedTextBox>();

        /// <summary>
        /// The 术语 section, appended to the settings list like any other
        /// setting: a hint with 清空全部 next to it, the editable table, then
        /// the save status. Each edit is written to
        /// <c>DeckBuilder\settings.txt</c> straight away, so the
        /// names survive a restart without a 保存 button.
        /// </summary>
        private void AppendTriggerGlossarySection()
        {
            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.BackColor = Color.Transparent;
            header.Margin = new Padding(0);
            header.Padding = new Padding(0);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var hintLabel = new Label();
            hintLabel.Text =
                "每行两组：左边是卡表里的英文名，右边填界面要显示的名称（例：Book → 书）。" +
                "留空或填英文名都会回到英文名，改完立即保存。";
            hintLabel.Dock = DockStyle.Fill;
            hintLabel.ForeColor = AppTheme.TextMuted;
            hintLabel.BackColor = Color.Transparent;
            hintLabel.Font = _settingsHintFont;
            hintLabel.TextAlign = ContentAlignment.TopLeft;
            hintLabel.Margin = new Padding(0);

            var resetButton = new RoundedButton();
            resetButton.Text = "清空全部";
            resetButton.AccessibleName = "清空全部触发名称";
            resetButton.Dock = DockStyle.Fill;
            resetButton.CornerRadius = 8;
            resetButton.SurfaceColor = AppTheme.TileBackground;
            resetButton.NormalColor = AppTheme.InputBackground;
            resetButton.HoverColor = AppTheme.Border;
            resetButton.PressedColor = AppTheme.Divider;
            resetButton.ForeColor = AppTheme.Text;
            resetButton.Font = _settingsHintFont;
            resetButton.Margin = new Padding(8, 0, 0, 8);
            resetButton.Click += delegate
            {
                _triggerGlossary.Reset();
                RefreshTriggerGlossaryBoxes();
                SaveTriggerGlossaryTable();
            };

            header.Controls.Add(hintLabel, 0, 0);
            header.Controls.Add(resetButton, 1, 0);
            AppendSettingsRow(header, TriggerGlossaryHintHeight);

            var list = new TableLayoutPanel();
            list.Dock = DockStyle.Fill;
            list.ColumnCount = TriggerGlossaryPairsPerRow * 2;
            list.RowCount = TriggerGlossaryRowCount + 1;
            list.BackColor = Color.Transparent;
            list.Margin = new Padding(0);
            list.Padding = new Padding(0);
            for (int pair = 0; pair < TriggerGlossaryPairsPerRow; pair++)
            {
                list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
                list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            }
            list.RowStyles.Add(
                new RowStyle(SizeType.Absolute, TriggerGlossaryHeaderHeight));
            FillTriggerGlossaryList(list);
            AppendSettingsRow(list, TriggerGlossaryTableHeight);

            var statusLabel = new Label();
            statusLabel.Text = "共 " + TriggerGlossary.EditableIcons.Length +
                " 个触发图标，改动会立即写入 " + SettingsStore.FileName + "。";
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.ForeColor = AppTheme.TextMuted;
            statusLabel.BackColor = Color.Transparent;
            statusLabel.Font = _settingsHintFont;
            statusLabel.TextAlign = ContentAlignment.BottomLeft;
            statusLabel.Margin = new Padding(0);
            _triggerGlossaryStatusLabel = statusLabel;
            AppendSettingsRow(statusLabel, TriggerGlossaryStatusHeight);
        }

        /// <summary>
        /// Fills the table with two 英文名 + 文本框 pairs per row, so the
        /// editable triggers need half as many rows as before. Every height is
        /// absolute, which keeps the section height predictable for the page
        /// wide scroll host without measuring any text.
        /// </summary>
        private void FillTriggerGlossaryList(TableLayoutPanel list)
        {
            _triggerGlossaryBoxes.Clear();

            for (int pair = 0; pair < TriggerGlossaryPairsPerRow; pair++)
            {
                int column = pair * 2;
                list.Controls.Add(CreateTriggerGlossaryHeaderLabel(
                    "英文名（卡表）",
                    pair == 0
                        ? new Padding(0, 0, 8, 2)
                        : new Padding(18, 0, 8, 2)), column, 0);
                list.Controls.Add(CreateTriggerGlossaryHeaderLabel(
                    "显示名称", new Padding(0, 0, 0, 2)), column + 1, 0);
            }

            _updatingTriggerGlossaryBoxes = true;
            for (int i = 0; i < TriggerGlossary.EditableIcons.Length; i++)
            {
                TriggerIcon icon = TriggerGlossary.EditableIcons[i];
                int row = i / TriggerGlossaryPairsPerRow + 1;
                int pair = i % TriggerGlossaryPairsPerRow;
                int column = pair * 2;

                var englishLabel = new Label();
                englishLabel.Text = icon.ToString();
                englishLabel.Dock = DockStyle.Fill;
                englishLabel.ForeColor = AppTheme.TextSoft;
                englishLabel.BackColor = Color.Transparent;
                englishLabel.Font = _settingsHintFont;
                englishLabel.TextAlign = ContentAlignment.MiddleLeft;
                englishLabel.Margin = pair == 0
                    ? new Padding(0, 0, 8, 0)
                    : new Padding(18, 0, 8, 0);
                englishLabel.AutoEllipsis = true;

                var box = new RoundedTextBox();
                box.Dock = DockStyle.Fill;
                box.Margin = pair == TriggerGlossaryPairsPerRow - 1
                    ? new Padding(0, 2, 0, 2)
                    : new Padding(0, 2, 18, 2);
                box.AccessibleName = icon + " 的显示名称";
                box.Text = _triggerGlossary.GetName(icon);
                box.Input.TextChanged +=
                    CreateTriggerGlossaryChangedHandler(icon, box);
                box.Input.Leave +=
                    CreateTriggerGlossaryLeaveHandler(icon, box);

                _triggerGlossaryBoxes[icon] = box;
                // One row style per row, not per icon: the second icon of the
                // pair lands in a cell that is already sized.
                if (pair == 0)
                {
                    list.RowStyles.Add(
                        new RowStyle(SizeType.Absolute,
                            TriggerGlossaryRowHeight));
                }
                list.Controls.Add(englishLabel, column, row);
                list.Controls.Add(box, column + 1, row);
            }
            _updatingTriggerGlossaryBoxes = false;
        }

        /// <summary>
        /// Column caption of the glossary table. The second pair of columns
        /// gets extra left margin so the two pairs read as separate blocks.
        /// </summary>
        private Label CreateTriggerGlossaryHeaderLabel(string text,
            Padding margin)
        {
            var header = new Label();
            header.Text = text;
            header.Dock = DockStyle.Fill;
            header.ForeColor = AppTheme.TextMuted;
            header.BackColor = Color.Transparent;
            header.Font = _settingsHintFont;
            header.TextAlign = ContentAlignment.BottomLeft;
            header.Margin = margin;
            return header;
        }

        /// <summary>
        /// Keeps the table in sync while the user types. The English name is
        /// what the fallback would show anyway, so typing it clears the custom
        /// name instead of storing a redundant line.
        /// </summary>
        private EventHandler CreateTriggerGlossaryChangedHandler(TriggerIcon icon,
            RoundedTextBox box)
        {
            return delegate
            {
                if (_updatingTriggerGlossaryBoxes)
                    return;

                string text = box.Text == null ? string.Empty : box.Text.Trim();
                if (string.Equals(text, icon.ToString(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    text = string.Empty;
                }

                string current = _triggerGlossary.IsCustomized(icon)
                    ? _triggerGlossary.GetName(icon)
                    : string.Empty;
                if (string.Equals(current, text, StringComparison.Ordinal))
                    return;

                _triggerGlossary.SetName(icon, text);
                SaveTriggerGlossaryTable();
            };
        }

        /// <summary>
        /// An emptied box falls back to the English name, so the field shows
        /// what the interface now displays instead of staying blank.
        /// </summary>
        private EventHandler CreateTriggerGlossaryLeaveHandler(TriggerIcon icon,
            RoundedTextBox box)
        {
            return delegate
            {
                string expected = _triggerGlossary.GetName(icon);
                if (string.Equals(box.Text, expected, StringComparison.Ordinal))
                    return;

                _updatingTriggerGlossaryBoxes = true;
                box.Text = expected;
                _updatingTriggerGlossaryBoxes = false;
            };
        }

        /// <summary>Rewrites every box from the table, e.g. after 清空全部.</summary>
        private void RefreshTriggerGlossaryBoxes()
        {
            _updatingTriggerGlossaryBoxes = true;
            foreach (KeyValuePair<TriggerIcon, RoundedTextBox> pair in
                _triggerGlossaryBoxes)
            {
                TriggerIcon icon = pair.Key;
                pair.Value.Text = _triggerGlossary.GetName(icon);
            }
            _updatingTriggerGlossaryBoxes = false;
        }

        /// <summary>
        /// Writes the table and reports the result under the list: green with
        /// the file name on success, red with the reason otherwise.
        /// </summary>
        private void SaveTriggerGlossaryTable()
        {
            string error;
            bool saved = _triggerGlossary.Save(out error);

            if (_triggerGlossaryStatusLabel != null)
            {
                _triggerGlossaryStatusLabel.Text = saved
                    ? "已保存：" + SettingsStore.FileName + "（" +
                        CountCustomTriggerNames() + " 项自定义名称）"
                    : error;
                _triggerGlossaryStatusLabel.ForeColor = saved
                    ? AppTheme.Success
                    : AppTheme.Danger;
            }

            RefreshTriggerNames();
        }

        private int CountCustomTriggerNames()
        {
            int count = 0;
            for (int i = 0; i < TriggerGlossary.EditableIcons.Length; i++)
            {
                if (_triggerGlossary.IsCustomized(TriggerGlossary.EditableIcons[i]))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Repaints the places that print a trigger name. The card table keeps
        /// the English names, so only the 卡组信息 tables have to follow the
        /// edited glossary.
        /// </summary>
        private void RefreshTriggerNames()
        {
            if (_deckStats != null)
                _deckStats.RefreshNames();

            if (_deckStatsGroup != null && _deckStats != null)
            {
                int height = _deckStats.MeasureHeight() +
                    DeckSidebarGroupCaptionHeight;
                if (_deckStatsGroup.Height != height)
                    _deckStatsGroup.Height = height;
            }

            if (_newDeckStrip != null)
                _newDeckStrip.RefreshTriggerNames();
        }
    }
}
