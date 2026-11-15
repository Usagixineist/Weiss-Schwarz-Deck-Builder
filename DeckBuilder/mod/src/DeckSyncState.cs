using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeissSchwarzDeckSync
{
    /// <summary>Content hashes the sync saw last time it looked at a deck.</summary>
    public sealed class DeckSyncEntry
    {
        public string FileHash { get; set; }

        public string GameHash { get; set; }
    }

    /// <summary>
    /// Tiny text database that remembers the content hash of both sides so the
    /// sync can tell "the file changed" from "the game changed" without relying
    /// on clock precision (the game stores save times down to the minute only).
    /// One line per deck: "&lt;fileHash&gt;\t&lt;gameHash&gt;\t&lt;deck name&gt;".
    /// </summary>
    public sealed class DeckSyncState
    {
        public const string Header = "# WeissSchwarzDeckSync state v1";

        private static readonly char[] FieldSeparator = { '\t' };

        private readonly Dictionary<string, DeckSyncEntry> _entries =
            new Dictionary<string, DeckSyncEntry>(StringComparer.Ordinal);

        public IList<string> Names
        {
            get
            {
                var names = new List<string>(_entries.Keys);
                names.Sort(StringComparer.Ordinal);
                return names;
            }
        }

        public static DeckSyncState Load(string path)
        {
            var state = new DeckSyncState();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return state;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (IOException)
            {
                return state;
            }
            catch (UnauthorizedAccessException)
            {
                return state;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == null)
                    continue;

                line = line.TrimEnd();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                string[] parts = line.Split(FieldSeparator, 3);
                if (parts.Length < 3)
                    continue;

                string name = parts[2];
                if (name.Length == 0)
                    continue;

                var entry = new DeckSyncEntry();
                entry.FileHash = parts[0];
                entry.GameHash = parts[1];
                state._entries[name] = entry;
            }

            return state;
        }

        public DeckSyncEntry Find(string deckName)
        {
            if (string.IsNullOrEmpty(deckName))
                return null;

            DeckSyncEntry entry;
            return _entries.TryGetValue(deckName, out entry) ? entry : null;
        }

        public void Set(string deckName, string fileHash, string gameHash)
        {
            if (string.IsNullOrEmpty(deckName))
                return;

            var entry = new DeckSyncEntry();
            entry.FileHash = fileHash ?? string.Empty;
            entry.GameHash = gameHash ?? string.Empty;
            _entries[deckName] = entry;
        }

        public void Remove(string deckName)
        {
            if (string.IsNullOrEmpty(deckName))
                return;

            _entries.Remove(deckName);
        }

        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            var builder = new StringBuilder();
            builder.AppendLine(Header);

            IList<string> names = Names;
            for (int i = 0; i < names.Count; i++)
            {
                DeckSyncEntry entry = _entries[names[i]];
                builder.Append(entry.FileHash ?? string.Empty);
                builder.Append('\t');
                builder.Append(entry.GameHash ?? string.Empty);
                builder.Append('\t');
                builder.Append(names[i]);
                builder.AppendLine();
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }
    }
}
