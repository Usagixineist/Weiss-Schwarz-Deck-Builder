using System;
using System.Collections.Generic;
using System.IO;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>One card of the 参考 index: just the fields the reference column shows.</summary>
    public sealed class CardReferenceEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string JapaneseName { get; set; }
        public string ImageId { get; set; }
        public string Text { get; set; }
        public string EffectCode { get; set; }
        public string Combo { get; set; }

        public CardCategory Category { get; set; }
        public CardColor Color { get; set; }
        public TriggerIcon Trigger { get; set; }
        public int Level { get; set; }
        public int Cost { get; set; }
        public int Power { get; set; }

        public string SeriesName { get; set; }
        public string SetName { get; set; }
        public string FolderPath { get; set; }
        public string FolderFullPath { get; set; }

        public CardReferenceEntry()
        {
            Id = string.Empty;
            Name = string.Empty;
            JapaneseName = string.Empty;
            ImageId = string.Empty;
            Text = string.Empty;
            EffectCode = string.Empty;
            Combo = string.Empty;
            SeriesName = string.Empty;
            SetName = string.Empty;
            FolderPath = string.Empty;
            FolderFullPath = string.Empty;
            Color = CardColor.Undefined;
            Category = CardCategory.Character;
            Trigger = TriggerIcon.None;
        }

        /// <summary>Card name shown in the grid: Japanese name first, like the card table.</summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(JapaneseName))
                    return JapaneseName;
                if (!string.IsNullOrEmpty(Name))
                    return Name;
                return Id;
            }
        }

        /// <summary>One-line stats used above the reference effect box.</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                parts.Add(WeissCard.CategoryText(Category));
                if (Color != CardColor.Undefined)
                    parts.Add(WeissCard.ColorText(Color));
                if (Category == CardCategory.Character)
                {
                    parts.Add("等级 " + Level);
                    parts.Add("COST " + Cost);
                    parts.Add("力量 " + Power);
                }
                else if (Trigger != TriggerIcon.None)
                {
                    parts.Add("触发 " + Trigger);
                }
                if (!string.IsNullOrEmpty(Combo))
                    parts.Add("联动 " + Combo);
                return string.Join(" · ", parts.ToArray());
            }
        }
    }

    /// <summary>One CardData.txt the reference index has to read.</summary>
    public sealed class CardReferenceFolder
    {
        public string SeriesName { get; set; }
        public string SetName { get; set; }
        public string FolderPath { get; set; }
        public string FolderFullPath { get; set; }
        public string FileName { get; set; }

        public CardReferenceFolder()
        {
            SeriesName = string.Empty;
            SetName = string.Empty;
            FolderPath = string.Empty;
            FolderFullPath = string.Empty;
            FileName = "CardData";
        }
    }

    /// <summary>
    /// Read-only copy of every card of every pack, kept for the 参考 column of
    /// 编辑卡片: one box searches 卡名 / 卡号, the other the effect text and the
    /// ability code. The index only parses CardData.txt (no images, no alt-art
    /// clones), so building it costs about a second and searching stays
    /// instant. Nothing in the game folder is written here.
    /// </summary>
    public sealed class CardReferenceIndex
    {
        private readonly List<CardReferenceEntry> _entries;

        private CardReferenceIndex(List<CardReferenceEntry> entries)
        {
            _entries = entries ?? new List<CardReferenceEntry>();
        }

        public static CardReferenceIndex Empty
        {
            get { return new CardReferenceIndex(null); }
        }

        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>
        /// Reads every pack of the database. <paramref name="progress"/> gets
        /// (done, total) after each file and <paramref name="cancelled"/> is
        /// polled between files, so closing the page abandons the build.
        /// </summary>
        public static CardReferenceIndex Build(CardDatabase database,
            Action<int, int> progress, Func<bool> cancelled)
        {
            if (database == null)
                return Empty;

            CommonEffectLibrary library = LoadCommonEffects(database.DataRoot);
            IList<CardReferenceFolder> folders = CollectFolders(database);
            return Build(folders, library, progress, cancelled);
        }

        public static CardReferenceIndex Build(IList<CardReferenceFolder> folders,
            CommonEffectLibrary library, Action<int, int> progress,
            Func<bool> cancelled)
        {
            var entries = new List<CardReferenceEntry>();
            if (folders == null)
                return new CardReferenceIndex(entries);

            for (int i = 0; i < folders.Count; i++)
            {
                if (cancelled != null && cancelled())
                    break;

                AddFolder(entries, folders[i], library);
                if (progress != null)
                    progress(i + 1, folders.Count);
            }

            entries.Sort(CompareEntries);
            return new CardReferenceIndex(entries);
        }

        /// <summary>
        /// Cards whose 卡名 / 卡号 matches <paramref name="nameOrCode"/> and
        /// whose effect text or ability code contains
        /// <paramref name="effectText"/>. An empty box matches everything, so
        /// one box alone narrows the search. A full card number sorts first.
        /// </summary>
        public List<CardReferenceEntry> Search(string nameOrCode,
            string effectText, int limit)
        {
            var results = new List<CardReferenceEntry>();
            string name = (nameOrCode ?? string.Empty).Trim();
            string text = (effectText ?? string.Empty).Trim();
            int max = limit <= 0 ? int.MaxValue : limit;

            if (name.Length == 0 && text.Length == 0)
                return results;

            string canonical = name.Length == 0
                ? string.Empty
                : CardCodeMatcher.Canonical(name);
            var exact = new List<CardReferenceEntry>();

            for (int i = 0; i < _entries.Count; i++)
            {
                CardReferenceEntry entry = _entries[i];
                if (name.Length > 0 && !MatchesName(entry, name, canonical))
                    continue;
                if (text.Length > 0 && !MatchesText(entry, text))
                    continue;

                if (canonical.Length > 0 &&
                    string.Equals(CardCodeMatcher.Canonical(entry.Id), canonical,
                        StringComparison.Ordinal))
                {
                    exact.Add(entry);
                    continue;
                }
                results.Add(entry);
            }

            if (exact.Count > 0)
                results.InsertRange(0, exact);

            if (results.Count > max)
                results.RemoveRange(max, results.Count - max);
            return results;
        }

        private static bool MatchesName(CardReferenceEntry entry, string name,
            string canonical)
        {
            if (Contains(entry.Id, name) ||
                Contains(entry.Name, name) ||
                Contains(entry.JapaneseName, name))
            {
                return true;
            }

            return canonical.Length > 0 &&
                string.Equals(CardCodeMatcher.Canonical(entry.Id), canonical,
                    StringComparison.Ordinal);
        }

        private static bool MatchesText(CardReferenceEntry entry, string text)
        {
            return Contains(entry.Text, text) ||
                Contains(entry.EffectCode, text) ||
                Contains(entry.Combo, text);
        }

        private static bool Contains(string value, string needle)
        {
            return !string.IsNullOrEmpty(value) &&
                value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddFolder(List<CardReferenceEntry> entries,
            CardReferenceFolder folder, CommonEffectLibrary library)
        {
            if (folder == null || string.IsNullOrEmpty(folder.FolderFullPath))
                return;

            string fileName = string.IsNullOrEmpty(folder.FileName)
                ? "CardData"
                : folder.FileName;
            if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                fileName += ".txt";

            string path = Path.Combine(folder.FolderFullPath, fileName);
            if (!File.Exists(path))
            {
                string fallback = Path.Combine(folder.FolderFullPath, "CardData.txt");
                if (!File.Exists(fallback))
                    return;
                path = fallback;
            }

            var seriesFolder = new SeriesFolder();
            seriesFolder.SeriesName = folder.SeriesName;
            seriesFolder.SetName = folder.SetName;
            seriesFolder.FolderPath = folder.FolderPath;
            seriesFolder.FullPath = folder.FolderFullPath;
            seriesFolder.FileName = Path.GetFileName(path);

            List<WeissCard> cards = CardDataParser.ParseFile(
                path, seriesFolder, library, null);
            for (int i = 0; i < cards.Count; i++)
                entries.Add(Convert(cards[i], folder));
        }

        private static CardReferenceEntry Convert(WeissCard card,
            CardReferenceFolder folder)
        {
            var entry = new CardReferenceEntry();
            entry.Id = card.Id ?? string.Empty;
            entry.Name = card.Name ?? string.Empty;
            entry.JapaneseName = card.JapaneseName ?? string.Empty;
            entry.ImageId = card.ImageId ?? string.Empty;
            entry.Text = card.Text ?? string.Empty;
            entry.EffectCode = card.EffectCode ?? string.Empty;
            entry.Combo = card.Combo ?? string.Empty;
            entry.Category = card.Category;
            entry.Color = card.Color;
            entry.Trigger = card.Trigger;
            entry.Level = card.Level;
            entry.Cost = card.Cost;
            entry.Power = card.Power;
            entry.SeriesName = folder.SeriesName ?? string.Empty;
            entry.SetName = folder.SetName ?? string.Empty;
            entry.FolderPath = folder.FolderPath ?? string.Empty;
            entry.FolderFullPath = folder.FolderFullPath ?? string.Empty;
            return entry;
        }

        private static IList<CardReferenceFolder> CollectFolders(CardDatabase database)
        {
            var result = new List<CardReferenceFolder>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < database.SeriesNames.Count; i++)
            {
                SeriesDefinition series = database.GetSeries(database.SeriesNames[i]);
                if (series == null)
                    continue;

                for (int j = 0; j < series.Folders.Count; j++)
                {
                    SeriesFolder folder = series.Folders[j];
                    string full = ResolveFolderFullPath(database, folder);
                    if (string.IsNullOrEmpty(full) || !seen.Add(full))
                        continue;

                    var target = new CardReferenceFolder();
                    target.SeriesName = series.Name;
                    target.SetName = folder.SetName;
                    target.FolderPath = folder.FolderPath;
                    target.FolderFullPath = full;
                    target.FileName = folder.FileName;
                    result.Add(target);
                }
            }
            return result;
        }

        private static string ResolveFolderFullPath(CardDatabase database,
            SeriesFolder folder)
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
                : Path.Combine(database.CardsRoot, relative);
        }

        private static CommonEffectLibrary LoadCommonEffects(string dataRoot)
        {
            if (string.IsNullOrEmpty(dataRoot))
                return CommonEffectLibrary.Empty;

            return CommonEffectLibrary.Load(
                Path.Combine(dataRoot, "CommonEffects.txt"));
        }

        private static int CompareEntries(CardReferenceEntry left,
            CardReferenceEntry right)
        {
            int result = string.Compare(left.SeriesName, right.SeriesName,
                StringComparison.OrdinalIgnoreCase);
            if (result != 0)
                return result;

            result = string.Compare(left.SetName, right.SetName,
                StringComparison.OrdinalIgnoreCase);
            if (result != 0)
                return result;

            return string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }
    }
}
