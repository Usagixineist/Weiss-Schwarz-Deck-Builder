namespace WeissDeckBuilder.Models
{
    /// <summary>
    /// 界面配色方案。只有深色（原先固定的那套）和浅色两种，写进
    /// <c>settings.txt</c> 的也是这两个名字。
    /// 强调色（蓝）与状态色（红 / 黄 / 绿）不随主题变化。
    /// </summary>
    public enum AppThemeMode
    {
        /// <summary>深色主题，程序一直以来的默认外观。</summary>
        Dark = 0,

        /// <summary>浅色主题，白天或投影时使用。</summary>
        Light = 1
    }
}
