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
    /// The directory and file naming rules live in
    /// <see cref="AltArtworkNaming"/>, the very same helper 添加异画 writes
    /// with, so a card written by this app is always found again here.
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

            string directory = AltArtworkNaming.ResolveDirectory(
                alternateArtworkRoot, folder);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
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
                if (!AltArtworkNaming.IsStoredImageExtension(
                    Path.GetExtension(file)))
                {
                    continue;
                }

                AltArtworkFileName name = AltArtworkNaming.Parse(
                    Path.GetFileNameWithoutExtension(file), prefix);
                if (name == null)
                    continue;

                WeissCard baseCard = CardDataParser.FindCard(
                    folder.Cards, name.NormalCardId);
                if (baseCard == null)
                    baseCard = CardDataParser.FindCard(
                        folder.Cards, name.ParallelCardId);

                if (baseCard == null)
                {
                    if (warnings != null)
                    {
                        warnings.Add("No card found for alt artwork of " +
                            name.NormalCardId + " or " + name.ParallelCardId);
                    }
                    continue;
                }

                // The clone id only keeps the last letter of the file name, so
                // two files can name the same variant (…_001sp and …_001ssp).
                // The first one wins instead of adding two cards with one id.
                string variantId = name.BuildVariantId(baseCard.Id);
                if (FindExact(folder.Cards, variantId) != null)
                    continue;

                var clone = new WeissCard();
                clone.Category = baseCard.Category;
                clone.Id = variantId;
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
    }
}
