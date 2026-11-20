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
        /// 效果内容 as the block spells it: one entry per `Text` line. A card
        /// with several 效果 has one entry per 效果, and a text that wraps on
        /// the card keeps its line breaks inside its own entry. Search only
        /// needs <see cref="Text"/>; 编辑卡片 uses this list to keep every
        /// 效果内容 next to its own 效果代码.
        /// </summary>
        public List<string> TextLines { get; private set; }

        /// <summary>
        /// 效果内容 a `*Shorthand` copies in from CommonEffects.txt. 编辑卡片
        /// shows them so the card face reads complete, but 保存 must not write
        /// them back as `Text` lines: the shorthand adds them again on load, so
        /// a saved card would show the same 效果 twice.
        /// </summary>
        public List<string> InheritedTextLines { get; private set; }

        /// <summary>
        /// 第 i 条 <see cref="TextLines"/> 属于第几段能力（`Act:` / `Auto:` /
        /// `*Shorthand` 各开一段，按卡块里的顺序从 0 数起）。编辑卡片靠它把
        /// 每条 `Text` 放回它自己的那段代码下面：只按“第几条文字”对位会在
        /// 简写自带的文字插进来之后整体错位，把第二条效果的文字写到第一条
        /// 的代码底下。
        /// </summary>
        public List<int> TextBlockIndexes { get; private set; }

        /// <summary>
        /// 第 i 条 <see cref="TextLines"/> 是不是 `*Shorthand` 从
        /// CommonEffects.txt 带进来的正文。编辑卡片把它显示成只读预览，
        /// 保存时不会写成 `Text` 行（简写每次读取都会把它再带进来）。
        /// </summary>
        public List<bool> TextInherited { get; private set; }

        /// <summary>
        /// 触发 a `*Shorthand` sets in CommonEffects.txt. A climax shorthand
        /// such as `*2SoulClimax` writes `Trigger DoubleSoul`, so the card has
        /// the icon even though its block carries no `Trigger` line. 编辑卡片
        /// shows the value; 保存 keeps it out of the block, otherwise the same
        /// tag would sit in the file twice.
        /// </summary>
        public TriggerIcon InheritedTrigger { get; set; }

        /// <summary>颜色 a `*Shorthand` sets in CommonEffects.txt.</summary>
        public CardColor InheritedColor { get; set; }

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

        /// <summary>
        /// 联动名 of the card (the `Combo` line, e.g. 《黒の王》の騎士). About
        /// one character in nine carries it; 编辑卡片 edits it next to the
        /// traits and the effect tree refers to it by name.
        /// </summary>
        public string Combo { get; set; }

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
            Combo = string.Empty;
            Color = CardColor.Undefined;
            Category = CardCategory.Character;
            Trigger = TriggerIcon.None;
            Soul = 1;
            DeckLimit = 4;
            Traits = new string[] { string.Empty, string.Empty, string.Empty };
            TextLines = new List<string>();
            InheritedTextLines = new List<string>();
            TextBlockIndexes = new List<int>();
            TextInherited = new List<bool>();
            InheritedTrigger = TriggerIcon.None;
            InheritedColor = CardColor.Undefined;
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
            TextLines.Clear();
            if (source.TextLines != null)
                TextLines.AddRange(source.TextLines);
            InheritedTextLines.Clear();
            if (source.InheritedTextLines != null)
                InheritedTextLines.AddRange(source.InheritedTextLines);
            TextBlockIndexes.Clear();
            if (source.TextBlockIndexes != null)
                TextBlockIndexes.AddRange(source.TextBlockIndexes);
            TextInherited.Clear();
            if (source.TextInherited != null)
                TextInherited.AddRange(source.TextInherited);
            Traits[0] = source.Traits[0];
            Traits[1] = source.Traits[1];
            Traits[2] = source.Traits[2];
            Combo = source.Combo;
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
