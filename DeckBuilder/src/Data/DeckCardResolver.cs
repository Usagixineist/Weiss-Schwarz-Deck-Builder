using System;
using System.Collections.Generic;
using System.IO;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Turns the card codes of a deck file into real cards. The deck files and
    /// the CardData.txt files do not always spell a code the same way, so every
    /// code is compared through <see cref="CardCodeMatcher.Canonical"/>. The
    /// index is built once per deck and reused by the image tiles and by the
    /// card detail page.
    /// </summary>
    public sealed class DeckCardResolver
    {
        private readonly CardDatabase _database;
        private readonly DeckCoverLocator _images;
        private readonly Dictionary<string, Dictionary<string, WeissCard>> _indexes;
        private readonly object _sync;

        public DeckCardResolver(CardDatabase database, DeckCoverLocator images)
        {
            if (database == null)
                throw new ArgumentNullException("database");

            _database = database;
            _images = images;
            _indexes = new Dictionary<string, Dictionary<string, WeissCard>>(
                StringComparer.OrdinalIgnoreCase);
            _sync = new object();
        }

        /// <summary>Card of a deck code, or null when no set defines it.</summary>
        public WeissCard Resolve(DeckDefinition deck, string cardCode)
        {
            if (deck == null || string.IsNullOrEmpty(cardCode))
                return null;

            Dictionary<string, WeissCard> index = GetIndex(deck);
            WeissCard card;
            if (index.TryGetValue(
                CardCodeMatcher.Canonical(cardCode), out card))
            {
                return card;
            }

            return null;
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

        private Dictionary<string, WeissCard> GetIndex(DeckDefinition deck)
        {
            string key = string.IsNullOrEmpty(deck.FilePath)
                ? deck.Name
                : deck.FilePath;

            lock (_sync)
            {
                Dictionary<string, WeissCard> cached;
                if (_indexes.TryGetValue(key, out cached))
                    return cached;

                Dictionary<string, WeissCard> index = BuildIndex(deck);
                _indexes[key] = index;
                return index;
            }
        }

        private Dictionary<string, WeissCard> BuildIndex(DeckDefinition deck)
        {
            var index = new Dictionary<string, WeissCard>(
                StringComparer.OrdinalIgnoreCase);
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
                    string canonical = CardCodeMatcher.Canonical(card.Id);
                    if (canonical.Length == 0 || index.ContainsKey(canonical))
                        continue;

                    index[canonical] = card;
                }
            }

            return index;
        }
    }
}
