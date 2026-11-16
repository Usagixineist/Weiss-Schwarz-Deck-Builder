using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        Text,
        Clone,
        CustomTriggerIcon
    }

    internal sealed class CardCommand
    {
        public CardCommandKind Kind { get; set; }
        public string Text { get; set; }
        public int Number { get; set; }

        public CardCommand()
        {
            Text = string.Empty;
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

            List<CardDefinition> definitions = ReadDefinitions(path, commonEffects, warnings);
            var pending = new List<CardDefinition>(definitions);

            while (pending.Count > 0)
            {
                bool progress = false;
                for (int i = 0; i < pending.Count; )
                {
                    WeissCard card = TryMaterialize(pending[i], cards, folder, false);
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
                        ApplyShorthand(current, trimmed.Substring(1).Trim(), commonEffects);
                        continue;
                    }

                    int space = trimmed.IndexOf(' ');
                    string key = space < 0 ? trimmed : trimmed.Substring(0, space);
                    string value = space < 0 ? string.Empty : trimmed.Substring(space + 1).Trim();

                    switch (key.ToLowerInvariant())
                    {
                        case "name":
                            AddTextCommand(current, CardCommandKind.Name, value);
                            break;
                        case "jpname":
                            AddTextCommand(current, CardCommandKind.JapaneseName, value);
                            break;
                        case "image":
                            AddTextCommand(current, CardCommandKind.Image, value);
                            break;
                        case "color":
                            AddColorCommand(current, value);
                            break;
                        case "level":
                            AddNumberCommand(current, CardCommandKind.Level, value);
                            break;
                        case "cost":
                            AddNumberCommand(current, CardCommandKind.Cost, value);
                            break;
                        case "power":
                            AddNumberCommand(current, CardCommandKind.Power, value);
                            break;
                        case "soul":
                            AddNumberCommand(current, CardCommandKind.Soul, value);
                            break;
                        case "backup":
                            AddNumberCommand(current, CardCommandKind.Backup, value);
                            break;
                        case "trigger":
                            AddTriggerCommand(current, value);
                            break;
                        case "trait1":
                            AddTextCommand(current, CardCommandKind.Trait1, value);
                            break;
                        case "trait2":
                            AddTextCommand(current, CardCommandKind.Trait2, value);
                            break;
                        case "trait3":
                            AddTextCommand(current, CardCommandKind.Trait3, value);
                            break;
                        case "text":
                            AddTextCommand(current, CardCommandKind.Text,
                                CommonEffectLibrary.DecodeText(value));
                            break;
                        case "clone":
                        case "copy":
                            AddTextCommand(current, CardCommandKind.Clone, value);
                            break;
                        case "customtriggericon":
                            AddNumberCommand(current, CardCommandKind.CustomTriggerIcon, value);
                            break;
                        default:
                            // Ability headers such as "Act: Rest 2 ..." or
                            // "Auto: ..." are not card fields: they open the
                            // effect tree and belong to 效果代码.
                            current.EffectLines.Add(line);
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

            string[] prefixes = { "Character:", "Event:", "Climax:" };
            CardCategory[] categories =
            {
                CardCategory.Character,
                CardCategory.Event,
                CardCategory.Climax
            };

            for (int i = 0; i < prefixes.Length; i++)
            {
                if (line.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    category = categories[i];
                    id = line.Substring(prefixes[i].Length).Trim();
                    return id.Length > 0;
                }
            }

            return false;
        }

        private static void ApplyShorthand(
            CardDefinition current,
            string effectName,
            CommonEffectLibrary commonEffects)
        {
            if (commonEffects == null || effectName.Length == 0)
                return;

            CommonEffectDefinition definition = commonEffects.Find(effectName);
            if (definition == null)
                return;

            if (definition.Color.HasValue)
            {
                current.Commands.Add(new CardCommand
                {
                    Kind = CardCommandKind.Color,
                    Number = (int)definition.Color.Value
                });
            }

            if (definition.Trigger.HasValue)
            {
                current.Commands.Add(new CardCommand
                {
                    Kind = CardCommandKind.Trigger,
                    Number = (int)definition.Trigger.Value
                });
            }

            for (int i = 0; i < definition.TextLines.Count; i++)
            {
                current.Commands.Add(new CardCommand
                {
                    Kind = CardCommandKind.Text,
                    Text = definition.TextLines[i]
                });
            }
        }

        private static void AddTextCommand(
            CardDefinition current,
            CardCommandKind kind,
            string value)
        {
            current.Commands.Add(new CardCommand
            {
                Kind = kind,
                Text = value ?? string.Empty
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
                    case CardCommandKind.Text:
                        AppendText(card, command.Text);
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

            return card;
        }

        private static void AppendText(WeissCard card, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (card.Text.Length == 0)
                card.Text = text;
            else
                card.Text = card.Text + "\n" + text;
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
