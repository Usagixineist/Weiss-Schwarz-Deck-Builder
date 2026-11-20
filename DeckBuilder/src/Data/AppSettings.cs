using System;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// 界面设置，现在只有主题一项。设置页的每一项都写进唯一的那一个
    /// <see cref="SettingsStore"/> 文件（分组 <c>[appearance]</c>，写出来是
    /// <c>theme=light</c>），删掉那个文件就等于恢复默认的深色主题。
    /// 文件缺失、行写坏或者写不进去都只当作“用默认值”，绝不抛出异常。
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>设置项的键名，写出来是 <c>theme=light</c>。</summary>
        public const string ThemeKey = "theme";

        private readonly SettingsStore _store;

        /// <summary>
        /// 读写共享的那一个设置文件。构造时就把主题读进来，读不到就是默认值，
        /// 界面因此不需要在启动时处理“设置还没加载”这种中间状态。
        /// </summary>
        public AppSettings(SettingsStore store)
        {
            _store = store;
            Load();
        }

        /// <summary>Full path of the shared settings file; empty when there is none.</summary>
        public string FilePath
        {
            get { return _store == null ? string.Empty : _store.FilePath; }
        }

        /// <summary>True when the shared settings file exists and can be read.</summary>
        public bool HasFile
        {
            get { return _store != null && _store.HasFile; }
        }

        /// <summary>当前主题。改动后调用 <see cref="Save"/> 才写进文件。</summary>
        public AppThemeMode Theme { get; set; }

        /// <summary>主题在文件里的写法：<c>dark</c> / <c>light</c>。</summary>
        public static string ToText(AppThemeMode mode)
        {
            return mode == AppThemeMode.Light ? "light" : "dark";
        }

        /// <summary>
        /// 把文件里的文字认成主题。只认 dark / light（不区分大小写），
        /// 其余一律返回 false，让调用方保留原值。
        /// </summary>
        public static bool TryParseTheme(string text, out AppThemeMode mode)
        {
            mode = AppThemeMode.Dark;
            if (text == null)
                return false;

            text = text.Trim();
            if (text.Length == 0)
                return false;

            if (string.Equals(text, "light", StringComparison.OrdinalIgnoreCase))
            {
                mode = AppThemeMode.Light;
                return true;
            }

            if (string.Equals(text, "dark", StringComparison.OrdinalIgnoreCase))
            {
                mode = AppThemeMode.Dark;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 重新读一遍设置文件里的主题。先回到默认值，所以把文件删掉之后调用也能
        /// 恢复深色；这一项读不懂时也留在深色，不会影响别的设置。
        /// </summary>
        public void Load()
        {
            Theme = AppThemeMode.Dark;
            if (_store == null)
                return;

            _store.Load();
            string text;
            if (!_store.TryGet(SettingsStore.AppearanceSection, ThemeKey, out text))
                return;

            AppThemeMode parsed;
            if (TryParseTheme(text, out parsed))
                Theme = parsed;
        }

        /// <summary>
        /// 把主题写回设置文件。整个文件重写一遍，所以不会留下过期的键，也不会
        /// 碰其他类别的设置。
        /// </summary>
        public bool Save(out string error)
        {
            if (_store == null)
            {
                error = "没有可用的设置文件，界面设置无法保存。";
                return false;
            }

            _store.Set(SettingsStore.AppearanceSection, ThemeKey, ToText(Theme));
            return _store.Save(out error);
        }
    }
}