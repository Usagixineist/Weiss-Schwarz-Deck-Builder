using System;
using System.Collections.Generic;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// 触发名称对照表。卡表里存的一直是英文枚举名（<c>Trigger Book</c>），
    /// 界面按这张表显示中文名。用户改过的名字和别的设置一起写进唯一的那一个
    /// <see cref="SettingsStore"/> 文件（分组 <c>[terminology]</c>），删掉该文件
    /// 就等于恢复内置的名字，所以升级程序不会覆盖用户的改动。
    /// </summary>
    public sealed class TriggerGlossary
    {
        /// <summary>
        /// Triggers the settings page can rename. 无 is not a trigger at all and
        /// 魂 already reads as 魂 everywhere, so both stay out; the rest of the
        /// CX icons are listed in <see cref="TriggerIcon"/> order.
        /// </summary>
        public static readonly TriggerIcon[] EditableIcons =
        {
            TriggerIcon.DoubleSoul,
            TriggerIcon.Book,
            TriggerIcon.Wind,
            TriggerIcon.Bag,
            TriggerIcon.Door,
            TriggerIcon.Gate,
            TriggerIcon.Shot,
            TriggerIcon.Bar,
            TriggerIcon.Standby,
            TriggerIcon.Choice,
            TriggerIcon.LoneStandby,
            TriggerIcon.LoneWind,
            TriggerIcon.LoneGate,
            TriggerIcon.LoneShot,
            TriggerIcon.Telescope,
            TriggerIcon.LightBulb,
            TriggerIcon.Focus,
            TriggerIcon.CustomIcon1,
            TriggerIcon.CustomIcon2,
            TriggerIcon.CustomIcon3,
            TriggerIcon.CustomIcon4,
            TriggerIcon.CustomIcon5,
            TriggerIcon.CustomIcon6
        };

        private readonly Dictionary<TriggerIcon, string> _names =
            new Dictionary<TriggerIcon, string>();
        private readonly SettingsStore _store;

        /// <summary>
        /// 读设置文件里的名称表。文件缺失或者读不懂都只当作“还没有自定义名称”，
        /// 绝不抛异常：界面继续用内置的名字。
        /// </summary>
        public TriggerGlossary(SettingsStore store)
        {
            _store = store;
            Load();
        }

        /// <summary>Full path of the shared settings file; empty when there is none.</summary>
        public string FilePath
        {
            get { return _store == null ? string.Empty : _store.FilePath; }
        }

        /// <summary>True when the shared settings file exists and can be read.</summary>
        public bool HasFile
        {
            get { return _store != null && _store.HasFile; }
        }

        /// <summary>
        /// Built-in Chinese name of one trigger; empty when the trigger keeps
        /// its English name. 魂 / 2魂 are the names the 卡组信息 table has always
        /// used, so they work without any file.
        /// </summary>
        public static string DefaultName(TriggerIcon icon)
        {
            switch (icon)
            {
                case TriggerIcon.None: return "无";
                case TriggerIcon.Soul: return "魂";
                case TriggerIcon.DoubleSoul: return "2魂";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Name the UI shows for one trigger: the custom name first, then the
        /// built-in Chinese name, then the English enum name the card table
        /// uses. Never returns null.
        /// </summary>
        public string GetName(TriggerIcon icon)
        {
            string name;
            if (_names.TryGetValue(icon, out name) && name.Length > 0)
                return name;

            string fallback = DefaultName(icon);
            return fallback.Length > 0 ? fallback : icon.ToString();
        }

        /// <summary>True when the user named this trigger themselves.</summary>
        public bool IsCustomized(TriggerIcon icon)
        {
            return _names.ContainsKey(icon);
        }

        /// <summary>
        /// Renames one trigger in memory. A blank name drops the entry, so the
        /// trigger falls back to the English name again. Writing the file is a
        /// separate step (<see cref="Save"/>), because the caller decides when
        /// the change should reach the disk.
        /// </summary>
        public void SetName(TriggerIcon icon, string name)
        {
            if (icon == TriggerIcon.None)
                return;

            name = name == null ? string.Empty : name.Trim();
            if (name.Length == 0)
                _names.Remove(icon);
            else
                _names[icon] = name;
        }

        /// <summary>Drops every custom name, i.e. the built-in names come back.</summary>
        public void Reset()
        {
            _names.Clear();
        }

        /// <summary>
        /// 重新读一遍设置文件里属于本表的那几行。先清空再读，所以把文件删掉之后
        /// 再调用也能回到内置名字；读不懂的行只跳过，不影响其它设置。
        /// </summary>
        public void Load()
        {
            _names.Clear();
            if (_store == null)
                return;

            _store.Load();
            List<KeyValuePair<string, string>> entries =
                _store.Entries(SettingsStore.TerminologySection);
            for (int i = 0; i < entries.Count; i++)
            {
                TriggerIcon icon;
                if (!TryParseIconName(entries[i].Key, out icon))
                    continue;
                SetName(icon, entries[i].Value);
            }
        }

        /// <summary>
        /// 把改过名字的触发写回设置文件：先清空本表的分组，再按枚举顺序写一遍，
        /// 所以同一个表存两次得到同样的内容，没改过的触发也不会占一行。
        /// </summary>
        public bool Save(out string error)
        {
            if (_store == null)
            {
                error = "没有可用的设置文件，触发名称无法保存。";
                return false;
            }

            _store.ClearSection(SettingsStore.TerminologySection);
            for (int i = (int)TriggerIcon.None;
                i <= (int)TriggerIcon.CustomIcon6; i++)
            {
                var icon = (TriggerIcon)i;
                string name;
                if (!_names.TryGetValue(icon, out name) || name.Length == 0)
                    continue;

                _store.Set(SettingsStore.TerminologySection, icon.ToString(), name);
            }

            return _store.Save(out error);
        }

        /// <summary>
        /// Turns what the user typed into a trigger: a number, the English enum
        /// name, or any Chinese name the table knows (including the built-in
        /// 魂 / 无). Returns false for blank or unknown text so the caller can
        /// show its own hint.
        /// </summary>
        public static bool TryParse(string text, TriggerGlossary glossary,
            out TriggerIcon icon)
        {
            icon = TriggerIcon.None;
            if (text == null)
                return false;

            text = text.Trim();
            if (text.Length == 0)
                return false;

            int number;
            if (int.TryParse(text, out number))
            {
                if (number < (int)TriggerIcon.None ||
                    number > (int)TriggerIcon.CustomIcon6)
                {
                    return false;
                }

                icon = (TriggerIcon)number;
                return true;
            }

            TriggerIcon parsed;
            if (Enum.TryParse(text, true, out parsed))
            {
                int value = (int)parsed;
                if (value < (int)TriggerIcon.None ||
                    value > (int)TriggerIcon.CustomIcon6)
                {
                    return false;
                }

                icon = parsed;
                return true;
            }

            if (glossary != null && glossary.TryParseName(text, out icon))
                return true;

            for (int i = (int)TriggerIcon.None;
                i <= (int)TriggerIcon.CustomIcon6; i++)
            {
                var candidate = (TriggerIcon)i;
                if (string.Equals(DefaultName(candidate), text,
                    StringComparison.OrdinalIgnoreCase))
                {
                    icon = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Matches one of the custom names of this table.</summary>
        public bool TryParseName(string name, out TriggerIcon icon)
        {
            icon = TriggerIcon.None;
            if (name == null)
                return false;

            name = name.Trim();
            if (name.Length == 0)
                return false;

            foreach (KeyValuePair<TriggerIcon, string> pair in _names)
            {
                if (string.Equals(pair.Value, name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    icon = pair.Key;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 认一个写在设置文件里的图标名（<c>Book</c>、<c>CustomIcon3</c>…）。无
        /// 不算触发，所以不认；旧格式迁移也靠它筛掉乱七八糟的键。
        /// </summary>
        public static bool TryParseIconName(string text, out TriggerIcon icon)
        {
            icon = TriggerIcon.None;
            if (string.IsNullOrEmpty(text))
                return false;

            TriggerIcon parsed;
            if (!Enum.TryParse(text, true, out parsed))
                return false;

            int value = (int)parsed;
            if (value <= (int)TriggerIcon.None ||
                value > (int)TriggerIcon.CustomIcon6)
            {
                return false;
            }

            icon = parsed;
            return true;
        }

    }
}
