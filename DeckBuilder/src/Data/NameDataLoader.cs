using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Loads the optional JPNames.txt/NameData.txt files shipped next to a
    /// CardData.txt. These names are display-only; the game's search routine
    /// never searches them.
    /// </summary>
    public static class NameDataLoader
    {
        public static void Apply(SeriesFolder folder, string folderFullPath)
        {
            if (folder == null || folder.Cards.Count == 0)
                return;

            Dictionary<string, string> names = LoadNames(folderFullPath);
            ApplyTo(folder.Cards, 0, folder.Cards.Count, names);
        }

        /// <summary>
        /// Reads JPNames.txt and NameData.txt once. 打开系列 applies the names in
        /// two passes (before and after the extra rarities were appended), and
        /// reading both files for each pass showed up as duplicate work.
        /// </summary>
        public static Dictionary<string, string> LoadNames(string folderFullPath)
        {
            var names = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(folderFullPath))
                return names;

            LoadNameFile(Path.Combine(folderFullPath, "JPNames.txt"), names);
            LoadNameFile(Path.Combine(folderFullPath, "NameData.txt"), names);
            return names;
        }

        /// <summary>
        /// Fills <see cref="WeissCard.JapaneseName"/> for the cards in
        /// <paramref name="start"/> .. <paramref name="start"/> +
        /// <paramref name="count"/> that do not have one yet.
        /// </summary>
        public static void ApplyTo(
            IList<WeissCard> cards,
            int start,
            int count,
            IDictionary<string, string> names)
        {
            if (cards == null || names == null || names.Count == 0)
                return;

            int end = Math.Min(cards.Count, start + count);
            for (int i = Math.Max(0, start); i < end; i++)
            {
                WeissCard card = cards[i];
                string name;
                if (string.IsNullOrEmpty(card.JapaneseName) &&
                    (names.TryGetValue(card.Id, out name) ||
                     names.TryGetValue(IdMinusE(card.Id), out name)))
                {
                    card.JapaneseName = name;
                }
            }
        }

        private static void LoadNameFile(string path, IDictionary<string, string> names)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                string raw;
                while ((raw = reader.ReadLine()) != null)
                {
                    string line = raw.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0)
                        continue;
                    if (line.StartsWith("//", StringComparison.Ordinal) ||
                        line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int split = IndexOfSeparator(line);
                    if (split <= 0 || split >= line.Length - 1)
                        continue;

                    string id = line.Substring(0, split).Trim();
                    string name = line.Substring(split + 1).Trim();
                    if (id.Length > 0 && name.Length > 0)
                        names[id] = name;
                }
            }
        }

        private static int IndexOfSeparator(string line)
        {
            int equal = line.IndexOf('=');
            if (equal >= 0)
                return equal;

            int space = line.IndexOf(' ');
            int tab = line.IndexOf('\t');
            int colon = line.IndexOf(':');

            int result = -1;
            if (space >= 0)
                result = space;
            if (tab >= 0 && (result < 0 || tab < result))
                result = tab;
            if (colon >= 0 && (result < 0 || colon < result))
                result = colon;
            return result;
        }

        private static string IdMinusE(string id)
        {
            if (string.IsNullOrEmpty(id))
                return id;

            int index = id.IndexOf('-');
            if (index < 0)
                index = id.LastIndexOf('_');
            if (index <= 0)
                return id;

            string prefix = id.Substring(0, index + 1);
            string suffix = id.Substring(index + 1);
            return prefix + suffix.Replace("E", string.Empty);
        }
    }
}
