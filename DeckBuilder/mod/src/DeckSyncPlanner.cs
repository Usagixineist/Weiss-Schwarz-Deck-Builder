using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WeissSchwarzDeckSync
{
    /// <summary>What the sync should do with a single deck name.</summary>
    public enum DeckSyncAction
    {
        None,
        PushToGame,
        PullToFile
    }

    /// <summary>
    /// Pure deck-text helpers shared by the plugin and the smoke tests.
    /// Nothing here touches Unity or the registry, so the file is compiled into
    /// both WeissSchwarzDeckSync.dll and the DeckBuilder test runner.
    /// The game stores a deck as "CODE|CODE|CODE|" (PlayerPrefs "Deck_&lt;name&gt;"),
    /// the deck list as "Name|Name|" (PlayerPrefs "DeckNames") and the save time
    /// as "HH:mm  MM/dd/yyyy" (PlayerPrefs "Date_&lt;name&gt;").
    /// </summary>
    public static class DeckSyncPlanner
    {
        public const string DeckTextSeparator = "|";
        public const string DateFormat = "HH:mm  MM/dd/yyyy";
        public const string SetDateFormat = "HH:mm MM/dd/yyyy";
        public const int StandardDeckSize = 50;
        public const int ConflictToleranceSeconds = 60;

        private static readonly string[] DateFormats = { DateFormat, SetDateFormat };

        /// <summary>Drops blank lines and comments, keeps everything else.</summary>
        public static IList<string> ParseCardLines(IEnumerable<string> lines)
        {
            var cards = new List<string>();
            if (lines == null)
                return cards;

            foreach (string line in lines)
            {
                if (line == null)
                    continue;

                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                    continue;

                cards.Add(trimmed);
            }

            return cards;
        }

        /// <summary>Turns card ids into the game's "CODE|CODE|" PlayerPrefs text.</summary>
        public static string ToDeckText(IList<string> cardIds)
        {
            if (cardIds == null || cardIds.Count == 0)
                return string.Empty;

            var builder = new StringBuilder(cardIds.Count * 16);
            for (int i = 0; i < cardIds.Count; i++)
            {
                string id = cardIds[i];
                if (string.IsNullOrEmpty(id))
                    continue;

                builder.Append(id.Trim());
                builder.Append(DeckTextSeparator);
            }

            return builder.ToString();
        }

        /// <summary>Reads the game's "CODE|CODE|" text back into card ids.</summary>
        public static IList<string> SplitDeckText(string text)
        {
            var cards = new List<string>();
            if (string.IsNullOrEmpty(text))
                return cards;

            string[] parts = text.Split(new[] { '|' }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                    continue;

                cards.Add(part);
            }

            return cards;
        }

        /// <summary>One card code per line, exactly the layout DeckLibrary reads.</summary>
        public static string ToFileText(IList<string> cardIds)
        {
            if (cardIds == null || cardIds.Count == 0)
                return string.Empty;

            var builder = new StringBuilder(cardIds.Count * 12);
            for (int i = 0; i < cardIds.Count; i++)
            {
                string id = cardIds[i];
                if (string.IsNullOrEmpty(id))
                    continue;

                builder.Append(id.Trim());
                builder.Append("\r\n");
            }

            return builder.ToString();
        }

        /// <summary>
        /// Accepts the card codes the game and the DeckBuilder write
        /// ("DAL/W79-E053SEC", "5HY/W101-E040"). Anything else is refused before
        /// it reaches the game, where an unknown id would break the deck.
        /// </summary>
        public static bool IsCardCode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            bool hasSeparator = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '/')
                {
                    if (hasSeparator)
                        return false;

                    hasSeparator = true;
                    continue;
                }

                if (c >= 'a' && c <= 'z')
                    continue;
                if (c >= 'A' && c <= 'Z')
                    continue;
                if (c >= '0' && c <= '9')
                    continue;
                if (c == '-' || c == '_' || c == '+')
                    continue;

                return false;
            }

            return hasSeparator;
        }

        /// <summary>Stable 64 bit FNV-1a hash, used as the sync baseline.</summary>
        public static string Hash(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "0";

            ulong hash = 14695981039346656037UL;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>Mirrors DeckSavingAndLoading.SaveDeck's date text.</summary>
        public static string FormatDate(DateTime value)
        {
            return value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>Parses DeckSavingAndLoading.GetDate's text.</summary>
        public static bool TryParseDate(string text, out DateTime value)
        {
            value = DateTime.MinValue;
            if (string.IsNullOrEmpty(text))
                return false;

            return DateTime.TryParseExact(
                text.Trim(),
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value);
        }

        /// <summary>
        /// Decides which side wins. fileText/gameText use the game's
        /// "CODE|CODE|" layout so both can be compared as plain strings; empty
        /// or null stands for "this side has no such deck".
        /// </summary>
        public static DeckSyncAction Decide(
            string fileText,
            string gameText,
            string lastFileHash,
            string lastGameHash,
            DateTime? fileTime,
            DateTime? gameTime)
        {
            bool hasFile = !string.IsNullOrEmpty(fileText);
            bool hasGame = !string.IsNullOrEmpty(gameText);

            if (!hasFile && !hasGame)
                return DeckSyncAction.None;
            if (hasFile && !hasGame)
                return DeckSyncAction.PushToGame;
            if (!hasFile && hasGame)
                return DeckSyncAction.PullToFile;
            if (string.Equals(fileText, gameText, StringComparison.Ordinal))
                return DeckSyncAction.None;

            string fileHash = Hash(fileText);
            string gameHash = Hash(gameText);
            bool fileChanged = !string.Equals(fileHash, lastFileHash, StringComparison.Ordinal);
            bool gameChanged = !string.Equals(gameHash, lastGameHash, StringComparison.Ordinal);

            if (fileChanged && !gameChanged)
                return DeckSyncAction.PushToGame;
            if (!fileChanged && gameChanged)
                return DeckSyncAction.PullToFile;

            // Both sides moved (or the plugin sees this name for the first time).
            if (fileTime.HasValue && gameTime.HasValue)
            {
                if (fileTime.Value >= gameTime.Value.AddSeconds(ConflictToleranceSeconds))
                    return DeckSyncAction.PushToGame;

                return DeckSyncAction.PullToFile;
            }

            // Without a game timestamp the local file is the only side that can
            // have changed, so it wins.
            return DeckSyncAction.PushToGame;
        }

        /// <summary>Splits the game's "Name|Name|" deck list, dropping blanks.</summary>
        public static IList<string> SplitDeckNames(string names)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(names))
                return result;

            string[] parts = names.Split(new[] { '|' }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                    continue;

                result.Add(part);
            }

            return result;
        }

        /// <summary>
        /// Appends a deck name in the layout the game writes ("A|B|") and keeps
        /// the text untouched when the name is already listed.
        /// </summary>
        public static string AppendDeckName(string names, string deckName)
        {
            if (string.IsNullOrEmpty(deckName))
                return names ?? string.Empty;

            string current = names ?? string.Empty;
            IList<string> existing = SplitDeckNames(current);
            for (int i = 0; i < existing.Count; i++)
            {
                if (string.Equals(existing[i], deckName, StringComparison.Ordinal))
                    return current;
            }

            var builder = new StringBuilder(current);
            if (builder.Length > 0 && builder[builder.Length - 1] != '|')
                builder.Append('|');

            builder.Append(deckName);
            builder.Append('|');
            return builder.ToString();
        }

        /// <summary>
        /// Removes one name from the game's "A|B|" deck list. The text is
        /// returned untouched when the name is absent.
        /// </summary>
        public static string RemoveDeckName(string names, string deckName)
        {
            if (string.IsNullOrEmpty(names) || string.IsNullOrEmpty(deckName))
                return names ?? string.Empty;

            var kept = new List<string>();
            bool changed = false;
            string[] parts = names.Split(new[] { '|' }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                    continue;

                if (string.Equals(part, deckName, StringComparison.Ordinal))
                {
                    changed = true;
                    continue;
                }

                kept.Add(part);
            }

            if (!changed)
                return names;

            if (kept.Count == 0)
                return string.Empty;

            return string.Join("|", kept.ToArray()) + "|";
        }

        /// <summary>
        /// True when the mirror file disappeared while the game deck is still
        /// exactly what the last sync left behind. That is a file-side delete,
        /// so the game deck should be deleted too instead of exported again.
        /// </summary>
        public static bool IsFileSideDeleted(
            string fileText,
            string gameText,
            string lastFileHash,
            string lastGameHash)
        {
            return string.IsNullOrEmpty(fileText) &&
                !string.IsNullOrEmpty(gameText) &&
                !string.IsNullOrEmpty(lastFileHash) &&
                !string.IsNullOrEmpty(lastGameHash) &&
                string.Equals(Hash(gameText), lastGameHash, StringComparison.Ordinal);
        }

        /// <summary>Turns a deck name into a file name DeckLibrary can list.</summary>
        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "deck";

            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (chars[i] == invalid[j])
                    {
                        chars[i] = '_';
                        break;
                    }
                }
            }

            string result = new string(chars).Trim().TrimEnd('.');
            return result.Length == 0 ? "deck" : result;
        }
    }
}
