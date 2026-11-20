using System;
using System.Collections.Generic;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// One 效果 of a card: the text printed on the card face plus the ability
    /// code that carries it (`Act` / `Auto` / `Cont` … and its `{ }` block).
    /// CardData.txt keeps the two together — every `Text` line sits right below
    /// the ability it belongs to — so the editor and the writer both walk these
    /// pairs in file order instead of collecting all texts above all code.
    /// </summary>
    public sealed class CardEffectPair
    {
        public string Text { get; set; }
        public string Code { get; set; }

        /// <summary>
        /// 效果内容 a `*Shorthand` of this 效果 copies in from CommonEffects.txt.
        /// 编辑卡片 shows it as a read-only preview so the card face reads
        /// complete; 保存 never writes it, because the shorthand adds the same
        /// 效果 again on every load.
        /// </summary>
        public string InheritedText { get; set; }

        /// <summary>True when this 效果 carries text from a `*Shorthand`.</summary>
        public bool FromShorthand
        {
            get { return !string.IsNullOrEmpty(InheritedText); }
        }

        public CardEffectPair()
            : this(string.Empty, string.Empty)
        {
        }

        public CardEffectPair(string text, string code)
        {
            Text = text ?? string.Empty;
            Code = code ?? string.Empty;
            InheritedText = string.Empty;
        }
    }

    /// <summary>
    /// Splits 效果代码 into the ability blocks it is made of and puts every
    /// 效果内容 next to the block that carries it. A multi-effect card therefore
    /// reads `内容 1 / 代码 1 / 内容 2 / 代码 2`, exactly like its CardData.txt
    /// block, and a save can write the `Text` lines back where they came from.
    /// </summary>
    public static class CardEffectSplitter
    {
        /// <summary>Ability blocks of one 效果代码, in file order.</summary>
        public static List<string[]> SplitCode(string code)
        {
            return SplitCode(CardDataWriter.SplitLines(code));
        }

        public static List<string[]> SplitCode(IList<string> lines)
        {
            var segments = new List<string[]>();
            if (lines == null || lines.Count == 0)
                return segments;

            var current = new List<string>();
            int depth = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                string trimmed = line.Trim();
                if (depth == 0 && current.Count > 0 &&
                    (IsAbilityHeader(trimmed) || IsShorthandLine(trimmed)))
                {
                    segments.Add(current.ToArray());
                    current.Clear();
                }

                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                    depth++;
                else if (trimmed.StartsWith("}", StringComparison.Ordinal))
                    depth = Math.Max(0, depth - 1);
                current.Add(line);
            }

            if (current.Count > 0)
                segments.Add(current.ToArray());
            return segments;
        }

        /// <summary>
        /// Pairs 效果内容 with 效果代码, one entry per 效果. A card without code
        /// lines, or with a single ability, keeps its whole 效果内容 in one
        /// entry: a text that wraps on the card is one 效果, not several.
        /// </summary>
        public static IList<CardEffectPair> Pair(string text, string code)
        {
            return Pair(SplitTexts(text), code);
        }

        /// <summary>
        /// Same pairing from the 效果内容 the block spells out: one entry per
        /// `Text` line, so a text that wraps keeps its line breaks inside its
        /// own 效果 instead of being read as the next one.
        /// </summary>
        public static IList<CardEffectPair> Pair(IList<string> texts, string code)
        {
            return PairSegments(SplitValues(texts), SplitCode(code));
        }

        /// <summary>
        /// Pairs one 效果内容 entry with one ability block, in file order. A
        /// card with a single block keeps its whole 效果内容 in one entry, and
        /// so does a card without any text.
        /// </summary>
        private static IList<CardEffectPair> PairSegments(string[] values,
            List<string[]> segments)
        {
            if (segments.Count <= 1 || values.Length == 0)
            {
                // 一条能力带着整段卡面文字，或者这张卡根本没有印刷效果文字：
                // 一行装下整段代码，免得拆出一堆空的“效果内容”框。
                var single = new CardEffectPair(
                    string.Join("\n", values),
                    string.Join("\n", JoinSegments(segments)));
                return new List<CardEffectPair> { single };
            }

            int count = Math.Max(segments.Count, values.Length);
            var pairs = new List<CardEffectPair>(count);
            for (int i = 0; i < count; i++)
            {
                pairs.Add(new CardEffectPair(
                    i < values.Length ? values[i] : string.Empty,
                    i < segments.Count ? string.Join("\n", segments[i]) : string.Empty));
            }
            return pairs;
        }

        /// <summary>
        /// Pairs the 效果 of an editor draft, one row per ability block, using
        /// the block every `Text` line was read from. A `*Shorthand` row
        /// therefore shows both the text the block prints itself
        /// (<see cref="CardEffectPair.Text"/>, editable) and the text the
        /// shorthand copies in (<see cref="CardEffectPair.InheritedText"/>,
        /// read-only): leaving the shorthand text out made the 内容 box of such
        /// a row stay blank and moved every later 效果 one row up.
        /// A draft built by hand carries no per-block information and falls
        /// back to pairing the lines in file order.
        /// </summary>
        public static IList<CardEffectPair> PairDraft(CardEditorDraft draft)
        {
            if (draft == null)
                return new List<CardEffectPair>();

            List<string[]> segments = SplitCode(draft.EffectCode);
            bool fromBlock;
            IList<string> lines = DraftTextLines(draft, out fromBlock);
            if (fromBlock && segments.Count > 0 && HasBlockIndexes(draft, lines))
            {
                var texts = new List<string>();
                var blocks = new List<int>();
                List<List<string>> inherited = CreateBlockTexts(segments.Count);
                ClassifyText(draft, lines, segments.Count, texts, blocks, inherited);
                return BuildPairs(segments, texts, blocks, inherited);
            }

            string[] own = SplitValues(OwnTextLines(draft));
            if (own.Length == 0 && lines != null && lines.Count > 0)
                return Pair(lines, draft.EffectCode);

            return PairOwnTexts(own, segments);
        }

        /// <summary>
        /// Pairs the 效果 the way CardData.txt has to receive them: the 效果内容
        /// a `*Shorthand` copies in from CommonEffects.txt is left out, because
        /// the shorthand adds it again on load and the card would show one more
        /// copy of the same 效果 after every save. What is left goes back below
        /// the ability block it was read from, `*Shorthand` rows included:
        /// CardData.txt writes `*A` + `Text T1` + `*B` + `Text T2`, so an
        /// untouched card writes every `Text` line back where it came from.
        /// </summary>
        public static IList<CardEffectPair> PairDraftForWrite(CardEditorDraft draft)
        {
            if (draft == null)
                return new List<CardEffectPair>();

            List<string[]> segments = SplitCode(draft.EffectCode);
            bool fromBlock;
            IList<string> lines = DraftTextLines(draft, out fromBlock);
            if (fromBlock && segments.Count > 0 && HasBlockIndexes(draft, lines))
            {
                var texts = new List<string>();
                var blocks = new List<int>();
                ClassifyText(draft, lines, segments.Count, texts, blocks, null);
                return BuildPairs(segments, texts, blocks, null);
            }

            return PairOwnTexts(SplitValues(OwnTextLines(draft)), segments);
        }

        /// <summary>True for a line that opens a 效果 block at depth 0: an
        /// ability header such as `Auto: OnAttack` or a `*Shorthand` line. The
        /// parser counts the blocks of a card with the same rule, so the block
        /// number it stores per `Text` line matches the rows 编辑卡片 shows.</summary>
        public static bool OpensSegment(string line)
        {
            string trimmed = (line ?? string.Empty).Trim();
            return IsAbilityHeader(trimmed) || IsShorthandLine(trimmed);
        }

        /// <summary>
        /// 效果内容 of a draft that belongs to the block itself: everything but
        /// the pieces a `*Shorthand` copies in from CommonEffects.txt.
        /// </summary>
        public static List<string> OwnTextLines(CardEditorDraft draft)
        {
            if (draft == null)
                return new List<string>();

            bool fromBlock;
            IList<string> lines = DraftTextLines(draft, out fromBlock);
            if (lines == null || lines.Count == 0)
                return new List<string>();

            if (!fromBlock || !HasBlockIndexes(draft, lines))
            {
                // 手工拼出来的草稿没有按段信息：只能按值比较，和以前一样。
                return DropInherited(lines, draft.InheritedTextLines);
            }

            var texts = new List<string>();
            ClassifyText(draft, lines, SplitCode(draft.EffectCode).Count, texts,
                new List<int>(), null);
            return texts;
        }

        /// <summary>
        /// Own 效果内容 of a draft, one entry per ability block, paired in file
        /// order. A draft without per-block information (built by hand, or
        /// with its 效果内容 rewritten as one flat string) only knows the order
        /// of its lines, so line i goes to block i; a `*Shorthand` row is no
        /// exception, CardData.txt writes that 效果's text right below it.
        /// Text that is left over (more `Text` lines than blocks) follows the
        /// last row, so nothing the player wrote is lost.
        /// </summary>
        private static IList<CardEffectPair> PairOwnTexts(string[] values,
            List<string[]> segments)
        {
            if (values.Length == 0 || segments.Count <= 1)
                return PairSegments(values, segments);

            var pairs = new List<CardEffectPair>(segments.Count);
            int next = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                string text = string.Empty;
                if (next < values.Length)
                {
                    text = values[next];
                    next++;
                }
                pairs.Add(new CardEffectPair(text, string.Join("\n", segments[i])));
            }

            if (next < values.Length)
            {
                // More 效果内容 than ability blocks: the rest stays in the
                // same `Text` line of the last row, so no effect is dropped.
                var rest = new List<string>();
                for (; next < values.Length; next++)
                    rest.Add(values[next]);
                string extra = string.Join("\n", rest.ToArray());
                CardEffectPair lastPair = pairs[pairs.Count - 1];
                lastPair.Text = lastPair.Text.Length == 0
                    ? extra
                    : lastPair.Text + "\n" + extra;
            }
            return pairs;
        }

        /// <summary>
        /// True when the draft still knows which 效果 block every `Text` line
        /// belongs to. A draft whose 效果内容 the player replaced as one flat
        /// string has no such information and is paired by position instead.
        /// </summary>
        private static bool HasBlockIndexes(CardEditorDraft draft, IList<string> lines)
        {
            if (draft == null || lines == null || lines.Count == 0)
                return false;

            IList<int> blocks = draft.TextBlockIndexes;
            return blocks != null && blocks.Count == lines.Count;
        }

        /// <summary>One empty list per 效果 block, so text i can be collected
        /// under its own row without inventing rows.</summary>
        private static List<List<string>> CreateBlockTexts(int count)
        {
            int rows = count <= 0 ? 1 : count;
            var lists = new List<List<string>>(rows);
            for (int i = 0; i < rows; i++)
                lists.Add(new List<string>());
            return lists;
        }

        /// <summary>
        /// True when entry <paramref name="index"/> of the draft's 效果内容 is
        /// still the text a `*Shorthand` copies in from CommonEffects.txt and
        /// the player did not rewrite it. A rewritten line counts as the
        /// card's own text: 保存 writes it into the block instead of dropping
        /// what the player typed.
        /// </summary>
        private static bool IsInheritedText(CardEditorDraft draft, IList<string> lines,
            int index)
        {
            IList<bool> flags = draft.TextInherited;
            if (flags == null || flags.Count != lines.Count || index >= flags.Count ||
                !flags[index])
            {
                return false;
            }

            IList<string> original = draft.OriginalTextLines;
            if (original == null || index >= original.Count)
                return true;

            return string.Equals(lines[index] ?? string.Empty,
                original[index] ?? string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// Sorts every `Text` line of a draft into its 效果 block. The text the
        /// card prints itself goes to <paramref name="ownTexts"/>, one entry
        /// per line and in file order; the text a `*Shorthand` copies in goes
        /// to <paramref name="inherited"/>, one list per block, when the caller
        /// shows it.
        /// A line the block spells out although the shorthand already brings
        /// the same text (the way older versions of this editor saved it) is
        /// dropped: the next save clears the duplicate away instead of keeping
        /// it forever.
        /// </summary>
        private static void ClassifyText(CardEditorDraft draft, IList<string> lines,
            int segmentCount, List<string> ownTexts, List<int> ownBlocks,
            List<List<string>> inherited)
        {
            int count = lines.Count;
            var blockOf = new int[count];
            var fromShorthand = new bool[count];
            IList<int> blocks = draft.TextBlockIndexes;
            for (int i = 0; i < count; i++)
            {
                int block = blocks != null && i < blocks.Count ? blocks[i] : i;
                if (block < 0)
                    block = 0;
                if (segmentCount > 0 && block >= segmentCount)
                    block = segmentCount - 1;
                blockOf[i] = block;
                fromShorthand[i] = IsInheritedText(draft, lines, i);
            }

            // 每段简写已经带进来的正文：按位置（第几段）认，只有同一段里的
            // 重复行才算旧版本留下的多余 `Text` 行。
            List<List<string>> shorthand = CreateBlockTexts(segmentCount);
            var duplicates = new List<HashSet<string>>(shorthand.Count);
            for (int i = 0; i < shorthand.Count; i++)
                duplicates.Add(new HashSet<string>(StringComparer.Ordinal));

            for (int i = 0; i < count; i++)
            {
                if (!fromShorthand[i])
                    continue;

                string line = lines[i] ?? string.Empty;
                string[] pieces = SplitValues(CardDataWriter.SplitLines(line));
                for (int j = 0; j < pieces.Length; j++)
                    duplicates[blockOf[i]].Add(pieces[j].Trim());
                if (inherited != null)
                    shorthand[blockOf[i]].Add(line);
            }

            for (int i = 0; i < count; i++)
            {
                if (fromShorthand[i])
                    continue;

                string line = lines[i] ?? string.Empty;
                string[] pieces = CardDataWriter.SplitLines(line);
                var kept = new List<string>();
                for (int j = 0; j < pieces.Length; j++)
                {
                    string piece = pieces[j].TrimEnd();
                    if (duplicates[blockOf[i]].Contains(piece.Trim()))
                        continue;
                    kept.Add(piece);
                }

                string[] entry = SplitValues(kept);
                if (entry.Length == 0)
                    continue;
                ownTexts.Add(string.Join("\n", entry));
                ownBlocks.Add(blockOf[i]);
            }

            if (inherited == null)
                return;
            for (int i = 0; i < inherited.Count && i < shorthand.Count; i++)
            {
                if (shorthand[i].Count == 0)
                    continue;
                inherited[i].Clear();
                inherited[i].AddRange(shorthand[i]);
            }
        }

        /// <summary>
        /// One row per 效果 block: the 效果内容 the card prints itself, editable,
        /// plus the text the block's `*Shorthand` brings in, shown read-only.
        /// </summary>
        private static IList<CardEffectPair> BuildPairs(List<string[]> segments,
            List<string> texts, List<int> blocks, List<List<string>> inherited)
        {
            List<List<string>> own = CreateBlockTexts(segments.Count);
            for (int i = 0; i < texts.Count; i++)
            {
                int block = i < blocks.Count ? blocks[i] : own.Count - 1;
                if (block < 0)
                    block = 0;
                if (block >= own.Count)
                    block = own.Count - 1;
                own[block].Add(texts[i]);
            }

            var pairs = new List<CardEffectPair>(segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                var pair = new CardEffectPair(
                    string.Join("\n", own[i].ToArray()),
                    string.Join("\n", segments[i]));
                if (inherited != null && i < inherited.Count && inherited[i].Count > 0)
                    pair.InheritedText = string.Join("\n", inherited[i].ToArray());
                pairs.Add(pair);
            }
            return pairs;
        }

        /// <summary>
        /// Drops every line that matches one of the inherited texts. Matching
        /// the text itself also cleans up a card that was saved with the
        /// expanded text before this rule existed: the shorthand puts the same
        /// 效果 back on the next load, so the copy the block spells out is a
        /// duplicate and must not survive the next save.
        /// The comparison is line by line, not entry by entry: 编辑卡片 puts a
        /// card whose 效果代码 is one single ability into one row, and that row
        /// keeps the shorthand text and the card's own text in the same box.
        /// Only the own text may reach the file, or the next load shows the
        /// inherited 效果 twice and every save adds another copy.
        /// </summary>
        public static List<string> DropInherited(IList<string> lines,
            IList<string> inherited)
        {
            var result = new List<string>();
            if (lines == null)
                return result;

            var inheritedLines = new HashSet<string>(StringComparer.Ordinal);
            if (inherited != null)
            {
                for (int i = 0; i < inherited.Count; i++)
                {
                    string[] pieces = CardDataWriter.SplitLines(
                        inherited[i] ?? string.Empty);
                    for (int j = 0; j < pieces.Length; j++)
                    {
                        string piece = pieces[j].Trim();
                        if (piece.Length > 0)
                            inheritedLines.Add(piece);
                    }
                }
            }

            if (inheritedLines.Count == 0)
            {
                for (int i = 0; i < lines.Count; i++)
                    result.Add(lines[i] ?? string.Empty);
                return result;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                string[] pieces = CardDataWriter.SplitLines(
                    lines[i] ?? string.Empty);
                var kept = new List<string>();
                for (int j = 0; j < pieces.Length; j++)
                {
                    string piece = pieces[j].TrimEnd();
                    if (inheritedLines.Contains(piece.Trim()))
                        continue;
                    kept.Add(piece);
                }

                string[] entry = SplitValues(kept);
                if (entry.Length > 0)
                    result.Add(string.Join("\n", entry));
            }
            return result;
        }

        /// <summary>
        /// The 效果内容 pieces of a draft: its per-`Text` pieces when the draft
        /// still spells the same text, otherwise the flat 效果内容 split by
        /// lines (a draft built by hand only has the flat text).
        /// <paramref name="fromBlock"/> tells the caller which of the two it
        /// got: only the pieces the block spells out carry the per-block
        /// numbers of <see cref="CardEditorDraft.TextBlockIndexes"/>.
        /// </summary>
        private static IList<string> DraftTextLines(CardEditorDraft draft,
            out bool fromBlock)
        {
            fromBlock = false;
            IList<string> textLines = draft.TextLines;
            if (textLines != null && textLines.Count > 0)
            {
                string pieces = FlattenText(string.Join("\n", SplitValues(textLines)));
                if (string.Equals(pieces, FlattenText(draft.Text),
                    StringComparison.Ordinal))
                {
                    fromBlock = true;
                    return textLines;
                }
            }

            return SplitValues(CardDataWriter.SplitLines(draft.Text));
        }

        /// <summary>Effect texts of a block, one entry per `Text` line.</summary>
        public static string[] SplitTexts(string text)
        {
            return SplitValues(CardDataWriter.SplitLines(text));
        }

        /// <summary>Trims the blank entries around the texts and keeps the inner
        /// ones, because entry i belongs to ability block i.</summary>
        private static string[] SplitValues(IList<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return new string[0];

            int first = 0;
            int last = lines.Count - 1;
            while (first <= last && (lines[first] ?? string.Empty).Trim().Length == 0)
                first++;
            while (last >= first && (lines[last] ?? string.Empty).Trim().Length == 0)
                last--;
            if (last < first)
                return new string[0];

            var result = new string[last - first + 1];
            for (int i = first; i <= last; i++)
                result[i - first] = (lines[i] ?? string.Empty).TrimEnd();
            return result;
        }

        /// <summary>All ability lines of one 效果代码, back in file order.</summary>
        private static List<string> JoinSegments(List<string[]> segments)
        {
            var lines = new List<string>();
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = 0; j < segments[i].Length; j++)
                    lines.Add(segments[i][j]);
            }
            return lines;
        }

        /// <summary>All text of a block as one comparable string: one trimmed,
        /// non-empty line per entry.</summary>
        private static string FlattenText(string text)
        {
            string[] lines = CardDataWriter.SplitLines(text);
            var parts = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length > 0)
                    parts.Add(line);
            }
            return string.Join("\n", parts.ToArray());
        }

        /// <summary>
        /// `Auto: OnAttack`, `Act: ...`, `Cont: ...` start a new ability.
        /// Braces, comments and the parameter lines that belong to the
        /// ability before them do not. A `*Shorthand` is no `Key value` field
        /// either: it is 一条封装函数, and IsShorthandLine() splits it off so
        /// that `*A` + `*B` are two 效果 instead of one row.
        /// </summary>
        private static bool IsAbilityHeader(string trimmed)
        {
            if (trimmed.Length == 0)
                return false;
            if (trimmed.StartsWith("{", StringComparison.Ordinal) ||
                trimmed.StartsWith("}", StringComparison.Ordinal) ||
                trimmed.StartsWith("*", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                return false;
            }

            int colon = trimmed.IndexOf(':');
            if (colon <= 0)
                return false;

            for (int i = 0; i < colon; i++)
            {
                char ch = trimmed[i];
                if (!char.IsLetterOrDigit(ch) && ch != '_')
                    return false;
            }
            return true;
        }

        /// <summary>
        /// True for a `*Shorthand` line: one 封装函数 the game expands from
        /// CommonEffects.txt, like `*HandEncore` or `*OptionalHeal(1000)`.
        /// CardData.txt writes it right above the ability it stands for, so
        /// 编辑卡片 pairs its 效果内容 with the shorthand itself and a save puts
        /// the `Text` line back below the shorthand it belongs to. Lines
        /// inside a `{ }` block belong to the ability around them: the code
        /// is only split at depth 0.
        /// </summary>
        private static bool IsShorthandLine(string trimmed)
        {
            return trimmed.Length > 1 && trimmed[0] == '*';
        }
    }
}
