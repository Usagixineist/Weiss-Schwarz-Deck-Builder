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
        /// order follows the game's own files (header, base fields, effects,
        /// EndCard) so a card written by the editor looks like the ones
        /// shipped with the game and other tools keep reading it. A card with
        /// several 效果 keeps every `Text` line right below its own ability.
        /// </summary>
        public static string BuildBlock(CardEditorDraft draft)
        {
            if (draft == null)
                return string.Empty;

            var lines = new List<string>();
            lines.Add(CategoryKey(draft.Category) + ": " +
                (draft.Id ?? string.Empty).Trim());

            List<FieldLine> fields = BuildFieldLines(draft);
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Line != null)
                    lines.Add(fields[i].Line);
            }

            AppendEffectSection(lines, draft);

            lines.Add("EndCard");
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>
        /// The 效果 area of a new block: every 效果内容 right below its own
        /// 效果代码, the way CardData.txt writes it. When the text pieces and
        /// the ability blocks cannot be matched one to one (a text that wraps
        /// on the card), the whole text goes below the code instead of
        /// inventing 效果 the card does not have.
        /// </summary>
        private static void AppendEffectSection(List<string> lines, CardEditorDraft draft)
        {
            List<string[]> segments = CardEffectSplitter.SplitCode(draft.EffectCode);
            IList<CardEffectPair> pairs = CardEffectSplitter.PairDraftForWrite(draft);
            if (segments.Count > 1 && pairs.Count == segments.Count)
            {
                AppendEffects(lines, segments, pairs);
                return;
            }

            AppendEffectCode(lines, draft.EffectCode);
            AppendTextLine(lines, JoinOwnText(draft));
        }

        /// <summary>
        /// 效果内容 of a draft as one string, without the pieces a `*Shorthand`
        /// copies in from CommonEffects.txt.
        /// </summary>
        private static string JoinOwnText(CardEditorDraft draft)
        {
            List<string> own = CardEffectSplitter.OwnTextLines(draft);
            return string.Join("\n", own.ToArray());
        }

        /// <summary>
        /// Writes the ability blocks back with the 效果内容 of each one between
        /// them. Text that has no block of its own (a row whose code the player
        /// emptied) follows the last block, so nothing is lost.
        /// </summary>
        private static void AppendEffects(List<string> lines,
            IList<string[]> segments, IList<CardEffectPair> pairs)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                string[] block = segments[i];
                for (int j = 0; j < block.Length; j++)
                    lines.Add(block[j]);
                if (i < pairs.Count)
                    AppendTextLine(lines, pairs[i].Text);
            }

            for (int i = segments.Count; i < pairs.Count; i++)
                AppendTextLine(lines, pairs[i].Text);
        }

        private static void AppendTextLine(List<string> lines, string text)
        {
            string value = EncodeText(text);
            if (value.Length > 0)
                lines.Add("Text " + value);
        }

        /// <summary>One `Key value` line the card editor owns.</summary>
        private sealed class FieldLine
        {
            /// <summary>Lower case field key as it appears in CardData.txt.</summary>
            public string Key;

            /// <summary>The new line, or null when the field carries no value.</summary>
            public string Line;

            /// <summary>
            /// True when a block that never had this line gets it added. The
            /// game leaves `Soul 1` and a climax's `Level 0` implicit, so the
            /// writer only adds numbers that carry information.
            /// </summary>
            public bool InsertWhenMissing;
        }

        private static void AddTextField(List<FieldLine> fields, string key,
            string label, string value)
        {
            string text = (value ?? string.Empty).Trim();
            fields.Add(new FieldLine
            {
                Key = key,
                Line = text.Length == 0 ? null : label + " " + text,
                InsertWhenMissing = text.Length > 0
            });
        }

        /// <summary>
        /// The fields 编辑卡片 edits, in the order the game's own files write
        /// them. Values the cards of a category never carry (a climax has no
        /// Power, an event no Soul) come back as a null line, so the writer
        /// drops such a line instead of inventing one.
        /// </summary>
        private static List<FieldLine> BuildFieldLines(CardEditorDraft draft)
        {
            var fields = new List<FieldLine>();
            bool character = draft.Category == CardCategory.Character;
            bool climax = draft.Category == CardCategory.Climax;

            AddTextField(fields, "name", "Name", draft.Name);
            AddTextField(fields, "jpname", "JPName", draft.JapaneseName);
            AddTextField(fields, "image", "Image", draft.ImageId);

            // Most climax blocks carry no Color line at all, so an undefined
            // color must not become "Color" without a value.
            fields.Add(new FieldLine
            {
                Key = "color",
                Line = draft.Color == CardColor.Undefined
                    ? null
                    : "Color " + ColorKey(draft.Color),
                // A `*Shorthand` may set the color itself (`*RedStockSoul`
                // writes `Color R`); writing it again would leave the tag
                // twice in the block.
                InsertWhenMissing = draft.Color != CardColor.Undefined &&
                    draft.Color != draft.InheritedColor
            });

            fields.Add(new FieldLine
            {
                Key = "level",
                Line = climax && draft.Level <= 0
                    ? null
                    : "Level " + Math.Max(0, draft.Level),
                InsertWhenMissing = !climax || draft.Level > 0
            });
            fields.Add(new FieldLine
            {
                Key = "cost",
                Line = climax && draft.Cost <= 0
                    ? null
                    : "Cost " + Math.Max(0, draft.Cost),
                InsertWhenMissing = !climax || draft.Cost > 0
            });
            fields.Add(new FieldLine
            {
                Key = "power",
                Line = character ? "Power " + Math.Max(0, draft.Power) : null,
                InsertWhenMissing = character && draft.Power > 0
            });
            fields.Add(new FieldLine
            {
                Key = "soul",
                Line = draft.Soul > 0 ? "Soul " + draft.Soul : null,
                InsertWhenMissing = draft.Soul > 1
            });
            fields.Add(new FieldLine
            {
                Key = "trigger",
                Line = draft.Trigger == TriggerIcon.None
                    ? null
                    : "Trigger " + draft.Trigger,
                // A climax shorthand already carries its own `Trigger`
                // (`*2SoulClimax` writes `Trigger DoubleSoul`), so the icon the
                // editor shows must not become a second tag in the block. Only
                // a trigger the player really picks is written.
                InsertWhenMissing = draft.Trigger != TriggerIcon.None &&
                    draft.Trigger != draft.InheritedTrigger
            });
            fields.Add(new FieldLine
            {
                Key = "backup",
                Line = draft.Backup > 0 ? "Backup " + draft.Backup : null,
                InsertWhenMissing = draft.Backup > 0
            });

            AddTextField(fields, "trait1", "Trait1", draft.Trait1);
            AddTextField(fields, "trait2", "Trait2", draft.Trait2);
            AddTextField(fields, "trait3", "Trait3", draft.Trait3);
            AddTextField(fields, "combo", "Combo", draft.Combo);
            return fields;
        }

        /// <summary>
        /// The field lines of a draft in <see cref="BuildFieldLines"/> order.
        /// 编辑卡片 remembers them as the state the block was opened in, so a
        /// `Clone xxx` block only receives the fields the player changed: every
        /// other value is inherited from the base card and stays implicit.
        /// </summary>
        public static string[] BuildFieldSignature(CardEditorDraft draft)
        {
            if (draft == null)
                return new string[0];

            List<FieldLine> fields = BuildFieldLines(draft);
            var lines = new string[fields.Count];
            for (int i = 0; i < fields.Count; i++)
                lines[i] = fields[i].Line ?? string.Empty;
            return lines;
        }

        /// <summary>
        /// Rewrites one existing block: every line the editor does not own
        /// (ability headers, `{ }` code, `*Shorthand`, `Requirement`, `Tag`,
        /// comments) is copied back untouched, so editing a card that uses a
        /// shorthand keeps the shorthand instead of exploding it. Only the
        /// values that actually changed are rewritten, and a missing field is
        /// added only when it carries information the game does not default to.
        /// An untouched 效果区 is copied back line by line, `Text` lines
        /// included, so a multi-effect card keeps 内容 1 / 代码 1 / 内容 2 /
        /// 代码 2 instead of collecting every text at the end.
        /// </summary>
        private static string[] PatchBlock(IList<string> block, CardEditorDraft draft)
        {
            if (block == null || block.Count == 0)
                return SplitLines(BuildBlock(draft));

            var output = new List<string>();
            output.Add(CategoryKey(draft.Category) + ": " +
                (draft.Id ?? string.Empty).Trim());

            List<FieldLine> fields = BuildFieldLines(draft);
            var fieldByKey = new Dictionary<string, FieldLine>(StringComparer.Ordinal);
            var inserted = new HashSet<string>(StringComparer.Ordinal);
            var present = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < fields.Count; i++)
                fieldByKey[fields[i].Key] = fields[i];

            // Line 0 is the header and the last line is EndCard (Save already
            // refused a block without one), so the body is everything between.
            int last = block.Count - 1;
            var others = new List<string>();
            var textValues = new List<string>();
            var body = new List<BodyLine>();
            int depth = 0;
            for (int i = 1; i < last; i++)
            {
                string line = block[i] ?? string.Empty;
                string trimmed = line.Trim();
                if (trimmed.StartsWith("}", StringComparison.Ordinal))
                    depth = Math.Max(0, depth - 1);

                string key = string.Empty;
                string value = string.Empty;
                bool isField = depth == 0 &&
                    TryReadOwnedField(trimmed, fieldByKey, out key, out value);
                if (isField)
                {
                    present.Add(key);
                    if (key == "text")
                    {
                        textValues.Add(CommonEffectLibrary.DecodeText(value));
                        // 效果内容 keeps its place in the body, so a block the
                        // player did not touch is copied back exactly as it was.
                        body.Add(BodyLine.Raw(line));
                    }
                    else
                    {
                        body.Add(BodyLine.Field(key));
                    }
                    continue;
                }

                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                    depth++;
                others.Add(line);
                body.Add(BodyLine.Raw(line));
            }

            // The 效果代码 box holds exactly the lines the parser did not turn
            // into card fields, so comparing the two tells whether the player
            // touched the effect tree at all.
            string[] newCode = NormalizeCode(SplitLines(draft.EffectCode));
            string[] oldCode = NormalizeCode(others);
            bool codeChanged = !SequenceEqual(newCode, oldCode);

            // A `Clone xxx` block has no 效果 of its own: the editor shows the
            // code the block inherits from its base card, and that inherited
            // code must not be written into the block. A clone block that turns
            // out empty simply keeps inheriting, like before the edit.
            bool cloneOnlyBlock = IsCloneOnlyBlock(present, others);
            if (codeChanged)
            {
                string[] originalCode = NormalizeCode(
                    SplitLines(draft.OriginalEffectCode ?? string.Empty));
                if (SequenceEqual(newCode, originalCode))
                    codeChanged = false;
                else if (cloneOnlyBlock && newCode.Length == 0)
                    codeChanged = false;
            }

            // 效果内容 a `*Shorthand` copies in from CommonEffects.txt is shown
            // in the editor but never written back: the shorthand adds it again
            // on load, so a saved card would show the same 效果 twice.
            List<string> ownText = CardEffectSplitter.OwnTextLines(draft);
            string currentText = NormalizeText(ownText);
            bool textChanged = present.Contains("text")
                ? !string.Equals(currentText, NormalizeText(textValues),
                    StringComparison.Ordinal)
                : !string.Equals(currentText,
                    NormalizeText(CardEffectSplitter.DropInherited(
                        SplitLines(draft.OriginalText ?? string.Empty),
                        draft.InheritedTextLines)),
                    StringComparison.Ordinal);
            bool changed = codeChanged || textChanged;

            bool effectsEmitted = false;
            for (int i = 0; i < body.Count; i++)
            {
                BodyLine item = body[i];
                if (item.Key != null)
                {
                    FieldLine field = fieldByKey[item.Key];
                    if (field.Line == null)
                        continue;

                    output.Add(field.Line);
                    inserted.Add(item.Key);
                    continue;
                }

                if (!changed)
                {
                    // Nothing of the effect tree moved: every ability line and
                    // every `Text` line goes back exactly where it was read.
                    if (item.Line != null)
                        output.Add(item.Line);
                    continue;
                }

                if (effectsEmitted)
                    continue;

                effectsEmitted = true;
                AppendEffects(output,
                    CardEffectSplitter.SplitCode(
                        codeChanged ? newCode : others.ToArray()),
                    CardEffectSplitter.PairDraftForWrite(draft));
            }

            if (changed && !effectsEmitted)
            {
                // A block without any 效果 line of its own (only fields) still
                // gets the code and the text the player typed.
                AppendEffects(output,
                    CardEffectSplitter.SplitCode(
                        codeChanged ? newCode : others.ToArray()),
                    CardEffectSplitter.PairDraftForWrite(draft));
            }

            for (int i = 0; i < fields.Count; i++)
            {
                FieldLine field = fields[i];
                if (field.Line == null || !field.InsertWhenMissing)
                    continue;
                if (present.Contains(field.Key) || inserted.Contains(field.Key))
                    continue;

                // A `Clone xxx` block inherits every field: only a value the
                // player really changed becomes a line of its own.
                if (cloneOnlyBlock && IsUnchangedField(draft, i, field.Line))
                    continue;

                output.Add(field.Line);
            }

            output.Add("EndCard");
            return output.ToArray();
        }

        /// <summary>
        /// True when a field still carries the line the block was opened with,
        /// so a `Clone xxx` block does not spell out what it inherits.
        /// </summary>
        private static bool IsUnchangedField(CardEditorDraft draft, int index,
            string line)
        {
            string[] original = draft.OriginalFieldValues;
            return original != null && index >= 0 && index < original.Length &&
                string.Equals(original[index] ?? string.Empty,
                    line ?? string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// True for a block that carries nothing but a `Clone xxx` / `Copy xxx`
        /// line. Every field and every 效果 of such a card comes from its base
        /// card, so 保存 must not expand the block into a copy of that card.
        /// </summary>
        private static bool IsCloneOnlyBlock(HashSet<string> present,
            IList<string> others)
        {
            if (present.Count > 0)
                return false;

            bool cloneLine = false;
            for (int i = 0; i < others.Count; i++)
            {
                string trimmed = (others[i] ?? string.Empty).Trim();
                if (trimmed.Length == 0)
                    continue;
                if (!IsCloneDirective(trimmed))
                    return false;
                cloneLine = true;
            }
            return cloneLine;
        }

        /// <summary>`Clone AW/S18-E017` and `Copy: AW/S18-E017` both clone.</summary>
        private static bool IsCloneDirective(string trimmed)
        {
            int colon = trimmed.IndexOf(':');
            string head = colon < 0 ? trimmed : trimmed.Substring(0, colon);
            int space = head.IndexOf(' ');
            if (space >= 0)
                head = head.Substring(0, space);

            return string.Equals(head, "clone", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(head, "copy", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A body line of an existing block: an owned field or verbatim text.</summary>
        private sealed class BodyLine
        {
            /// <summary>Field key for an owned field, null for a verbatim line.</summary>
            public string Key;

            /// <summary>The line as it stands in the file, for verbatim lines.</summary>
            public string Line;

            public static BodyLine Field(string key)
            {
                var line = new BodyLine();
                line.Key = key;
                return line;
            }

            public static BodyLine Raw(string line)
            {
                var value = new BodyLine();
                value.Line = line ?? string.Empty;
                return value;
            }
        }


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

        /// <summary>
        /// Reads `Key value` for a field the editor owns. Ability headers
        /// (`Act: ...`), shorthand lines (`*Healer`), braces and comments all
        /// return false and stay in the 效果代码 half untouched.
        /// </summary>
        private static bool TryReadOwnedField(string trimmed,
            IDictionary<string, FieldLine> owned, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;
            if (trimmed.Length == 0 ||
                trimmed.StartsWith("*", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                return false;
            }

            int space = trimmed.IndexOf(' ');
            string token = space < 0 ? trimmed : trimmed.Substring(0, space);
            if (token.Length == 0 || token.EndsWith(":", StringComparison.Ordinal))
                return false;

            for (int i = 0; i < token.Length; i++)
            {
                char ch = token[i];
                if (!char.IsLetterOrDigit(ch) && ch != '_')
                    return false;
            }

            string candidate = token.ToLowerInvariant();
            if (candidate != "text" && !owned.ContainsKey(candidate))
                return false;

            key = candidate;
            value = space < 0 ? string.Empty : trimmed.Substring(space + 1);
            return true;
        }

        /// <summary>
        /// Effect code without the empty lines, so a code box that merely lost
        /// trailing blanks does not count as an edit.
        /// </summary>
        private static string[] NormalizeCode(IList<string> lines)
        {
            if (lines == null)
                return new string[0];

            var result = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                if (line.Trim().Length == 0)
                    continue;
                result.Add(line.TrimEnd());
            }
            return result.ToArray();
        }

        private static string NormalizeText(IList<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return string.Empty;

            var parts = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                string value = NormalizeTextValue(lines[i] ?? string.Empty);
                if (value.Length > 0)
                    parts.Add(value);
            }
            return string.Join("\n", parts.ToArray());
        }

        private static string NormalizeTextValue(string value)
        {
            string[] parts = SplitLines(CommonEffectLibrary.DecodeText(value));
            var cleaned = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string line = parts[i].Trim();
                if (line.Length > 0)
                    cleaned.Add(line);
            }
            return string.Join("\n", cleaned.ToArray());
        }

        private static bool SequenceEqual(string[] left, string[] right)
        {
            if (left == null || right == null)
                return left == right;
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
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

            bool added = start < 0;
            string[] block = added
                ? SplitLines(BuildBlock(draft))
                : PatchBlock(lines.GetRange(start, end - start + 1), draft);
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
