using System;
using System.Collections.Generic;
using System.IO;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// One file below StreamingAssets\AlternateArtwork, decoded the way the game
    /// (and <see cref="ExtraArtworkLoader"/>) links it back to its base card.
    /// `AW_S18_E101a.png` means: folder prefix `AW/S18`, card number `101`,
    /// variant letter `a` and therefore the clone id `AW/S18-E101a`.
    /// </summary>
    public sealed class AltArtworkFileName
    {
        public string FileBaseName { get; set; }

        /// <summary>Number part of the name, e.g. `101`.</summary>
        public string NumberKey { get; set; }

        /// <summary>Card id the loader looks up first: `AW/S18-101`.</summary>
        public string NormalCardId { get; set; }

        /// <summary>Card id with the parallel E: `AW/S18-E101` (also `-TE` / `-PE`).</summary>
        public string ParallelCardId { get; set; }

        /// <summary>The letter the clone id is built from, e.g. `a`.</summary>
        public char Suffix { get; set; }

        /// <summary>Clone id the loader creates for a given base card.</summary>
        public string BuildVariantId(string baseCardId)
        {
            if (string.IsNullOrEmpty(baseCardId))
                return string.Empty;
            return baseCardId + Suffix;
        }

        /// <summary>
        /// True when this file belongs to that card: either the normal or the
        /// parallel form of the number names it.
        /// </summary>
        public bool MatchesBase(string baseCardId)
        {
            if (string.IsNullOrEmpty(baseCardId))
                return false;

            return string.Equals(baseCardId, NormalCardId,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseCardId, ParallelCardId,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The single source of truth for how alternate art is stored and found:
    /// 添加异画 writes with these rules, <see cref="ExtraArtworkLoader"/> reads
    /// with them. Keeping both sides on one helper is what stops a written file
    /// from becoming invisible to the loader later on.
    ///
    /// Rules (mirrors CardLibrary.ReadExtraRarities):
    /// directory  = StreamingAssets\AlternateArtwork\&lt;last prefix segment&gt;
    /// file name  = &lt;prefix&gt;_&lt;number&gt;&lt;variant letter&gt;.png|.jpg
    /// clone id   = base card id + that variant letter
    /// </summary>
    public static class AltArtworkNaming
    {
        /// <summary>Folder below StreamingAssets that holds the alternate art.</summary>
        public const string RootName = "AlternateArtwork";

        /// <summary>Letters offered for a new variant; `e` is skipped.</summary>
        public const string SuffixAlphabet = "abcdfghijklmnopqrstuvwxyz";

        /// <summary>
        /// Set code of a card pack: the last segment of its Prefix line
        /// (`AW/S18` → `S18`, `OS05/R05` → `R05`).
        /// </summary>
        public static string GetSetCode(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return string.Empty;

            string normalized = prefix.Trim().Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            return slash < 0 ? normalized : normalized.Substring(slash + 1);
        }

        /// <summary>
        /// Directory the loader reads for one card pack. The set code wins; two
        /// shipped packs (Aokana/OS05-R05, Holostars/2022_Promo) keep their art
        /// in a directory named after the folder instead, so that one is used
        /// when the set directory does not exist.
        /// </summary>
        public static string ResolveDirectory(string alternateRoot, SeriesFolder folder)
        {
            if (string.IsNullOrEmpty(alternateRoot) || folder == null)
                return string.Empty;

            string setCode = GetSetCode(folder.Prefix);
            if (setCode.Length == 0)
                return string.Empty;

            string directory = Path.Combine(alternateRoot, setCode);
            if (Directory.Exists(directory))
                return directory;

            string folderName = GetFolderName(folder.FolderPath);
            if (folderName.Length > 0 &&
                !string.Equals(folderName, setCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                string fallback = Path.Combine(alternateRoot, folderName);
                if (Directory.Exists(fallback))
                    return fallback;
            }

            return directory;
        }

        /// <summary>
        /// File name of a card id without the variant letter:
        /// `AW/S18-E101` → `AW_S18_E101`.
        /// </summary>
        public static string GetFileBasePrefix(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
                return string.Empty;

            return cardId.Trim()
                .Replace('/', '_')
                .Replace('\\', '_')
                .Replace('-', '_');
        }

        /// <summary>
        /// File name (without extension) of a new alternate art:
        /// `AW/S18-E101` + `a` → `AW_S18_E101a`.
        /// </summary>
        public static string GetFileBaseName(string cardId, char suffix)
        {
            if (string.IsNullOrEmpty(cardId))
                return string.Empty;

            return GetFileBasePrefix(cardId) + suffix;
        }

        /// <summary>
        /// A variant letter the loader keeps as-is. Uppercase `E` is what marks
        /// the parallel print and is stripped from the number, and a digit would
        /// be read as part of the card number, so neither can carry a variant.
        /// </summary>
        public static bool IsUsableSuffix(char suffix)
        {
            if (!char.IsLetter(suffix))
                return false;
            return suffix != 'E';
        }

        /// <summary>First alphabet letter that the directory does not use yet.</summary>
        public static char SuggestSuffix(string directory, string fileBasePrefix)
        {
            return SuggestSuffix(directory, fileBasePrefix, null);
        }

        /// <summary>
        /// Same, but the letters in <paramref name="reserved"/> are skipped too:
        /// three images dropped at once become a、b、c instead of all asking for
        /// `a` and then failing one by one while the batch writes them.
        /// </summary>
        public static char SuggestSuffix(string directory, string fileBasePrefix,
            ISet<char> reserved)
        {
            for (int i = 0; i < SuffixAlphabet.Length; i++)
            {
                char candidate = SuffixAlphabet[i];
                if (reserved != null && reserved.Contains(candidate))
                    continue;

                if (string.IsNullOrEmpty(directory) ||
                    !Directory.Exists(directory) ||
                    Directory.GetFiles(directory,
                        fileBasePrefix + candidate + ".*").Length == 0)
                {
                    return candidate;
                }
            }
            return SuffixAlphabet[SuffixAlphabet.Length - 1];
        }

        /// <summary>Last segment of a Folder line (`Aokana/OS05-R05` → `OS05-R05`).</summary>
        public static string GetFolderName(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return string.Empty;

            string normalized = folderPath.Trim().Replace('\\', '/').TrimEnd('/');
            int slash = normalized.LastIndexOf('/');
            return slash < 0 ? normalized : normalized.Substring(slash + 1);
        }

        /// <summary>
        /// Decodes a file name (without extension) the way the loader does.
        /// Returns null when the name cannot carry a variant, i.e. it has no
        /// number or does not end in a letter: such a file would otherwise
        /// produce a clone id with a digit glued to the card number.
        /// </summary>
        public static AltArtworkFileName Parse(string fileBaseName, string prefix)
        {
            if (string.IsNullOrEmpty(fileBaseName) || string.IsNullOrEmpty(prefix))
                return null;

            string withoutE = StripParallelMarker(fileBaseName);
            if (withoutE.Length == 0 || !char.IsLetter(withoutE[withoutE.Length - 1]))
                return null;

            string key = GetNumberKey(fileBaseName);
            if (key.Length == 0 || !char.IsDigit(key[0]) && !IsTrialOrPromoKey(key))
                return null;

            string trimmed = prefix.Trim().TrimEnd('/', '\\');
            if (trimmed.Length == 0)
                return null;

            var name = new AltArtworkFileName();
            name.FileBaseName = fileBaseName;
            name.NumberKey = key;
            name.NormalCardId = trimmed + "-" + key;
            name.ParallelCardId = GetParallelCardId(trimmed, key);
            name.Suffix = withoutE[withoutE.Length - 1];
            return name;
        }

        /// <summary>
        /// `AW/S18-E101` style equivalent of a number: a plain number gets the
        /// E marker (`-E101`), a trial or promo card gets it behind T / P
        /// (`-TE16`, `-PE06`), anything else stays untouched.
        /// </summary>
        public static string GetParallelCardId(string prefix, string numberKey)
        {
            if (string.IsNullOrEmpty(numberKey))
                return string.Empty;

            if (char.IsDigit(numberKey[0]))
                return prefix + "-E" + numberKey;

            char first = char.ToUpperInvariant(numberKey[0]);
            if ((first == 'T' || first == 'P') && numberKey.Length > 1)
                return prefix + "-" + numberKey[0] + "E" + numberKey.Substring(1);

            return prefix + "-" + numberKey;
        }

        /// <summary>
        /// Number part of a file name: the E marker is dropped first, then
        /// everything up to the last digit is kept and, when the name carries
        /// the folder prefix, only its last `_` segment
        /// (`os05_r05_001s` → `001`, `ks_w49_te16b` → `T16`).
        /// </summary>
        public static string GetNumberKey(string fileBaseName)
        {
            if (string.IsNullOrEmpty(fileBaseName))
                return string.Empty;

            string key = RemoveLettersFromEnd(StripParallelMarker(fileBaseName));
            int underscore = key.LastIndexOf('_');
            if (underscore >= 0)
                key = key.Substring(underscore + 1);
            return key;
        }

        private static bool IsTrialOrPromoKey(string key)
        {
            char first = char.ToUpperInvariant(key[0]);
            return (first == 'T' || first == 'P') && key.Length > 1 &&
                char.IsDigit(key[1]);
        }

        /// <summary>Drops the parallel `E` from the part behind `-` (or the last `_`).</summary>
        public static string StripParallelMarker(string fileBaseName)
        {
            if (string.IsNullOrEmpty(fileBaseName))
                return string.Empty;

            int index = fileBaseName.IndexOf('-');
            if (index < 0)
                index = fileBaseName.LastIndexOf('_');
            if (index <= 0)
                return fileBaseName;

            string head = fileBaseName.Substring(0, index + 1);
            string tail = fileBaseName.Substring(index + 1);
            return head + tail.Replace("E", string.Empty);
        }

        /// <summary>Keeps everything up to the last digit of the name.</summary>
        public static string RemoveLettersFromEnd(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            int index = value.Length - 1;
            while (index > 0 && "0123456789".IndexOf(value[index]) < 0)
                index--;
            return value.Substring(0, index + 1);
        }

        /// <summary>Image extensions the game reads out of AlternateArtwork.</summary>
        public static bool IsStoredImageExtension(string extension)
        {
            return string.Equals(extension, ".png",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".jpg",
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Formats a full path the way the status lines show it: relative to
        /// StreamingAssets when possible, so the message names the storage
        /// location instead of an absolute drive path.
        /// </summary>
        public static string DisplayPath(string alternateRoot, string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return string.Empty;

            string root = (alternateRoot ?? string.Empty).TrimEnd('\\', '/');
            if (root.Length > 0)
            {
                string parent = Path.GetDirectoryName(root);
                if (!string.IsNullOrEmpty(parent) &&
                    fullPath.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath.Substring(parent.Length)
                        .TrimStart('\\', '/');
                }
            }

            return Path.GetFileName(fullPath);
        }
    }
}
