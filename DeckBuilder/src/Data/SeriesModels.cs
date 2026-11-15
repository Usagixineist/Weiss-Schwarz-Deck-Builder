using System;
using System.Collections.Generic;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    public sealed class SeriesFolder
    {
        public string SeriesName { get; set; }
        public string FolderPath { get; set; }
        public string Prefix { get; set; }

        /// <summary>
        /// Every Prefix declared for this folder. A folder can contain cards
        /// from more than one series, for example the Key/Clannad/WE50 folder
        /// declares both Kcl/WE50 and Kta/WE50.
        /// </summary>
        public List<string> Prefixes { get; private set; }

        public string SetName { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public List<WeissCard> Cards { get; private set; }

        public SeriesFolder()
        {
            SeriesName = string.Empty;
            FolderPath = string.Empty;
            Prefix = string.Empty;
            Prefixes = new List<string>();
            SetName = string.Empty;
            FileName = "CardData";
            Cards = new List<WeissCard>();
        }

        public IEnumerable<string> EnumeratePrefixes()
        {
            if (Prefixes.Count > 0)
            {
                for (int i = 0; i < Prefixes.Count; i++)
                    yield return Prefixes[i];
                yield break;
            }

            if (!string.IsNullOrEmpty(Prefix))
                yield return Prefix;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(SetName) ? FolderPath : SetName;
        }
    }

    public sealed class SeriesDefinition
    {
        public string Name { get; private set; }

        /// <summary>
        /// Name of the folder below StreamingAssets\Cards that contains the
        /// SingleSetData.txt file, for example "Accel World" or "Key".
        /// </summary>
        public string FolderName { get; set; }

        public List<SeriesFolder> Folders { get; private set; }
        public bool CardsLoaded { get; set; }
        public string LoadError { get; set; }

        public SeriesDefinition(string name)
        {
            Name = name ?? string.Empty;
            FolderName = string.Empty;
            Folders = new List<SeriesFolder>();
        }

        public IEnumerable<WeissCard> EnumerateCards()
        {
            for (int i = 0; i < Folders.Count; i++)
            {
                var cards = Folders[i].Cards;
                for (int j = 0; j < cards.Count; j++)
                    yield return cards[j];
            }
        }

        public int CardCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Folders.Count; i++)
                    count += Folders[i].Cards.Count;
                return count;
            }
        }

        /// <summary>
        /// Every distinct set abbreviation in this series, for example
        /// "AW/GAW" for 加速世界 or all Kxx codes for Key. Prefixes are
        /// stored per folder, and one folder can declare several of them.
        /// </summary>
        public List<string> Abbreviations
        {
            get
            {
                var result = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < Folders.Count; i++)
                {
                    foreach (string prefix in Folders[i].EnumeratePrefixes())
                    {
                        string abbreviation = GetAbbreviation(prefix);
                        if (abbreviation.Length > 0 && seen.Add(abbreviation))
                            result.Add(abbreviation);
                    }
                }

                if (result.Count == 0 && !string.IsNullOrEmpty(FolderName))
                    result.Add(FolderName);

                return result;
            }
        }

        /// <summary>
        /// All abbreviations joined with "/" for display and text search.
        /// </summary>
        public string AbbreviationText
        {
            get { return string.Join("/", Abbreviations.ToArray()); }
        }

        /// <summary>
        /// Card-number heads found in the set metadata, for example S18,
        /// W129 or WE50. Both the last folder path segment and the part of
        /// each Prefix after "/" are included.
        /// </summary>
        public IList<string> CardNumberHeads
        {
            get
            {
                var result = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < Folders.Count; i++)
                {
                    SeriesFolder folder = Folders[i];
                    AddCardNumberHead(result, seen, GetLastPathSegment(folder.FolderPath));

                    foreach (string prefix in folder.EnumeratePrefixes())
                    {
                        int slash = prefix.IndexOf('/');
                        if (slash >= 0 && slash + 1 < prefix.Length)
                        {
                            AddCardNumberHead(result, seen,
                                prefix.Substring(slash + 1).Trim());
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// First abbreviation, kept for callers that still need one code.
        /// </summary>
        public string Abbreviation
        {
            get
            {
                IList<string> abbreviations = Abbreviations;
                return abbreviations.Count > 0
                    ? abbreviations[0]
                    : (FolderName ?? string.Empty);
            }
        }

        private static string GetAbbreviation(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return string.Empty;

            int slash = prefix.IndexOf('/');
            return (slash > 0 ? prefix.Substring(0, slash) : prefix).Trim();
        }

        private static string GetLastPathSegment(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string normalized = path.Replace('\\', '/').TrimEnd('/');
            int slash = normalized.LastIndexOf('/');
            return (slash >= 0 ? normalized.Substring(slash + 1) : normalized).Trim();
        }

        private static void AddCardNumberHead(
            List<string> result,
            HashSet<string> seen,
            string value)
        {
            if (string.IsNullOrEmpty(value) || !seen.Add(value))
                return;
            result.Add(value);
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
