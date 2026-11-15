using System;
using System.Collections.Generic;
using System.IO;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Finds a representative card image for the series overview. The game
    /// data has no separate series-cover artwork, so the first available card
    /// image in the series folders is used. Results are cached and no game
    /// file is modified.
    /// </summary>
    public sealed class SeriesCoverLocator
    {
        private static readonly string[] Extensions =
            { ".png", ".jpg", ".jpeg" };

        private readonly string _cardsRoot;
        private readonly Dictionary<string, string> _cache;
        private readonly HashSet<string> _misses;
        private readonly object _sync;

        public SeriesCoverLocator(string cardsRoot)
        {
            _cardsRoot = cardsRoot ?? string.Empty;
            _cache = new Dictionary<string, string>(StringComparer.Ordinal);
            _misses = new HashSet<string>(StringComparer.Ordinal);
            _sync = new object();
        }

        public string GetCoverPath(SeriesDefinition series)
        {
            if (series == null || string.IsNullOrEmpty(series.Name))
                return null;

            string key = series.Name;
            lock (_sync)
            {
                string cached;
                if (_cache.TryGetValue(key, out cached))
                    return cached;
                if (_misses.Contains(key))
                    return null;
            }

            string path = FindCover(series);

            lock (_sync)
            {
                if (string.IsNullOrEmpty(path))
                    _misses.Add(key);
                else
                    _cache[key] = path;
            }

            return path;
        }

        /// <summary>Clears cached paths; mostly useful for tests.</summary>
        public void Clear()
        {
            lock (_sync)
            {
                _cache.Clear();
                _misses.Clear();
            }
        }

        private string FindCover(SeriesDefinition series)
        {
            for (int i = 0; i < series.Folders.Count; i++)
            {
                string folderPath = ResolveFolderPath(
                    series.Folders[i].FolderPath);
                if (!Directory.Exists(folderPath))
                    continue;

                string[] files;
                try
                {
                    files = Directory.GetFiles(folderPath);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                string best = null;
                string bestName = null;
                for (int j = 0; j < files.Length; j++)
                {
                    if (!IsImageFile(files[j]))
                        continue;

                    string name = Path.GetFileName(files[j]);
                    if (best == null ||
                        StringComparer.OrdinalIgnoreCase.Compare(name, bestName) < 0)
                    {
                        best = files[j];
                        bestName = name;
                    }
                }

                if (!string.IsNullOrEmpty(best))
                    return best;
            }

            return null;
        }

        private string ResolveFolderPath(string relativePath)
        {
            string normalized = (relativePath ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            try
            {
                return Path.Combine(_cardsRoot, normalized);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static bool IsImageFile(string path)
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
    }
}
