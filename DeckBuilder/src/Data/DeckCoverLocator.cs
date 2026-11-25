using System;
using System.Collections.Generic;
using System.IO;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Finds the images of a deck without parsing any CardData.txt file. A
    /// card code is turned into the matching image file name, for example
    /// MTI/S83-E101 becomes MTI_S83_E101.jpg; the first card of a deck doubles
    /// as its cover and falls back to the series cover when its own image is
    /// missing.
    /// </summary>
    public sealed class DeckCoverLocator
    {
        private static readonly string[] Extensions =
            { ".png", ".jpg", ".jpeg", ".webp" };

        private readonly CardDatabase _database;
        private readonly SeriesCoverLocator _seriesCovers;
        private readonly Dictionary<string, string> _cache;
        private readonly HashSet<string> _misses;
        private readonly Dictionary<string, string> _cardCache;
        private readonly HashSet<string> _cardMisses;
        private readonly object _sync;
        private Dictionary<string, string> _seriesByPrefix;

        public DeckCoverLocator(CardDatabase database)
        {
            if (database == null)
                throw new ArgumentNullException("database");

            _database = database;
            _seriesCovers = new SeriesCoverLocator(database.CardsRoot);
            _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _misses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _cardCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _cardMisses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _sync = new object();
        }

        public string GetCoverPath(DeckDefinition deck)
        {
            if (deck == null || deck.CardCount == 0)
                return null;

            string key = string.IsNullOrEmpty(deck.FilePath)
                ? deck.Name
                : deck.FilePath;
            lock (_sync)
            {
                string cached;
                if (_cache.TryGetValue(key, out cached))
                    return cached;
                if (_misses.Contains(key))
                    return null;
            }

            string path = FindCover(deck);

            lock (_sync)
            {
                if (string.IsNullOrEmpty(path))
                    _misses.Add(key);
                else
                    _cache[key] = path;
            }

            return path;
        }

        /// <summary>
        /// Resolves the image of one card code, for example MTI/S83-E055S to
        /// the card image or its base art when the parallel version has no
        /// file of its own. Returns null when nothing is found.
        /// </summary>
        public string GetCardImagePath(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return null;

            string key = cardCode.Trim();
            if (key.Length == 0)
                return null;

            lock (_sync)
            {
                string cached;
                if (_cardCache.TryGetValue(key, out cached))
                    return cached;
                if (_cardMisses.Contains(key))
                    return null;
            }

            SeriesDefinition series = FindSeries(DeckDefinition.GetPrefix(key));
            string path = series == null ? null : FindCardImage(series, key);

            lock (_sync)
            {
                if (string.IsNullOrEmpty(path))
                    _cardMisses.Add(key);
                else
                    _cardCache[key] = path;
            }

            return path;
        }

        /// <summary>
        /// Series the deck belongs to, ordered by the first appearance of their
        /// set prefixes. Only lightweight metadata is read, so the side panel
        /// can show series names without loading any card file.
        /// </summary>
        public IList<SeriesDefinition> ResolveSeries(DeckDefinition deck)
        {
            var result = new List<SeriesDefinition>();
            if (deck == null)
                return result;

            IList<string> abbreviations = deck.Abbreviations;
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < abbreviations.Count; i++)
            {
                string name;
                if (!EnsurePrefixMap().TryGetValue(abbreviations[i], out name))
                    continue;
                if (!seenNames.Add(name))
                    continue;

                SeriesDefinition series = _database.GetSeriesMetadata(name);
                if (series != null)
                    result.Add(series);
            }

            return result;
        }

        /// <summary>
        /// 卡号所属的系列名，例如 AW/S18-001 返回"加速世界"。只读
        /// SingleSetData.txt 的元数据，不解析任何 CardData.txt；元数据里没
        /// 声明这个前缀时退回前缀本身，调用方总能拿到能显示的文字。
        /// 插件（禁限表）按系列分组时用它，和侧栏的系列名保持一致。
        /// </summary>
        public string GetSeriesName(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return string.Empty;

            string prefix = DeckDefinition.GetPrefix(cardCode.Trim());
            SeriesDefinition series = FindSeries(prefix);
            return series != null && !string.IsNullOrEmpty(series.Name)
                ? series.Name
                : prefix;
        }

        /// <summary>Clears cached paths; mostly useful after a data rescan.</summary>
        public void Clear()
        {
            lock (_sync)
            {
                _cache.Clear();
                _misses.Clear();
                _cardCache.Clear();
                _cardMisses.Clear();
                _seriesByPrefix = null;
            }
        }

        private string FindCover(DeckDefinition deck)
        {
            SeriesDefinition series = FindSeries(deck.PrimaryAbbreviation);
            if (series != null)
            {
                string image = FindCardImage(series, deck.CardCodes[0]);
                if (!string.IsNullOrEmpty(image))
                    return image;
            }

            return _seriesCovers.GetCoverPath(series);
        }

        private SeriesDefinition FindSeries(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return null;

            Dictionary<string, string> map = EnsurePrefixMap();
            string name;
            if (!map.TryGetValue(prefix, out name))
                return null;

            return _database.GetSeriesMetadata(name);
        }

        /// <summary>
        /// Maps every set prefix of every series to the series name. Only the
        /// lightweight metadata is touched, exactly like the series overview.
        /// </summary>
        private Dictionary<string, string> EnsurePrefixMap()
        {
            lock (_sync)
            {
                if (_seriesByPrefix != null)
                    return _seriesByPrefix;

                var map = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                IList<string> names = _database.SeriesNames;
                for (int i = 0; i < names.Count; i++)
                {
                    SeriesDefinition metadata = _database.GetSeriesMetadata(names[i]);
                    if (metadata == null)
                        continue;

                    IList<string> abbreviations = metadata.Abbreviations;
                    for (int j = 0; j < abbreviations.Count; j++)
                    {
                        string abbreviation = abbreviations[j];
                        if (!string.IsNullOrEmpty(abbreviation) &&
                            !map.ContainsKey(abbreviation))
                        {
                            map[abbreviation] = metadata.Name;
                        }
                    }
                }

                _seriesByPrefix = map;
                return _seriesByPrefix;
            }
        }

        private string FindCardImage(SeriesDefinition series, string cardCode)
        {
            string[] names = BuildImageNames(cardCode);
            if (names.Length == 0)
                return null;

            string setSuffix = GetSetSuffix(cardCode);
            for (int i = 0; i < series.Folders.Count; i++)
            {
                string folderPath = ResolveFolderPath(series.Folders[i].FolderPath);
                if (string.IsNullOrEmpty(folderPath))
                    continue;

                string found = SearchFolder(folderPath, names);
                if (!string.IsNullOrEmpty(found))
                    return found;

                if (setSuffix.Length == 0)
                    continue;

                string nested = Path.Combine(folderPath, setSuffix);
                found = SearchFolder(nested, names);
                if (!string.IsNullOrEmpty(found))
                    return found;
            }

            return null;
        }

        private static string SearchFolder(string folderPath, string[] names)
        {
            if (!Directory.Exists(folderPath))
                return null;

            for (int i = 0; i < names.Length; i++)
            {
                for (int j = 0; j < Extensions.Length; j++)
                {
                    string candidate = Path.Combine(folderPath,
                        names[i] + Extensions[j]);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Mirrors ImageLocator: the card code itself, then the same string
        /// with '/' and '-' replaced by '_'. Deck files also reference parallel
        /// art such as MTI/S83-E055S although only the base image exists, so
        /// the trailing rarity letter is tried as one more candidate.
        /// </summary>
        private static string[] BuildImageNames(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return new string[0];

            string code = cardCode.Trim();
            var names = new List<string>();
            AddImageNames(names, code);

            // Decks write the base print without the parallel-art E while the
            // files use it: KS/W49-003 is KS_W49_E003.jpg and KS/W49-T16 is
            // KS_W49_TE16.jpg.
            string parallel = CardCodeMatcher.EnsureParallelE(code);
            if (!string.Equals(parallel, code, StringComparison.OrdinalIgnoreCase))
                AddImageNames(names, parallel);

            string baseCode = CardCodeMatcher.TrimRaritySuffix(code);
            if (!string.Equals(baseCode, code, StringComparison.OrdinalIgnoreCase))
            {
                AddImageNames(names, baseCode);

                string baseParallel = CardCodeMatcher.EnsureParallelE(baseCode);
                if (!string.Equals(baseParallel, baseCode,
                    StringComparison.OrdinalIgnoreCase))
                {
                    AddImageNames(names, baseParallel);
                }
            }

            return names.ToArray();
        }

        private static void AddImageNames(List<string> names, string cardCode)
        {
            string underscore = cardCode.Replace('/', '_').Replace('-', '_');
            names.Add(cardCode);
            names.Add(underscore);
            names.Add(cardCode.ToLowerInvariant());
            names.Add(underscore.ToLowerInvariant());
        }

        /// <summary>
        /// "MTI/S83-E101" yields "S83": folders are sometimes nested below the
        /// series folder under the set number.
        /// </summary>
        private static string GetSetSuffix(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return string.Empty;

            int slash = cardCode.IndexOf('/');
            if (slash < 0 || slash + 1 >= cardCode.Length)
                return string.Empty;

            string rest = cardCode.Substring(slash + 1);
            int dash = rest.IndexOf('-');
            return (dash >= 0 ? rest.Substring(0, dash) : rest).Trim();
        }

        private string ResolveFolderPath(string relativePath)
        {
            string normalized = (relativePath ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            try
            {
                return Path.Combine(_database.CardsRoot, normalized);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }
}
