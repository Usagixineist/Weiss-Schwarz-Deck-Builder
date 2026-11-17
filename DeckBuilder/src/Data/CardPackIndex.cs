using System;
using System.Collections.Generic;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// One card pack a new card can be written to: the folder declared in
    /// SingleSetData.txt (one CardData.txt per folder) together with the
    /// series that owns it.
    /// </summary>
    public sealed class CardPackTarget
    {
        public string SeriesName { get; set; }
        public SeriesFolder Folder { get; set; }
        public string FolderFullPath { get; set; }

        /// <summary>
        /// How well the folder matches the set the card number names:
        /// 0 = the folder itself is called like the set (`DaCapo/W01`),
        /// 1 = one of its path segments is (`…/S93/IAS`), 2 = it is the only
        /// candidate that mentions the set, 3 = no match. Only used to
        /// preselect a pack when several folders declare the same prefix.
        /// </summary>
        internal int Rank { get; set; }

        public CardPackTarget()
        {
            SeriesName = string.Empty;
            FolderFullPath = string.Empty;
        }

        /// <summary>Set label of the pack, e.g. `加速世界 (S18)`.</summary>
        public string SetName
        {
            get
            {
                if (Folder == null)
                    return string.Empty;
                return string.IsNullOrEmpty(Folder.SetName)
                    ? (Folder.FolderPath ?? string.Empty)
                    : Folder.SetName;
            }
        }

        /// <summary>
        /// Caption of a 卡包 chip. 添加卡片 resolves packs across series, so
        /// the series name is always part of the label.
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                string series = SeriesName ?? string.Empty;
                string set = SetName;
                if (string.IsNullOrEmpty(series))
                    return set;
                return string.IsNullOrEmpty(set) ? series : series + " · " + set;
            }
        }

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    /// <summary>
    /// Maps a card number to the card pack its CardData.txt lives in: the
    /// prefix of the number (`AW/S18` for `AW/S18-E101`) is the `Prefix` line
    /// of SingleSetData.txt. 添加卡片 uses this to write a new card without
    /// asking for a series first. Built from the metadata only, so no
    /// CardData.txt is read.
    /// </summary>
    public sealed class CardPackIndex
    {
        private const int NoMatchRank = 3;

        private readonly Dictionary<string, List<CardPackTarget>> _byPrefix;
        private readonly Dictionary<string, List<CardPackTarget>> _bySet;
        private readonly List<CardPackTarget> _targets;

        private CardPackIndex()
        {
            _byPrefix = new Dictionary<string, List<CardPackTarget>>(
                StringComparer.OrdinalIgnoreCase);
            _bySet = new Dictionary<string, List<CardPackTarget>>(
                StringComparer.OrdinalIgnoreCase);
            _targets = new List<CardPackTarget>();
        }

        /// <summary>Number of distinct card packs (folders) known to the index.</summary>
        public int Count
        {
            get { return _targets.Count; }
        }

        /// <summary>
        /// Reads every series' metadata (SingleSetData.txt) without loading any
        /// card table, the same way the sidebar does when the app starts.
        /// </summary>
        public static CardPackIndex Build(CardDatabase database)
        {
            var index = new CardPackIndex();
            if (database == null)
                return index;

            var seen = new Dictionary<string, CardPackTarget>(
                StringComparer.OrdinalIgnoreCase);
            IList<string> names = database.SeriesNames;
            for (int i = 0; i < names.Count; i++)
            {
                index.AddSeries(database.GetSeriesMetadata(names[i]),
                    database.GetFolderFullPath, seen);
            }
            return index;
        }

        /// <summary>
        /// Builds the index from prepared metadata, for tests and for callers
        /// that already hold the series list.
        /// </summary>
        public static CardPackIndex Build(IEnumerable<SeriesDefinition> series,
            Func<SeriesFolder, string> resolveFolder)
        {
            var index = new CardPackIndex();
            var seen = new Dictionary<string, CardPackTarget>(
                StringComparer.OrdinalIgnoreCase);
            if (series == null)
                return index;

            foreach (SeriesDefinition definition in series)
                index.AddSeries(definition, resolveFolder, seen);
            return index;
        }

        /// <summary>
        /// Card-number head, i.e. the part that names the card pack:
        /// `AW/S18-E101` -> `AW/S18`, `ks/w49-te16` -> `KS/W49`,
        /// `HOL_W104_041` -> `HOL/W104`. Returns an empty string when the
        /// number holds no usable prefix.
        /// </summary>
        public static string CardIdHead(string cardId)
        {
            string text = NormalizeKey(cardId);
            if (text.Length == 0)
                return string.Empty;

            // The rarity suffix starts at the first dash and never belongs to
            // the prefix (AW/S18-E101S, MTI/S83-E055S).
            int dash = text.IndexOf('-');
            if (dash >= 0)
                text = text.Substring(0, dash);

            int slash = text.IndexOf('/');
            if (slash < 0)
                return text;
            if (slash == 0)
                return string.Empty;
            if (slash == text.Length - 1)
                return text.Substring(0, slash);

            int next = text.IndexOf('/', slash + 1);
            string head = text.Substring(0, slash);
            string set = next < 0
                ? text.Substring(slash + 1)
                : text.Substring(slash + 1, next - slash - 1);
            return set.Length == 0 ? head : head + "/" + set;
        }

        /// <summary>
        /// Every pack that declares the card number's prefix, best match
        /// first. More than one entry means the prefix is shared by several
        /// folders (promo packs mostly) and the player has to pick one.
        /// </summary>
        public IList<CardPackTarget> Find(string cardId)
        {
            var result = new List<CardPackTarget>();
            string head = CardIdHead(cardId);
            if (head.Length == 0)
                return result;

            List<CardPackTarget> matches;
            if (!_byPrefix.TryGetValue(head, out matches))
                matches = FindBySetCode(head);
            if (matches == null || matches.Count == 0)
                return result;

            result.AddRange(matches);
            RankMatches(head, result);
            result.Sort(CompareTargets);
            return result;
        }

        /// <summary>
        /// The pack a card number belongs to, or null when the number names no
        /// pack or names several of them equally well.
        /// </summary>
        public CardPackTarget Resolve(string cardId)
        {
            IList<CardPackTarget> matches = Find(cardId);
            if (matches.Count == 0)
                return null;
            if (matches.Count == 1)
                return matches[0];
            return matches[0].Rank < matches[1].Rank ? matches[0] : null;
        }

        private void AddSeries(SeriesDefinition definition,
            Func<SeriesFolder, string> resolveFolder,
            Dictionary<string, CardPackTarget> seen)
        {
            if (definition == null)
                return;

            for (int i = 0; i < definition.Folders.Count; i++)
            {
                SeriesFolder folder = definition.Folders[i];
                if (folder == null)
                    continue;

                string full = resolveFolder == null
                    ? (folder.FullPath ?? string.Empty)
                    : resolveFolder(folder);
                if (string.IsNullOrEmpty(full))
                    continue;

                // 编辑卡片 writes through SeriesFolder.FullPath; a folder that
                // was never opened only knows its relative FolderPath.
                if (string.IsNullOrEmpty(folder.FullPath))
                    folder.FullPath = full;

                CardPackTarget target;
                if (!seen.TryGetValue(full, out target))
                {
                    target = new CardPackTarget();
                    target.SeriesName = string.IsNullOrEmpty(folder.SeriesName)
                        ? definition.Name
                        : folder.SeriesName;
                    target.Folder = folder;
                    target.FolderFullPath = full;
                    target.Rank = NoMatchRank;
                    seen[full] = target;
                    _targets.Add(target);
                }

                foreach (string prefix in folder.EnumeratePrefixes())
                {
                    string key = NormalizeKey(prefix);
                    if (key.Length == 0)
                        continue;

                    AddLookup(_byPrefix, key, target);
                    string set = SetPart(key);
                    if (set.Length > 0)
                        AddLookup(_bySet, set, target);
                }
            }
        }

        private static void AddLookup(
            Dictionary<string, List<CardPackTarget>> lookup,
            string key,
            CardPackTarget target)
        {
            List<CardPackTarget> list;
            if (!lookup.TryGetValue(key, out list))
            {
                list = new List<CardPackTarget>();
                lookup[key] = list;
            }

            if (!list.Contains(target))
                list.Add(target);
        }

        /// <summary>
        /// A card table may use an abbreviation its SingleSetData.txt does not
        /// declare; a set code that belongs to exactly one pack is still safe
        /// to use, anything else would guess.
        /// </summary>
        private List<CardPackTarget> FindBySetCode(string head)
        {
            string set = SetPart(head);
            if (set.Length == 0)
                return null;

            List<CardPackTarget> list;
            if (!_bySet.TryGetValue(set, out list))
                return null;
            return list.Count == 1 ? list : null;
        }

        private static void RankMatches(string head, List<CardPackTarget> targets)
        {
            string set = SetPart(head);
            for (int i = 0; i < targets.Count; i++)
            {
                SeriesFolder folder = targets[i].Folder;
                targets[i].Rank = BaseRank(set,
                    folder == null ? null : folder.FolderPath);
            }

            if (set.Length == 0 || targets.Count < 2)
                return;

            // DaCapo/DS_W46 vs DaCapo/DS_PR: neither segment equals W46, but
            // only one of them mentions the set at all.
            int contains = 0;
            int containsIndex = -1;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Rank != NoMatchRank)
                    continue;

                SeriesFolder folder = targets[i].Folder;
                string path = folder == null
                    ? string.Empty
                    : folder.FolderPath ?? string.Empty;
                if (path.IndexOf(set, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                contains++;
                containsIndex = i;
            }

            if (contains == 1)
                targets[containsIndex].Rank = 2;
        }

        private static int BaseRank(string set, string folderPath)
        {
            if (set.Length == 0 || string.IsNullOrEmpty(folderPath))
                return NoMatchRank;

            string[] segments = folderPath.Replace('\\', '/')
                .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return NoMatchRank;

            if (string.Equals(segments[segments.Length - 1], set,
                StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            for (int i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], set,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return 1;
                }
            }
            return NoMatchRank;
        }

        private static int CompareTargets(CardPackTarget left, CardPackTarget right)
        {
            int byRank = left.Rank.CompareTo(right.Rank);
            if (byRank != 0)
                return byRank;

            int bySeries = string.Compare(left.SeriesName, right.SeriesName,
                StringComparison.OrdinalIgnoreCase);
            if (bySeries != 0)
                return bySeries;
            return string.Compare(left.SetName, right.SetName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string SetPart(string head)
        {
            int slash = head.IndexOf('/');
            if (slash < 0 || slash + 1 >= head.Length)
                return string.Empty;
            return head.Substring(slash + 1);
        }

        /// <summary>
        /// Prefixes and card numbers are written in any case and with either a
        /// slash or an underscore (HOL_W104_041), so both sides are folded
        /// before they are compared.
        /// </summary>
        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Trim().ToUpperInvariant()
                .Replace(" ", string.Empty)
                .Replace('_', '/');
        }
    }
}
