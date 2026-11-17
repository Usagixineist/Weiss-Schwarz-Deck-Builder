using System;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Everything 编辑卡片 edits in one place: the card fields, the card pack
    /// the card is written to, and the art the player dropped into the editor
    /// that still has to be copied next to the card data on save.
    /// </summary>
    public sealed class CardEditorDraft
    {
        /// <summary>True for 添加卡片, false while an existing card is edited.</summary>
        public bool IsNew { get; set; }

        /// <summary>
        /// Card number of the block that is replaced. Stays at the original
        /// value after renaming, so a changed 卡号 still finds its old block.
        /// </summary>
        public string OriginalId { get; set; }

        public string SeriesName { get; set; }
        public string SetName { get; set; }

        /// <summary>Folder below StreamingAssets\Cards, as written in SingleSetData.txt.</summary>
        public string FolderPath { get; set; }

        /// <summary>Resolved folder the CardData file lives in.</summary>
        public string FolderFullPath { get; set; }

        /// <summary>CardData file name of the folder, normally "CardData".</summary>
        public string FileName { get; set; }

        /// <summary>
        /// Prefix line of the pack (`AW/S18`). 添加异画 needs it to find the
        /// AlternateArtwork sub folder the game reads for this pack.
        /// </summary>
        public string Prefix { get; set; }

        public string Id { get; set; }
        public string Name { get; set; }
        public string JapaneseName { get; set; }
        public string ImageId { get; set; }
        public string Text { get; set; }
        public string EffectCode { get; set; }

        /// <summary>
        /// 效果内容 the card had when it was opened. A block without a `Text`
        /// line gets one only when the player changed the text, because the
        /// text of a `*Shorthand` card comes from CommonEffects.txt and must
        /// not be duplicated on save.
        /// </summary>
        public string OriginalText { get; set; }

        public string Combo { get; set; }
        public string Trait1 { get; set; }
        public string Trait2 { get; set; }
        public string Trait3 { get; set; }

        public CardCategory Category { get; set; }
        public CardColor Color { get; set; }
        public TriggerIcon Trigger { get; set; }
        public int Level { get; set; }
        public int Cost { get; set; }
        public int Power { get; set; }
        public int Soul { get; set; }
        public int Backup { get; set; }

        /// <summary>
        /// Full path of a card art the player dropped or picked. It is copied
        /// into the card pack folder when the card is saved; null keeps the
        /// art the card already has.
        /// </summary>
        public string ImageSourcePath { get; set; }

        /// <summary>
        /// Art shown in the editor's image box. For an open card this is the
        /// resolved image of the card; a dropped file replaces it directly.
        /// </summary>
        public string PreviewImagePath { get; set; }

        public CardEditorDraft()
        {
            OriginalId = string.Empty;
            SeriesName = string.Empty;
            SetName = string.Empty;
            FolderPath = string.Empty;
            FolderFullPath = string.Empty;
            Prefix = string.Empty;
            FileName = "CardData";
            Id = string.Empty;
            Name = string.Empty;
            JapaneseName = string.Empty;
            ImageId = string.Empty;
            Text = string.Empty;
            EffectCode = string.Empty;
            OriginalText = string.Empty;
            Combo = string.Empty;
            Trait1 = string.Empty;
            Trait2 = string.Empty;
            Trait3 = string.Empty;
            ImageSourcePath = string.Empty;
            PreviewImagePath = string.Empty;
            Category = CardCategory.Character;
            Color = CardColor.Undefined;
            Trigger = TriggerIcon.None;
            Soul = 1;
        }

        /// <summary>True once a folder was resolved and the card can be written.</summary>
        public bool HasFolder
        {
            get { return !string.IsNullOrEmpty(FolderFullPath); }
        }

        /// <summary>Caption shown in the editor header, e.g. "加速世界 · S18".</summary>
        public string TargetLabel
        {
            get
            {
                string series = string.IsNullOrEmpty(SeriesName) ? "未选择系列" : SeriesName;
                string set = string.IsNullOrEmpty(SetName) ? FolderPath : SetName;
                return string.IsNullOrEmpty(set) ? series : series + " · " + set;
            }
        }

        public static CardEditorDraft FromCard(WeissCard card, SeriesFolder folder)
        {
            if (card == null)
                return null;

            var draft = new CardEditorDraft();
            draft.IsNew = false;
            draft.OriginalId = card.Id ?? string.Empty;
            draft.Id = card.Id ?? string.Empty;
            draft.Name = card.Name ?? string.Empty;
            draft.JapaneseName = card.JapaneseName ?? string.Empty;
            draft.ImageId = card.ImageId ?? string.Empty;
            draft.Text = card.Text ?? string.Empty;
            draft.EffectCode = card.EffectCode ?? string.Empty;
            draft.OriginalText = draft.Text;
            draft.Combo = card.Combo ?? string.Empty;
            draft.Trait1 = card.Traits.Length > 0 ? card.Traits[0] : string.Empty;
            draft.Trait2 = card.Traits.Length > 1 ? card.Traits[1] : string.Empty;
            draft.Trait3 = card.Traits.Length > 2 ? card.Traits[2] : string.Empty;
            draft.Category = card.Category;
            draft.Color = card.Color;
            draft.Trigger = card.Trigger;
            draft.Level = card.Level;
            draft.Cost = card.Cost;
            draft.Power = card.Power;
            draft.Soul = card.Soul;
            draft.Backup = card.BackupValue;
            draft.SeriesName = card.SeriesName ?? string.Empty;
            draft.SetName = card.SetName ?? string.Empty;
            draft.FolderPath = card.FolderPath ?? string.Empty;
            draft.PreviewImagePath = card.ImagePath ?? string.Empty;

            UseFolder(draft, folder);
            return draft;
        }

        public static CardEditorDraft CreateBlank(SeriesDefinition series,
            SeriesFolder folder)
        {
            var draft = new CardEditorDraft();
            draft.IsNew = true;
            draft.OriginalId = string.Empty;
            draft.SeriesName = series == null ? string.Empty : series.Name;
            UseFolder(draft, folder);
            return draft;
        }

        /// <summary>
        /// Points the draft at another card pack of the same series. 添加卡片
        /// uses this when the player switches the 卡包 chip in the editor.
        /// </summary>
        public static void UseFolder(CardEditorDraft draft, SeriesFolder folder)
        {
            if (draft == null || folder == null)
                return;

            draft.FolderPath = folder.FolderPath ?? string.Empty;
            draft.FolderFullPath = folder.FullPath ?? string.Empty;
            draft.Prefix = folder.Prefix ?? string.Empty;
            draft.FileName = string.IsNullOrEmpty(folder.FileName)
                ? "CardData"
                : folder.FileName;
            if (!string.IsNullOrEmpty(folder.SeriesName))
                draft.SeriesName = folder.SeriesName;
            draft.SetName = folder.SetName ?? string.Empty;
        }
    }
}
