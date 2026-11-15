using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeissSchwarzDeckSync
{
    /// <summary>
    /// The DeckBuilder's folder (StreamingAssets\Decks). The DeckBuilder reads it
    /// as "my decks", the game reads it when the player imports, so a deck the
    /// sync exports here shows up in both programs.
    /// </summary>
    internal sealed class DeckFolderStore
    {
        private static readonly string[] Extensions = { ".txt", ".deck" };

        private readonly string _folder;

        public DeckFolderStore(string folder)
        {
            _folder = folder ?? string.Empty;
        }

        public string Folder
        {
            get { return _folder; }
        }

        /// <summary>Deck file name (without extension) to file path.</summary>
        public IDictionary<string, string> IndexByName()
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder))
                return index;

            string[] files;
            try
            {
                files = Directory.GetFiles(_folder);
            }
            catch (IOException)
            {
                return index;
            }
            catch (UnauthorizedAccessException)
            {
                return index;
            }

            for (int i = 0; i < files.Length; i++)
            {
                if (!IsDeckFile(files[i]))
                    continue;

                string name = Path.GetFileNameWithoutExtension(files[i]);
                if (string.IsNullOrEmpty(name) || index.ContainsKey(name))
                    continue;

                index[name] = files[i];
            }

            return index;
        }

        /// <summary>Finds the file of a deck, tolerating names Windows cannot store.</summary>
        public string FindPath(IDictionary<string, string> index, string deckName)
        {
            if (index == null || string.IsNullOrEmpty(deckName))
                return null;

            string path;
            if (index.TryGetValue(deckName, out path))
                return path;

            return index.TryGetValue(
                DeckSyncPlanner.SanitizeFileName(deckName), out path) ? path : null;
        }

        public IList<string> ReadLines(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                return File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public DateTime? ReadWriteTime(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                return File.GetLastWriteTime(path);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Writes one deck, reusing the existing file when there is one.</summary>
        public string WriteDeck(string deckName, IList<string> cardIds, string existingPath)
        {
            string path = existingPath;
            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(
                    _folder, DeckSyncPlanner.SanitizeFileName(deckName) + ".txt");
            }

            if (!Directory.Exists(_folder))
                Directory.CreateDirectory(_folder);

            File.WriteAllText(
                path, DeckSyncPlanner.ToFileText(cardIds), new UTF8Encoding(false));
            return path;
        }

        private static bool IsDeckFile(string path)
        {
            string extension = Path.GetExtension(path);
            for (int i = 0; i < Extensions.Length; i++)
            {
                if (string.Equals(extension, Extensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
