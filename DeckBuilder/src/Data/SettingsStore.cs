using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// 设置页面唯一的落盘位置：程序目录里的 <c>DeckBuilder\settings.txt</c>。
    /// 主题、触发名称，以及以后新增的每一项设置都写进这一个文件，备份或清理
    /// 只需要处理它一个；删掉文件就等于恢复全部默认设置。
    ///
    /// 内容是 INI 风格的分组，<c>#</c> 开头是注释：
    /// <code>
    /// [appearance]
    /// theme=light
    ///
    /// [terminology]
    /// Book=书
    /// </code>
    /// 读不懂的行只跳过，不认识的键原样保留并写回去，所以旧程序读到新版本写的
    /// 文件也不会把新设置抹掉。文件缺失、写不进去都只当作"用默认值"，绝不抛出
    /// 异常。
    /// </summary>
    public sealed class SettingsStore
    {
        /// <summary>File name in the DeckBuilder folder; no folder.</summary>
        public const string FileName = "settings.txt";

        /// <summary>设置页“外观”分类的分组名。</summary>
        public const string AppearanceSection = "appearance";

        /// <summary>设置页“术语”分类的分组名，即触发名称表。</summary>
        public const string TerminologySection = "terminology";

        private const string LegacySettingsFileName = "AppSettings.txt";
        private const string LegacyGlossaryFileName = "TriggerGlossary.txt";

        private const string FileHeader =
            "# 界面设置：设置页里的每一项改动都写在这个文件里。\r\n" +
            "# 格式：[分类] 下面一行一个 键=值，# 开头为注释。\r\n" +
            "# 删掉本文件即恢复全部默认设置。\r\n";

        /// <summary>One [section] of the file: values plus the order they were added.</summary>
        private sealed class Section
        {
            public readonly Dictionary<string, string> Values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public readonly List<string> Keys = new List<string>();

            public void Remove(string key)
            {
                if (Values.Remove(key))
                    Keys.Remove(key);
            }
        }

        private readonly Dictionary<string, Section> _sections =
            new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _sectionOrder = new List<string>();
        private readonly string _filePath;

        /// <summary>
        /// Reads one settings file. A missing or unreadable file only means
        /// "everything is default", never an exception.
        /// </summary>
        public SettingsStore(string filePath)
        {
            _filePath = filePath == null ? string.Empty : filePath;
            Load();
        }

        /// <summary>
        /// Opens the settings file of the running program (see
        /// <see cref="DefaultFilePath"/>). The first start after this change also
        /// imports the old <c>StreamingAssets\AppSettings.txt</c> and
        /// <c>TriggerGlossary.txt</c> and then deletes them, so names the user
        /// already renamed are not lost.
        /// </summary>
        public static SettingsStore Open(string dataRoot)
        {
            return Open(DefaultFilePath(), dataRoot);
        }

        /// <summary>
        /// Opens the settings file at <paramref name="filePath"/>. The legacy
        /// import only runs while that file does not exist yet.
        /// </summary>
        public static SettingsStore Open(string filePath, string dataRoot)
        {
            var store = new SettingsStore(filePath);
            if (!store.HasFile && !string.IsNullOrEmpty(dataRoot))
                store.ImportLegacy(dataRoot);
            return store;
        }

        /// <summary>
        /// 设置文件的位置：程序目录上一层的 <c>settings.txt</c>。从
        /// <c>DeckBuilder\dist\WeissDeckBuilder.exe</c> 运行时就是
        /// <c>DeckBuilder\settings.txt</c>（重建、覆盖 dist 都不会碰到它）；
        /// 从别的目录运行时就是程序目录里的 <c>settings.txt</c>。
        /// </summary>
        public static string DefaultFilePath()
        {
            string directory;
            try
            {
                directory = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(directory))
                    return string.Empty;

                directory = directory.TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                if (directory.Length == 0)
                    return string.Empty;

                var info = new DirectoryInfo(directory);
                if (info.Parent != null &&
                    string.Equals(info.Name, "dist", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(info.Parent.FullName, FileName);
                }
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (PathTooLongException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }

            return Path.Combine(directory, FileName);
        }

        /// <summary>Full path of the settings file; empty when none is known.</summary>
        public string FilePath
        {
            get { return _filePath; }
        }

        /// <summary>True when the settings file exists and can be read.</summary>
        public bool HasFile
        {
            get { return _filePath.Length > 0 && File.Exists(_filePath); }
        }

        /// <summary>Value of one key, or null when the key is not in the file.</summary>
        public string Get(string section, string key)
        {
            string value;
            return TryGet(section, key, out value) ? value : null;
        }

        /// <summary>Looks one key up; false when it is missing.</summary>
        public bool TryGet(string section, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(key))
                return false;

            Section found;
            if (!_sections.TryGetValue(section.Trim(), out found))
                return false;
            return found.Values.TryGetValue(key.Trim(), out value);
        }

        /// <summary>
        /// Every key of one section in file order. The list is a snapshot, so
        /// the caller may save while iterating over it.
        /// </summary>
        public List<KeyValuePair<string, string>> Entries(string section)
        {
            var entries = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(section))
                return entries;

            Section found;
            if (!_sections.TryGetValue(section.Trim(), out found))
                return entries;

            for (int i = 0; i < found.Keys.Count; i++)
            {
                entries.Add(new KeyValuePair<string, string>(found.Keys[i],
                    found.Values[found.Keys[i]]));
            }

            return entries;
        }

        /// <summary>
        /// Sets one key. A null or blank value removes the key (and with it the
        /// line in the file), which is how an emptied box drops a name again.
        /// </summary>
        public void Set(string section, string key, string value)
        {
            if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(key))
                return;

            section = section.Trim();
            key = key.Trim();
            if (section.Length == 0 || key.Length == 0)
                return;

            value = value == null ? string.Empty : value.Trim();
            if (value.Length == 0)
            {
                Section existing;
                if (_sections.TryGetValue(section, out existing))
                    existing.Remove(key);
                return;
            }

            Section target = EnsureSection(section);
            if (!target.Values.ContainsKey(key))
                target.Keys.Add(key);
            target.Values[key] = value;
        }

        /// <summary>Drops every key of one section; the section stays known.</summary>
        public void ClearSection(string section)
        {
            if (string.IsNullOrEmpty(section))
                return;

            Section found;
            if (!_sections.TryGetValue(section.Trim(), out found))
                return;
            found.Values.Clear();
            found.Keys.Clear();
        }

        /// <summary>
        /// Re-reads the file and drops everything in memory first, so deleting
        /// the file and loading again really is "back to defaults".
        /// </summary>
        public void Load()
        {
            _sections.Clear();
            _sectionOrder.Clear();
            if (_filePath.Length == 0)
                return;

            string[] lines;
            try
            {
                if (!File.Exists(_filePath))
                    return;
                lines = File.ReadAllLines(_filePath);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            string section = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? string.Empty : lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    section = close <= 1
                        ? null
                        : line.Substring(1, close - 1).Trim();
                    if (string.IsNullOrEmpty(section))
                        section = null;
                    else
                        EnsureSection(section);
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0 || section == null)
                    continue;

                Set(section, line.Substring(0, separator),
                    line.Substring(separator + 1));
            }
        }

        /// <summary>
        /// Writes every known key back to <see cref="FilePath"/>. The file is
        /// rewritten whole, so it never grows with stale keys, and the sections
        /// keep the order they were first seen in.
        /// </summary>
        public bool Save(out string error)
        {
            error = null;
            if (_filePath.Length == 0)
            {
                error = "没有可用的设置文件，界面设置无法保存。";
                return false;
            }

            try
            {
                string folder = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                var builder = new StringBuilder();
                builder.Append(FileHeader);
                for (int i = 0; i < _sectionOrder.Count; i++)
                {
                    Section section = _sections[_sectionOrder[i]];
                    if (section.Keys.Count == 0)
                        continue;

                    builder.Append("\r\n[");
                    builder.Append(_sectionOrder[i]);
                    builder.Append("]\r\n");
                    for (int k = 0; k < section.Keys.Count; k++)
                    {
                        string key = section.Keys[k];
                        builder.Append(key);
                        builder.Append('=');
                        builder.Append(section.Values[key]);
                        builder.Append("\r\n");
                    }
                }

                File.WriteAllText(_filePath, builder.ToString(),
                    new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                error = "设置保存失败：" + ex.Message;
                return false;
            }
        }

        private Section EnsureSection(string section)
        {
            Section found;
            if (_sections.TryGetValue(section, out found))
                return found;

            found = new Section();
            _sections[section] = found;
            _sectionOrder.Add(section);
            return found;
        }

        /// <summary>
        /// Imports the two files the settings used to live in
        /// (<c>AppSettings.txt</c>, <c>TriggerGlossary.txt</c>) and removes them
        /// once the new file is written. It only runs while the new file does
        /// not exist, so it never overwrites the current settings.
        /// </summary>
        private void ImportLegacy(string dataRoot)
        {
            try
            {
                bool imported = false;

                List<KeyValuePair<string, string>> pairs =
                    ReadPairs(Path.Combine(dataRoot, LegacySettingsFileName));
                for (int i = 0; i < pairs.Count; i++)
                {
                    if (!string.Equals(pairs[i].Key, AppSettings.ThemeKey,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AppThemeMode mode;
                    if (!AppSettings.TryParseTheme(pairs[i].Value, out mode))
                        continue;

                    Set(AppearanceSection, AppSettings.ThemeKey,
                        AppSettings.ToText(mode));
                    imported = true;
                }

                pairs = ReadPairs(Path.Combine(dataRoot, LegacyGlossaryFileName));
                for (int i = 0; i < pairs.Count; i++)
                {
                    TriggerIcon icon;
                    if (!TriggerGlossary.TryParseIconName(pairs[i].Key, out icon))
                        continue;

                    Set(TerminologySection, icon.ToString(), pairs[i].Value);
                    imported = true;
                }

                if (!imported)
                    return;

                string error;
                if (!Save(out error))
                    return;

                DeleteLegacy(Path.Combine(dataRoot, LegacySettingsFileName));
                DeleteLegacy(Path.Combine(dataRoot, LegacyGlossaryFileName));
            }
            catch (ArgumentException)
            {
            }
            catch (PathTooLongException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        /// <summary>key=value pairs of one legacy file; comments and junk are skipped.</summary>
        private static List<KeyValuePair<string, string>> ReadPairs(string path)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            string[] lines;
            try
            {
                if (!File.Exists(path))
                    return pairs;
                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                return pairs;
            }
            catch (UnauthorizedAccessException)
            {
                return pairs;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? string.Empty : lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (key.Length == 0 || value.Length == 0)
                    continue;
                pairs.Add(new KeyValuePair<string, string>(key, value));
            }

            return pairs;
        }

        private static void DeleteLegacy(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}