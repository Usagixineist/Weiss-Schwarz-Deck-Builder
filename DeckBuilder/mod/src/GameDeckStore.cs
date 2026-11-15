using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeissSchwarzDeckSync
{
    /// <summary>
    /// The game's deck store on Windows: Unity PlayerPrefs, which ends up in
    /// HKCU\Software\Blake Thoennes\Weiss Schwarz. The key layout is the one
    /// DeckSavingAndLoading.SaveDeck writes:
    /// "Deck_&lt;name&gt;" = "CODE|CODE|", "DeckNames" = "Name|Name|",
    /// "Date_&lt;name&gt;" = "HH:mm  MM/dd/yyyy", "Sleeve_&lt;name&gt;" = sleeve file.
    /// Writing through PlayerPrefs keeps Unity's ANSI key mangling (deck names
    /// with Chinese characters) correct without reimplementing it.
    /// </summary>
    internal static class GameDeckStore
    {
        public const string NamesKey = "DeckNames";
        public const string DeckPrefix = "Deck_";
        public const string DatePrefix = "Date_";
        public const string SleevePrefix = "Sleeve_";

        public static IList<string> GetDeckNames()
        {
            return DeckSyncPlanner.SplitDeckNames(
                PlayerPrefs.GetString(NamesKey, string.Empty));
        }

        public static string ReadDeckText(string deckName)
        {
            if (string.IsNullOrEmpty(deckName))
                return string.Empty;

            string text = PlayerPrefs.GetString(DeckPrefix + deckName, string.Empty);
            return text ?? string.Empty;
        }

        public static DateTime? GetDate(string deckName)
        {
            if (string.IsNullOrEmpty(deckName))
                return null;

            string text = PlayerPrefs.GetString(DatePrefix + deckName, string.Empty);
            DateTime value;
            return DeckSyncPlanner.TryParseDate(text, out value) ? (DateTime?)value : null;
        }

        public static bool HasDeck(string deckName)
        {
            return !string.IsNullOrEmpty(deckName) &&
                PlayerPrefs.HasKey(DeckPrefix + deckName);
        }

        /// <summary>Writes one deck exactly like DeckSavingAndLoading.SaveDeck.</summary>
        public static void WriteDeck(string deckName, string deckText)
        {
            if (string.IsNullOrEmpty(deckName))
                return;

            PlayerPrefs.SetString(DeckPrefix + deckName, deckText ?? string.Empty);
            EnsureListed(deckName);
            PlayerPrefs.SetString(
                DatePrefix + deckName, DeckSyncPlanner.FormatDate(DateTime.Now));

            // Only fill in the sleeve for new decks: the player may have picked
            // one in the game already.
            if (!PlayerPrefs.HasKey(SleevePrefix + deckName))
                PlayerPrefs.SetString(SleevePrefix + deckName, string.Empty);
        }

        /// <summary>
        /// Deletes a deck exactly like the game's own delete button: the deck,
        /// its date and its sleeve leave PlayerPrefs, and the name leaves
        /// DeckNames. Returns true when something was actually removed.
        /// </summary>
        public static bool DeleteDeck(string deckName)
        {
            if (string.IsNullOrEmpty(deckName))
                return false;

            bool hadDeck = PlayerPrefs.HasKey(DeckPrefix + deckName);
            PlayerPrefs.DeleteKey(DeckPrefix + deckName);
            PlayerPrefs.DeleteKey(DatePrefix + deckName);
            PlayerPrefs.DeleteKey(SleevePrefix + deckName);

            string names = PlayerPrefs.GetString(NamesKey, string.Empty);
            string updated = DeckSyncPlanner.RemoveDeckName(names, deckName);
            bool namesChanged = !string.Equals(updated, names, StringComparison.Ordinal);
            if (namesChanged)
                PlayerPrefs.SetString(NamesKey, updated);

            return hadDeck || namesChanged;
        }

        public static void EnsureListed(string deckName)
        {
            if (string.IsNullOrEmpty(deckName))
                return;

            string names = PlayerPrefs.GetString(NamesKey, string.Empty);
            string updated = DeckSyncPlanner.AppendDeckName(names, deckName);
            if (!string.Equals(updated, names, StringComparison.Ordinal))
                PlayerPrefs.SetString(NamesKey, updated);
        }

        public static void Flush()
        {
            PlayerPrefs.Save();
        }
    }
}
