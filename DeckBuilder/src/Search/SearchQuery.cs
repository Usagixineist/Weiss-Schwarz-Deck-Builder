using System.Collections.Generic;

namespace WeissDeckBuilder.Search
{
    public enum CardSortMode
    {
        ColorCategoryLevelCostPowerName = 0,
        CardNumber = 1,
        Name = 2,
        Trait = 3
    }

    public sealed class SearchQuery
    {
        public string SeriesName { get; set; }
        public string SetName { get; set; }
        public string CardName { get; set; }
        public string Level { get; set; }
        public string Cost { get; set; }
        public string Color { get; set; }
        public string Type { get; set; }
        public string Trait { get; set; }
        public string Text { get; set; }
        public bool ShowAltArt { get; set; }
        public bool OnlyAltArt { get; set; }
        public CardSortMode SortMode { get; set; }

        /// <summary>
        /// Multi-select filter values used by the desktop UI. An empty list
        /// means the group is unrestricted. The scalar SetName/Level/Cost/
        /// Color/Type properties remain as a compatibility fallback.
        /// </summary>
        public List<string> SetNames { get; private set; }
        public List<string> Levels { get; private set; }
        public List<string> Costs { get; private set; }
        public List<string> Colors { get; private set; }
        public List<string> Types { get; private set; }

        public SearchQuery()
        {
            SeriesName = string.Empty;
            SetName = "所有";
            CardName = string.Empty;
            Level = "所有";
            Cost = "所有";
            Color = "所有";
            Type = "所有";
            Trait = string.Empty;
            Text = string.Empty;
            SortMode = CardSortMode.ColorCategoryLevelCostPowerName;
            SetNames = new List<string>();
            Levels = new List<string>();
            Costs = new List<string>();
            Colors = new List<string>();
            Types = new List<string>();
        }

        public static bool IsAll(string value)
        {
            return string.IsNullOrEmpty(value) ||
                string.Equals(value, "所有", System.StringComparison.Ordinal);
        }
    }
}
