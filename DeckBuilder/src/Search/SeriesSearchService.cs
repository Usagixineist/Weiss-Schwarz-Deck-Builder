using System;
using System.Collections.Generic;
using WeissDeckBuilder.Data;

namespace WeissDeckBuilder.Search
{
    /// <summary>
    /// Metadata-only search and sorting for the series cover overview.
    /// No card files are read, so typing in the series search box stays
    /// responsive even with the full 199-series catalog.
    /// </summary>
    public static class SeriesSearchService
    {
        public static bool Matches(SeriesDefinition series, string query)
        {
            if (series == null)
                return false;

            string term = Normalize(query);
            if (term.Length == 0)
                return true;

            if (Contains(series.Name, term) ||
                Contains(series.FolderName, term))
            {
                return true;
            }

            if (Contains(series.AbbreviationText, term))
                return true;

            IList<string> abbreviations = series.Abbreviations;
            for (int i = 0; i < abbreviations.Count; i++)
            {
                if (Contains(abbreviations[i], term))
                    return true;
            }

            IList<string> cardNumberHeads = series.CardNumberHeads;
            for (int i = 0; i < cardNumberHeads.Count; i++)
            {
                if (Contains(cardNumberHeads[i], term))
                    return true;
            }

            for (int i = 0; i < series.Folders.Count; i++)
            {
                SeriesFolder folder = series.Folders[i];
                if (Contains(folder.FolderPath, term))
                    return true;

                foreach (string prefix in folder.EnumeratePrefixes())
                {
                    if (Contains(prefix, term))
                        return true;
                }
            }

            return false;
        }

        public static string FormatAbbreviations(SeriesDefinition series)
        {
            if (series == null)
                return string.Empty;
            return series.AbbreviationText;
        }

        public static int CompareByName(SeriesDefinition x, SeriesDefinition y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;
            return GbkStringComparer.Instance.Compare(x.Name, y.Name);
        }

        public static int CompareByAbbreviation(
            SeriesDefinition x,
            SeriesDefinition y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;

            int result = StringComparer.OrdinalIgnoreCase.Compare(
                x.AbbreviationText, y.AbbreviationText);
            return result != 0 ? result : CompareByName(x, y);
        }

        private static bool Contains(string value, string normalizedTerm)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            return Normalize(value).IndexOf(
                normalizedTerm, StringComparison.Ordinal) >= 0;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                    continue;
                if (c == '\uFF0F')
                    c = '/';
                builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }
    }
}
