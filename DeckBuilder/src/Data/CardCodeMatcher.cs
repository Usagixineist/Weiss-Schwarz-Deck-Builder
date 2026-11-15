using System;
using System.Text;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Normalises card codes so a deck file and a CardData.txt entry still match
    /// when they disagree about the parallel-art notation. Decks write
    /// "NGL/S58-E001" while the card file stores "NGL/S58-001", trial cards use
    /// "KS/W49-TE16" for the image but "KS/W49-T16" as the id, and parallel
    /// rarities append letters such as "GBS/S63-E068S" to a base card.
    /// </summary>
    public static class CardCodeMatcher
    {
        /// <summary>
        /// Lookup key of a card code: upper case, '_' folded to '-', the
        /// parallel-art E in front of the number part removed and the trailing
        /// rarity letters dropped. "MTI/S83-E055S" and "MTI/S83-055" share the
        /// key "MTI/S83-055".
        /// </summary>
        public static string Canonical(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return string.Empty;

            string code = cardCode.Trim().ToUpperInvariant().Replace('_', '-');
            int separator = code.IndexOf('-');
            if (separator <= 0 || separator >= code.Length - 1)
                return code;

            string head = code.Substring(0, separator + 1);
            string suffix = code.Substring(separator + 1);

            var builder = new StringBuilder(suffix.Length);
            for (int i = 0; i < suffix.Length; i++)
            {
                char current = suffix[i];
                if (current == 'E' &&
                    i + 1 < suffix.Length &&
                    char.IsDigit(suffix[i + 1]))
                {
                    continue;
                }

                builder.Append(current);
            }

            string normalized = builder.ToString();
            int end = normalized.Length;
            while (end > 0 && char.IsLetter(normalized[end - 1]))
                end--;

            if (end > 0 && end < normalized.Length)
                normalized = normalized.Substring(0, end);

            return head + normalized;
        }

        /// <summary>
        /// Image name the game uses for the base print of a card:
        /// "KS/W49-003" becomes "KS/W49-E003", "KS/W49-T16" becomes
        /// "KS/W49-TE16" and "KS/W49-P06" becomes "KS/W49-PE06". Codes that
        /// already carry the E marker are returned unchanged. Both '/' + '-'
        /// codes and plain file names such as "HOL_W104_041" are supported.
        /// </summary>
        public static string EnsureParallelE(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return cardCode;

            string code = cardCode.Trim();
            int separator = code.IndexOf('-');
            if (separator <= 0)
                separator = code.LastIndexOf('_');

            if (separator <= 0 || separator >= code.Length - 1)
                return code;

            string suffix = code.Substring(separator + 1);
            if (suffix.Length == 0)
                return code;

            string prefix = code.Substring(0, separator + 1);
            if (char.IsDigit(suffix[0]))
                return prefix + "E" + suffix;

            char first = char.ToUpperInvariant(suffix[0]);
            if ((first == 'T' || first == 'P') &&
                suffix.Length > 1 &&
                char.IsDigit(suffix[1]))
            {
                return prefix + suffix[0] + "E" + suffix.Substring(1);
            }

            return code;
        }

        /// <summary>
        /// "GBS/S63-E068S" yields "GBS/S63-E068"; codes without trailing
        /// letters or without a card number are returned unchanged.
        /// </summary>
        public static string TrimRaritySuffix(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return cardCode;

            int end = cardCode.Length;
            while (end > 0 && char.IsLetter(cardCode[end - 1]))
                end--;

            if (end == cardCode.Length || end == 0)
                return cardCode;

            return char.IsDigit(cardCode[end - 1])
                ? cardCode.Substring(0, end)
                : cardCode;
        }
    }
}
