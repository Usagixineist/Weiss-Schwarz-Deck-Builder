using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Reads the deck files the game keeps below StreamingAssets and writes the
    /// decks the player saves from 编辑卡组 back into StreamingAssets\Decks.
    /// Every non-empty line of a deck file is one card code such as
    /// MTI/S83-E101, the layout the game and the bundled AI decks use.
    /// </summary>
    public sealed class DeckLibrary
    {
        public const string LocalFolderName = "Decks";
        public const string DefaultFolderName = "DefaultDecks";
        public const string DeckExtension = ".txt";
        public const int MaxFileNameLength = 80;

        private static readonly string[] Extensions = { ".txt", ".deck" };

        private readonly string _dataRoot;
        private readonly List<DeckDefinition> _decks;
        private readonly List<string> _warnings;

        public DeckLibrary(string dataRoot)
        {
            _dataRoot = dataRoot ?? string.Empty;
            _decks = new List<DeckDefinition>();
            _warnings = new List<string>();
        }

        public string DataRoot
        {
            get { return _dataRoot; }
        }

        public string LocalRoot
        {
            get { return Path.Combine(_dataRoot, LocalFolderName); }
        }

        public string DefaultRoot
        {
            get { return Path.Combine(_dataRoot, DefaultFolderName); }
        }

        /// <summary>Local decks first, then the bundled default decks.</summary>
        public IList<DeckDefinition> Decks
        {
            get { return _decks; }
        }

        public IList<string> Warnings
        {
            get { return _warnings; }
        }

        public int LocalCount
        {
            get { return CountOf(DeckSource.Local); }
        }

        public int DefaultCount
        {
            get { return CountOf(DeckSource.Default); }
        }

        public int CountOf(DeckSource source)
        {
            int count = 0;
            for (int i = 0; i < _decks.Count; i++)
            {
                if (_decks[i].Source == source)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// The decks of StreamingAssets\Decks in file order. The 查看卡组 page
        /// puts them in their own block above the bundled decks.
        /// </summary>
        public IList<DeckDefinition> LocalDecks
        {
            get { return SelectDecks(DeckSource.Local); }
        }

        /// <summary>The bundled decks of StreamingAssets\DefaultDecks.</summary>
        public IList<DeckDefinition> DefaultDecks
        {
            get { return SelectDecks(DeckSource.Default); }
        }

        private IList<DeckDefinition> SelectDecks(DeckSource source)
        {
            var result = new List<DeckDefinition>(CountOf(source));
            for (int i = 0; i < _decks.Count; i++)
            {
                if (_decks[i].Source == source)
                    result.Add(_decks[i]);
            }
            return result;
        }

        public static DeckLibrary Load(string dataRoot)
        {
            var library = new DeckLibrary(dataRoot);
            library.Reload();
            return library;
        }

        /// <summary>Re-reads both deck folders, replacing the previous list.</summary>
        public void Reload()
        {
            _decks.Clear();
            _warnings.Clear();

            LoadFolder(LocalRoot, DeckSource.Local);
            LoadFolder(DefaultRoot, DeckSource.Default);
        }

        private void LoadFolder(string folder, DeckSource source)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                _warnings.Add("卡组目录不存在: " + folder);
                return;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (IOException ex)
            {
                _warnings.Add(folder + ": " + ex.Message);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                _warnings.Add(folder + ": " + ex.Message);
                return;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                if (!IsDeckFile(files[i]))
                    continue;

                try
                {
                    IList<string> codes = ParseFile(files[i]);
                    var deck = new DeckDefinition(
                        Path.GetFileNameWithoutExtension(files[i]),
                        files[i], source, codes);
                    _decks.Add(deck);
                }
                catch (IOException ex)
                {
                    _warnings.Add(files[i] + ": " + ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _warnings.Add(files[i] + ": " + ex.Message);
                }
            }
        }

        private static bool IsDeckFile(string path)
        {
            string extension = Path.GetExtension(path);
            for (int i = 0; i < Extensions.Length; i++)
            {
                if (string.Equals(extension, Extensions[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static IList<string> ParseFile(string path)
        {
            return ParseCardCodes(File.ReadAllLines(path));
        }

        /// <summary>
        /// Keeps the card codes of a deck file and drops blank lines, comment
        /// lines and anything that cannot be a card code (no series separator).
        /// </summary>
        public static IList<string> ParseCardCodes(IEnumerable<string> lines)
        {
            var codes = new List<string>();
            if (lines == null)
                return codes;

            foreach (string raw in lines)
            {
                if (raw == null)
                    continue;

                string line = raw.Trim().Trim('\uFEFF').Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("#", StringComparison.Ordinal) ||
                    line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.IndexOf('/') < 0)
                    continue;

                codes.Add(line);
            }

            return codes;
        }

        /// <summary>
        /// File name a deck is saved under, without extension. Characters
        /// Windows does not accept are replaced, so a name typed into the tray
        /// tab always becomes a readable file next to the other decks.
        /// </summary>
        public static string GetSafeFileName(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return string.Empty;

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(trimmed.Length);
            for (int i = 0; i < trimmed.Length; i++)
            {
                char value = trimmed[i];
                builder.Append(Array.IndexOf(invalid, value) >= 0 ? '_' : value);
            }

            string result = builder.ToString().Trim().TrimEnd('.');
            if (result.Length > MaxFileNameLength)
                result = result.Substring(0, MaxFileNameLength).TrimEnd();
            return result;
        }

        /// <summary>Full path of the local deck file of a deck name.</summary>
        public string GetDeckPath(string name)
        {
            string safeName = GetSafeFileName(name);
            return safeName.Length == 0
                ? null
                : Path.Combine(LocalRoot, safeName + DeckExtension);
        }

        /// <summary>
        /// Writes one deck into StreamingAssets\Decks. The file holds one card
        /// code per line and no header, exactly like the decks the game already
        /// reads, so it stays readable in any text editor. Returns false with a
        /// message instead of throwing, because the page reports the failure in
        /// its status line.
        /// </summary>
        public bool SaveDeck(string name, IList<string> cardCodes, bool overwrite,
            out string path, out string error)
        {
            path = GetDeckPath(name);
            error = null;
            if (path == null)
            {
                error = "卡组名称不能为空";
                return false;
            }
            if (cardCodes == null || cardCodes.Count == 0)
            {
                error = "卡组里没有卡片";
                path = null;
                return false;
            }

            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                if (!overwrite && File.Exists(path))
                {
                    error = "已存在同名卡组";
                    return false;
                }

                var lines = new List<string>(cardCodes.Count);
                for (int i = 0; i < cardCodes.Count; i++)
                    lines.Add(cardCodes[i]);
                File.WriteAllLines(path, lines.ToArray(),
                    new UTF8Encoding(false));
                Reload();
                return true;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (NotSupportedException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Deletes one local deck file and re-reads the folder, so the counts
        /// in the page header follow the file system straight away. The bundled
        /// 预设卡组 are game data, so they are refused before a path is touched:
        /// 删除 in the UI can never take one away. Like <see cref="SaveDeck"/>
        /// this reports a message instead of throwing, because the deck page
        /// prints the failure in its own status line.
        /// </summary>
        public bool DeleteDeck(DeckDefinition deck, out string error)
        {
            error = null;
            if (deck == null)
            {
                error = "卡组不存在";
                return false;
            }
            if (deck.Source != DeckSource.Local)
            {
                error = "预设卡组不能删除";
                return false;
            }

            string path = deck.FilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                // The game or a sync plugin may have removed the file already;
                // the reload lets the page drop the stale tile.
                error = "卡组文件已不存在";
                Reload();
                return false;
            }

            try
            {
                File.Delete(path);
                Reload();
                return true;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
