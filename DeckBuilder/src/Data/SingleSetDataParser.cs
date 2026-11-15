using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeissDeckBuilder.Data
{
    public static class SingleSetDataParser
    {
        public static void ParseFile(
            string path,
            IDictionary<string, SeriesDefinition> seriesByName,
            IList<string> warnings)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            using (StreamReader reader = TextFileReader.Open(path))
            {
                Parse(reader, path, GetSourceFolderName(path), seriesByName, warnings);
            }
        }

        /// <summary>
        /// Parses SingleSetData text that was not read from disk, for example
        /// the copy inside a card pack archive. <paramref name="sourceLabel"/>
        /// only shows up in warnings and <paramref name="sourceFolderName"/> is
        /// the folder below StreamingAssets\Cards that owns the series.
        /// </summary>
        public static void ParseText(
            string text,
            string sourceLabel,
            string sourceFolderName,
            IDictionary<string, SeriesDefinition> seriesByName,
            IList<string> warnings)
        {
            if (text == null)
                return;

            using (var reader = new StringReader(text))
            {
                Parse(reader, sourceLabel ?? string.Empty, sourceFolderName,
                    seriesByName, warnings);
            }
        }

        private static void Parse(
            TextReader reader,
            string sourceLabel,
            string sourceFolderName,
            IDictionary<string, SeriesDefinition> seriesByName,
            IList<string> warnings)
        {
            SeriesDefinition currentSeries = null;
            SeriesFolder currentFolder = null;
            int lineNumber = 0;

            string raw;
            while ((raw = reader.ReadLine()) != null)
            {
                lineNumber++;

                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("//", StringComparison.Ordinal) ||
                    line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;

                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();

                if (string.Equals(key, "Series", StringComparison.OrdinalIgnoreCase))
                {
                    currentSeries = GetOrCreateSeries(
                        seriesByName, value, sourceFolderName);
                    currentFolder = null;
                }
                else if (string.Equals(key, "Folder", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentSeries == null)
                    {
                        warnings.Add(string.Format(
                            "{0}:{1}: Folder appears before any Series line: {2}",
                            sourceLabel, lineNumber, value));
                        continue;
                    }

                    if (value.Length == 0)
                        continue;

                    currentFolder = new SeriesFolder();
                    currentFolder.SeriesName = currentSeries.Name;
                    currentFolder.FolderPath = value;
                    currentSeries.Folders.Add(currentFolder);
                }
                else if (currentFolder == null)
                {
                    continue;
                }
                else if (string.Equals(key, "Prefix", StringComparison.OrdinalIgnoreCase))
                {
                    string prefix = value.ToUpperInvariant();
                    currentFolder.Prefix = prefix;
                    if (prefix.Length > 0 &&
                        !currentFolder.Prefixes.Contains(prefix))
                    {
                        currentFolder.Prefixes.Add(prefix);
                    }
                }
                else if (string.Equals(key, "SetName", StringComparison.OrdinalIgnoreCase))
                {
                    currentFolder.SetName = value;
                }
                else if (string.Equals(key, "FileName", StringComparison.OrdinalIgnoreCase))
                {
                    currentFolder.FileName = value.Length == 0 ? "CardData" : value;
                }
            }
        }

        private static SeriesDefinition GetOrCreateSeries(
            IDictionary<string, SeriesDefinition> seriesByName,
            string name,
            string sourceFolderName)
        {
            name = (name ?? string.Empty).Trim();
            SeriesDefinition series;
            if (!seriesByName.TryGetValue(name, out series))
            {
                series = new SeriesDefinition(name);
                seriesByName[name] = series;
            }

            if (string.IsNullOrEmpty(series.FolderName) &&
                !string.IsNullOrEmpty(sourceFolderName))
            {
                series.FolderName = sourceFolderName;
            }

            return series;
        }

        private static string GetSourceFolderName(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                return string.IsNullOrEmpty(directory)
                    ? string.Empty
                    : Path.GetFileName(directory);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (PathTooLongException)
            {
                return string.Empty;
            }
        }
    }
}
