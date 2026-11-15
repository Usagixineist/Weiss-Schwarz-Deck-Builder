using System;
using System.Collections.Generic;
using System.IO;

namespace WeissDeckBuilder.Infrastructure
{
    public static class DataPathResolver
    {
        public static string Resolve(string[] args)
        {
            string explicitPath = ReadCommandLine(args);
            if (!string.IsNullOrEmpty(explicitPath))
                return Normalize(explicitPath);

            string environment = Environment.GetEnvironmentVariable("WS_DECKBUILDER_DATA");
            if (!string.IsNullOrEmpty(environment))
            {
                string normalized = Normalize(environment);
                if (normalized != null)
                    return normalized;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(baseDirectory, "data-root.txt");
            if (File.Exists(configPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configPath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                            continue;

                        if (!Path.IsPathRooted(line))
                            line = Path.Combine(baseDirectory, line);
                        string normalized = Normalize(line);
                        if (normalized != null)
                            return normalized;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            var starts = new List<string>();
            starts.Add(baseDirectory);
            starts.Add(Directory.GetCurrentDirectory());

            for (int i = 0; i < starts.Count; i++)
            {
                string directory = Path.GetFullPath(starts[i]);
                for (int level = 0; level < 5 && !string.IsNullOrEmpty(directory); level++)
                {
                    string[] candidates =
                    {
                        Path.Combine(directory, "Weiss Schwarz_Data", "StreamingAssets"),
                        Path.Combine(directory, "StreamingAssets"),
                        directory
                    };

                    for (int c = 0; c < candidates.Length; c++)
                    {
                        string normalized = Normalize(candidates[c]);
                        if (normalized != null)
                            return normalized;
                    }

                    DirectoryInfo parent = Directory.GetParent(directory);
                    directory = parent == null ? null : parent.FullName;
                }
            }

            return null;
        }

        private static string ReadCommandLine(string[] args)
        {
            if (args == null)
                return null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? string.Empty;
                if (string.Equals(arg, "--data", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-d", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        return args[i + 1];
                }
                else if (arg.StartsWith("--data=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring("--data=".Length);
                }
            }

            return null;
        }

        /// <summary>
        /// Accepts either a StreamingAssets folder or its Cards subfolder.
        /// Returns null unless the Cards subfolder exists.
        /// </summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                string full = Path.GetFullPath(path.Trim().Trim('"'));
                if (string.Equals(Path.GetFileName(full.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    "Cards", StringComparison.OrdinalIgnoreCase))
                {
                    full = Path.GetDirectoryName(full);
                }

                if (string.IsNullOrEmpty(full))
                    return null;
                return Directory.Exists(Path.Combine(full, "Cards")) ? full : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }
    }
}
