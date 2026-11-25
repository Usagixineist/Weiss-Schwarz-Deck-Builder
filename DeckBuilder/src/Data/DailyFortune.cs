using System;
using System.Collections.Generic;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// One day's fortune: a 0-100 今日人品 score, its 吉凶 level and the two
    /// 宜 / 忌 tags shown on the landing page.
    /// </summary>
    public sealed class FortuneReport
    {
        public FortuneReport(DateTime date, int score, string level,
            IList<string> good, IList<string> bad)
        {
            Date = date.Date;
            Score = score;
            Level = level ?? string.Empty;
            Good = good ?? new List<string>();
            Bad = bad ?? new List<string>();
        }

        /// <summary>The local calendar day this report belongs to.</summary>
        public DateTime Date { get; private set; }

        /// <summary>今日人品, from 0 to 100.</summary>
        public int Score { get; private set; }

        /// <summary>大吉 / 中吉 / 小吉 / 吉 / 末吉 / 凶 / 大凶.</summary>
        public string Level { get; private set; }

        /// <summary>Things that are favourable today (always two entries).</summary>
        public IList<string> Good { get; private set; }

        /// <summary>Things to avoid today (always two entries).</summary>
        public IList<string> Bad { get; private set; }
    }

    /// <summary>
    /// A small, process-independent hash of a calendar date. Random's default
    /// seed is time based, and string.GetHashCode is randomised per process;
    /// both would change the daily card and fortune between launches. Hashing
    /// year/month/day ourselves keeps every launch on the same day identical.
    /// </summary>
    internal static class DailySeed
    {
        public static int ForDate(DateTime date, int salt)
        {
            DateTime day = date.Date;
            unchecked
            {
                uint hash = (uint)(day.Year * 10000 +
                    day.Month * 100 + day.Day);
                hash ^= (uint)salt + 0x9E3779B9u;
                hash = (hash ^ (hash >> 16)) * 0x85EBCA6Bu;
                hash = (hash ^ (hash >> 13)) * 0xC2B2AE35u;
                hash ^= hash >> 16;
                return (int)(hash & 0x7FFFFFFFu);
            }
        }
    }

    /// <summary>
    /// Builds the landing page's daily fortune from the local date. There is no
    /// network call and nothing is stored: asking twice for the same day gives
    /// the same score, level and tags.
    /// </summary>
    public static class DailyFortune
    {
        private static readonly string[] GoodTags =
        {
            "开包", "对战", "组卡", "复习", "早睡",
            "喝茶", "听歌", "整理卡册", "尝试新卡组", "和朋友切磋"
        };

        private static readonly string[] BadTags =
        {
            "熬夜", "冲动合卡", "盲目抄卡组", "忘记洗牌", "连败上头",
            "临时改卡组", "忘记收卡", "拖延", "错过触发", "氪金"
        };

        public static FortuneReport ForDate(DateTime date)
        {
            DateTime day = date.Date;
            var random = new Random(DailySeed.ForDate(day, 0x51F0));
            int score = random.Next(101);
            string level = LevelForScore(score);
            List<string> good = PickTags(GoodTags, random, 2);
            List<string> bad = PickTags(BadTags, random, 2);
            return new FortuneReport(day, score, level, good, bad);
        }

        /// <summary>
        /// Maps a 0-100 score to its 吉凶 label. The bands are intentionally
        /// even so a good day feels earned rather than guaranteed.
        /// </summary>
        public static string LevelForScore(int score)
        {
            if (score >= 90)
                return "大吉";
            if (score >= 75)
                return "中吉";
            if (score >= 60)
                return "小吉";
            if (score >= 45)
                return "吉";
            if (score >= 30)
                return "末吉";
            if (score >= 15)
                return "凶";
            return "大凶";
        }

        /// <summary>
        /// Draws distinct tags from one pool. The full index list is shuffled
        /// rather than drawing with retries, so a short pool can never loop.
        /// </summary>
        private static List<string> PickTags(string[] pool, Random random,
            int count)
        {
            var indexes = new List<int>(pool.Length);
            for (int i = 0; i < pool.Length; i++)
                indexes.Add(i);

            for (int i = indexes.Count - 1; i > 0; i--)
            {
                int swap = random.Next(i + 1);
                int value = indexes[i];
                indexes[i] = indexes[swap];
                indexes[swap] = value;
            }

            var result = new List<string>(count);
            for (int i = 0; i < count && i < indexes.Count; i++)
                result.Add(pool[indexes[i]]);
            return result;
        }
    }
}
