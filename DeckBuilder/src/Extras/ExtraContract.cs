using System;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Extras
{
    /// <summary>
    /// 一个附加功能插件的显示信息，由 plugin.txt 提供。
    /// 插件通过 <see cref="IExtraHost.Info"/> 读自己这一份。
    /// </summary>
    public sealed class ExtraInfo
    {
        public ExtraInfo()
        {
            Id = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            Version = string.Empty;
            Author = string.Empty;
            FolderPath = string.Empty;
            AssemblyPath = string.Empty;
        }

        /// <summary>插件标识，同一目录里唯一。</summary>
        public string Id { get; set; }

        /// <summary>侧边栏与页头的显示名。</summary>
        public string Name { get; set; }

        /// <summary>页头副标题 / 悬停提示。</summary>
        public string Description { get; set; }

        /// <summary>版本号，仅用于显示。</summary>
        public string Version { get; set; }

        /// <summary>作者，仅用于显示。</summary>
        public string Author { get; set; }

        /// <summary>插件文件夹的完整路径，插件自己的数据文件可以放这里。</summary>
        public string FolderPath { get; set; }

        /// <summary>插件程序集的完整路径。</summary>
        public string AssemblyPath { get; set; }
    }

    /// <summary>
    /// 附加功能插件必须实现的接口：程序集里找一个公开、可实例化、
    /// 无参构造的类型即可，不需要注册表或特性。
    ///
    /// <see cref="CreateContent"/> 在用户第一次点开这个插件时调用，返回的
    /// 控件会被放进内容区。插件抛出的异常由程序接住，界面显示错误页，
    /// 不会影响其他插件，也不会影响主程序。
    /// </summary>
    public interface IExtraPlugin
    {
        /// <summary>创建插件页面。返回 null 视作加载失败。</summary>
        Control CreateContent(IExtraHost host);
    }

    /// <summary>
    /// 可选的生命周期回调。<see cref="CreateContent"/> 返回的控件实现它以后，
    /// 每次切到这个插件、或离开它时都会被通知；切回来不会重建控件。
    /// 回调抛出的异常同样会被接住并写进 extras.log。
    /// </summary>
    public interface IExtraPage
    {
        /// <summary>插件页成为当前页时调用。</summary>
        void OnActivated();

        /// <summary>用户切换到别的插件或别的页面时调用。</summary>
        void OnDeactivated();
    }

    /// <summary>
    /// 主程序交给插件的一切东西。插件只依赖这个接口，不需要引用主程序的
    /// 内部类型；数据都是只读入口，写操作仍走主程序自己的流程
    /// （例如刷新卡组用 <see cref="ReloadDeckLibrary"/>）。
    /// </summary>
    public interface IExtraHost
    {
        /// <summary>本插件自己的清单信息。</summary>
        ExtraInfo Info { get; }

        /// <summary>主窗口，用来当对话框的父窗口或做布局参考。</summary>
        Form Owner { get; }

        /// <summary>数据根目录（StreamingAssets）；未知时为空字符串。</summary>
        string DataRoot { get; }

        /// <summary>只读的卡表数据库。</summary>
        CardDatabase Database { get; }

        /// <summary>触发名称表（界面显示用的中文名）。</summary>
        TriggerGlossary Glossary { get; }

        /// <summary>当前配色。换主题后 <see cref="ThemeChanged"/> 会通知。</summary>
        AppThemeMode Theme { get; }

        /// <summary>extras.log 的完整路径；写日志请直接用 <see cref="Log"/>。</summary>
        string LogPath { get; }

        /// <summary>
        /// 换主题后触发。插件自绘的颜色要在这里重新取 AppTheme 的值并重绘。
        /// </summary>
        event EventHandler ThemeChanged;

        /// <summary>重新从磁盘读一遍卡组（本地 + 默认卡组）。</summary>
        DeckLibrary LoadDecks();

        /// <summary>让主程序重新加载它已经打开的卡组列表。</summary>
        void ReloadDeckLibrary();

        /// <summary>
        /// 弹出主程序的卡片详情层（大图、信息区、效果），效果和在卡表里点
        /// 一张卡完全一样。插件只给卡号，不用自己解析 CardData.txt；卡表里
        /// 没有这张卡时返回 false，插件可以自己决定怎么提示。
        /// </summary>
        bool ShowCard(string cardCode);

        /// <summary>往 extras.log 追加一行，带时间戳和插件 id。</summary>
        void Log(string message);

        /// <summary>弹一个信息框，标题是本插件的名字。</summary>
        void ShowMessage(string message);

        /// <summary>弹一个错误框，标题是本插件的名字。</summary>
        void ShowError(string message);
    }
}
