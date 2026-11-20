using System;
using System.Collections.Generic;
using System.IO;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    public static class ImageLocator
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp" };

        public static Dictionary<string, string> BuildIndex(string folderPath)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return index;

            string[] files;
            try
            {
                // One enumeration for the whole folder: asking for *.png, *.jpg,
                // *.jpeg and *.webp separately walked the same directory four
                // times, which showed up as ~20 ms while opening a series.
                files = Directory.GetFiles(folderPath);
            }
            catch (IOException)
            {
                return index;
            }
            catch (UnauthorizedAccessException)
            {
                return index;
            }

            for (int j = 0; j < files.Length; j++)
            {
                string fileName = Path.GetFileName(files[j]);
                if (!IsImageExtension(Path.GetExtension(fileName)))
                    continue;

                index[fileName] = files[j];
                index[Path.GetFileNameWithoutExtension(fileName)] = files[j];
            }

            return index;
        }

        private static bool IsImageExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return false;

            for (int i = 0; i < Extensions.Length; i++)
            {
                if (string.Equals(extension, Extensions[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static void Resolve(WeissCard card, IDictionary<string, string> imageIndex)
        {
            if (card == null)
                return;

            if (!string.IsNullOrEmpty(card.AltImagePath) && File.Exists(card.AltImagePath))
            {
                card.ImagePath = card.AltImagePath;
                return;
            }

            card.ImagePath = Find(card, imageIndex);
        }

        private static string Find(WeissCard card, IDictionary<string, string> imageIndex)
        {
            if (imageIndex == null || imageIndex.Count == 0)
                return null;

            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(card.ImageId))
            {
                AddCandidates(candidates, card.ImageId);
                AddCandidates(candidates, Path.GetFileName(card.ImageId));
            }
            if (!string.IsNullOrEmpty(card.Id))
                AddCandidates(candidates, card.Id);

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                string path;
                if (imageIndex.TryGetValue(candidate, out path))
                    return path;

                string baseName = Path.GetFileNameWithoutExtension(candidate);
                if (imageIndex.TryGetValue(baseName, out path))
                    return path;
            }

            return null;
        }

        private static void AddCandidates(List<string> candidates, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            var names = new List<string>();
            names.Add(value);

            // CardData.txt often names the art without the parallel E while the
            // file carries it (Image HOL_W104_041 -> HOL_W104_E041.jpg).
            string parallel = CardCodeMatcher.EnsureParallelE(value);
            if (!string.Equals(parallel, value, StringComparison.OrdinalIgnoreCase))
                names.Add(parallel);

            string trimmed = CardCodeMatcher.TrimRaritySuffix(value);
            if (!string.Equals(trimmed, value, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(trimmed);

                string trimmedParallel = CardCodeMatcher.EnsureParallelE(trimmed);
                if (!string.Equals(trimmedParallel, trimmed,
                    StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(trimmedParallel);
                }
            }

            for (int i = 0; i < names.Count; i++)
            {
                AddUnique(candidates, names[i]);
                AddUnique(candidates,
                    names[i].Replace('/', '_').Replace('\\', '_').Replace('-', '_'));
            }

            string baseName = Path.GetFileNameWithoutExtension(value);
            if (!string.IsNullOrEmpty(baseName) &&
                !string.Equals(baseName, value, StringComparison.Ordinal))
            {
                AddCandidates(candidates, baseName);
            }
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            values.Add(value);
        }
    }
}
