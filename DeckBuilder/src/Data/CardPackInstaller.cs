using System;
using System.Collections.Generic;
using System.IO;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Outcome of <see cref="CardPackInstaller.Install"/>. The caller shows the
    /// message to the player and, on success, opens <see cref="SeriesName"/>
    /// when the pack declared one. Supplement packs without SingleSetData.txt
    /// leave <see cref="SeriesName"/> empty.
    /// </summary>
    public sealed class CardPackInstallResult
    {
        public bool Success { get; set; }
        public string SeriesName { get; set; }
        public string SeriesFolderName { get; set; }
        public int SeriesCount { get; set; }
        public int CardCount { get; set; }
        public int ExtractedFileCount { get; set; }
        public bool ReplacedExistingFolder { get; set; }
        public string ErrorMessage { get; set; }

        public CardPackInstallResult()
        {
            SeriesName = string.Empty;
            SeriesFolderName = string.Empty;
            ErrorMessage = string.Empty;
        }
    }

    /// <summary>
    /// Installs a ZIP, RAR or 7z card pack archive into StreamingAssets. A
    /// compliant archive mirrors the game layout, for example
    ///
    ///   Weiss Schwarz_Data/StreamingAssets/Cards/Stella Sora/SingleSetData.txt
    ///   Weiss Schwarz_Data/StreamingAssets/Cards/Stella Sora/W140/CardData.txt
    ///   Weiss Schwarz_Data/StreamingAssets/Cards/Stella Sora/W140/*.png
    ///
    /// Only the Cards and AlternateArtwork subtrees are written; README files
    /// and everything else in the archive are ignored, and no path outside the
    /// data root can be written (path traversal guard). A pack may contain only
    /// supplement files such as images or CommonEffects for an already
    /// installed series: as long as one safe file lives below Cards or
    /// AlternateArtwork, the pack can be installed. SingleSetData.txt and
    /// CardData.txt are parsed when present, but are not required.
    /// </summary>
    public static class CardPackInstaller
    {
        private const string CardsRootName = "Cards";
        private const string AlternateArtworkRootName = "AlternateArtwork";
        private const string SingleSetDataFileName = "SingleSetData.txt";

        private static readonly string[] CardHeaderPrefixes =
        {
            "Character:", "Event:", "Climax:"
        };

        public static CardPackInstallResult Install(string archivePath, string dataRoot)
        {
            var result = new CardPackInstallResult();

            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
            {
                result.ErrorMessage = "压缩包不存在";
                return result;
            }

            if (!IsSupportedArchive(archivePath))
            {
                result.ErrorMessage = "只支持 .zip、.rar 或 .7z 格式的卡表压缩包";
                return result;
            }

            if (string.IsNullOrEmpty(dataRoot) || !Directory.Exists(dataRoot))
            {
                result.ErrorMessage = "找不到游戏卡牌数据目录";
                return result;
            }

            try
            {
                using (CardPackArchive archive = CardPackArchive.Open(archivePath))
                {
                    List<PayloadFile> files = CollectFiles(archive);
                    if (files.Count == 0)
                    {
                        result.ErrorMessage =
                            "压缩包里没有 Cards 或 AlternateArtwork 下的有效文件";
                        return result;
                    }

                    // SingleSetData.txt is optional. Supplement packs often
                    // contain only images or CommonEffects for an already
                    // installed series, so missing metadata must not reject
                    // the archive. When metadata is present it still drives
                    // the series summary and the series opened by the UI.
                    var dataFiles = new List<PayloadFile>();
                    for (int i = 0; i < files.Count; i++)
                    {
                        if (IsSingleSetData(files[i].RelativePath))
                            dataFiles.Add(files[i]);
                    }

                    var seriesByName =
                        new Dictionary<string, SeriesDefinition>(StringComparer.Ordinal);
                    var warnings = new List<string>();
                    for (int i = 0; i < dataFiles.Count; i++)
                    {
                        PayloadFile dataFile = dataFiles[i];
                        try
                        {
                            SingleSetDataParser.ParseText(
                                ReadText(dataFile.Entry),
                                dataFile.RelativePath,
                                GetContainingFolderName(dataFile.RelativePath),
                                seriesByName,
                                warnings);
                        }
                        catch (Exception)
                        {
                            // A malformed metadata file must not block an
                            // otherwise valid image/effect supplement.
                        }
                    }

                    SeriesDefinition firstSeries = null;
                    foreach (SeriesDefinition series in seriesByName.Values)
                    {
                        if (firstSeries == null)
                            firstSeries = series;
                    }

                    int cardCount = seriesByName.Count == 0
                        ? 0
                        : CountAvailableCards(files, seriesByName);
                    result.SeriesName = firstSeries == null
                        ? string.Empty
                        : firstSeries.Name;
                    result.SeriesFolderName = firstSeries == null
                        ? string.Empty
                        : firstSeries.FolderName;
                    result.SeriesCount = seriesByName.Count;
                    result.CardCount = cardCount;

                    string root = Path.GetFullPath(dataRoot);
                    if (firstSeries != null &&
                        !string.IsNullOrEmpty(firstSeries.FolderName))
                    {
                        string existing = Path.Combine(
                            root, CardsRootName, firstSeries.FolderName);
                        result.ReplacedExistingFolder = Directory.Exists(existing);
                    }

                    int written;
                    string error = ExtractFiles(files, root, out written);
                    if (error != null)
                    {
                        result.ErrorMessage = error;
                        return result;
                    }

                    result.ExtractedFileCount = written;
                    result.Success = true;
                    return result;
                }
            }
            catch (InvalidDataException)
            {
                result.ErrorMessage = "压缩包已损坏或不是有效的压缩包";
            }
            catch (SharpCompress.Common.PasswordProtectedException)
            {
                result.ErrorMessage = "压缩包已加密，暂不支持带密码的卡表压缩包";
            }
            catch (SharpCompress.Common.InvalidFormatException)
            {
                result.ErrorMessage = "压缩包已损坏或格式不受支持";
            }
            catch (SharpCompress.Common.ExtractionException ex)
            {
                result.ErrorMessage = "解压失败：" + ex.Message;
            }
            catch (NotSupportedException)
            {
                result.ErrorMessage = "压缩包使用了不支持的压缩方式";
            }
            catch (UnauthorizedAccessException ex)
            {
                result.ErrorMessage = "没有权限写入游戏目录：" + ex.Message;
            }
            catch (IOException ex)
            {
                result.ErrorMessage = "读写文件失败：" + ex.Message;
            }
            catch (ArgumentException ex)
            {
                result.ErrorMessage = "压缩包内容无效：" + ex.Message;
            }
            catch (Exception ex)
            {
                // SharpCompress reports some corrupt 7z/RAR structures with its
                // own exception types. Without this catch such a pack would
                // escape the installer and close the app instead of showing
                // the usual 添加失败 dialog.
                result.ErrorMessage = "读取压缩包失败：" + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// True for the archive types the installer can read. The settings
        /// dialog and the drop target share this check so an unsupported file
        /// is rejected before it reaches the installer.
        /// </summary>
        public static bool IsSupportedArchive(string archivePath)
        {
            return CardPackArchive.IsSupported(archivePath);
        }

        /// <summary>
        /// Keeps the entries that live below a Cards or AlternateArtwork
        /// folder and rewrites their paths relative to StreamingAssets.
        /// </summary>
        private static List<PayloadFile> CollectFiles(CardPackArchive archive)
        {
            var files = new List<PayloadFile>();
            for (int i = 0; i < archive.Count; i++)
            {
                CardPackEntry entry = archive[i];
                if (entry == null || entry.IsDirectory)
                    continue;

                string relative = MapEntryPath(entry.FullName);
                if (relative == null)
                    continue;

                files.Add(new PayloadFile(entry, relative));
            }

            return files;
        }

        private static string MapEntryPath(string entryFullName)
        {
            if (string.IsNullOrEmpty(entryFullName))
                return null;

            string normalized = entryFullName
                .Replace('\\', '/')
                .TrimStart('/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);
            if (normalized.Length == 0)
                return null;

            string candidate = TrimToRoot(normalized, CardsRootName);
            if (candidate == null)
                candidate = TrimToRoot(normalized, AlternateArtworkRootName);
            if (candidate == null)
                return null;

            return IsSafeRelativePath(candidate) ? candidate : null;
        }

        private static string TrimToRoot(string normalizedPath, string rootName)
        {
            if (normalizedPath.StartsWith(rootName + "/",
                StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath;
            }

            int index = normalizedPath.IndexOf("/" + rootName + "/",
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return null;

            return normalizedPath.Substring(index + 1);
        }

        private static bool IsSafeRelativePath(string relativePath)
        {
            string[] segments = relativePath.Split('/');
            if (segments.Length < 2)
                return false;

            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0 || segment == "." || segment == "..")
                    return false;
                if (segment.IndexOfAny(invalid) >= 0)
                    return false;
            }

            return true;
        }

        private static bool IsSingleSetData(string relativePath)
        {
            return relativePath.StartsWith(CardsRootName + "/",
                       StringComparison.OrdinalIgnoreCase) &&
                   relativePath.EndsWith("/" + SingleSetDataFileName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string GetContainingFolderName(string relativePath)
        {
            int slash = relativePath.LastIndexOf('/');
            if (slash <= 0)
                return string.Empty;

            string directory = relativePath.Substring(0, slash);
            int previous = directory.LastIndexOf('/');
            return previous >= 0 ? directory.Substring(previous + 1) : directory;
        }

        /// <summary>
        /// Counts cards from any CardData files that happen to be present.
        /// Supplement packs may omit both SingleSetData and CardData, and a
        /// pack may add only images to a series whose card table already lives
        /// in the game, so a missing or unreadable card file is ignored here
        /// instead of rejecting the whole archive.
        /// </summary>
        private static int CountAvailableCards(
            List<PayloadFile> files,
            Dictionary<string, SeriesDefinition> seriesByName)
        {
            int cardCount = 0;

            foreach (SeriesDefinition series in seriesByName.Values)
            {
                for (int i = 0; i < series.Folders.Count; i++)
                {
                    SeriesFolder folder = series.Folders[i];
                    string folderPath = (folder.FolderPath ?? string.Empty)
                        .Replace('\\', '/').Trim('/');
                    if (folderPath.Length == 0)
                        continue;

                    PayloadFile cardFile =
                        FindCardDataFile(files, folderPath, folder.FileName);
                    if (cardFile == null)
                        continue;

                    try
                    {
                        cardCount += CountCards(ReadText(cardFile.Entry));
                    }
                    catch (Exception)
                    {
                        // The file is still extracted below; a malformed card
                        // table must not block an image/effect supplement.
                    }
                }
            }

            return cardCount;
        }

        private static string GetCardDataName(string fileName)
        {
            string name = string.IsNullOrEmpty(fileName) ? "CardData" : fileName;
            return name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4)
                : name;
        }

        private static PayloadFile FindCardDataFile(
            List<PayloadFile> files,
            string folderPath,
            string fileName)
        {
            string declared = string.Format("{0}/{1}/{2}.txt",
                CardsRootName, folderPath, GetCardDataName(fileName));
            PayloadFile match = FindFile(files, declared);
            if (match != null)
                return match;

            // CardDatabase falls back to CardData.txt when the declared file is
            // missing, so the archive may do the same.
            return FindFile(files, string.Format("{0}/{1}/CardData.txt",
                CardsRootName, folderPath));
        }

        private static PayloadFile FindFile(List<PayloadFile> files, string relativePath)
        {
            for (int i = 0; i < files.Count; i++)
            {
                if (string.Equals(files[i].RelativePath, relativePath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return files[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Counts the cards of one CardData file by its Character/Event/Climax
        /// headers, which are the same start markers the card parser uses.
        /// </summary>
        private static int CountCards(string cardDataText)
        {
            if (string.IsNullOrEmpty(cardDataText))
                return 0;

            int count = 0;
            using (var reader = new StringReader(cardDataText))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    for (int i = 0; i < CardHeaderPrefixes.Length; i++)
                    {
                        if (trimmed.StartsWith(CardHeaderPrefixes[i],
                            StringComparison.OrdinalIgnoreCase))
                        {
                            count++;
                            break;
                        }
                    }
                }
            }

            return count;
        }

        private static string ReadText(CardPackEntry entry)
        {
            using (Stream stream = entry.Open())
            {
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    using (StreamReader reader = TextFileReader.Open(buffer.ToArray()))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
        }

        private static string ExtractFiles(
            List<PayloadFile> files,
            string root,
            out int written)
        {
            written = 0;
            string rootPrefix = root.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            for (int i = 0; i < files.Count; i++)
            {
                PayloadFile file = files[i];
                string destination = Path.GetFullPath(Path.Combine(
                    root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(rootPrefix,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return "压缩包包含非法路径：" + file.RelativePath;
                }

                string directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                using (Stream input = file.Entry.Open())
                {
                    using (var output = new FileStream(destination, FileMode.Create,
                        FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                }

                written++;
            }

            return null;
        }

        private sealed class PayloadFile
        {
            public PayloadFile(CardPackEntry entry, string relativePath)
            {
                Entry = entry;
                RelativePath = relativePath;
            }

            public CardPackEntry Entry { get; private set; }
            public string RelativePath { get; private set; }
        }
    }
}
