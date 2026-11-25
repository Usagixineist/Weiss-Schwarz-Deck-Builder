using System;
using System.Collections.Generic;
using System.IO;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Picks the random card shown on the landing page. The date seeds both
    /// the series and the card inside it, so the same day always produces the
    /// same card without a settings file. Alternate artworks are ordinary
    /// clones inside <see cref="SeriesDefinition.EnumerateCards"/>, so they
    /// take part in the draw just like the base card.
    /// </summary>
    public static class DailyCardPicker
    {
        // One series is enough to parse at startup; a few fallbacks cover
        // empty folders without ever turning the landing page into a full
        // database scan.
        private const int MaxSeriesAttempts = 8;
        private const int SeedSalt = 0x2C1B;

        public static WeissCard Pick(CardDatabase database, DateTime date)
        {
            if (database == null)
                return null;

            IList<string> names = database.SeriesNames;
            if (names == null || names.Count == 0)
                return null;

            var random = new Random(DailySeed.ForDate(date, SeedSalt));
            var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<WeissCard>();
            WeissCard fallback = null;

            int attempts = Math.Min(MaxSeriesAttempts, names.Count);
            int tried = 0;
            while (tried < attempts)
            {
                string seriesName = names[random.Next(names.Count)];
                if (!attempted.Add(seriesName))
                    continue;

                tried++;
                SeriesDefinition series;
                try
                {
                    series = database.GetSeries(seriesName);
                }
                catch (Exception)
                {
                    // A broken folder must not break the landing page.
                    continue;
                }

                if (series == null)
                    continue;

                candidates.Clear();
                foreach (WeissCard card in series.EnumerateCards())
                {
                    if (card != null)
                        candidates.Add(card);
                }

                if (candidates.Count == 0)
                    continue;

                var withImage = new List<WeissCard>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    WeissCard card = candidates[i];
                    if (fallback == null)
                        fallback = card;
                    if (!string.IsNullOrEmpty(card.ImagePath) &&
                        File.Exists(card.ImagePath))
                    {
                        withImage.Add(card);
                    }
                }

                if (withImage.Count > 0)
                    return withImage[random.Next(withImage.Count)];
            }

            return fallback;
        }
    }
}
