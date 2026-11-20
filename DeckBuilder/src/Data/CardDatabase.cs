using System;
using System.Collections.Generic;
using System.IO;
using WeissDeckBuilder.Infrastructure;
using WeissDeckBuilder.Models;
using WeissDeckBuilder.Search;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Owns the parsed metadata for every series and lazily loads one series'
    /// card files at a time. Reading never writes to the game folder; the only
    /// writer is 编辑卡片, which saves through <see cref="CardDataWriter"/>.
    /// </summary>
    public sealed class CardDatabase
    {
        private readonly string _dataRoot;
        private readonly string _cardsRoot;
        private readonly string _alternateArtworkRoot;
        private readonly CommonEffectLibrary _commonEffects;
        private readonly Dictionary<string, SeriesDefinition> _series;
        private readonly List<string> _seriesNames;
        private readonly List<string> _warnings;
        private readonly object _loadLock;

        public CardDatabase(string dataRoot)
        {
            _dataRoot = dataRoot;
            _cardsRoot = Path.Combine(dataRoot, "Cards");
            _alternateArtworkRoot = Path.Combine(dataRoot, "AlternateArtwork");
            _commonEffects = CommonEffectLibrary.Load(
                Path.Combine(dataRoot, "CommonEffects.txt"));
            _series = new Dictionary<string, SeriesDefinition>(StringComparer.Ordinal);
            _seriesNames = new List<string>();
            _warnings = new List<string>();
            _loadLock = new object();
        }

        public string DataRoot
        {
            get { return _dataRoot; }
        }

        public string CardsRoot
        {
            get { return _cardsRoot; }
        }

        public string AlternateArtworkRoot
        {
            get { return _alternateArtworkRoot; }
        }

        public IList<string> SeriesNames
        {
            get { return _seriesNames; }
        }

        public IList<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>
        /// Full path of a card pack folder. The folder metadata only keeps the
        /// relative FolderPath from SingleSetData.txt until its series is
        /// opened; 添加卡片 needs the path before that, because the new card is
        /// written straight into the pack's CardData.txt.
        /// </summary>
        public string GetFolderFullPath(SeriesFolder folder)
        {
            if (folder == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(folder.FullPath) &&
                Directory.Exists(folder.FullPath))
            {
                return folder.FullPath;
            }

            string relative = (folder.FolderPath ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            return string.IsNullOrEmpty(relative)
                ? string.Empty
                : Path.Combine(_cardsRoot, relative);
        }

        /// <summary>
        /// Re-reads every SingleSetData.txt after a card pack was installed.
        /// Cached lookups (series names, warnings, previously loaded series)
        /// are dropped so the new series shows up without restarting the app.
        /// </summary>
        public void Reload()
        {
            lock (_loadLock)
            {
                _series.Clear();
                _seriesNames.Clear();
                _warnings.Clear();
                LoadMetadata(_cardsRoot);
            }
        }

        public static CardDatabase Load(string dataRoot)
        {
            if (string.IsNullOrEmpty(dataRoot))
                throw new ArgumentException("dataRoot is empty", "dataRoot");

            dataRoot = Path.GetFullPath(dataRoot);
            string cardsRoot = Path.Combine(dataRoot, "Cards");
            if (!Directory.Exists(cardsRoot))
            {
                throw new DirectoryNotFoundException(
                    "Cards folder not found: " + cardsRoot);
            }

            var database = new CardDatabase(dataRoot);
            database.LoadMetadata(cardsRoot);
            return database;
        }

        private void LoadMetadata(string cardsRoot)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(cardsRoot, "SingleSetData.txt",
                    SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                _warnings.Add("Failed to enumerate SingleSetData.txt: " + ex.Message);
                files = new string[0];
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    SingleSetDataParser.ParseFile(files[i], _series, _warnings);
                }
                catch (Exception ex)
                {
                    _warnings.Add(files[i] + ": " + ex.Message);
                }
            }

            _seriesNames.AddRange(_series.Keys);
            _seriesNames.Sort(GbkStringComparer.Instance);
        }

        public SeriesDefinition GetSeries(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            SeriesDefinition series;
            lock (_loadLock)
            {
                if (!_series.TryGetValue(name, out series))
                    return null;
                if (series.CardsLoaded)
                    return series;

                using (LoadProfiler.Measure("GetSeries",
                    "load " + series.Name))
                    LoadSeriesCards(series);
                return series;
            }
        }

        /// <summary>
        /// Returns the parsed metadata without loading any CardData.txt files.
        /// Used by the series overview so opening the app does not parse all
        /// 199 series at once.
        /// </summary>
        public SeriesDefinition GetSeriesMetadata(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            SeriesDefinition series;
            return _series.TryGetValue(name, out series) ? series : null;
        }

        public IList<string> GetSetNames(string seriesName)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool hasEmptySetName = false;

            SeriesDefinition series;
            if (!_series.TryGetValue(seriesName ?? string.Empty, out series))
                return result;

            for (int i = 0; i < series.Folders.Count; i++)
            {
                string setName = series.Folders[i].SetName ?? string.Empty;
                if (setName.Length == 0)
                {
                    hasEmptySetName = true;
                    continue;
                }
                if (seen.Add(setName))
                    result.Add(setName);
            }

            if (hasEmptySetName && !seen.Contains(series.Name))
                result.Insert(0, series.Name);

            return result;
        }

        private void LoadSeriesCards(SeriesDefinition series)
        {
            for (int i = 0; i < series.Folders.Count; i++)
            {
                SeriesFolder folder = series.Folders[i];
                try
                {
                    LoadFolder(folder);
                }
                catch (Exception ex)
                {
                    _warnings.Add(folder.FolderPath + ": " + ex.Message);
                }
            }

            series.CardsLoaded = true;
        }

        private void LoadFolder(SeriesFolder folder)
        {
            string folderPath = ResolveFolderPath(folder.FolderPath);
            folder.FullPath = folderPath;
            if (!Directory.Exists(folderPath))
            {
                _warnings.Add("Folder not found: " + folderPath);
                return;
            }

            string fileName = folder.FileName;
            if (string.IsNullOrEmpty(fileName))
                fileName = "CardData";
            if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                fileName += ".txt";

            string cardFilePath = Path.Combine(folderPath, fileName);
            if (!File.Exists(cardFilePath) &&
                !string.Equals(fileName, "CardData.txt", StringComparison.OrdinalIgnoreCase))
            {
                cardFilePath = Path.Combine(folderPath, "CardData.txt");
            }

            string label = string.IsNullOrEmpty(folder.SetName)
                ? folder.FolderPath
                : folder.SetName;

            List<WeissCard> cards;
            using (LoadProfiler.Measure("LoadFolder", "parse " + label))
            {
                cards = CardDataParser.ParseFile(
                    cardFilePath, folder, _commonEffects, _warnings);
            }

            folder.Cards.AddRange(cards);

            // JPNames.txt / NameData.txt are read once and applied twice: the
            // parsed cards get their name first, because an extra rarity copies
            // the name of its base card, and the appended clones then get a
            // lookup of their own. Reading both files for each pass was the
            // duplicate work on the way into a series; the loops are cheap.
            Dictionary<string, string> names;
            int parsedCount = folder.Cards.Count;
            using (LoadProfiler.Measure("LoadFolder", "names " + label))
            {
                names = NameDataLoader.LoadNames(folderPath);
                NameDataLoader.ApplyTo(folder.Cards, 0, parsedCount, names);
            }

            using (LoadProfiler.Measure("LoadFolder", "extra art " + label))
                ExtraArtworkLoader.Load(folder, _alternateArtworkRoot, _warnings);

            using (LoadProfiler.Measure("LoadFolder", "names alt art " + label))
                NameDataLoader.ApplyTo(folder.Cards, 0, folder.Cards.Count, names);

            Dictionary<string, string> imageIndex;
            using (LoadProfiler.Measure("LoadFolder", "image index " + label))
                imageIndex = ImageLocator.BuildIndex(folderPath);
            using (LoadProfiler.Measure("LoadFolder", "image resolve " + label))
            {
                for (int i = 0; i < folder.Cards.Count; i++)
                    ImageLocator.Resolve(folder.Cards[i], imageIndex);
            }
        }

        private string ResolveFolderPath(string relativePath)
        {
            string normalized = (relativePath ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(_cardsRoot, normalized);
        }
    }
}
