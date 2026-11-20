using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace WeissDeckBuilder.Infrastructure
{
    /// <summary>
    /// Opt-in stopwatch log for the "opening a series feels slow" reports.
    /// It stays off during a normal start: only -profile / --profile on the
    /// command line (or WSDECK_PROFILE=1 in the environment) enables it, and
    /// then every measured stage appends one line to %TEMP%\wsdeck-load.log.
    /// While disabled each call is a single readonly bool check, so the load
    /// path keeps its speed and the log never appears.
    /// </summary>
    public static class LoadProfiler
    {
        private const string EnvironmentVariable = "WSDECK_PROFILE";
        private const string LogFileName = "wsdeck-load.log";

        /// <summary>
        /// A repainting grid can log a lot of frames; the cap keeps the file
        /// readable on a long session.
        /// </summary>
        private const int MaxLines = 4000;

        private static readonly object Sync = new object();
        private static readonly string TimeFormat = "HH:mm:ss.fff";

        private static bool _enabled;
        private static string _logPath;
        private static StreamWriter _writer;
        private static int _lines;

        public static bool Enabled
        {
            get { return _enabled; }
        }

        /// <summary>Path of the log file, or null while profiling is off.</summary>
        public static string LogPath
        {
            get { return _logPath; }
        }

        /// <summary>
        /// Starts logging when the command line or the environment asks for
        /// it. Called once at startup by Program.Main; a second call is a no-op.
        /// </summary>
        public static void Initialize(string[] args)
        {
            if (_enabled || !Requested(args))
                return;

            try
            {
                _logPath = Path.Combine(Path.GetTempPath(), LogFileName);
                _writer = new StreamWriter(_logPath, false,
                    new UTF8Encoding(false));
                _writer.AutoFlush = true;
                _enabled = true;
                Write("start " + DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                Shutdown();
            }
            catch (UnauthorizedAccessException)
            {
                Shutdown();
            }
        }

        /// <summary>
        /// Timestamp to hand to <see cref="Since"/> or the mark overload of
        /// <see cref="Note(string, string, long, string)"/>. Returns 0 while
        /// profiling is off so a caller needs no branch of its own.
        /// </summary>
        public static long Mark()
        {
            return _enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        /// <summary>Milliseconds elapsed since <paramref name="mark"/>.</summary>
        public static double Since(long mark)
        {
            if (!_enabled || mark == 0L)
                return 0.0;

            long delta = Stopwatch.GetTimestamp() - mark;
            return delta * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>
        /// Measures the block it guards:
        /// <c>using (LoadProfiler.Measure("RunSearch", "Search")) { ... }</c>.
        /// </summary>
        public static Segment Measure(string scope, string step)
        {
            return _enabled
                ? new Segment(scope, step, Stopwatch.GetTimestamp())
                : default(Segment);
        }

        /// <summary>Writes a free-form line, for example a summary.</summary>
        public static void Note(string scope, string text)
        {
            if (!_enabled)
                return;

            Write("[" + scope + "] " + text);
        }

        /// <summary>
        /// Writes "<paramref name="step"/>: N ms since the mark", optionally
        /// followed by extra counters such as the number of decoded tiles.
        /// </summary>
        public static void Note(string scope, string step, long mark,
            string extra)
        {
            if (!_enabled)
                return;

            string line = "[" + scope + "] " + step + ": " +
                Format(Since(mark)) + " ms";
            if (!string.IsNullOrEmpty(extra))
                line += " · " + extra;
            Write(line);
        }

        /// <summary>Timed block returned by <see cref="Measure"/>.</summary>
        public struct Segment : IDisposable
        {
            private readonly string _scope;
            private readonly string _step;
            private readonly long _start;

            internal Segment(string scope, string step, long start)
            {
                _scope = scope;
                _step = step;
                _start = start;
            }

            public void Dispose()
            {
                // default(Segment) is what Measure hands out while profiling is
                // off; its scope stays null and nothing is written.
                if (_scope == null)
                    return;

                LoadProfiler.Write("[" + _scope + "] " + _step + ": " +
                    Format(LoadProfiler.Since(_start)) + " ms");
            }
        }

        private static bool Requested(string[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i] ?? string.Empty;
                    if (string.Equals(arg, "-profile",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "--profile",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            string value = Environment.GetEnvironmentVariable(
                EnvironmentVariable);
            return !string.IsNullOrEmpty(value) &&
                !string.Equals(value, "0", StringComparison.Ordinal);
        }

        private static string Format(double milliseconds)
        {
            return milliseconds.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static void Write(string text)
        {
            lock (Sync)
            {
                if (_writer == null || _lines >= MaxLines)
                    return;

                _lines++;
                try
                {
                    _writer.WriteLine(DateTime.Now.ToString(TimeFormat,
                        CultureInfo.InvariantCulture) + " " + text);
                }
                catch (IOException)
                {
                    Shutdown();
                }
                catch (ObjectDisposedException)
                {
                    Shutdown();
                }
            }
        }

        private static void Shutdown()
        {
            _enabled = false;
            StreamWriter writer = _writer;
            _writer = null;
            if (writer == null)
                return;

            try
            {
                writer.Dispose();
            }
            catch (IOException)
            {
            }
        }
    }
}
