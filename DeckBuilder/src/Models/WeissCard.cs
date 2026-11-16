using System;
using System.Collections.Generic;

namespace WeissDeckBuilder.Models
{
    /// <summary>
    /// Minimal card model containing every field used by the game's search,
    /// alternate-art filter and sort routines.
    /// </summary>
    public sealed class WeissCard
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string JapaneseName { get; set; }
        public string ImageId { get; set; }
        public string Text { get; set; }

        /// <summary>
        /// Raw ability code of the card: the ability header lines (Act / Auto /
        /// Event / ...) plus the braced block, exactly as written in
        /// CardData.txt. Search never needs it; 编辑卡片 shows it in the lower
        /// half of the effect box and writes it back unchanged, so the effect
        /// tree survives a round trip through the editor.
        /// </summary>
        public string EffectCode { get; set; }

        public int Level { get; set; }
        public int Cost { get; set; }
        public int Power { get; set; }
        public int Soul { get; set; }
        public int BackupValue { get; set; }
        public int DeckLimit { get; set; }

        public CardColor Color { get; set; }
        public CardCategory Category { get; set; }
        public TriggerIcon Trigger { get; set; }
        public string[] Traits { get; private set; }

        public WeissCard CloneOf { get; set; }
        public List<WeissCard> Clones { get; private set; }

        public string SeriesName { get; set; }
        public string SetName { get; set; }
        public string FolderPath { get; set; }

        /// <summary>Resolved full path of the normal card image, or null.</summary>
        public string ImagePath { get; set; }

        /// <summary>Resolved full path of an alternate-art image, or null.</summary>
        public string AltImagePath { get; set; }

        public WeissCard()
        {
            Id = string.Empty;
            Name = string.Empty;
            JapaneseName = string.Empty;
            ImageId = string.Empty;
            Text = string.Empty;
            EffectCode = string.Empty;
            Color = CardColor.Undefined;
            Category = CardCategory.Character;
            Trigger = TriggerIcon.None;
            Soul = 1;
            DeckLimit = 4;
            Traits = new string[] { string.Empty, string.Empty, string.Empty };
            Clones = new List<WeissCard>();
        }

        public bool IsClone
        {
            get { return CloneOf != null; }
        }

        public bool IsSameNameClone
        {
            get { return CloneOf != null && string.Equals(CloneOf.Name, Name, StringComparison.Ordinal); }
        }

        public string ColorDisplay
        {
            get { return ColorText(Color); }
        }

        public string CategoryDisplay
        {
            get { return CategoryText(Category); }
        }

        public string TriggerDisplay
        {
            get { return Trigger == TriggerIcon.None ? string.Empty : Trigger.ToString(); }
        }

        public string TraitsDisplay
        {
            get
            {
                var parts = new List<string>();
                for (int i = 0; i < Traits.Length; i++)
                {
                    if (!string.IsNullOrEmpty(Traits[i]))
                        parts.Add(Traits[i]);
                }
                return string.Join(" / ", parts.ToArray());
            }
        }

        public string PowerDisplay
        {
            get { return Category == CardCategory.Character ? Power.ToString() : string.Empty; }
        }

        public string JapaneseNameDisplay
        {
            get { return JapaneseName ?? string.Empty; }
        }

        public string AltDisplay
        {
            get { return IsClone ? "异画" : string.Empty; }
        }

        /// <summary>
        /// Mirrors WeissCard.CopyFrom in Assembly-CSharp. The id, image id and
        /// image paths are intentionally not copied.
        /// </summary>
        public void CopyFrom(WeissCard source)
        {
            if (source == null)
                return;

            BackupValue = source.BackupValue;
            DeckLimit = source.DeckLimit;
            Category = source.Category;
            Color = source.Color;
            Cost = source.Cost;
            Level = source.Level;
            Name = source.Name;
            Power = source.Power;
            Soul = source.Soul;
            Text = source.Text;
            EffectCode = source.EffectCode ?? string.Empty;
            Traits[0] = source.Traits[0];
            Traits[1] = source.Traits[1];
            Traits[2] = source.Traits[2];
            Trigger = source.Trigger;
            SeriesName = source.SeriesName;
            SetName = source.SetName;
            FolderPath = source.FolderPath;
            JapaneseName = source.JapaneseName;
            CloneOf = source;
            source.Clones.Add(this);
        }

        public static string ColorText(CardColor color)
        {
            switch (color)
            {
                case CardColor.Yellow: return "黄";
                case CardColor.Green: return "绿";
                case CardColor.Red: return "红";
                case CardColor.Blue: return "蓝";
                case CardColor.Purple: return "紫";
                default: return "未定义";
            }
        }

        public static string CategoryText(CardCategory category)
        {
            switch (category)
            {
                case CardCategory.Character: return "角色";
                case CardCategory.Event: return "事件";
                case CardCategory.Climax: return "高潮";
                default: return category.ToString();
            }
        }

        public override string ToString()
        {
            return Id + " " + Name;
        }
    }
}
