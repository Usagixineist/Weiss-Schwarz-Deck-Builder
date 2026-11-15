using System;
using System.Windows.Forms;
using WeissDeckBuilder.Data;
using WeissDeckBuilder.Infrastructure;
using WeissDeckBuilder.UI;

namespace WeissDeckBuilder
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender,
                System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.ToString(), "未处理的错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            string dataRoot = DataPathResolver.Resolve(args);
            if (string.IsNullOrEmpty(dataRoot))
            {
                MessageBox.Show(
                    "找不到游戏卡牌数据目录。\r\n\r\n" +
                    "默认会从程序目录向上查找 " +
                    "Weiss Schwarz_Data\\StreamingAssets。\r\n" +
                    "也可以使用以下任一方式指定：\r\n" +
                    "1. 启动参数 --data \"D:\\...\\StreamingAssets\"\r\n" +
                    "2. 环境变量 WS_DECKBUILDER_DATA\r\n" +
                    "3. 程序目录下 data-root.txt（写入路径）",
                    "Weiss Schwarz Deck Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                CardDatabase database = CardDatabase.Load(dataRoot);
                Application.Run(new MainForm(database));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "加载卡牌数据失败：\r\n" + dataRoot + "\r\n\r\n" + ex.Message,
                    "Weiss Schwarz Deck Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
