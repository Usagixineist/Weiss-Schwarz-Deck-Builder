using System;
using System.Collections.Generic;
using System.Text;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Search
{
    /// <summary>
    /// Shared helpers for the names written inside an effect text.
    /// The data files mix Chinese, Japanese and English conventions: names may
    /// be wrapped in 「」, 『』, “” or a plain ASCII double quote, and a name can
    /// itself contain quotes (for example “作为恋人…”漆原琉华). Matching therefore
    /// happens after normalising width, case, quotes, middle dots, dashes and
    /// whitespace instead of comparing the raw strings.
    /// </summary>
    public static class CardNameMatcher
    {
        public const int DefaultMaxResults = 12;

        private const string QuoteCharacters = "\"'“”„‟‘’「」『』＂＇";
        private const string MiddleDots = "・·･•∙";
        private const string DashCharacters = "‐‑‒–—―−﹘﹣－";
        private const string WordSeparators =
            " \t\r\n.,:;!?()[]{}<>/\\|+~'\"“”„‟‘’「」『』";

        /// <summary>
        /// Case/width/quote-insensitive form of a card name used for lookups.
        /// </summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string source = ToWidthInsensitive(value);
            var builder = new StringBuilder(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsWhiteSpace(c))
                    continue;
                if (QuoteCharacters.IndexOf(c) >= 0)
                    continue;
                if (MiddleDots.IndexOf(c) >= 0)
                    continue;
                if (DashCharacters.IndexOf(c) >= 0)
                {
                    builder.Append('-');
                    continue;
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        /// <summary>
        /// Returns 0 for an exact match, 1 when the candidate starts with the
        /// target, 2 when it merely contains it, 3 when the target contains the
        /// candidate and 4 when every word of the target appears somewhere in
        /// the candidate. -1 means no match.
        /// </summary>
        public static int Score(string candidate, string target)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(target))
                return -1;

            string goal = Normalize(target);
            if (goal.Length == 0)
                return -1;

            return ScoreNormalized(Normalize(candidate), target, goal);
        }

        /// <summary>
        /// Resolves a name written inside an effect text to the cards of a
        /// series. Clones are skipped and the list is ordered from the most to
        /// the least plausible match; cards of <paramref name="preferredFolder"/>
        /// win when two matches score the same.
        /// </summary>
        public static List<WeissCard> Resolve(
            IEnumerable<WeissCard> cards, string target, int maxResults,
            string preferredFolder)
        {
            var results = new List<WeissCard>();
            if (cards == null || string.IsNullOrEmpty(target))
                return results;

            string goal = Normalize(target);
            if (goal.Length == 0)
                return results;

            if (maxResults <= 0)
                maxResults = DefaultMaxResults;

            bool hasFolder = !string.IsNullOrEmpty(preferredFolder);
            var scored = new List<ScoredCard>();
            foreach (WeissCard card in cards)
            {
                if (card == null || card.IsClone)
                    continue;

                int score = ScoreNormalized(
                    Normalize(card.JapaneseName), target, goal);
                int other = ScoreNormalized(Normalize(card.Name), target, goal);
                if (other >= 0 && (score < 0 || other < score))
                    score = other;
                if (score < 0)
                    continue;

                var entry = new ScoredCard();
                entry.Score = score;
                entry.Card = card;
                entry.SameFolder = hasFolder &&
                    string.Equals(card.FolderPath, preferredFolder,
                        StringComparison.OrdinalIgnoreCase);
                scored.Add(entry);
            }

            scored.Sort(CompareScored);
            for (int i = 0; i < scored.Count && results.Count < maxResults; i++)
                results.Add(scored[i].Card);
            return results;
        }

        /// <summary>
        /// Same as <see cref="Resolve(IEnumerable{WeissCard}, string, int, string)"/>
        /// without a preferred set folder.
        /// </summary>
        public static List<WeissCard> Resolve(
            IEnumerable<WeissCard> cards, string target, int maxResults)
        {
            return Resolve(cards, target, maxResults, null);
        }

        private struct ScoredCard
        {
            public int Score;
            public bool SameFolder;
            public WeissCard Card;
        }

        /// <summary>
        /// Extracts every quoted name of an effect text. 「」, 『』, “”, „“ and
        /// a plain ASCII double quote are supported; an unmatched opener is
        /// skipped so a later quoted name is still found.
        /// </summary>
        public static List<string> ExtractNames(string text)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(text))
                return names;

            int index = 0;
            while (index < text.Length)
            {
                int open = IndexOfNameOpener(text, index);
                if (open < 0)
                    break;

                char closer = CloserForNameOpener(text[open]);
                int close = closer == '\0'
                    ? -1
                    : text.IndexOf(closer, open + 1);
                if (close < 0)
                {
                    index = open + 1;
                    continue;
                }

                string name = text.Substring(open + 1, close - open - 1).Trim();
                if (LooksLikeCardName(name) && !ContainsName(names, name))
                    names.Add(name);
                index = close + 1;
            }
            return names;
        }

        private static string ToWidthInsensitive(string value)
        {
            try
            {
                return value.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                return value;
            }
        }

        private static int ScoreNormalized(
            string candidate, string target, string goal)
        {
            if (candidate.Length == 0)
                return -1;

            if (string.Equals(candidate, goal, StringComparison.Ordinal))
                return 0;

            if (candidate.Length > goal.Length &&
                candidate.StartsWith(goal, StringComparison.Ordinal))
            {
                return 1;
            }

            if (candidate.IndexOf(goal, StringComparison.Ordinal) >= 0)
                return 2;

            if (candidate.Length >= 3 && goal.Length > candidate.Length &&
                goal.IndexOf(candidate, StringComparison.Ordinal) >= 0)
            {
                return 3;
            }

            return ContainsEveryWord(candidate, target) ? 4 : -1;
        }

        private static bool ContainsEveryWord(string candidate, string target)
        {
            string[] words = SplitWords(target);
            if (words.Length < 2)
                return false;

            for (int i = 0; i < words.Length; i++)
            {
                if (candidate.IndexOf(words[i], StringComparison.Ordinal) < 0)
                    return false;
            }
            return true;
        }

        private static string[] SplitWords(string target)
        {
            var words = new List<string>();
            int start = 0;
            for (int i = 0; i <= target.Length; i++)
            {
                bool end = i == target.Length ||
                    WordSeparators.IndexOf(target[i]) >= 0;
                if (!end)
                    continue;

                string word = Normalize(target.Substring(start, i - start));
                start = i + 1;
                if (word.Length < 2 || IsNumber(word) || words.Contains(word))
                    continue;
                words.Add(word);
            }
            return words.ToArray();
        }

        private static bool IsNumber(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                    return false;
            }
            return true;
        }

        private static int CompareScored(ScoredCard left, ScoredCard right)
        {
            int result = left.Score.CompareTo(right.Score);
            if (result != 0)
                return result;

            if (left.SameFolder != right.SameFolder)
                return left.SameFolder ? -1 : 1;

            return string.CompareOrdinal(left.Card.Id, right.Card.Id);
        }

        private static bool ContainsName(List<string> names, string name)
        {
            string normalized = Normalize(name);
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(
                    Normalize(names[i]), normalized, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static int IndexOfNameOpener(string text, int start)
        {
            for (int i = start; i < text.Length; i++)
            {
                if (CloserForNameOpener(text[i]) != '\0')
                    return i;
            }
            return -1;
        }

        private static char CloserForNameOpener(char value)
        {
            switch (value)
            {
                case '「': return '」';
                case '『': return '』';
                case '“': return '”';
                case '”': return '“';
                case '„': return '“';
                case '"':
                case '＂': return '"';
                default: return '\0';
            }
        }

        private static bool LooksLikeCardName(string name)
        {
            if (name.Length < 2 || name.Length > 60)
                return false;

            bool hasContent = false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '\r' || c == '\n' || c == '\t')
                    return false;
                if (char.IsLetterOrDigit(c))
                    hasContent = true;
            }
            return hasContent;
        }
    }
}
