using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>Outcome of one 保存 in the card editor.</summary>
    public sealed class CardWriteResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public string FilePath { get; private set; }
        public string BackupPath { get; private set; }
        public string ImagePath { get; private set; }

        /// <summary>True when a new block was appended instead of replaced.</summary>
        public bool Added { get; private set; }

        private CardWriteResult()
        {
            Message = string.Empty;
            FilePath = string.Empty;
            BackupPath = string.Empty;
            ImagePath = string.Empty;
        }

        public static CardWriteResult Fail(string message)
        {
            var result = new CardWriteResult();
            result.Success = false;
            result.Message = message ?? string.Empty;
            return result;
        }

        public static CardWriteResult Saved(string message, string filePath,
            string backupPath, string imagePath, bool added)
        {
            var result = new CardWriteResult();
            result.Success = true;
            result.Message = message ?? string.Empty;
            result.FilePath = filePath ?? string.Empty;
            result.BackupPath = backupPath ?? string.Empty;
            result.ImagePath = imagePath ?? string.Empty;
            result.Added = added;
            return result;
        }
    }

    /// <summary>
    /// Writes the 编辑卡片 page back into CardData.txt. One card block is
    /// replaced in place (or appended for 添加卡片) and every other byte of the
    /// file is kept: the original encoding (UTF-8 with or without BOM, GBK,
    /// UTF-16), the original line ending and the other cards. The file is
    /// copied to CardData.txt.bak first so a bad edit can always be undone.
    /// </summary>
    public static class CardDataWriter
    {
        public const string BackupSuffix = ".bak";

        private static readonly string[] ImageExtensions =
            { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

        public static string ResolveCardFilePath(SeriesFolder folder)
        {
            if (folder == null)
                return string.Empty;
            return ResolveCardFilePath(folder.FullPath, folder.FileName);
        }

        public static string ResolveCardFilePath(string folderFullPath, string fileName)
        {
            if (string.IsNullOrEmpty(folderFullPath))
                return string.Empty;

            string name = string.IsNullOrEmpty(fileName)
                ? "CardData"
                : Path.GetFileName(fileName.Trim());
            if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                name += ".txt";

            return Path.Combine(folderFullPath, name);
        }

        public static bool IsSupportedImage(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string extension = Path.GetExtension(path);
            for (int i = 0; i < ImageExtensions.Length; i++)
            {
                if (string.Equals(extension, ImageExtensions[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Turns a card number or an 图片 field into the file name used next to
        /// CardData.txt: AW/S18-E101 becomes AW_S18_E101, exactly the spelling
        /// <see cref="ImageLocator"/> and <see cref="CardCodeMatcher"/> expect.
        /// </summary>
        public static string NormalizeImageBaseName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string name = value.Trim();
            int dot = name.LastIndexOf('.');
            if (dot > 0 && IsSupportedImage(name.Substring(dot)))
                name = name.Substring(0, dot);

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if (ch == '/' || ch == '\\' || ch == '-' || ch == ' ')
                    ch = '_';
                else if (Array.IndexOf(invalid, ch) >= 0)
                    ch = '_';
                builder.Append(ch);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Text of one card block, without a trailing line break. The field
        /// order follows the game's own files (header, base fields, effect
        /// code, Text, EndCard) so a card written by the editor looks like the
        /// ones shipped with the game and other tools keep reading it.
        /// </summary>
        public static string BuildBlock(CardEditorDraft draft)
        {
            if (draft == null)
                return string.Empty;

            var lines = new List<string>();
            lines.Add(CategoryKey(draft.Category) + ": " +
                (draft.Id ?? string.Empty).Trim());
            AddField(lines, "Name", draft.Name);
            AddField(lines, "JpName", draft.JapaneseName);
            AddField(lines, "Image", draft.ImageId);

            if (draft.Color != CardColor.Undefined)
                lines.Add("Color " + ColorKey(draft.Color));

            lines.Add("Level " + Math.Max(0, draft.Level));
            lines.Add("Cost " + Math.Max(0, draft.Cost));
            if (draft.Category == CardCategory.Character)
                lines.Add("Power " + Math.Max(0, draft.Power));
            lines.Add("Soul " + Math.Max(0, draft.Soul));
            if (draft.Trigger != TriggerIcon.None)
                lines.Add("Trigger " + draft.Trigger.ToString());
            if (draft.Backup > 0)
                lines.Add("Backup " + draft.Backup);

            AddField(lines, "Trait1", draft.Trait1);
            AddField(lines, "Trait2", draft.Trait2);
            AddField(lines, "Trait3", draft.Trait3);

            AppendEffectCode(lines, draft.EffectCode);

            string text = EncodeText(draft.Text);
            if (text.Length > 0)
                lines.Add("Text " + text);

            lines.Add("EndCard");
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Line based split that keeps an empty entry after a final newline.</summary>
        public static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new string[0];

            var lines = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch != '\r' && ch != '\n')
                    continue;

                lines.Add(text.Substring(start, i - start));
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                start = i + 1;
            }

            lines.Add(text.Substring(start));
            return lines.ToArray();
        }

        private static void AddField(List<string> lines, string key, string value)
        {
            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0)
                return;
            lines.Add(key + " " + value.Trim());
        }

        private static void AppendEffectCode(List<string> lines, string code)
        {
            if (string.IsNullOrEmpty(code))
                return;

            string[] parts = SplitLines(code);
            int first = 0;
            int last = parts.Length - 1;
            while (first <= last && parts[first].Trim().Length == 0)
                first++;
            while (last >= first && parts[last].Trim().Length == 0)
                last--;

            for (int i = first; i <= last; i++)
                lines.Add(parts[i].TrimEnd());
        }

        /// <summary>
        /// Effect texts are stored on one line: the parser reads the literal
        /// "\n" escape back into a line break, so multi-line text in the editor
        /// must not become extra effect code lines in the file.
        /// </summary>
        private static string EncodeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string[] parts = SplitLines(text);
            int first = 0;
            int last = parts.Length - 1;
            while (first <= last && parts[first].Trim().Length == 0)
                first++;
            while (last >= first && parts[last].Trim().Length == 0)
                last--;

            var cleaned = new List<string>();
            for (int i = first; i <= last; i++)
                cleaned.Add(parts[i].TrimEnd());
            return string.Join("\\n", cleaned.ToArray());
        }

        private static string CategoryKey(CardCategory category)
        {
            switch (category)
            {
                case CardCategory.Event: return "Event";
                case CardCategory.Climax: return "Climax";
                default: return "Character";
            }
        }

        private static string ColorKey(CardColor color)
        {
            switch (color)
            {
                case CardColor.Yellow: return "Y";
                case CardColor.Green: return "G";
                case CardColor.Red: return "R";
                case CardColor.Blue: return "B";
                case CardColor.Purple: return "P";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Saves one card: the picked art is copied first, then the matching
        /// block of the card pack's CardData file is replaced. 添加卡片 appends
        /// a new block instead and refuses a card number that already exists.
        /// </summary>
        public static CardWriteResult Save(CardEditorDraft draft)
        {
            if (draft == null)
                return CardWriteResult.Fail("没有可保存的卡片。");

            string id = (draft.Id ?? string.Empty).Trim();
            if (id.Length == 0)
                return CardWriteResult.Fail("卡号不能为空。");
            draft.Id = id;

            if (string.IsNullOrEmpty(draft.FolderFullPath) ||
                !Directory.Exists(draft.FolderFullPath))
            {
                return CardWriteResult.Fail(
                    "找不到卡包目录：" + (draft.FolderFullPath ?? string.Empty));
            }

            string imagePath = string.Empty;
            if (!string.IsNullOrEmpty(draft.ImageSourcePath))
            {
                string baseName = NormalizeImageBaseName(
                    string.IsNullOrEmpty(draft.ImageId) ? id : draft.ImageId);
                imagePath = CopyImage(draft.ImageSourcePath, draft.FolderFullPath,
                    baseName);
                if (imagePath == null)
                {
                    return CardWriteResult.Fail(
                        "卡图无法复制（只支持 png/jpg/jpeg/webp/bmp）：" +
                        draft.ImageSourcePath);
                }
                if (string.IsNullOrEmpty(draft.ImageId))
                    draft.ImageId = baseName;
                draft.ImageSourcePath = null;
            }

            string path = ResolveCardFilePath(draft.FolderFullPath, draft.FileName);
            string newline = "\r\n";
            var lines = new List<string>();
            Encoding encoding = new UTF8Encoding(false);
            byte[] preamble = new byte[0];

            try
            {
                if (File.Exists(path))
                {
                    byte[] existingBytes = File.ReadAllBytes(path);
                    if (existingBytes.Length > 0)
                    {
                        encoding = TextFileReader.DetectEncoding(existingBytes);
                        preamble = ReadPreamble(existingBytes, encoding);
                        string existing = encoding.GetString(existingBytes,
                            preamble.Length, existingBytes.Length - preamble.Length);
                        newline = DetectNewline(existing);
                        lines.AddRange(SplitLines(existing));
                    }
                }
            }
            catch (Exception ex)
            {
                return CardWriteResult.Fail("读取 " + path + " 失败：" + ex.Message);
            }

            string matchId = draft.IsNew
                ? null
                : (string.IsNullOrEmpty(draft.OriginalId)
                    ? id
                    : draft.OriginalId.Trim());

            int start = -1;
            int end = -1;
            bool duplicate = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string headerId;
                if (!TryReadHeaderId(lines[i], out headerId))
                    continue;

                if (matchId == null)
                {
                    if (string.Equals(headerId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                    continue;
                }

                if (!string.Equals(headerId, matchId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                start = i;
                end = FindEndCard(lines, i + 1);
                break;
            }

            if (duplicate)
            {
                return CardWriteResult.Fail("卡号已存在：" + id +
                    "，请换一个卡号，或在 修改卡片 里编辑这张卡。");
            }
            if (matchId != null && start < 0)
            {
                // Alternate-art clones only exist through AlternateArtwork and
                // have no block of their own; appending one here would create a
                // second card behind the player's back.
                return CardWriteResult.Fail("卡表里没有 " + matchId +
                    " 的卡块（可能是 AlternateArtwork 异画克隆），" +
                    "请修改基础卡，或改用 添加卡片。");
            }
            if (start >= 0 && end < 0)
            {
                return CardWriteResult.Fail("卡块没有 EndCard，无法定位：" +
                    matchId + "（" + path + "）");
            }

            string[] block = SplitLines(BuildBlock(draft));
            bool added = start < 0;
            if (added)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Length > 0)
                    lines.Add(string.Empty);
                lines.AddRange(block);
            }
            else
            {
                lines.RemoveRange(start, end - start + 1);
                lines.InsertRange(start, block);
            }

            string output = string.Join(newline, lines.ToArray());
            if (!output.EndsWith(newline, StringComparison.Ordinal))
                output += newline;

            byte[] body = encoding.GetBytes(output);
            byte[] payload;
            if (preamble.Length > 0)
            {
                payload = new byte[preamble.Length + body.Length];
                Buffer.BlockCopy(preamble, 0, payload, 0, preamble.Length);
                Buffer.BlockCopy(body, 0, payload, preamble.Length, body.Length);
            }
            else
            {
                payload = body;
            }

            string backup = string.Empty;
            try
            {
                if (File.Exists(path))
                {
                    backup = path + BackupSuffix;
                    File.Copy(path, backup, true);
                }

                string temp = path + ".tmp";
                File.WriteAllBytes(temp, payload);
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
            catch (Exception ex)
            {
                return CardWriteResult.Fail("写入 " + path + " 失败：" + ex.Message);
            }

            string message = (added ? "已添加 " : "已保存 ") + id + " → " +
                Path.GetFileName(path);
            if (backup.Length > 0)
                message += "（备份 " + Path.GetFileName(backup) + "）";
            if (imagePath.Length > 0)
                message += " · 卡图 " + Path.GetFileName(imagePath);

            return CardWriteResult.Saved(message, path, backup, imagePath, added);
        }

        /// <summary>
        /// Copies a dropped card art next to the card data. The destination is
        /// named after the card number (AW/S18-E101 → AW_S18_E101.jpg), which
        /// is what <see cref="ImageLocator"/> matches without extra metadata.
        /// Returns the written path, or null when nothing could be copied.
        /// </summary>
        public static string CopyImage(string sourcePath, string folderFullPath,
            string baseName)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return null;
            if (!IsSupportedImage(sourcePath))
                return null;
            if (string.IsNullOrEmpty(folderFullPath))
                return null;

            string name = NormalizeImageBaseName(baseName);
            if (name.Length == 0)
                return null;

            string destination = Path.Combine(folderFullPath,
                name + Path.GetExtension(sourcePath).ToLowerInvariant());
            try
            {
                Directory.CreateDirectory(folderFullPath);
                if (!string.Equals(Path.GetFullPath(sourcePath),
                    Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, destination, true);
                }
                return destination;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string DetectNewline(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "\r\n";

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    return i + 1 < text.Length && text[i + 1] == '\n'
                        ? "\r\n"
                        : "\r";
                }
                if (text[i] == '\n')
                    return "\n";
            }
            return "\r\n";
        }

        private static byte[] ReadPreamble(byte[] bytes, Encoding encoding)
        {
            byte[] preamble = encoding.GetPreamble();
            if (preamble.Length == 0 || bytes.Length < preamble.Length)
                return new byte[0];

            for (int i = 0; i < preamble.Length; i++)
            {
                if (bytes[i] != preamble[i])
                    return new byte[0];
            }
            return preamble;
        }

        private static bool TryReadHeaderId(string line, out string id)
        {
            id = string.Empty;
            if (string.IsNullOrEmpty(line))
                return false;

            string trimmed = line.Trim();
            string[] headers = { "Character:", "Event:", "Climax:" };
            for (int i = 0; i < headers.Length; i++)
            {
                if (!trimmed.StartsWith(headers[i], StringComparison.OrdinalIgnoreCase))
                    continue;

                id = trimmed.Substring(headers[i].Length).Trim();
                return id.Length > 0;
            }
            return false;
        }

        private static int FindEndCard(List<string> lines, int from)
        {
            for (int i = from; i < lines.Count; i++)
            {
                if (string.Equals(lines[i].Trim(), "EndCard",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
