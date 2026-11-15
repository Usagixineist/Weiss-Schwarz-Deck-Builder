using System;
using System.Collections.Generic;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// One card code together with how often it appears in a deck file. Only
    /// the 缩略 view needs the repeat count; the 展开 view keeps every copy.
    /// </summary>
    public sealed class DeckCardCount
    {
        public string Code { get; private set; }
        public int Count { get; private set; }

        public DeckCardCount(string code, int count)
        {
            Code = code ?? string.Empty;
            Count = count < 0 ? 0 : count;
        }

        public override string ToString()
        {
            return Count > 1 ? Code + " x" + Count : Code;
        }
    }

    /// <summary>
    /// Ordering helpers for the 编辑卡组 card view. A deck file keeps the order
    /// the player saved, but both deck views list cards by code so the same
    /// card always appears at the same place and duplicates sit next to each
    /// other.
    /// </summary>
    public static class DeckComposition
    {
        /// <summary>
        /// Returns every card code sorted by code. Duplicates are preserved so
        /// the result has the same length as the deck file.
        /// </summary>
        public static IList<string> OrderByCode(IList<string> cardCodes)
        {
            var ordered = new List<string>();
            if (cardCodes == null)
                return ordered;

            ordered.AddRange(cardCodes);
            ordered.Sort(CompareCodes);
            return ordered;
        }

        /// <summary>
        /// One entry per card of the deck, used by the ←/→ arrows of the card
        /// detail page. The codes are ordered by <see cref="OrderByCode"/> and
        /// copies as well as art variants of the same card collapse into the
        /// first code that carries them, so the arrows never stop twice on the
        /// same card.
        /// </summary>
        public static IList<string> DistinctCards(IList<string> cardCodes)
        {
            IList<string> ordered = OrderByCode(cardCodes);
            var distinct = new List<string>(ordered.Count);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (keys.Add(CardCodeMatcher.Canonical(ordered[i])))
                    distinct.Add(ordered[i]);
            }

            return distinct;
        }

        /// <summary>
        /// Collapses duplicate codes into one entry with a repeat count: the
        /// data behind the 异画 option of the 缩略 view. Entries keep the
        /// <see cref="OrderByCode"/> order and the counts add up to the deck
        /// size.
        /// </summary>
        public static IList<DeckCardCount> GroupByCode(IList<string> cardCodes)
        {
            return Group(cardCodes, null);
        }

        /// <summary>
        /// Collapses every art variant of the same card into one entry, the
        /// default data of the 缩略 view. Parallel art codes such as
        /// MTI/S83-E055S merge with their normal print MTI/S83-E055 and the
        /// group keeps the normal code, so only the base art is shown.
        /// </summary>
        public static IList<DeckCardCount> GroupByBaseCode(IList<string> cardCodes)
        {
            return Group(cardCodes, GetBaseCode);
        }

        /// <summary>
        /// Removes the parallel art suffix of a card code: "MTI/S83-E055S"
        /// yields "MTI/S83-E055". Codes that do not end in a set number plus
        /// letters are returned unchanged, so unusual names stay intact.
        /// </summary>
        public static string GetBaseCode(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return string.Empty;

            string code = cardCode.Trim();
            int end = code.Length;
            while (end > 0 && char.IsLetter(code[end - 1]))
                end--;

            if (end == code.Length || end == 0)
                return code;

            // Only strip when a card number precedes the suffix.
            return char.IsDigit(code[end - 1])
                ? code.Substring(0, end)
                : code;
        }

        /// <summary>
        /// Shared grouping pass. <paramref name="keySelector"/> decides which
        /// codes belong together; passing null groups by the exact code.
        /// </summary>
        private static IList<DeckCardCount> Group(
            IList<string> cardCodes, Func<string, string> keySelector)
        {
            var groups = new List<DeckCardCount>();
            if (cardCodes == null)
                return groups;

            IList<string> ordered = OrderByCode(cardCodes);
            var indexByKey = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ordered.Count; i++)
            {
                string code = ordered[i] ?? string.Empty;
                string key = keySelector == null ? code : keySelector(code);

                int index;
                if (indexByKey.TryGetValue(key, out index))
                {
                    DeckCardCount existing = groups[index];
                    groups[index] = new DeckCardCount(
                        existing.Code, existing.Count + 1);
                    continue;
                }

                indexByKey[key] = groups.Count;
                groups.Add(new DeckCardCount(key, 1));
            }

            return groups;
        }

        /// <summary>
        /// Sorts by code, ignoring case first and falling back to an ordinal
        /// compare so codes that differ only in case still have a stable order.
        /// </summary>
        private static int CompareCodes(string left, string right)
        {
            int result = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            return result != 0
                ? result
                : string.CompareOrdinal(left, right);
        }
    }
}
