using System;
using System.Collections.Generic;
using System.IO;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// One 添加异画 save: the base card whose alt art is added, the card pack it
    /// lives in (the prefix decides the AlternateArtwork sub folder) and the
    /// image the player dropped.
    /// </summary>
    public sealed class AltArtworkRequest
    {
        /// <summary>Card number of the base card, e.g. `AW/S18-E101`.</summary>
        public string BaseCardId { get; set; }

        /// <summary>Folder line of the pack (`Aokana/OS05-R05`).</summary>
        public string FolderPath { get; set; }

        /// <summary>
        /// Full path of the card pack folder, used to look for a real card that
        /// already owns the variant number.
        /// </summary>
        public string FolderFullPath { get; set; }

        /// <summary>CardData file name of the pack, normally "CardData".</summary>
        public string FileName { get; set; }

        /// <summary>Prefix line of the pack (`AW/S18`).</summary>
        public string Prefix { get; set; }

        /// <summary>StreamingAssets\AlternateArtwork.</summary>
        public string AlternateArtworkRoot { get; set; }

        /// <summary>Card number typed for the variant, e.g. `AW/S18-E101a`.</summary>
        public string AltId { get; set; }

        /// <summary>Image the player picked; png / jpg / jpeg only.</summary>
        public string ImageSourcePath { get; set; }

        public AltArtworkRequest()
        {
            BaseCardId = string.Empty;
            FolderPath = string.Empty;
            FolderFullPath = string.Empty;
            FileName = "CardData";
            Prefix = string.Empty;
            AlternateArtworkRoot = string.Empty;
            AltId = string.Empty;
            ImageSourcePath = string.Empty;
        }
    }

    /// <summary>Outcome of one 添加异画 save.</summary>
    public sealed class AltArtworkResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }

        /// <summary>Written image, or the path that was refused.</summary>
        public string FilePath { get; private set; }

        /// <summary>Card number the game will show for the variant.</summary>
        public string AltId { get; private set; }

        private AltArtworkResult()
        {
            Message = string.Empty;
            FilePath = string.Empty;
            AltId = string.Empty;
        }

        public static AltArtworkResult Fail(string message)
        {
            var result = new AltArtworkResult();
            result.Success = false;
            result.Message = message ?? string.Empty;
            return result;
        }

        public static AltArtworkResult Saved(string message, string filePath,
            string altId)
        {
            var result = new AltArtworkResult();
            result.Success = true;
            result.Message = message ?? string.Empty;
            result.FilePath = filePath ?? string.Empty;
            result.AltId = altId ?? string.Empty;
            return result;
        }
    }

    /// <summary>
    /// Writes one alternate-art image into
    /// StreamingAssets\AlternateArtwork\&lt;set code&gt;, the only place the game
    /// (and <see cref="ExtraArtworkLoader"/>) looks for one. No CardData block
    /// is touched: a variant exists through its image alone, and the clone the
    /// loader builds carries the base card's fields.
    /// </summary>
    /// <summary>
    /// Outcome of a whole 添加异画 queue: one <see cref="AltArtworkResult"/> per
    /// image, so the page can drop the rows that were written and keep the ones
    /// that were refused together with the reason.
    /// </summary>
    public sealed class AltArtBatchResult
    {
        /// <summary>One result per request, in the order they were sent.</summary>
        public IList<AltArtworkResult> Results { get; private set; }

        public int SavedCount { get; private set; }
        public int FailedCount { get; private set; }

        /// <summary>Footer line naming what happened and why.</summary>
        public string Message { get; private set; }

        private AltArtBatchResult()
        {
            Results = new List<AltArtworkResult>();
            Message = string.Empty;
        }

        public static AltArtBatchResult From(IList<AltArtworkResult> results)
        {
            var batch = new AltArtBatchResult();
            var all = new List<AltArtworkResult>();
            if (results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    AltArtworkResult one = results[i];
                    if (one == null)
                        one = AltArtworkResult.Fail("没有可保存的异画。");

                    all.Add(one);
                    if (one.Success)
                        batch.SavedCount++;
                    else
                        batch.FailedCount++;
                }
            }

            batch.Results = all;
            batch.Message = BuildMessage(all, batch.SavedCount, batch.FailedCount);
            return batch;
        }

        private static string BuildMessage(IList<AltArtworkResult> results,
            int saved, int failed)
        {
            if (results.Count == 0)
                return "没有待保存的异画。";

            // One image keeps the writer's own sentence, which names the file
            // and the card number the game will show.
            if (results.Count == 1)
                return results[0].Message;

            string firstFailure = null;
            var ids = new List<string>();
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].Success)
                {
                    if (firstFailure == null)
                        firstFailure = results[i].Message;
                    continue;
                }

                if (!string.IsNullOrEmpty(results[i].AltId))
                    ids.Add(results[i].AltId);
            }

            if (saved == 0)
                return "这 " + results.Count + " 张都没保存：" + firstFailure;

            string message = "已保存 " + saved + " 张异画：" +
                string.Join("、", ids.ToArray());
            if (failed > 0)
                message += "；" + failed + " 张失败：" + firstFailure;
            return message;
        }
    }

    public static class AltArtworkWriter
    {
        /// <summary>
        /// Card number offered for a new variant: the base number plus the
        /// first letter the pack folder does not use yet.
        /// </summary>
        public static string SuggestAltId(string baseCardId, string prefix,
            string folderPath, string alternateArtworkRoot)
        {
            return SuggestAltId(baseCardId, prefix, folderPath,
                alternateArtworkRoot, null);
        }

        /// <summary>
        /// Same, also skipping the letters the page has already handed out to
        /// the rest of the queue, so a multi image drop gets a、b、c.
        /// </summary>
        public static string SuggestAltId(string baseCardId, string prefix,
            string folderPath, string alternateArtworkRoot, ISet<char> reserved)
        {
            string baseId = (baseCardId ?? string.Empty).Trim();
            if (baseId.Length == 0)
                return string.Empty;

            string directory = ResolveDirectory(prefix, folderPath,
                alternateArtworkRoot);
            char suffix = AltArtworkNaming.SuggestSuffix(directory,
                AltArtworkNaming.GetFileBasePrefix(baseId), reserved);
            return baseId + suffix;
        }

        /// <summary>
        /// Saves the image for <see cref="AltArtworkRequest.AltId"/>. Every
        /// refusal names the reason, because a variant the loader cannot find
        /// would silently never show up in the game.
        /// </summary>
        public static AltArtworkResult Save(AltArtworkRequest request)
        {
            if (request == null)
                return AltArtworkResult.Fail("没有可保存的异画。");

            string baseId = (request.BaseCardId ?? string.Empty).Trim();
            if (baseId.Length == 0)
                return AltArtworkResult.Fail("没有基卡卡号，无法添加异画。");

            string prefix = (request.Prefix ?? string.Empty).Trim();
            if (prefix.Length == 0)
            {
                return AltArtworkResult.Fail(
                    "这个卡包的 SingleSetData.txt 没有 Prefix 行，游戏读不到它的异画，" +
                    "所以不能在这里添加。");
            }

            if (!char.IsDigit(baseId[baseId.Length - 1]))
            {
                return AltArtworkResult.Fail(
                    "基卡卡号要以数字结尾才能加异画：" + baseId +
                    "（异画文件名要写成 前缀_编号+字母）。");
            }

            string altId = (request.AltId ?? string.Empty).Trim();
            if (altId.Length == 0)
            {
                return AltArtworkResult.Fail("异画卡号不能为空，例如 " +
                    baseId + "a。");
            }

            string canonicalId;
            string refused = CheckAltId(altId, baseId, out canonicalId);
            if (refused != null)
                return AltArtworkResult.Fail(refused);

            string fileBase = AltArtworkNaming.GetFileBasePrefix(canonicalId);
            AltArtworkFileName parsed = AltArtworkNaming.Parse(fileBase, prefix);
            if (parsed == null || !parsed.MatchesBase(baseId) ||
                !string.Equals(parsed.BuildVariantId(baseId), canonicalId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return AltArtworkResult.Fail(
                    "卡号 " + canonicalId + " 与卡包前缀 " + prefix +
                    " 对不上，游戏会找不回这张异画。基卡卡号应为 " +
                    (parsed == null ? baseId : parsed.NormalCardId));
            }

            string imagePath = (request.ImageSourcePath ?? string.Empty).Trim();
            if (imagePath.Length == 0 || !File.Exists(imagePath))
                return AltArtworkResult.Fail("请先选择异画卡图。");

            // A CardData block that already carries this number would win over
            // the clone, so the new image must not use it.
            if (CardIdExists(request.FolderFullPath, request.FileName, canonicalId))
            {
                return AltArtworkResult.Fail(
                    "卡表里已经有一张 " + canonicalId +
                    " 的卡块，异画会被它挡住；请换一个字母。");
            }

            string extension = Path.GetExtension(imagePath);
            if (!AltArtworkNaming.IsStoredImageExtension(extension))
            {
                if (string.Equals(extension, ".jpeg",
                    StringComparison.OrdinalIgnoreCase))
                {
                    extension = ".jpg";
                }
                else
                {
                    return AltArtworkResult.Fail(
                        "异画只认 png / jpg：游戏只读 AlternateArtwork 里这两种扩展名，" +
                        "请先另存为 png 再添加。");
                }
            }

            string directory = ResolveDirectory(prefix, request.FolderPath,
                request.AlternateArtworkRoot);
            if (directory.Length == 0)
                return AltArtworkResult.Fail("找不到 AlternateArtwork 目录，无法保存异画。");

            string destination = Path.Combine(directory,
                fileBase + extension.ToLowerInvariant());

            string taken = FindVariantFile(directory, prefix, baseId, canonicalId);
            if (taken != null)
            {
                return AltArtworkResult.Fail(
                    "这个异画卡号已经被占用：" + Path.GetFileName(taken) +
                    "，请换一个字母。");
            }

            try
            {
                Directory.CreateDirectory(directory);
                File.Copy(imagePath, destination, false);
            }
            catch (IOException)
            {
                return AltArtworkResult.Fail(
                    "异画无法写入（文件可能正被占用）：" + destination);
            }
            catch (UnauthorizedAccessException)
            {
                return AltArtworkResult.Fail(
                    "异画无法写入（没有权限）：" + destination);
            }

            string shown = AltArtworkNaming.DisplayPath(
                request.AlternateArtworkRoot, destination);
            return AltArtworkResult.Saved(
                "异画已写入 " + shown + "，游戏里显示为 " + canonicalId +
                "（" + baseId + " 的异画）",
                destination, canonicalId);
        }

        /// <summary>
        /// Writes a whole 添加异画 queue. One refused row does not stop the
        /// batch: every image is answered on its own, so the page can keep the
        /// rows that failed and leave the ones that were written out of it.
        /// </summary>
        public static AltArtBatchResult SaveAll(IList<AltArtworkRequest> requests)
        {
            var results = new List<AltArtworkResult>();
            if (requests != null)
            {
                for (int i = 0; i < requests.Count; i++)
                    results.Add(Save(requests[i]));
            }
            return AltArtBatchResult.From(results);
        }

        /// <summary>
        /// Directory the pack's art belongs in; a pack without a prefix has
        /// none, and a missing root simply means the first variant creates it.
        /// </summary>
        private static string ResolveDirectory(string prefix, string folderPath,
            string alternateArtworkRoot)
        {
            if (string.IsNullOrEmpty(prefix) ||
                string.IsNullOrEmpty(alternateArtworkRoot))
            {
                return string.Empty;
            }

            var folder = new SeriesFolder();
            folder.Prefix = prefix;
            folder.FolderPath = folderPath ?? string.Empty;
            return AltArtworkNaming.ResolveDirectory(
                alternateArtworkRoot.Trim(), folder);
        }

        /// <summary>
        /// Checks the 异画卡号 field: the variant number has to be the base
        /// number plus exactly one letter the loader keeps as-is.
        /// </summary>
        private static string CheckAltId(string altId, string baseId,
            out string canonicalId)
        {
            canonicalId = string.Empty;
            if (!altId.StartsWith(baseId, StringComparison.OrdinalIgnoreCase))
            {
                return "异画卡号要在基卡卡号后面加一个字母：例如 " + baseId + "a，" +
                    "当前写的是 " + altId + "。";
            }

            string tail = altId.Substring(baseId.Length);
            if (tail.Length != 1)
            {
                return tail.Length == 0
                    ? "异画卡号要带上异画字母：例如 " + baseId + "a。"
                    : "异画卡号只能比基卡多一个字母（只有最后一个字母会写进卡号），" +
                        "例如 " + baseId + "a。";
            }

            char suffix = tail[0];
            if (!char.IsLetter(suffix))
            {
                return "异画卡号的最后一位必须是字母（数字会被当成卡号的一部分），例如 " +
                    baseId + "a。";
            }
            if (!AltArtworkNaming.IsUsableSuffix(suffix))
            {
                return "异画卡号不能以大写 E 结尾：E 是卡表的异画标记，读图时会被去掉。";
            }

            canonicalId = baseId + suffix;
            return null;
        }

        /// <summary>
        /// True when the pack's CardData already declares this number, i.e. a
        /// real card would answer to the variant id and the loader would skip
        /// the clone.
        /// </summary>
        private static bool CardIdExists(string folderFullPath, string fileName,
            string cardId)
        {
            if (string.IsNullOrEmpty(folderFullPath) ||
                !Directory.Exists(folderFullPath) || string.IsNullOrEmpty(cardId))
            {
                return false;
            }

            string path = CardDataWriter.ResolveCardFilePath(folderFullPath, fileName);
            if (path.Length == 0 || !File.Exists(path))
                return false;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            string[] headers = { "Character:", "Event:", "Climax:" };
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = (lines[i] ?? string.Empty).Trim();
                for (int h = 0; h < headers.Length; h++)
                {
                    if (!trimmed.StartsWith(headers[h],
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string id = trimmed.Substring(headers[h].Length).Trim();
                    if (string.Equals(id, cardId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Another file of the same base that already yields this variant id.
        /// The loader keeps only the last letter, so `…_001sp.png` occupies
        /// `…-001p` exactly like `…_001p.png` would.
        /// </summary>
        private static string FindVariantFile(string directory, string prefix,
            string baseId, string canonicalId)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return null;

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.png");
                string[] jpg = Directory.GetFiles(directory, "*.jpg");
                if (jpg.Length > 0)
                {
                    var merged = new List<string>(files);
                    merged.AddRange(jpg);
                    files = merged.ToArray();
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            for (int i = 0; i < files.Length; i++)
            {
                AltArtworkFileName name = AltArtworkNaming.Parse(
                    Path.GetFileNameWithoutExtension(files[i]), prefix);
                if (name == null || !name.MatchesBase(baseId))
                    continue;

                if (string.Equals(name.BuildVariantId(baseId), canonicalId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return files[i];
                }
            }
            return null;
        }
    }
}
