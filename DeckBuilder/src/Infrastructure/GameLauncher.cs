using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace WeissDeckBuilder.Infrastructure
{
    /// <summary>
    /// Everything needed to start the game: the exe, the folder it has to run
    /// in (the game reads <c>UnityPlayer.dll</c> and the Doorstop config from
    /// there) and the argument list.
    /// </summary>
    public sealed class GameLaunchTarget
    {
        public GameLaunchTarget(string exePath, string gameRoot, string arguments)
        {
            ExePath = exePath;
            GameRoot = gameRoot;
            Arguments = arguments;
        }

        public string ExePath { get; private set; }

        public string GameRoot { get; private set; }

        public string Arguments { get; private set; }
    }

    /// <summary>
    /// Starts the game the same way <c>不热更启动游戏.lnk</c> does. The shortcut
    /// resolves to <c>cmd /c start "" "Weiss Schwarz.exe" noUpdate</c>, and that
    /// <c>noUpdate</c> argument is what keeps the bundled updater
    /// (<c>更新卡牌数据.exe</c>) from overwriting the card table, so this class
    /// only has to find the exe and hand it the same argument.
    ///
    /// The exe is started directly instead of through the shortcut: the
    /// shortcut uses a relative exe path with an empty working directory, which
    /// would depend on whoever launched it, and going through <c>cmd</c> flashes
    /// a console window.
    ///
    /// Resolution walks up from the card data folder, so a portable DeckBuilder
    /// installed somewhere else still finds the game; the fixed layout of
    /// <c>DataPathResolver</c> supplies that folder.
    /// </summary>
    public static class GameLauncher
    {
        /// <summary>File name of the game executable inside the install root.</summary>
        public const string ExeName = "Weiss Schwarz.exe";

        /// <summary>
        /// Argument that tells the game to skip its auto update. It is the exact
        /// value <c>不热更启动游戏.lnk</c> passes, so the shortcut and this button
        /// behave identically.
        /// </summary>
        public const string NoUpdateArgument = "noUpdate";

        /// <summary>
        /// Optional override: full path to the game exe. Useful when the game
        /// lives outside the folders the upward search visits.
        /// </summary>
        public const string ExePathVariable = "WS_GAME_EXE";

        /// <summary>Process name without extension, as reported by Windows.</summary>
        public const string ProcessName = "Weiss Schwarz";

        // StreamingAssets -> Weiss Schwarz_Data -> install root is the normal
        // case; the extra levels cover wrappers such as a nested copy named by
        // data-root.txt.
        private const int MaxParentLevels = 5;

        /// <summary>
        /// Returns the folder that holds <see cref="ExeName"/>, or null when the
        /// data folder is not inside a game install.
        /// </summary>
        public static string FindGameRoot(string dataRoot)
        {
            string overridden = ReadOverrideDirectory();
            if (!string.IsNullOrEmpty(overridden))
                return overridden;

            string directory = ToFullPath(dataRoot);
            if (string.IsNullOrEmpty(directory))
                directory = ToFullPath(AppDomain.CurrentDomain.BaseDirectory);
            if (string.IsNullOrEmpty(directory))
                return null;

            for (int level = 0; level < MaxParentLevels; level++)
            {
                if (string.IsNullOrEmpty(directory))
                    return null;

                if (File.Exists(Path.Combine(directory, ExeName)))
                    return directory;

                DirectoryInfo parent = Directory.GetParent(directory);
                directory = parent == null ? null : parent.FullName;
            }

            return null;
        }

        /// <summary>
        /// Turns the card data folder into a launch target. Returns null and
        /// fills <paramref name="error"/> with a message for the user when the
        /// game cannot be located.
        /// </summary>
        public static GameLaunchTarget Resolve(string dataRoot, out string error)
        {
            error = null;

            string gameRoot = FindGameRoot(dataRoot);
            if (string.IsNullOrEmpty(gameRoot))
            {
                error = "找不到 " + ExeName + "。\r\n\r\n" +
                    "请把 DeckBuilder 文件夹放到游戏目录（和 " + ExeName +
                    " 同一层），或用 --data 指向游戏的数据目录；也可以设置环境变量 " +
                    ExePathVariable + " 直接指定 exe 的完整路径。";
                return null;
            }

            return new GameLaunchTarget(
                Path.Combine(gameRoot, ExeName), gameRoot, NoUpdateArgument);
        }

        /// <summary>
        /// True while a game process is alive, so the button does not start a
        /// second copy on top of the running one.
        /// </summary>
        public static bool IsRunning()
        {
            try
            {
                Process[] running = Process.GetProcessesByName(ProcessName);
                bool found = running.Length > 0;
                for (int i = 0; i < running.Length; i++)
                {
                    if (running[i] != null)
                        running[i].Dispose();
                }

                return found;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Starts the game without the auto update. Returns false and fills
        /// <paramref name="error"/> when Windows refuses the start.
        /// </summary>
        public static bool TryLaunch(GameLaunchTarget target, out string error)
        {
            error = null;

            if (target == null || string.IsNullOrEmpty(target.ExePath) ||
                !File.Exists(target.ExePath))
            {
                error = "找不到 " + ExeName + "，无法启动游戏。";
                return false;
            }

            var startInfo = new ProcessStartInfo();
            startInfo.FileName = target.ExePath;
            startInfo.Arguments = target.Arguments;
            startInfo.WorkingDirectory = target.GameRoot;
            startInfo.UseShellExecute = true;

            try
            {
                // The shell owns the game process from here on; the returned
                // handle is only good for releasing it.
                using (Process process = Process.Start(startInfo))
                {
                }

                return true;
            }
            catch (Win32Exception ex)
            {
                error = "启动游戏失败：" + ex.Message;
                return false;
            }
            catch (InvalidOperationException ex)
            {
                error = "启动游戏失败：" + ex.Message;
                return false;
            }
            catch (IOException ex)
            {
                error = "启动游戏失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Directory named by <see cref="ExePathVariable"/>, but only when it
        /// really contains the game exe; a stale value falls back to the search.
        /// </summary>
        private static string ReadOverrideDirectory()
        {
            string value;
            try
            {
                value = Environment.GetEnvironmentVariable(ExePathVariable);
            }
            catch (System.Security.SecurityException)
            {
                return null;
            }

            if (string.IsNullOrEmpty(value))
                return null;

            string directory = ToFullPath(
                Path.GetDirectoryName(value.Trim().Trim('"')));
            if (string.IsNullOrEmpty(directory))
                return null;

            return File.Exists(Path.Combine(directory, ExeName)) ? directory : null;
        }

        private static string ToFullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                return Path.GetFullPath(path.Trim().Trim('"'));
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
