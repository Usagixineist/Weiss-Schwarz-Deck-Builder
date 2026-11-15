using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// The player's own card counts ("我的收藏"). Every card pack — one
    /// <see cref="SeriesFolder"/>, i.e. one CardData.txt — is stored in its own
    /// small text file below StreamingAssets\MyCollocation, so the data of one
    /// series can be backed up, copied or deleted without touching the others.
    /// A file holds one <c>code count</c> line per owned card, sorted by code:
    /// the plainest format that still stays readable in any editor and needs
    /// only a few bytes per card.
    /// </summary>
    public sealed class CollectionStore
    {
        /// <summary>Folder holding the user files, next to <c>Decks</c>.</summary>
        public const string LocalFolderName = "MyCollocation";

        /// <summary>Extension of the per-pack count file.</summary>
        public const string PackExtension = ".txt";

        /// <summary>
        /// Highest count of a single card: the marking gesture cycles
        /// 0 → 1 → … → 99 → 0, and hand edited files stop here as well, so a
        /// sticker never shows more than "×99".
        /// </summary>
        public const int MaxCount = 99;

        /// <summary>Longest pack file name, matching the deck file limit.</summary>
        private const int MaxFileNameLength = 80;

        private readonly string _dataRoot;
        private readonly Dictionary<string, Dictionary<string, int>> _packs;
        private readonly object _sync;
        private int _summaryTypes;
        private int _summaryCopies;
        private bool _summaryValid;

        public CollectionStore(string dataRoot)
        {
            _dataRoot = dataRoot ?? string.Empty;
            _packs = new Dictionary<string, Dictionary<string, int>>(
                StringComparer.OrdinalIgnoreCase);
            _sync = new object();
            _summaryTypes = 0;
            _summaryCopies = 0;
            _summaryValid = false;
            TryCreateRoot();
        }

        public string DataRoot
        {
            get { return _dataRoot; }
        }

        /// <summary>StreamingAssets\MyCollocation, created on first use.</summary>
        public string Root
        {
            get { return Path.Combine(_dataRoot, LocalFolderName); }
        }

        /// <summary>
        /// Cache key of a pack: the folder path relative to Cards, with '/'
        /// separators. Cards carry this path in <c>FolderPath</c>, so the UI can
        /// look a count up without knowing which series it belongs to.
        /// </summary>
        public static string GetPackKey(string folderPath)
        {
            return (folderPath ?? string.Empty)
                .Replace('\\', '/')
                .Trim()
                .Trim('/');
        }

        /// <summary>
        /// File name of a pack: the folder path with the separators and every
        /// character Windows does not accept replaced by '_', so
        /// "Accel World/S18" becomes "Accel World_S18.txt".
        /// </summary>
        public static string GetPackFileName(string folderPath)
        {
            string key = GetPackKey(folderPath);
            if (key.Length == 0)
                return string.Empty;

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(key.Length);
            for (int i = 0; i < key.Length; i++)
            {
                char value = key[i];
                builder.Append(value == '/' || Array.IndexOf(invalid, value) >= 0
                    ? '_'
                    : value);
            }

            string name = builder.ToString().Trim().TrimEnd('.');
            if (name.Length > MaxFileNameLength)
                name = name.Substring(0, MaxFileNameLength).TrimEnd();
            return name.Length == 0 ? string.Empty : name + PackExtension;
        }

        /// <summary>Full path of a pack file, or null for an empty folder path.</summary>
        public string GetPackPath(string folderPath)
        {
            string fileName = GetPackFileName(folderPath);
            return fileName.Length == 0 ? null : Path.Combine(Root, fileName);
        }

        /// <summary>
        /// Counts of one pack, loaded from its file once and then cached. The
        /// dictionary belongs to the store; callers copy it when they need to
        /// edit. A missing or unreadable file yields an empty dictionary, so a
        /// card simply reads as "not owned".
        /// </summary>
        public IDictionary<string, int> GetPack(string folderPath)
        {
            string key = GetPackKey(folderPath);
            lock (_sync)
            {
                Dictionary<string, int> pack;
                if (!_packs.TryGetValue(key, out pack))
                {
                    pack = LoadPack(key);
                    _packs[key] = pack;
                }
                return pack;
            }
        }

        /// <summary>Owned copies of one card, 0 when it was never marked.</summary>
        public int GetCount(string folderPath, string cardCode)
        {
            string code = NormalizeCode(cardCode);
            if (code.Length == 0)
                return 0;

            int count;
            return GetPack(folderPath).TryGetValue(code, out count) ? count : 0;
        }

        /// <summary>True when the series has at least one owned card.</summary>
        public bool HasAnyOwned(SeriesDefinition series)
        {
            if (series == null)
                return false;

            for (int i = 0; i < series.Folders.Count; i++)
            {
                if (GetPack(series.Folders[i].FolderPath).Count > 0)
                    return true;
            }
            return false;
        }

        /// <summary>Distinct owned cards of a series, duplicates counted once.</summary>
        public int GetSeriesOwnedTypes(SeriesDefinition series)
        {
            int copies;
            return CountSeries(series, out copies);
        }

        /// <summary>
        /// Distinct owned cards and their total copies across the pack files
        /// of one series.
        /// </summary>
        public int CountSeries(SeriesDefinition series, out int copies)
        {
            copies = 0;
            if (series == null)
                return 0;

            int types = 0;
            for (int i = 0; i < series.Folders.Count; i++)
            {
                IDictionary<string, int> pack =
                    GetPack(series.Folders[i].FolderPath);
                types += pack.Count;
                foreach (KeyValuePair<string, int> entry in pack)
                    copies += entry.Value;
            }
            return types;
        }

        /// <summary>
        /// Owned cards over every pack file, for the header of the collection
        /// page. Files are parsed directly (not through the per-folder cache)
        /// because a file name cannot be mapped back to a folder path.
        /// </summary>
        public void GetTotals(out int types, out int copies)
        {
            lock (_sync)
            {
                if (_summaryValid)
                {
                    types = _summaryTypes;
                    copies = _summaryCopies;
                    return;
                }

                types = 0;
                copies = 0;
                string[] files;
                try
                {
                    files = Directory.Exists(Root)
                        ? Directory.GetFiles(Root, "*" + PackExtension)
                        : new string[0];
                }
                catch (IOException)
                {
                    files = new string[0];
                }
                catch (UnauthorizedAccessException)
                {
                    files = new string[0];
                }

                for (int i = 0; i < files.Length; i++)
                {
                    var pack = new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase);
                    if (!TryParsePack(ReadFile(files[i]), pack))
                        continue;

                    types += pack.Count;
                    foreach (KeyValuePair<string, int> entry in pack)
                        copies += entry.Value;
                }

                _summaryTypes = types;
                _summaryCopies = copies;
                _summaryValid = true;
            }
        }

        /// <summary>
        /// Writes one pack. Entries are clamped, zero counts and blank codes are
        /// dropped and the list is sorted by code, so the file never depends on
        /// the order the player happened to click in. A pack without entries has
        /// its file removed, which keeps the user folder free of empty files.
        /// Returns false with a message instead of throwing, because the card
        /// page reports the failure in its status line.
        /// </summary>
        public bool SavePack(string folderPath, IDictionary<string, int> counts,
            out string path, out string error)
        {
            path = GetPackPath(folderPath);
            error = null;
            if (path == null)
            {
                error = "卡包路径为空";
                return false;
            }

            try
            {
                List<KeyValuePair<string, int>> entries = SortEntries(counts);
                if (entries.Count == 0)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                else
                {
                    string folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                        Directory.CreateDirectory(folder);

                    File.WriteAllText(path, BuildContent(entries),
                        new UTF8Encoding(false));
                }

                lock (_sync)
                {
                    _packs[GetPackKey(folderPath)] = ToDictionary(entries);
                    _summaryValid = false;
                }
                return true;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (NotSupportedException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Normalised, sorted view of a count dictionary: upper-case codes,
        /// positive counts clamped to <see cref="MaxCount"/>, sorted ordinal so
        /// the file and its in-memory cache always agree.
        /// </summary>
        public static List<KeyValuePair<string, int>> SortEntries(
            IDictionary<string, int> counts)
        {
            var entries = new List<KeyValuePair<string, int>>();
            if (counts != null)
            {
                foreach (KeyValuePair<string, int> pair in counts)
                {
                    string code = NormalizeCode(pair.Key);
                    if (code.Length == 0 || pair.Value <= 0)
                        continue;

                    int count = pair.Value > MaxCount ? MaxCount : pair.Value;
                    entries.Add(new KeyValuePair<string, int>(code, count));
                }
            }

            entries.Sort(delegate(KeyValuePair<string, int> left,
                KeyValuePair<string, int> right)
            {
                return string.CompareOrdinal(left.Key, right.Key);
            });
            return entries;
        }

        /// <summary>
        /// Body of a pack file: one "code count" line per entry, sorted. Kept
        /// public so the test can assert the exact file layout.
        /// </summary>
        public static string BuildPackContent(IDictionary<string, int> counts)
        {
            return BuildContent(SortEntries(counts));
        }

        /// <summary>
        /// Writes the sorted entries as lines without a trailing newline, so
        /// the file stays as small as the format allows.
        /// </summary>
        private static string BuildContent(
            IList<KeyValuePair<string, int>> entries)
        {
            var builder = new StringBuilder(entries.Count * 16);
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                    builder.Append("\r\n");
                builder.Append(entries[i].Key).Append(' ')
                    .Append(entries[i].Value);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Reads a pack file body. Blank lines, '#' / '//' comments and lines
        /// without a readable positive count are skipped, so an edited file
        /// still loads. Returns false only when the text is null.
        /// </summary>
        public static bool TryParsePack(string text, IDictionary<string, int> target)
        {
            if (target == null)
                return false;
            if (text == null)
                return false;

            string[] lines = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim().Trim('\uFEFF').Trim();
                if (line.Length == 0 ||
                    line.StartsWith("#", StringComparison.Ordinal) ||
                    line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                int count;
                if (!int.TryParse(parts[parts.Length - 1], out count) || count <= 0)
                    continue;

                string code = NormalizeCode(parts[0]);
                if (code.Length == 0)
                    continue;

                if (count > MaxCount)
                    count = MaxCount;
                target[code] = count;
            }
            return true;
        }

        /// <summary>
        /// Upper-case, trimmed key of a card code. The identifier printed on
        /// the card is stored as is apart from the case, so reading a hand
        /// edited file still finds the card.
        /// </summary>
        public static string NormalizeCode(string cardCode)
        {
            return (cardCode ?? string.Empty).Trim().ToUpperInvariant();
        }

        /// <summary>Drops every cached pack and the totals, e.g. after an install.</summary>
        public void Reload()
        {
            lock (_sync)
            {
                _packs.Clear();
                _summaryValid = false;
            }
        }

        private Dictionary<string, int> LoadPack(string key)
        {
            var pack = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string path = GetPackPath(key);
            if (path == null)
                return pack;

            TryParsePack(ReadFile(path), pack);
            return pack;
        }

        private static string ReadFile(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static Dictionary<string, int> ToDictionary(
            IList<KeyValuePair<string, int>> entries)
        {
            var pack = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
                pack[entries[i].Key] = entries[i].Value;
            return pack;
        }

        private void TryCreateRoot()
        {
            if (string.IsNullOrEmpty(_dataRoot))
                return;

            try
            {
                if (!Directory.Exists(Root))
                    Directory.CreateDirectory(Root);
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
    }
}
