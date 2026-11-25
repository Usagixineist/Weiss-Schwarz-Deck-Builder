using System;
using System.Collections.Generic;
using System.IO;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Turns the card codes of a deck file into real cards. The deck files and
    /// the CardData.txt files do not always spell a code the same way, so a code
    /// is tried against an exact lookup first and against
    /// <see cref="CardCodeMatcher.Canonical"/> afterwards. The index is built
    /// once per deck and reused by the image tiles and by the card detail page.
    ///
    /// The exact lookup has to come first: Canonical drops the trailing letters
    /// that name an alternate art, so a deck code such as `BDY/W135-046a` and
    /// its base card `BDY/W135-046` share one canonical key. The base card is
    /// parsed before 异画 clones are appended, so it used to win the key and
    /// 卡组预览 painted the normal print although the deck stored the 异画 code.
    ///
    /// A rarity suffix behaves the same way: `MTI/S83-E055S` matches the clone
    /// built from `mti_s83_055s.png`, so the deck shows the extracted rarity
    /// art and only falls back to the base print when no file carries it.
    /// </summary>
    public sealed class DeckCardResolver
    {
        /// <summary>
        /// Lookup tables of one deck. <see cref="Exact"/> keeps the variant
        /// letter of an alternate art, <see cref="Canonical"/> is the lenient
        /// fallback for codes CardData.txt spells another way.
        /// </summary>
        private sealed class DeckCardIndex
        {
            public readonly Dictionary<string, WeissCard> Exact;
            public readonly Dictionary<string, WeissCard> Canonical;

            public DeckCardIndex()
            {
                Exact = new Dictionary<string, WeissCard>(
                    StringComparer.OrdinalIgnoreCase);
                Canonical = new Dictionary<string, WeissCard>(
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        private readonly CardDatabase _database;
        private readonly DeckCoverLocator _images;
        private readonly Dictionary<string, DeckCardIndex> _indexes;
        private readonly object _sync;

        public DeckCardResolver(CardDatabase database, DeckCoverLocator images)
        {
            if (database == null)
                throw new ArgumentNullException("database");

            _database = database;
            _images = images;
            _indexes = new Dictionary<string, DeckCardIndex>(
                StringComparer.OrdinalIgnoreCase);
            _sync = new object();
        }

        /// <summary>Card of a deck code, or null when no set defines it.</summary>
        public WeissCard Resolve(DeckDefinition deck, string cardCode)
        {
            if (deck == null || string.IsNullOrEmpty(cardCode))
                return null;

            DeckCardIndex index = GetIndex(deck);
            WeissCard card;
            if (TryExact(index, cardCode, out card))
                return card;

            if (index.Canonical.TryGetValue(
                CardCodeMatcher.Canonical(cardCode), out card))
            {
                return card;
            }

            return null;
        }

        /// <summary>
        /// Looks a code up without losing the trailing letter of an alternate
        /// art (`BDY/W135-046a` stays itself here). The spelling with the
        /// parallel E is tried as well because deck files and CardData.txt
        /// disagree on it (`KS/W49-E003a` vs `KS/W49-003a`).
        /// </summary>
        private static bool TryExact(DeckCardIndex index, string cardCode,
            out WeissCard card)
        {
            card = null;
            string code = cardCode == null ? string.Empty : cardCode.Trim();
            if (code.Length == 0)
                return false;

            if (index.Exact.TryGetValue(ExactKey(code), out card))
                return true;

            string parallel = CardCodeMatcher.EnsureParallelE(code);
            if (!string.IsNullOrEmpty(parallel) &&
                !string.Equals(parallel, code, StringComparison.OrdinalIgnoreCase) &&
                index.Exact.TryGetValue(ExactKey(parallel), out card))
            {
                return true;
            }

            card = null;
            return false;
        }

        /// <summary>Index key of a code, with the variant letter kept.</summary>
        private static string ExactKey(string cardCode)
        {
            string code = cardCode == null ? string.Empty : cardCode.Trim();
            return code.ToUpperInvariant().Replace('_', '-');
        }

        /// <summary>
        /// Image of a deck code. The parsed card wins because CardData.txt also
        /// names the art of trial and promo prints (SY/WP02-01 uses P01.jpg);
        /// the code based lookup stays as fallback for codes without card data.
        /// </summary>
        public string GetImagePath(DeckDefinition deck, string cardCode)
        {
            WeissCard card = Resolve(deck, cardCode);
            if (card != null)
            {
                string path = ExistingPath(card.ImagePath);
                if (path != null)
                    return path;

                path = ExistingPath(card.AltImagePath);
                if (path != null)
                    return path;
            }

            return _images == null
                ? null
                : _images.GetCardImagePath(cardCode);
        }

        public void Clear()
        {
            lock (_sync)
            {
                _indexes.Clear();
            }
        }

        private static string ExistingPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            return File.Exists(path) ? path : null;
        }

        private DeckCardIndex GetIndex(DeckDefinition deck)
        {
            string key = string.IsNullOrEmpty(deck.FilePath)
                ? deck.Name
                : deck.FilePath;

            lock (_sync)
            {
                DeckCardIndex cached;
                if (_indexes.TryGetValue(key, out cached))
                    return cached;

                DeckCardIndex index = BuildIndex(deck);
                _indexes[key] = index;
                return index;
            }
        }

        private DeckCardIndex BuildIndex(DeckDefinition deck)
        {
            var index = new DeckCardIndex();
            if (_images == null)
                return index;

            IList<SeriesDefinition> seriesList = _images.ResolveSeries(deck);
            for (int i = 0; i < seriesList.Count; i++)
            {
                SeriesDefinition series = _database.GetSeries(seriesList[i].Name);
                if (series == null)
                    continue;

                foreach (WeissCard card in series.EnumerateCards())
                {
                    AddExact(index.Exact, card);

                    string canonical = CardCodeMatcher.Canonical(card.Id);
                    if (canonical.Length == 0 || index.Canonical.ContainsKey(canonical))
                        continue;

                    index.Canonical[canonical] = card;
                }
            }

            return index;
        }

        /// <summary>
        /// Registers a card under its own id and under the parallel E spelling.
        /// CardData.txt is parsed before 异画 clones are appended, so a key that
        /// is already taken keeps the card that was there first.
        /// </summary>
        private static void AddExact(Dictionary<string, WeissCard> exact,
            WeissCard card)
        {
            AddExactKey(exact, ExactKey(card.Id), card);

            string parallel = CardCodeMatcher.EnsureParallelE(card.Id);
            if (!string.IsNullOrEmpty(parallel))
                AddExactKey(exact, ExactKey(parallel), card);
        }

        private static void AddExactKey(Dictionary<string, WeissCard> exact,
            string key, WeissCard card)
        {
            if (key.Length == 0 || exact.ContainsKey(key))
                return;

            exact[key] = card;
        }
    }
}
