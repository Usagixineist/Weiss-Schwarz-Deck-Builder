using System;
using System.Collections.Generic;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// 卡号 → 系列 / 卡片。附加功能插件（例如禁限表）只拿得到卡号，这里先用
    /// SingleSetData 的元数据把卡号对到系列（不解析任何 CardData.txt），
    /// 再由调用方让 <see cref="CardDatabase"/> 按自己的缓存加载这个系列，
    /// 所以插件打开的详情层和卡表里点开的一模一样。
    /// </summary>
    public static class CardCodeLocator
    {
        /// <summary>
        /// 卡号属于哪个系列：组成套装的文件夹声明的 Prefix 和卡号完全对上
        /// （DC/W01-091 → DC/W01）时优先；只有系列缩写（DC）对得上时退一步
        /// 用先找到的那个系列。找不到返回 null。
        /// </summary>
        public static SeriesDefinition FindSeries(CardDatabase database,
            string cardCode)
        {
            if (database == null || string.IsNullOrEmpty(cardCode))
                return null;

            string code = cardCode.Trim();
            if (code.Length == 0)
                return null;

            SeriesDefinition loose = null;
            IList<string> names = database.SeriesNames;
            for (int i = 0; i < names.Count; i++)
            {
                SeriesDefinition series = database.GetSeriesMetadata(names[i]);
                if (series == null)
                    continue;

                for (int f = 0; f < series.Folders.Count; f++)
                {
                    foreach (string prefix in series.Folders[f].EnumeratePrefixes())
                    {
                        if (string.IsNullOrEmpty(prefix))
                            continue;

                        if (code.StartsWith(prefix + "-",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return series;
                        }

                        if (loose == null && code.StartsWith(
                            PrefixHead(prefix) + "/",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            loose = series;
                        }
                    }
                }
            }

            return loose;
        }

        /// <summary>
        /// 在已经加载好的系列里找这张卡：先找卡号完全一致的，再按
        /// <see cref="CardCodeMatcher.Canonical"/> 的写法匹配，这样 Banlist.txt
        /// 里不带稀有度字母的卡号也能找到对应卡片。找不到返回 null。
        /// </summary>
        public static WeissCard FindCard(SeriesDefinition series,
            string cardCode)
        {
            if (series == null || string.IsNullOrEmpty(cardCode))
                return null;

            string code = cardCode.Trim();
            if (code.Length == 0)
                return null;

            string key = CardCodeMatcher.Canonical(code);
            WeissCard loose = null;
            for (int i = 0; i < series.Folders.Count; i++)
            {
                List<WeissCard> cards = series.Folders[i].Cards;
                for (int c = 0; c < cards.Count; c++)
                {
                    WeissCard card = cards[c];
                    if (card == null)
                        continue;

                    if (string.Equals(card.Id, code,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return card;
                    }

                    if (loose == null && key.Length > 0 &&
                        string.Equals(CardCodeMatcher.Canonical(card.Id), key,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        loose = card;
                    }
                }
            }

            return loose;
        }

        private static string PrefixHead(string prefix)
        {
            int slash = prefix.IndexOf('/');
            return (slash > 0 ? prefix.Substring(0, slash) : prefix).Trim();
        }
    }
}
