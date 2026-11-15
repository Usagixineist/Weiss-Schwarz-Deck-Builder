using System;
using System.Collections.Generic;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Numbers behind the 卡组信息 block of the deck detail sidebar: the card
    /// type / colour matrix, the level histogram and the soul trigger icons.
    /// Only cards that resolve to a <see cref="WeissCard"/> take part in the
    /// statistics; codes the card data does not know are counted in
    /// <see cref="Unresolved"/> so the UI can stay honest about them.
    /// </summary>
    public sealed class DeckStatistics
    {
        /// <summary>Row order of the colour matrix: 黄 绿 红 蓝.</summary>
        public static readonly CardColor[] MatrixColors =
        {
            CardColor.Yellow,
            CardColor.Green,
            CardColor.Red,
            CardColor.Blue
        };

        /// <summary>Column order of the colour matrix: 角色 事件 高潮.</summary>
        public static readonly CardCategory[] MatrixCategories =
        {
            CardCategory.Character,
            CardCategory.Event,
            CardCategory.Climax
        };

        /// <summary>Highest level of the level histogram, 0 to 3.</summary>
        public const int MaxLevel = 3;

        private readonly int[] _categoryTotals;
        private readonly int[] _colorTotals;
        private readonly int[,] _colorByCategory;
        private readonly int[] _levelTotals;

        public DeckStatistics()
        {
            _categoryTotals = new int[MatrixCategories.Length];
            _colorTotals = new int[MatrixColors.Length];
            _colorByCategory = new int[MatrixColors.Length, MatrixCategories.Length];
            _levelTotals = new int[MaxLevel + 1];
        }

        /// <summary>Cards that were resolved, i.e. the 合计 of the matrix.</summary>
        public int Total { get; private set; }

        /// <summary>Card codes without card data, excluded from every count.</summary>
        public int Unresolved { get; private set; }

        /// <summary>Cards whose trigger icon is 魂.</summary>
        public int SoulTriggerCards { get; private set; }

        /// <summary>Cards whose trigger icon is 2魂.</summary>
        public int DoubleSoulTriggerCards { get; private set; }

        public int GetCategoryTotal(CardCategory category)
        {
            int index = IndexOfCategory(category);
            return index < 0 ? 0 : _categoryTotals[index];
        }

        /// <summary>Cards of one colour across every category.</summary>
        public int GetColorTotal(CardColor color)
        {
            int index = IndexOfColor(color);
            return index < 0 ? 0 : _colorTotals[index];
        }

        public int GetColorCategoryTotal(CardColor color, CardCategory category)
        {
            int colorIndex = IndexOfColor(color);
            int categoryIndex = IndexOfCategory(category);
            if (colorIndex < 0 || categoryIndex < 0)
                return 0;

            return _colorByCategory[colorIndex, categoryIndex];
        }

        /// <summary>
        /// Cards of one level. 高潮 cards never carry a level, so they stay out
        /// of the histogram and do not inflate the 0 级 count.
        /// </summary>
        public int GetLevelTotal(int level)
        {
            return level < 0 || level > MaxLevel ? 0 : _levelTotals[level];
        }

        /// <summary>Adds one resolved card to every table.</summary>
        public void Add(WeissCard card)
        {
            if (card == null)
            {
                Unresolved++;
                return;
            }

            Total++;

            int categoryIndex = IndexOfCategory(card.Category);
            int colorIndex = IndexOfColor(card.Color);

            if (categoryIndex >= 0)
                _categoryTotals[categoryIndex]++;

            // 紫 and undefined cards keep their place in the 合计 row only,
            // because the matrix has exactly the four basic colours.
            if (colorIndex >= 0)
            {
                _colorTotals[colorIndex]++;
                if (categoryIndex >= 0)
                    _colorByCategory[colorIndex, categoryIndex]++;
            }

            if (card.Category != CardCategory.Climax &&
                card.Level >= 0 && card.Level <= MaxLevel)
            {
                _levelTotals[card.Level]++;
            }

            if (card.Trigger == TriggerIcon.Soul)
                SoulTriggerCards++;
            else if (card.Trigger == TriggerIcon.DoubleSoul)
                DoubleSoulTriggerCards++;
        }

        /// <summary>
        /// Builds the statistics of one deck. <paramref name="resolve"/> turns a
        /// card code into its card and may return null for unknown codes.
        /// </summary>
        public static DeckStatistics Compute(IList<string> cardCodes,
            Func<string, WeissCard> resolve)
        {
            var statistics = new DeckStatistics();
            if (cardCodes == null)
                return statistics;

            for (int i = 0; i < cardCodes.Count; i++)
            {
                WeissCard card = resolve == null ? null : resolve(cardCodes[i]);
                statistics.Add(card);
            }

            return statistics;
        }

        private static int IndexOfColor(CardColor color)
        {
            for (int i = 0; i < MatrixColors.Length; i++)
            {
                if (MatrixColors[i] == color)
                    return i;
            }

            return -1;
        }

        private static int IndexOfCategory(CardCategory category)
        {
            for (int i = 0; i < MatrixCategories.Length; i++)
            {
                if (MatrixCategories[i] == category)
                    return i;
            }

            return -1;
        }
    }
}
