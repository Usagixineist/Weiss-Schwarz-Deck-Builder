using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WeissDeckBuilder.Infrastructure;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    internal enum CardCommandKind
    {
        Name,
        JapaneseName,
        Image,
        Color,
        Level,
        Cost,
        Power,
        Soul,
        Backup,
        Trigger,
        Trait1,
        Trait2,
        Trait3,
        Combo,
        Text,
        Clone,
        CustomTriggerIcon
    }

    internal sealed class CardCommand
    {
        public CardCommandKind Kind { get; set; }
        public string Text { get; set; }
        public int Number { get; set; }

        /// <summary>
        /// 第几段能力（`Act:` / `Auto:` / `*Shorthand` 各开一段，按卡块里的
        /// 顺序从 0 数起）。只有 `Text` 命令用到它，让每条卡面文字记住自己
        /// 属于哪段代码；-1 表示文字排在第一条能力之前。
        /// </summary>
        public int BlockIndex { get; set; }

        /// <summary>
        /// True for 效果内容 a `*Shorthand` copies in from CommonEffects.txt.
        /// 编辑卡片 shows it as a read-only preview; 保存 never writes it as a
        /// `Text` line, because the shorthand adds it again on load.
        /// </summary>
        public bool Inherited { get; set; }

        public CardCommand()
        {
            Text = string.Empty;
            BlockIndex = -1;
        }
    }

    internal sealed class CardDefinition
    {
        public string Id { get; set; }
        public CardCategory Category { get; set; }
        public List<CardCommand> Commands { get; private set; }

        /// <summary>
        /// Ability header lines and the braced block, kept verbatim. They are
        /// skipped while search data is built but 编辑卡片 shows them as the
        /// 效果代码 half of the effect box, so they must not be lost.
        /// </summary>
        public List<string> EffectLines { get; private set; }

        /// <summary>
        /// 触发 / 颜色 a `*Shorthand` sets in CommonEffects.txt (an old climax
        /// shorthand already carries `Trigger DoubleSoul`). The card keeps them
        /// apart from its own lines so 保存 does not write a tag the shorthand
        /// puts there again on every load.
        /// </summary>
        public TriggerIcon? InheritedTrigger { get; set; }
        public CardColor? InheritedColor { get; set; }

        public CardDefinition()
        {
            Id = string.Empty;
            Commands = new List<CardCommand>();
            EffectLines = new List<string>();
        }
    }

    /// <summary>
    /// Parser for CardData.txt. Top-level commands and CommonEffects.txt
    /// shorthand are converted into card data; the ability header lines and the
    /// braced effect block are kept verbatim on
    /// <see cref="WeissCard.EffectCode"/> so 编辑卡片 can show and rewrite them.
    /// </summary>
    public static class CardDataParser
    {
        /// <summary>
        /// Card field names. Parsing looks them up here instead of calling
        /// key.ToLowerInvariant() on every line, which allocated one string per
        /// line of every CardData.txt of a series.
        /// </summary>
        private static readonly Dictionary<string, CardCommandKind> CommandKinds =
            CreateCommandKinds();

        private static readonly string[] HeaderPrefixes =
            { "Character:", "Event:", "Climax:" };

        private static readonly CardCategory[] HeaderCategories =
        {
            CardCategory.Character,
            CardCategory.Event,
            CardCategory.Climax
        };

        private static Dictionary<string, CardCommandKind> CreateCommandKinds()
        {
            var kinds = new Dictionary<string, CardCommandKind>(
                StringComparer.OrdinalIgnoreCase);
            kinds["name"] = CardCommandKind.Name;
            kinds["jpname"] = CardCommandKind.JapaneseName;
            kinds["image"] = CardCommandKind.Image;
            kinds["color"] = CardCommandKind.Color;
            kinds["level"] = CardCommandKind.Level;
            kinds["cost"] = CardCommandKind.Cost;
            kinds["power"] = CardCommandKind.Power;
            kinds["soul"] = CardCommandKind.Soul;
            kinds["backup"] = CardCommandKind.Backup;
            kinds["trigger"] = CardCommandKind.Trigger;
            kinds["trait1"] = CardCommandKind.Trait1;
            kinds["trait2"] = CardCommandKind.Trait2;
            kinds["trait3"] = CardCommandKind.Trait3;
            kinds["combo"] = CardCommandKind.Combo;
            kinds["text"] = CardCommandKind.Text;
            kinds["clone"] = CardCommandKind.Clone;
            kinds["copy"] = CardCommandKind.Clone;
            kinds["customtriggericon"] = CardCommandKind.CustomTriggerIcon;
            return kinds;
        }

        public static List<WeissCard> ParseFile(
            string path,
            SeriesFolder folder,
            CommonEffectLibrary commonEffects,
            IList<string> warnings)
        {
            var cards = new List<WeissCard>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (warnings != null)
                    warnings.Add("Card data file not found: " + path);
                return cards;
            }

            List<CardDefinition> definitions;
            using (LoadProfiler.Measure("ParseFile",
                "read " + Path.GetFileName(path)))
                definitions = ReadDefinitions(path, commonEffects, warnings);
            var pending = new List<CardDefinition>(definitions);

            using (LoadProfiler.Measure("ParseFile",
                "materialize " + Path.GetFileName(path)))
            {
                while (pending.Count > 0)
                {
                    bool progress = false;
                    for (int i = 0; i < pending.Count; )
                    {
                        WeissCard card = TryMaterialize(pending[i], cards, folder,
                            false);
                        if (card != null)
                        {
                            cards.Add(card);
                            pending.RemoveAt(i);
                            progress = true;
                        }
                        else
                        {
                            i++;
                        }
                    }

                    if (!progress)
                        break;
                }
            }

            for (int i = 0; i < pending.Count; i++)
            {
                if (warnings != null)
                {
                    warnings.Add(string.Format(
                        "{0}: unresolved Clone reference for {1}",
                        Path.GetFileName(path), pending[i].Id));
                }

                WeissCard fallback = TryMaterialize(pending[i], cards, folder, true);
                if (fallback != null)
                    cards.Add(fallback);
            }

            return cards;
        }

        private static List<CardDefinition> ReadDefinitions(
            string path,
            CommonEffectLibrary commonEffects,
            IList<string> warnings)
        {
            var definitions = new List<CardDefinition>();
            CardDefinition current = null;
            int depth = 0;
            // 第几段能力：`Act:` / `Auto:` 与 `*Shorthand` 各开一段，CardEffectSplitter
            // 用同一条规则切代码，所以这里数出来的序号和编辑卡片里的“效果 N”对得上。
            int blockIndex = -1;

            using (StreamReader reader = TextFileReader.Open(path))
            {
                string raw;
                while ((raw = reader.ReadLine()) != null)
                {
                    string line = raw.TrimEnd().TrimStart('\uFEFF');
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0)
                        continue;

                    if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                        trimmed.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (depth == 0)
                    {
                        CardCategory category;
                        string id;
                        if (TryReadCardHeader(trimmed, out category, out id))
                        {
                            current = new CardDefinition();
                            current.Id = id;
                            current.Category = category;
                            definitions.Add(current);
                            depth = 0;
                            blockIndex = -1;
                            continue;
                        }
                    }

                    if (current == null)
                        continue;

                    if (trimmed.StartsWith("{", StringComparison.Ordinal))
                    {
                        current.EffectLines.Add(line);
                        depth++;
                        continue;
                    }

                    if (trimmed.StartsWith("}", StringComparison.Ordinal))
                    {
                        current.EffectLines.Add(line);
                        if (depth > 0)
                            depth--;
                        continue;
                    }

                    if (depth != 0)
                    {
                        current.EffectLines.Add(line);
                        continue;
                    }

                    if (string.Equals(trimmed, "EndCard", StringComparison.OrdinalIgnoreCase))
                    {
                        current = null;
                        depth = 0;
                        continue;
                    }

                    if (trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        // The shorthand line stays in the effect code so 编辑卡片
                        // shows it and the writer can put it back untouched.
                        current.EffectLines.Add(line);
                        blockIndex++;
                        ApplyShorthand(current, trimmed.Substring(1).Trim(),
                            commonEffects, blockIndex);
                        continue;
                    }

                    int space = trimmed.IndexOf(' ');
                    string key = space < 0 ? trimmed : trimmed.Substring(0, space);
                    string value = space < 0 ? string.Empty : trimmed.Substring(space + 1).Trim();

                    CardCommandKind kind;
                    if (!CommandKinds.TryGetValue(key, out kind))
                    {
                        // Ability headers such as "Act: Rest 2 ..." or
                        // "Auto: ..." are not card fields: they open the
                        // effect tree and belong to 效果代码.
                        current.EffectLines.Add(line);
                        if (CardEffectSplitter.OpensSegment(line))
                            blockIndex++;
                        continue;
                    }

                    switch (kind)
                    {
                        case CardCommandKind.Color:
                            AddColorCommand(current, value);
                            break;
                        case CardCommandKind.Trigger:
                            AddTriggerCommand(current, value);
                            break;
                        case CardCommandKind.Text:
                            AddTextCommand(current, kind,
                                CommonEffectLibrary.DecodeText(value), blockIndex);
                            break;
                        case CardCommandKind.Level:
                        case CardCommandKind.Cost:
                        case CardCommandKind.Power:
                        case CardCommandKind.Soul:
                        case CardCommandKind.Backup:
                        case CardCommandKind.CustomTriggerIcon:
                            AddNumberCommand(current, kind, value);
                            break;
                        default:
                            // Name / JpName / Image / Trait1-3 / Combo / Clone
                            // all keep their value verbatim.
                            AddTextCommand(current, kind, value);
                            break;
                    }
                }
            }

            return definitions;
        }

        private static bool TryReadCardHeader(
            string line,
            out CardCategory category,
            out string id)
        {
            category = CardCategory.Character;
            id = string.Empty;

            for (int i = 0; i < HeaderPrefixes.Length; i++)
            {
                string prefix = HeaderPrefixes[i];
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    category = HeaderCategories[i];
                    id = line.Substring(prefix.Length).Trim();
                    return id.Length > 0;
                }
            }

            return false;
        }

        private static void ApplyShorthand(
            CardDefinition current,
            string effectName,
            CommonEffectLibrary commonEffects,
            int blockIndex)
        {
            if (commonEffects == null || effectName.Length == 0)
                return;

            CommonEffectDefinition definition = commonEffects.Find(effectName);
            if (definition == null)
                return;

            if (definition.Color.HasValue)
            {
                current.InheritedColor = definition.Color;
                current.Commands.Add(new CardCommand
                {
                    Kind = CardCommandKind.Color,
                    Number = (int)definition.Color.Value,
                    BlockIndex = blockIndex
                });
            }

            if (definition.Trigger.HasValue)
            {
                current.InheritedTrigger = definition.Trigger;
                current.Commands.Add(new CardCommand
                {
                    Kind = CardCommandKind.Trigger,
                    Number = (int)definition.Trigger.Value,
                    BlockIndex = blockIndex
                });
            }

            for (int i = 0; i < definition.TextLines.Count; i++)
            {
                current.Commands.Add(new CardCommand
                {
                    Kind = CardCommandKind.Text,
                    Text = definition.TextLines[i],
                    BlockIndex = blockIndex,
                    Inherited = true
                });
            }
        }

        private static void AddTextCommand(
            CardDefinition current,
            CardCommandKind kind,
            string value,
            int blockIndex = -1)
        {
            current.Commands.Add(new CardCommand
            {
                Kind = kind,
                Text = value ?? string.Empty,
                BlockIndex = blockIndex
            });
        }

        private static void AddNumberCommand(
            CardDefinition current,
            CardCommandKind kind,
            string value)
        {
            int number = 0;
            if (!string.IsNullOrEmpty(value))
            {
                int space = value.IndexOf(' ');
                string first = space < 0 ? value : value.Substring(0, space);
                int.TryParse(first, out number);
            }

            current.Commands.Add(new CardCommand
            {
                Kind = kind,
                Number = number
            });
        }

        private static void AddColorCommand(CardDefinition current, string value)
        {
            CardColor color;
            if (!CommonEffectLibrary.TryParseColor(value, out color))
                return;

            current.Commands.Add(new CardCommand
            {
                Kind = CardCommandKind.Color,
                Number = (int)color
            });
        }

        private static void AddTriggerCommand(CardDefinition current, string value)
        {
            TriggerIcon trigger;
            if (!CommonEffectLibrary.TryParseTrigger(value, out trigger))
                return;

            current.Commands.Add(new CardCommand
            {
                Kind = CardCommandKind.Trigger,
                Number = (int)trigger
            });
        }

        private static WeissCard TryMaterialize(
            CardDefinition definition,
            List<WeissCard> existingCards,
            SeriesFolder folder,
            bool allowMissingClone)
        {
            if (!allowMissingClone)
            {
                for (int i = 0; i < definition.Commands.Count; i++)
                {
                    if (definition.Commands[i].Kind != CardCommandKind.Clone)
                        continue;
                    if (FindCard(existingCards, definition.Commands[i].Text) == null)
                        return null;
                }
            }

            var card = new WeissCard();
            card.Id = definition.Id;
            card.Category = definition.Category;
            card.SeriesName = folder.SeriesName;
            card.SetName = folder.SetName;
            card.FolderPath = folder.FolderPath;
            card.EffectCode = definition.EffectLines.Count == 0
                ? string.Empty
                : string.Join("\n", definition.EffectLines.ToArray());

            for (int i = 0; i < definition.Commands.Count; i++)
            {
                CardCommand command = definition.Commands[i];
                switch (command.Kind)
                {
                    case CardCommandKind.Name:
                        card.Name = command.Text;
                        break;
                    case CardCommandKind.JapaneseName:
                        card.JapaneseName = command.Text;
                        break;
                    case CardCommandKind.Image:
                        card.ImageId = command.Text;
                        break;
                    case CardCommandKind.Color:
                        card.Color = (CardColor)command.Number;
                        break;
                    case CardCommandKind.Level:
                        card.Level = command.Number;
                        break;
                    case CardCommandKind.Cost:
                        card.Cost = command.Number;
                        break;
                    case CardCommandKind.Power:
                        card.Power = command.Number;
                        break;
                    case CardCommandKind.Soul:
                        card.Soul = command.Number;
                        break;
                    case CardCommandKind.Backup:
                        card.BackupValue = command.Number;
                        break;
                    case CardCommandKind.Trigger:
                        card.Trigger = (TriggerIcon)command.Number;
                        break;
                    case CardCommandKind.CustomTriggerIcon:
                        if (command.Number >= (int)TriggerIcon.None &&
                            command.Number <= (int)TriggerIcon.CustomIcon6)
                        {
                            card.Trigger = (TriggerIcon)command.Number;
                        }
                        break;
                    case CardCommandKind.Trait1:
                        card.Traits[0] = command.Text;
                        break;
                    case CardCommandKind.Trait2:
                        card.Traits[1] = command.Text;
                        break;
                    case CardCommandKind.Trait3:
                        card.Traits[2] = command.Text;
                        break;
                    case CardCommandKind.Combo:
                        card.Combo = command.Text;
                        break;
                    case CardCommandKind.Text:
                        AppendText(card, command);
                        break;
                    case CardCommandKind.Clone:
                        WeissCard baseCard = FindCard(existingCards, command.Text);
                        if (baseCard != null)
                            card.CopyFrom(baseCard);
                        else if (!allowMissingClone)
                            return null;
                        break;
                }
            }

            // A `*Shorthand` fills the same 效果内容 into Text/TextLines; the
            // card remembers which pieces came from CommonEffects.txt (the
            // `Inherited` flag of each `Text` command, added by AppendText) so
            // the writer never turns them into `Text` lines of its own.

            // The 触发 / 颜色 a shorthand sets are remembered the same way: the
            // editor shows them, but a saved block must not spell them out a
            // second time.
            if (definition.InheritedTrigger.HasValue)
                card.InheritedTrigger = definition.InheritedTrigger.Value;
            if (definition.InheritedColor.HasValue)
                card.InheritedColor = definition.InheritedColor.Value;

            return card;
        }

        private static void AppendText(WeissCard card, CardCommand command)
        {
            string text = command == null ? null : command.Text;
            if (string.IsNullOrEmpty(text))
                return;

            if (card.Text.Length == 0)
                card.Text = text;
            else
                card.Text = card.Text + "\n" + text;
            card.TextLines.Add(text);

            // 每条 `Text` 记住自己属于哪段能力、是不是简写带进来的正文：
            // 编辑卡片靠这两个标记把文字放回自己的代码下面，并只把玩家自己的
            // 文字写回卡块。
            card.TextBlockIndexes.Add(command.BlockIndex < 0 ? 0 : command.BlockIndex);
            card.TextInherited.Add(command.Inherited);
            if (command.Inherited)
                card.InheritedTextLines.Add(text);
        }

        internal static WeissCard FindCard(List<WeissCard> cards, string id)
        {
            if (cards == null || string.IsNullOrEmpty(id))
                return null;

            string plusE = IdPlusE(id);
            string minusE = IdMinusE(id);

            for (int i = 0; i < cards.Count; i++)
            {
                if (IdEquals(cards[i].Id, id) ||
                    IdEquals(cards[i].Id, plusE) ||
                    IdEquals(cards[i].Id, minusE))
                {
                    return cards[i];
                }
            }

            char last = id[id.Length - 1];
            if ((last >= 'a' && last <= 'z') || (last >= 'A' && last <= 'Z'))
            {
                string prefix = id.Substring(0, id.Length - 1);
                for (int i = 0; i < cards.Count; i++)
                {
                    if (IdEquals(cards[i].Id, prefix) ||
                        IdEquals(cards[i].Id, IdPlusE(prefix)) ||
                        IdEquals(cards[i].Id, IdMinusE(prefix)))
                    {
                        return cards[i];
                    }
                }
            }

            return null;
        }

        private static bool IdEquals(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        // Mirrors CardLibrary.idPlusE.
        private static string IdPlusE(string id)
        {
            if (string.IsNullOrEmpty(id))
                return id;

            int index = id.IndexOf('-');
            if (index < 0)
                index = id.LastIndexOf('_');

            if (index < 0 || index >= id.Length - 1)
                return id;
            if (index <= 0)
                return id;

            string prefix = id.Substring(0, index + 1);
            string suffix = id.Substring(index + 1);
            if (suffix.Length == 0)
                return id;

            if (suffix[0] >= 'A' && suffix[0] <= 'Z')
                return prefix + suffix[0] + "E" + suffix.Substring(1);

            return prefix + "E" + suffix;
        }

        // Mirrors CardLibrary.idMinusE.
        private static string IdMinusE(string id)
        {
            if (string.IsNullOrEmpty(id))
                return id;

            int index = id.IndexOf('-');
            if (index < 0)
                index = id.LastIndexOf('_');

            if (index <= 0)
                return id;

            string prefix = id.Substring(0, index + 1);
            string suffix = id.Substring(index + 1);
            return prefix + suffix.Replace("E", string.Empty);
        }
    }
}
