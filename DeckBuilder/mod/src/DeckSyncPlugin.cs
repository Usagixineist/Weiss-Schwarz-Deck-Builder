using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeissSchwarzDeckSync
{
    /// <summary>
    /// Keeps StreamingAssets\Decks (the folder the DeckBuilder saves into) and the
    /// game's PlayerPrefs deck store in sync: a deck saved in the DeckBuilder
    /// appears in the game's deck list, and a deck built in the game appears as a
    /// .txt file the DeckBuilder lists after 刷新卡组.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class DeckSyncPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ws.deckbuilder.decksync";
        public const string PluginName = "Weiss Schwarz Deck Sync";
        public const string PluginVersion = "1.0.0";

        private const string Section = "DeckSync";
        private const float StartupDelaySeconds = 3f;
        private const float SceneDelaySeconds = 1.5f;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _syncToGame;
        private ConfigEntry<bool> _syncFromGame;
        private ConfigEntry<bool> _includeDefaultDecks;
        private ConfigEntry<bool> _requireFullDeck;
        private ConfigEntry<bool> _validateCardCodes;
        private ConfigEntry<int> _pollSeconds;
        private ConfigEntry<bool> _syncOnSceneLoad;
        private ConfigEntry<bool> _refreshDeckSelector;
        private ConfigEntry<bool> _verbose;
        private ConfigEntry<string> _skipDeckNames;
        private ConfigEntry<KeyboardShortcut> _hotkey;
        private ConfigEntry<string> _stateFile;

        private DeckSyncService _service;
        private string _statePath;
        private bool _sceneSyncPending;
        private bool _platformSupported;

        private void Awake()
        {
            _enabled = Config.Bind(Section, "Enabled", true,
                "总开关，关掉后不再同步卡组。");
            _syncToGame = Config.Bind(Section, "SyncToGame", true,
                "StreamingAssets\\Decks 里新增或修改的卡组自动写进游戏卡组列表。");
            _syncFromGame = Config.Bind(Section, "SyncFromGame", true,
                "游戏里新建或修改的卡组自动导出到 StreamingAssets\\Decks，DeckBuilder 点刷新即可看到。");
            _includeDefaultDecks = Config.Bind(Section, "IncludeDefaultDecks", false,
                "连游戏自带的预设/AI 卡组一起导出；游戏启动时会重建这些卡组，一般不需要。");
            _requireFullDeck = Config.Bind(Section, "RequireFullDeck", true,
                "只有正好 50 张的卡组才写进游戏；关掉后未完成的草稿也会同步。");
            _validateCardCodes = Config.Bind(Section, "ValidateCardCodes", true,
                "写入游戏前检查每一行卡号，遇到无法识别的行就跳过该卡组。");
            _pollSeconds = Config.Bind(Section, "PollSeconds", 15,
                "自动同步间隔（秒），最小 3 秒。");
            _syncOnSceneLoad = Config.Bind(Section, "SyncOnSceneLoad", true,
                "每次切换场景（例如回到主菜单）同步一次。");
            _refreshDeckSelector = Config.Bind(Section, "RefreshDeckSelector", true,
                "导入卡组后，如果游戏正开着卡组选择界面就立刻刷新列表。");
            _verbose = Config.Bind(Section, "Verbose", false,
                "把没有变化的卡组也写进日志。");
            _skipDeckNames = Config.Bind(Section, "SkipDeckNames", "Placeholder",
                "完全忽略的卡组名，用逗号分隔。");
            _hotkey = Config.Bind(Section, "Hotkey", new KeyboardShortcut(KeyCode.F9),
                "手动同步一次的热键。");
            _stateFile = Config.Bind(Section, "StateFile", "deck-sync-state.txt",
                "同步基线文件名；相对路径相对于插件目录。");

            _platformSupported = Application.platform == RuntimePlatform.WindowsPlayer ||
                Application.platform == RuntimePlatform.WindowsEditor;

            string pluginDirectory = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            _statePath = _stateFile.Value;
            if (!string.IsNullOrEmpty(_statePath) && !Path.IsPathRooted(_statePath))
                _statePath = Path.Combine(pluginDirectory, _statePath);

            string streamingAssets = Application.streamingAssetsPath;
            string deckFolder = Path.Combine(streamingAssets, "Decks");
            string defaultFolder = Path.Combine(streamingAssets, "DefaultDecks");

            _service = new DeckSyncService(
                deckFolder,
                _statePath,
                defaultFolder,
                delegate(string message) { Logger.LogInfo(message); },
                delegate(string message) { Logger.LogWarning(message); });

            Logger.LogInfo("[DeckSync] deck folder: " + deckFolder);
            Logger.LogInfo("[DeckSync] state file: " + _statePath);
            if (!_platformSupported)
            {
                Logger.LogWarning(
                    "[DeckSync] only the Windows PlayerPrefs deck store is supported; " +
                    "the sync stays off on " + Application.platform);
            }
        }

        private void Start()
        {
            StartCoroutine(SyncLoop());
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += SceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
        }

        private void Update()
        {
            if (_hotkey.Value.IsDown())
                RunSync("hotkey");
        }

        /// <summary>Polls the deck folder so a save in the DeckBuilder shows up in game.</summary>
        private IEnumerator SyncLoop()
        {
            yield return new WaitForSecondsRealtime(StartupDelaySeconds);
            RunSync("startup");

            while (true)
            {
                float seconds = Mathf.Max(3f, _pollSeconds.Value);
                yield return new WaitForSecondsRealtime(seconds);
                RunSync("poll");
            }
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_syncOnSceneLoad.Value || _sceneSyncPending)
                return;

            StartCoroutine(SceneSync(scene.name));
        }

        private IEnumerator SceneSync(string sceneName)
        {
            _sceneSyncPending = true;
            yield return new WaitForSecondsRealtime(SceneDelaySeconds);
            RunSync("scene " + sceneName);
            _sceneSyncPending = false;
        }

        private void RunSync(string reason)
        {
            if (!_enabled.Value || !_platformSupported || _service == null)
                return;

            try
            {
                DeckSyncReport report = _service.Run(BuildOptions());
                if (report.Changed)
                {
                    Logger.LogInfo(string.Format(
                        "[DeckSync] {0}: imported {1}, exported {2}, removed {3}",
                        reason, report.Pushed, report.Pulled, report.Removed));
                    if (report.Pushed > 0 || report.Removed > 0)
                        RefreshDeckSelector();
                }
                else if (_verbose.Value)
                {
                    Logger.LogInfo(string.Format(
                        "[DeckSync] {0}: nothing changed ({1} decks checked)",
                        reason, report.Inspected));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[DeckSync] sync failed: " + ex);
            }
        }

        private DeckSyncOptions BuildOptions()
        {
            var options = new DeckSyncOptions();
            options.SyncToGame = _syncToGame.Value;
            options.SyncFromGame = _syncFromGame.Value;
            options.IncludeDefaultDecks = _includeDefaultDecks.Value;
            options.RequireFullDeck = _requireFullDeck.Value;
            options.ValidateCardCodes = _validateCardCodes.Value;
            options.Verbose = _verbose.Value;
            options.SkipDeckNames = DeckSyncPlanner.SplitDeckNames(
                (_skipDeckNames.Value ?? string.Empty).Replace(',', '|'));
            return options;
        }

        /// <summary>
        /// Rebuilds the open deck list so freshly imported decks are selectable
        /// without closing it. DeckSelector keeps both steps private, so they are
        /// invoked by name and any failure is logged instead of thrown.
        /// </summary>
        private void RefreshDeckSelector()
        {
            if (!_refreshDeckSelector.Value)
                return;

            try
            {
                Type selectorType = FindType("DeckSelector");
                if (selectorType == null)
                    return;

                UnityEngine.Object selector = UnityEngine.Object.FindObjectOfType(selectorType);
                if (selector == null)
                    return;

                FieldInfo open = selectorType.GetField(
                    "isOpen",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (open != null && open.FieldType == typeof(bool) && !(bool)open.GetValue(selector))
                    return;

                Invoke(selector, selectorType, "LoadDeckList");
                Invoke(selector, selectorType, "MakeDeckListButtons");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[DeckSync] deck list refresh failed: " + ex.Message);
            }
        }

        private static void Invoke(UnityEngine.Object target, Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null || method.GetParameters().Length != 0)
                return;

            method.Invoke(target, null);
        }

        private static Type FindType(string name)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type type = assemblies[i].GetType(name, false);
                    if (type != null)
                        return type;
                }
                catch (Exception)
                {
                    // Dynamic assemblies can throw while their types are listed.
                }
            }

            return null;
        }
    }
}
