using System;
using System.Collections.Generic;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Where a deck file was found. Local decks live in
    /// StreamingAssets\Decks, the bundled AI decks in StreamingAssets\DefaultDecks.
    /// </summary>
    public enum DeckSource
    {
        Local = 0,
        Default = 1
    }

    /// <summary>
    /// One deck file. Only the card codes are kept; resolving them to card
    /// metadata happens lazily in the UI so opening the deck page stays cheap.
    /// </summary>
    public sealed class DeckDefinition
    {
        private readonly List<string> _cardCodes;
        private readonly List<string> _abbreviations;

        public string Name { get; private set; }
        public string FilePath { get; private set; }
        public DeckSource Source { get; private set; }

        /// <summary>Card codes in file order, duplicates included.</summary>
        public IList<string> CardCodes
        {
            get { return _cardCodes; }
        }

        /// <summary>Set prefixes such as MTI or DAL, in first-seen order.</summary>
        public IList<string> Abbreviations
        {
            get { return _abbreviations; }
        }

        public DeckDefinition(string name, string filePath, DeckSource source,
            IList<string> cardCodes)
        {
            Name = name ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            Source = source;
            _cardCodes = new List<string>();
            _abbreviations = new List<string>();

            if (cardCodes != null)
                _cardCodes.AddRange(cardCodes);
            CollectAbbreviations();
        }

        public int CardCount
        {
            get { return _cardCodes.Count; }
        }

        public string SourceDisplay
        {
            get { return Source == DeckSource.Default ? "预设卡组" : "我的卡组"; }
        }

        public string AbbreviationText
        {
            get { return string.Join("/", _abbreviations.ToArray()); }
        }

        /// <summary>
        /// Prefix of the first card code, used to pick a cover image without
        /// parsing any CardData.txt file.
        /// </summary>
        public string PrimaryAbbreviation
        {
            get { return _abbreviations.Count > 0 ? _abbreviations[0] : string.Empty; }
        }

        private void CollectAbbreviations()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _cardCodes.Count; i++)
            {
                string prefix = GetPrefix(_cardCodes[i]);
                if (prefix.Length == 0 || !seen.Add(prefix))
                    continue;
                _abbreviations.Add(prefix);
            }
        }

        /// <summary>
        /// "MTI/S83-E101" becomes "MTI"; a code without a slash is returned
        /// unchanged so unusual files still show something useful.
        /// </summary>
        public static string GetPrefix(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return string.Empty;

            string code = cardCode.Trim();
            int slash = code.IndexOf('/');
            return slash > 0 ? code.Substring(0, slash) : code;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
