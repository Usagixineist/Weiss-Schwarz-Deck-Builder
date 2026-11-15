using System;
using System.Collections.Generic;
using System.IO;

namespace WeissSchwarzDeckSync
{
    /// <summary>Settings the plugin reads from its config file for every run.</summary>
    public sealed class DeckSyncOptions
    {
        public bool SyncToGame = true;
        public bool SyncFromGame = true;
        public bool IncludeDefaultDecks;
        public bool RequireFullDeck = true;
        public bool ValidateCardCodes = true;
        public bool Verbose;
        public IList<string> SkipDeckNames = new List<string>();
    }

    /// <summary>What one run did, so the plugin can log and refresh the UI.</summary>
    public sealed class DeckSyncReport
    {
        public int Inspected;
        public int Pushed;
        public int Pulled;
        public int Removed;

        public bool Changed
        {
            get { return Pushed > 0 || Pulled > 0 || Removed > 0; }
        }
    }

    /// <summary>
    /// Two-way sync between StreamingAssets\Decks (the DeckBuilder's deck folder,
    /// one card code per line) and the game's PlayerPrefs deck store. A deck name
    /// is the identity: whichever side changed since the last run wins, and when
    /// both moved the newer save time wins. Decks the game bundles in
    /// DefaultDecks are never exported unless the player asks for it.
    /// </summary>
    internal sealed class DeckSyncService
    {
        private readonly DeckFolderStore _folder;
        private readonly string _statePath;
        private readonly string _defaultDeckFolder;
        private readonly Action<string> _log;
        private readonly Action<string> _warn;
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        public DeckSyncService(
            string deckFolder,
            string statePath,
            string defaultDeckFolder,
            Action<string> log,
            Action<string> warn)
        {
            _folder = new DeckFolderStore(deckFolder);
            _statePath = statePath ?? string.Empty;
            _defaultDeckFolder = defaultDeckFolder ?? string.Empty;
            _log = log ?? delegate { };
            _warn = warn ?? delegate { };
        }

        public string DeckFolder
        {
            get { return _folder.Folder; }
        }

        public DeckSyncReport Run(DeckSyncOptions options)
        {
            var report = new DeckSyncReport();
            if (options == null)
                options = new DeckSyncOptions();

            if (!Directory.Exists(_folder.Folder))
            {
                _warn("[DeckSync] deck folder missing: " + _folder.Folder);
                return report;
            }

            IDictionary<string, string> files = _folder.IndexByName();
            DeckSyncState state = DeckSyncState.Load(_statePath);
            IList<string> gameNames = GameDeckStore.GetDeckNames();
            HashSet<string> skip = ToNameSet(options.SkipDeckNames);
            HashSet<string> bundled = LoadBundledNames();

            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < gameNames.Count; i++)
                AddCandidate(order, seen, gameNames[i]);
            foreach (string fileName in files.Keys)
                AddCandidate(order, seen, fileName);

            for (int i = 0; i < order.Count; i++)
            {
                string name = order[i];
                if (skip.Contains(name))
                {
                    Note(options, "[DeckSync] skipped by config: " + name);
                    continue;
                }

                if (!SyncOne(name, files, state, bundled, options, report))
                    continue;
            }

            RemoveStaleEntries(state, order);

            if (report.Pushed > 0 || report.Removed > 0)
                GameDeckStore.Flush();

            try
            {
                state.Save(_statePath);
            }
            catch (Exception ex)
            {
                _warn("[DeckSync] cannot save state file " + _statePath + ": " + ex.Message);
            }

            return report;
        }

        /// <summary>Syncs one deck name. Returns false when the entry is unusable.</summary>
        private bool SyncOne(
            string name,
            IDictionary<string, string> files,
            DeckSyncState state,
            HashSet<string> bundled,
            DeckSyncOptions options,
            DeckSyncReport report)
        {
            string path = _folder.FindPath(files, name);
            IList<string> fileLines = path != null ? _folder.ReadLines(path) : null;
            if (path != null && fileLines == null)
            {
                _warn("[DeckSync] cannot read " + path);
                return false;
            }

            IList<string> fileIds = fileLines != null
                ? DeckSyncPlanner.ParseCardLines(fileLines)
                : null;
            string fileText = fileIds != null ? DeckSyncPlanner.ToDeckText(fileIds) : null;
            DateTime? fileTime = path != null ? _folder.ReadWriteTime(path) : null;
            string gameText = GameDeckStore.ReadDeckText(name);
            DateTime? gameTime = GameDeckStore.GetDate(name);

            if (string.IsNullOrEmpty(fileText) && string.IsNullOrEmpty(gameText))
            {
                state.Remove(name);
                return false;
            }

            report.Inspected++;
            DeckSyncEntry entry = state.Find(name);

            // A deck the player removed in the game must not come back: the
            // mirror file is unchanged since the last sync, so the game side is
            // the one that moved. Only the file is dropped.
            if (entry != null &&
                !string.IsNullOrEmpty(entry.GameHash) &&
                string.IsNullOrEmpty(gameText) &&
                !string.IsNullOrEmpty(fileText) &&
                string.Equals(DeckSyncPlanner.Hash(fileText), entry.FileHash, StringComparison.Ordinal))
            {
                if (RemoveMirrorFile(path))
                {
                    state.Remove(name);
                    report.Removed++;
                    _log("[DeckSync] removed from the game, mirror file deleted: " + name);
                }

                return true;
            }

            // The mirror side was deleted (DeckBuilder's 查看卡组 or Explorer)
            // while the game deck is still the content of the last sync. Delete
            // the game deck instead of exporting it back into Decks.
            if (path == null &&
                DeckSyncPlanner.IsFileSideDeleted(
                    fileText,
                    gameText,
                    entry != null ? entry.FileHash : null,
                    entry != null ? entry.GameHash : null))
            {
                if (!options.SyncToGame)
                {
                    Note(options, "[DeckSync] file -> game disabled, keeping game deck: " + name);
                    return false;
                }

                if (!options.IncludeDefaultDecks && bundled.Contains(name))
                {
                    Note(options, "[DeckSync] bundled deck not deleted: " + name);
                    return false;
                }

                try
                {
                    bool removed = GameDeckStore.DeleteDeck(name);
                    state.Remove(name);
                    if (removed)
                    {
                        report.Removed++;
                        _log("[DeckSync] removed from the game, mirror file deleted: " + name);
                    }
                    else
                    {
                        Note(options, "[DeckSync] already gone from the game: " + name);
                    }
                }
                catch (Exception ex)
                {
                    _warn("[DeckSync] cannot delete deck from the game: " +
                        name + ": " + ex.Message);
                    return false;
                }

                return true;
            }

            DeckSyncAction action = DeckSyncPlanner.Decide(
                fileText,
                gameText,
                entry != null ? entry.FileHash : null,
                entry != null ? entry.GameHash : null,
                fileTime,
                gameTime);

            bool settled = true;
            if (action == DeckSyncAction.PushToGame)
            {
                settled = PushToGame(name, path, fileIds, fileText, options, report,
                    ref gameText, ref gameTime);
            }
            else if (action == DeckSyncAction.PullToFile)
            {
                settled = PullToFile(name, path, gameText, bundled, options, report,
                    ref fileText, ref fileTime);
            }
            else
            {
                Note(options, "[DeckSync] unchanged: " + name);
            }

            // A deck that a rule or a disabled direction held back keeps its old
            // baseline, so the next run tries again once the rule allows it.
            if (settled)
            {
                state.Set(
                    name,
                    DeckSyncPlanner.Hash(fileText),
                    DeckSyncPlanner.Hash(gameText));
            }

            return true;
        }

        /// <summary>File -> game. Returns false when the deck was held back.</summary>
        private bool PushToGame(
            string name,
            string path,
            IList<string> fileIds,
            string fileText,
            DeckSyncOptions options,
            DeckSyncReport report,
            ref string gameText,
            ref DateTime? gameTime)
        {
            if (!options.SyncToGame)
            {
                Note(options, "[DeckSync] file -> game disabled, waiting: " + name);
                return false;
            }

            string problem = ValidatePush(name, fileIds, options);
            if (problem != null)
            {
                WarnOnce(
                    "push:" + name + ":" + DeckSyncPlanner.Hash(fileText),
                    "[DeckSync] not imported: " + name + " (" + problem + ")");
                return false;
            }

            try
            {
                GameDeckStore.WriteDeck(name, fileText);
            }
            catch (Exception ex)
            {
                _warn("[DeckSync] cannot write deck to the game: " + name + ": " + ex.Message);
                return false;
            }

            gameText = fileText;
            gameTime = GameDeckStore.GetDate(name);
            report.Pushed++;
            _log("[DeckSync] imported into the game: " + name +
                " (" + fileIds.Count + " cards, " +
                Path.GetFileName(path ?? string.Empty) + ")");
            return true;
        }

        /// <summary>Game -> file. Returns false when the deck was held back.</summary>
        private bool PullToFile(
            string name,
            string path,
            string gameText,
            HashSet<string> bundled,
            DeckSyncOptions options,
            DeckSyncReport report,
            ref string fileText,
            ref DateTime? fileTime)
        {
            if (!options.SyncFromGame)
            {
                Note(options, "[DeckSync] game -> file disabled, waiting: " + name);
                return false;
            }

            if (!options.IncludeDefaultDecks && bundled.Contains(name))
            {
                Note(options, "[DeckSync] bundled deck not exported: " + name);
                return false;
            }

            IList<string> ids = DeckSyncPlanner.SplitDeckText(gameText);
            if (ids.Count == 0)
            {
                _warn("[DeckSync] game deck is empty: " + name);
                return false;
            }

            string written;
            try
            {
                written = _folder.WriteDeck(name, ids, path);
            }
            catch (Exception ex)
            {
                _warn("[DeckSync] cannot write deck file: " + name + ": " + ex.Message);
                return false;
            }

            fileText = DeckSyncPlanner.ToDeckText(ids);
            fileTime = _folder.ReadWriteTime(written);
            report.Pulled++;
            _log("[DeckSync] exported from the game: " + name +
                " (" + ids.Count + " cards, " + Path.GetFileName(written) + ")");
            return true;
        }

        private static string ValidatePush(
            string name, IList<string> ids, DeckSyncOptions options)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                return "empty deck name";
            if (name.IndexOf('|') >= 0)
                return "the name contains |";
            if (ids == null || ids.Count == 0)
                return "the file has no cards";
            if (options.RequireFullDeck && ids.Count != DeckSyncPlanner.StandardDeckSize)
            {
                return string.Format(
                    "{0} cards, {1} required (turn off RequireFullDeck to sync partial decks)",
                    ids.Count, DeckSyncPlanner.StandardDeckSize);
            }

            if (options.ValidateCardCodes)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    if (!DeckSyncPlanner.IsCardCode(ids[i]))
                        return "line " + (i + 1) + " is not a card code: " + ids[i];
                }
            }

            return null;
        }

        /// <summary>Deletes the mirror file of a deck that is gone from the game.</summary>
        private bool RemoveMirrorFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                File.Delete(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void Note(DeckSyncOptions options, string message)
        {
            if (options != null && options.Verbose)
                _log(message);
        }

        /// <summary>Warns once per deck content so a poll loop cannot flood the log.</summary>
        private void WarnOnce(string key, string message)
        {
            if (_warned.Add(key))
                _warn(message);
        }

        private static void AddCandidate(
            IList<string> order, HashSet<string> seen, string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            string trimmed = name.Trim();
            if (trimmed.Length == 0 || trimmed.IndexOf('|') >= 0)
                return;
            if (!seen.Add(trimmed))
                return;

            order.Add(trimmed);
        }

        private static HashSet<string> ToNameSet(IList<string> names)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (names == null)
                return set;

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                set.Add(name.Trim());
            }

            return set;
        }

        /// <summary>Names the game rebuilds from StreamingAssets\DefaultDecks on boot.</summary>
        private HashSet<string> LoadBundledNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(_defaultDeckFolder) ||
                !Directory.Exists(_defaultDeckFolder))
            {
                return names;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(_defaultDeckFolder, "*.txt");
            }
            catch (IOException)
            {
                return names;
            }
            catch (UnauthorizedAccessException)
            {
                return names;
            }

            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileNameWithoutExtension(files[i]);
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }

            return names;
        }

        private static void RemoveStaleEntries(DeckSyncState state, IList<string> names)
        {
            var live = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
                live.Add(names[i]);

            IList<string> stored = state.Names;
            for (int i = 0; i < stored.Count; i++)
            {
                if (!live.Contains(stored[i]))
                    state.Remove(stored[i]);
            }
        }
    }
}
