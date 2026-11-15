using System;
using System.Collections.Generic;
using System.Linq;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Search
{
    /// <summary>
    /// Exact port of the relevant parts of CardLibrary.SearchRoutine.
    /// The ordering of the filter checks and the alt-art test are deliberate.
    /// </summary>
    public sealed class CardSearchService
    {
        public List<WeissCard> Search(SeriesDefinition series, SearchQuery query)
        {
            var results = new List<WeissCard>();
            if (series == null || query == null)
                return results;

            string searchName = (query.CardName ?? string.Empty).Trim();
            string searchTrait = (query.Trait ?? string.Empty).Trim();
            string searchText = (query.Text ?? string.Empty).Trim().ToLower();

            for (int i = 0; i < series.Folders.Count; i++)
            {
                SeriesFolder folder = series.Folders[i];
                if (!MatchesSet(folder, query))
                    continue;

                for (int j = 0; j < folder.Cards.Count; j++)
                {
                    WeissCard card = folder.Cards[j];
                    if (!Matches(card, query, searchName, searchTrait, searchText))
                        continue;
                    if (!MatchesAltArtFilter(card, query))
                        continue;
                    results.Add(card);
                }
            }

            return ApplySort(results, query.SortMode);
        }

        private static bool MatchesSet(SeriesFolder folder, SearchQuery query)
        {
            if (query.SetNames.Count > 0)
            {
                if (string.IsNullOrEmpty(folder.SetName))
                    return true;

                for (int i = 0; i < query.SetNames.Count; i++)
                {
                    if (string.Equals(folder.SetName, query.SetNames[i],
                        StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (SearchQuery.IsAll(query.SetName))
                return true;
            if (string.IsNullOrEmpty(folder.SetName))
                return true;
            return string.Equals(folder.SetName, query.SetName ?? string.Empty,
                StringComparison.Ordinal);
        }

        private static bool Matches(
            WeissCard card,
            SearchQuery query,
            string searchName,
            string searchTrait,
            string searchText)
        {
            if (searchName.Length > 0 && !CompareName(card.Name, searchName))
                return false;

            if (!MatchesLevel(card, query))
                return false;

            if (!MatchesCost(card, query))
                return false;

            if (!MatchesColor(card, query))
                return false;

            if (!MatchesType(card, query))
                return false;

            if (searchTrait.Length > 0 && !IsTraitMatch(card, searchTrait))
                return false;

            if (searchText.Length > 0 &&
                (card.Text == null || !card.Text.ToLower().Contains(searchText)))
            {
                return false;
            }

            return true;
        }

        private static bool MatchesLevel(WeissCard card, SearchQuery query)
        {
            if (query.Levels.Count > 0)
                return ContainsValue(query.Levels, card.Level.ToString());

            return SearchQuery.IsAll(query.Level) ||
                string.Equals(card.Level.ToString(), query.Level ?? string.Empty,
                    StringComparison.Ordinal);
        }

        private static bool MatchesCost(WeissCard card, SearchQuery query)
        {
            if (query.Costs.Count > 0)
            {
                for (int i = 0; i < query.Costs.Count; i++)
                {
                    if (CompareCost(card, query.Costs[i]))
                        return true;
                }
                return false;
            }

            return SearchQuery.IsAll(query.Cost) || CompareCost(card, query.Cost);
        }

        private static bool MatchesColor(WeissCard card, SearchQuery query)
        {
            if (query.Colors.Count > 0)
                return ContainsValue(query.Colors, card.Color.ToString());

            return SearchQuery.IsAll(query.Color) ||
                string.Equals(card.Color.ToString(), query.Color ?? string.Empty,
                    StringComparison.Ordinal);
        }

        private static bool MatchesType(WeissCard card, SearchQuery query)
        {
            if (query.Types.Count > 0)
                return ContainsValue(query.Types, card.Category.ToString());

            return SearchQuery.IsAll(query.Type) ||
                string.Equals(card.Category.ToString(), query.Type ?? string.Empty,
                    StringComparison.Ordinal);
        }

        private static bool ContainsValue(
            IList<string> values,
            string candidate)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], candidate,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CompareCost(WeissCard card, string cost)
        {
            string value = (cost ?? string.Empty).Trim();
            if (value.EndsWith("+", StringComparison.Ordinal))
            {
                int minimum;
                if (int.TryParse(value.Substring(0, value.Length - 1), out minimum))
                    return card.Cost >= minimum;
            }

            return string.Equals(card.Cost.ToString(), value, StringComparison.Ordinal);
        }

        private static bool CompareName(string cardName, string searchName)
        {
            if (string.IsNullOrEmpty(cardName))
                return false;

            string normalizedCard = cardName.Replace('å', 'e');
            string normalizedSearch = searchName.Replace('å', 'e');
            return normalizedCard.IndexOf(normalizedSearch,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTraitMatch(WeissCard card, string searchTrait)
        {
            for (int i = 0; i < card.Traits.Length; i++)
            {
                if (string.Equals(card.Traits[i], searchTrait, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool MatchesAltArtFilter(WeissCard card, SearchQuery query)
        {
            // Mirrors DeckBuilderSearch.SearchButtonClicked: the two toggles
            // are applied as two independent removal passes.
            if (!query.ShowAltArt && card.IsSameNameClone)
                return false;

            if (query.OnlyAltArt && !card.IsSameNameClone)
                return false;

            return true;
        }

        private static List<WeissCard> ApplySort(
            List<WeissCard> results,
            CardSortMode mode)
        {
            switch (mode)
            {
                case CardSortMode.ColorCategoryLevelCostPowerName:
                    return results
                        .OrderBy(c => (int)c.Color)
                        .ThenBy(c => (int)c.Category)
                        .ThenBy(c => c.Level)
                        .ThenBy(c => c.Cost)
                        .ThenBy(c => c.Power)
                        .ThenBy(c => c.Name)
                        .ToList();

                case CardSortMode.CardNumber:
                    return results
                        .OrderBy(c => NormalizeCardNumber(c.Id))
                        .ToList();

                case CardSortMode.Name:
                    return results
                        .OrderBy(c => NormalizeNameForSort(c.Name))
                        .ToList();

                case CardSortMode.Trait:
                    return SortByTrait(results);
            }

            return results;
        }

        private static string NormalizeCardNumber(string id)
        {
            if (id == null)
                return string.Empty;
            return id.Replace("-E", "-").Replace("-PE", "-P");
        }

        private static string NormalizeNameForSort(string name)
        {
            if (name == null)
                return string.Empty;
            return name.ToLower().Replace("\"", string.Empty);
        }

        private static List<WeissCard> SortByTrait(List<WeissCard> cards)
        {
            var result = new List<WeissCard>();
            var others = new List<WeissCard>();
            var noTrait = new List<WeissCard>();
            var groups = new Dictionary<string, List<WeissCard>>(StringComparer.Ordinal);
            int traitCount = 0;

            for (int i = 0; i < cards.Count; i++)
            {
                WeissCard card = cards[i];
                if (card.Category != CardCategory.Character)
                {
                    others.Add(card);
                }
                else if (string.IsNullOrEmpty(card.Traits[0]))
                {
                    noTrait.Add(card);
                }
                else
                {
                    traitCount++;
                    for (int traitIndex = 0; traitIndex < card.Traits.Length; traitIndex++)
                    {
                        string trait = card.Traits[traitIndex];
                        if (string.IsNullOrEmpty(trait))
                            continue;

                        string key = trait.ToLower();
                        List<WeissCard> group;
                        if (!groups.TryGetValue(key, out group))
                        {
                            group = new List<WeissCard>();
                            groups[key] = group;
                        }
                        group.Add(card);
                    }
                }
            }

            var traitNames = new List<string>(groups.Keys);
            traitNames.Sort();

            for (int i = 0; i < traitNames.Count; i++)
            {
                List<WeissCard> group = groups[traitNames[i]];
                if ((double)group.Count <= 0.8 * traitCount)
                    result.AddRange(SortCharacterGroup(group));
            }

            var groupedCards = new HashSet<WeissCard>(result);
            var remaining = new List<WeissCard>();
            for (int i = 0; i < cards.Count; i++)
            {
                WeissCard card = cards[i];
                if (card.Category == CardCategory.Character &&
                    !groupedCards.Contains(card) &&
                    !noTrait.Contains(card))
                {
                    remaining.Add(card);
                }
            }

            result.AddRange(SortCharacterGroup(remaining));
            result.AddRange(SortCharacterGroup(noTrait));
            result.AddRange(SortOtherCards(others));
            return result;
        }

        private static List<WeissCard> SortCharacterGroup(List<WeissCard> cards)
        {
            return cards
                .OrderBy(c => (int)c.Color)
                .ThenBy(c => c.Level)
                .ThenBy(c => c.Cost)
                .ThenBy(c => c.Power)
                .ThenBy(c => c.Name)
                .ToList();
        }

        private static List<WeissCard> SortOtherCards(List<WeissCard> cards)
        {
            return cards
                .OrderBy(c => (int)c.Category)
                .ThenBy(c => (int)c.Color)
                .ThenBy(c => c.Level)
                .ThenBy(c => c.Cost)
                .ThenBy(c => (int)c.Trigger)
                .ThenBy(c => c.Name)
                .ToList();
        }
    }
}
