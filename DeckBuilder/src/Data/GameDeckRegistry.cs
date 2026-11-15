using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// The game's own deck library on Windows is Unity PlayerPrefs in
    /// HKCU\Software\Blake Thoennes\Weiss Schwarz. Deleting only the .txt file
    /// leaves Deck_&lt;name&gt; in that key, so the sync plug-in exports the
    /// deck again on the next launch. This helper removes the same values the
    /// game's delete button removes.
    ///
    /// Unity stores a PlayerPrefs key as "&lt;key&gt;_h&lt;djb2 xor hash&gt;".
    /// String values are REG_BINARY payloads containing UTF-8 plus a trailing
    /// NUL. The hash is computed over the UTF-8 bytes with their signed char
    /// values; the visible part of the registry value name is the UTF-8 bytes
    /// decoded with the current ANSI code page, so it may look like mojibake
    /// for Chinese deck names.
    /// </summary>
    public static class GameDeckRegistry
    {
        public const string KeyPath = @"Software\Blake Thoennes\Weiss Schwarz";
        public const string NamesKey = "DeckNames";
        public const string DeckPrefix = "Deck_";
        public const string DatePrefix = "Date_";
        public const string SleevePrefix = "Sleeve_";

        private static Encoding _ansi;

        /// <summary>
        /// Removes a deck from the game's PlayerPrefs. A missing registry key or
        /// a deck that was never synced is not an error; removedFromGame simply
        /// stays false. Returns false only when the registry could not be read
        /// or written, with a user readable reason in warning.
        /// </summary>
        public static bool DeleteDeck(
            string deckName,
            out bool removedFromGame,
            out string warning)
        {
            removedFromGame = false;
            warning = null;
            if (string.IsNullOrEmpty(deckName))
                return true;

            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(KeyPath, true))
                {
                    if (root == null)
                        return true;

                    // Drop the name from DeckNames first. If a later delete fails,
                    // the orphaned deck value is hidden instead of leaving a
                    // listed deck whose content is already gone.
                    bool namesChanged = RemoveNameFromList(root, deckName);
                    bool deckRemoved = DeleteValue(root, DeckPrefix + deckName);
                    bool dateRemoved = DeleteValue(root, DatePrefix + deckName);
                    bool sleeveRemoved = DeleteValue(root, SleevePrefix + deckName);

                    removedFromGame = namesChanged || deckRemoved ||
                        dateRemoved || sleeveRemoved;
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Deleting a deck must not crash the builder. The file is already
                // gone, so the UI shows this warning instead of a failure dialog.
                warning = ex.Message;
                removedFromGame = false;
                return false;
            }
        }

        /// <summary>
        /// The registry value name Unity derives from a PlayerPrefs key. Public
        /// so the smoke tests can pin the hash against known registry entries.
        /// </summary>
        public static string GetValueName(string key)
        {
            if (key == null)
                throw new ArgumentNullException("key");

            string ansi = GetAnsiEncoding().GetString(Encoding.UTF8.GetBytes(key));
            return ansi + "_h" + HashKey(key).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Removes one name from the game's "Name|Name|" deck list. The text is
        /// returned untouched when the name is absent, so an unrelated deck
        /// list is never rewritten just because a delete was requested.
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

        private static bool RemoveNameFromList(RegistryKey root, string deckName)
        {
            string valueName = FindValueName(root, NamesKey);
            if (valueName == null)
                return false;

            string names = ReadString(root, valueName);
            string updated = RemoveDeckName(names, deckName);
            if (string.Equals(names, updated, StringComparison.Ordinal))
                return false;

            WriteString(root, valueName, updated);
            return true;
        }

        private static bool DeleteValue(RegistryKey root, string logicalKey)
        {
            string valueName = FindValueName(root, logicalKey);
            if (valueName == null)
                return false;

            root.DeleteValue(valueName, false);
            return true;
        }

        /// <summary>
        /// Finds an exact name, Unity's computed name, or finally a value with
        /// the same hash suffix. The suffix fallback keeps deletion working when
        /// the process ANSI code page differs from the one that originally
        /// created the key.
        /// </summary>
        private static string FindValueName(RegistryKey root, string logicalKey)
        {
            string[] names = root.GetValueNames();
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], logicalKey, StringComparison.Ordinal))
                    return names[i];
            }

            string computed = GetValueName(logicalKey);
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], computed, StringComparison.Ordinal))
                    return names[i];
            }

            string suffix = "_h" + HashKey(logicalKey).ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (name.Length > suffix.Length &&
                    name.EndsWith(suffix, StringComparison.Ordinal) &&
                    IsSameFamily(name, logicalKey))
                {
                    return name;
                }
            }

            return null;
        }

        private static bool IsSameFamily(string valueName, string logicalKey)
        {
            int underscore = logicalKey.IndexOf('_');
            string prefix = underscore >= 0
                ? logicalKey.Substring(0, underscore + 1)
                : logicalKey;

            if (!valueName.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            // DeckNames has no underscore, so "DeckNames_h..." is its only
            // suffixed form. Deck_/Date_/Sleeve_ already end with '_'.
            return underscore >= 0 ||
                valueName.Length == prefix.Length ||
                valueName[prefix.Length] == '_';
        }

        private static string ReadString(RegistryKey root, string valueName)
        {
            object value = root.GetValue(
                valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            byte[] bytes = value as byte[];
            if (bytes != null)
            {
                int length = bytes.Length;
                while (length > 0 && bytes[length - 1] == 0)
                    length--;

                return Encoding.UTF8.GetString(bytes, 0, length);
            }

            string text = value as string;
            return text ?? string.Empty;
        }

        private static void WriteString(RegistryKey root, string valueName, string text)
        {
            if (root.GetValueKind(valueName) == RegistryValueKind.String)
            {
                root.SetValue(valueName, text ?? string.Empty, RegistryValueKind.String);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var payload = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, payload, 0, bytes.Length);

            // PlayerPrefs strings are REG_BINARY: UTF-8 plus a NUL terminator.
            root.SetValue(valueName, payload, RegistryValueKind.Binary);
        }

        private static Encoding GetAnsiEncoding()
        {
            if (_ansi == null)
                _ansi = Encoding.Default;

            return _ansi;
        }

        private static uint HashKey(string key)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(key);
            uint hash = 5381;
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    int value = bytes[i] > 0x7F ? bytes[i] - 0x100 : bytes[i];
                    hash = (hash * 33) ^ (uint)value;
                }
            }

            return hash;
        }
    }
}
