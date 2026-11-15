using System;
using System.Collections.Generic;
using System.IO;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Adds the extra rarities found in StreamingAssets/AlternateArtwork.
    /// Mirrors CardLibrary.ReadExtraRarities for .jpg/.png files: a clone is
    /// created from the base card, and the alternate image is kept separately.
    /// </summary>
    public static class ExtraArtworkLoader
    {
        public static void Load(
            SeriesFolder folder,
            string alternateArtworkRoot,
            IList<string> warnings)
        {
            if (folder == null || folder.Cards.Count == 0)
                return;
            if (string.IsNullOrEmpty(alternateArtworkRoot) ||
                !Directory.Exists(alternateArtworkRoot))
            {
                return;
            }

            string prefix = folder.Prefix ?? string.Empty;
            if (prefix.Length == 0)
                return;

            string setID = GetSetID(prefix);
            if (setID.Length == 0)
                return;

            string directory = Path.Combine(alternateArtworkRoot, setID);
            if (!Directory.Exists(directory))
                return;

            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string extension = Path.GetExtension(file);
                if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string idBase = IdMinusE(Path.GetFileNameWithoutExtension(file));
                string key = RemoveLettersFromEnd(idBase);
                if (key.IndexOf('_') >= 0)
                    key = key.Substring(key.LastIndexOf('_') + 1);

                string normalId = prefix + "-" + key;
                string eId = prefix + "-E" + key;
                if (key.StartsWith("T", StringComparison.Ordinal))
                    eId = prefix + "-TE" + key.Substring(1);
                else if (key.StartsWith("P", StringComparison.Ordinal))
                    eId = prefix + "-PE" + key.Substring(1);

                WeissCard baseCard = CardDataParser.FindCard(folder.Cards, normalId);
                if (baseCard == null)
                    baseCard = CardDataParser.FindCard(folder.Cards, eId);

                string variantId = prefix + "-" + idBase;
                if (FindExact(folder.Cards, variantId) != null)
                    continue;

                if (baseCard == null)
                {
                    if (warnings != null)
                    {
                        warnings.Add("No card found for alt artwork of " +
                            normalId + " or " + eId);
                    }
                    continue;
                }

                var clone = new WeissCard();
                char suffix = idBase[idBase.Length - 1];
                clone.Id = baseCard.Id + suffix;
                clone.Category = baseCard.Category;
                clone.CopyFrom(baseCard);
                clone.ImageId = string.Empty;
                clone.AltImagePath = file;
                folder.Cards.Add(clone);
            }
        }

        private static WeissCard FindExact(List<WeissCard> cards, string id)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (string.Equals(cards[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return cards[i];
            }
            return null;
        }

        private static string GetSetID(string prefix)
        {
            string normalized = prefix.Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            return slash < 0 ? normalized : normalized.Substring(slash + 1);
        }

        private static string RemoveLettersFromEnd(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            int index = value.Length - 1;
            while (index > 0 && "0123456789".IndexOf(value[index]) < 0)
                index--;
            return value.Substring(0, index + 1);
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
