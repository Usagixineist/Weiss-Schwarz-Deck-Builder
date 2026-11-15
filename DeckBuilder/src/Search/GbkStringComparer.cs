using System;
using System.Collections.Generic;
using System.Text;

namespace WeissDeckBuilder.Search
{
    /// <summary>
    /// Mirrors CardLibrary.SerieCompare: each UTF-16 character is encoded to
    /// GBK, and two-byte characters are compared as (first &lt;&lt; 8) + second.
    /// This is intentionally not a normal string comparison.
    /// </summary>
    public sealed class GbkStringComparer : IComparer<string>
    {
        public static readonly GbkStringComparer Instance = new GbkStringComparer();
        private static Encoding _gbk;

        private GbkStringComparer()
        {
        }

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;

            int length = Math.Min(x.Length, y.Length);
            for (int i = 0; i < length; i++)
            {
                int left = GetGbkValue(x[i]);
                int right = GetGbkValue(y[i]);
                if (left < right)
                    return -1;
                if (left > right)
                    return 1;
            }

            return x.Length.CompareTo(y.Length);
        }

        private static int GetGbkValue(char value)
        {
            Encoding encoding = GetEncoding();
            byte[] bytes = encoding.GetBytes(value.ToString());
            if (bytes.Length >= 2)
                return (bytes[0] << 8) + bytes[1];
            if (bytes.Length == 1)
                return bytes[0];
            return value;
        }

        private static Encoding GetEncoding()
        {
            if (_gbk != null)
                return _gbk;

            try
            {
                _gbk = Encoding.GetEncoding("gbk");
            }
            catch (ArgumentException)
            {
                try
                {
                    _gbk = Encoding.GetEncoding(936);
                }
                catch (ArgumentException)
                {
                    _gbk = Encoding.Default;
                }
            }
            catch (NotSupportedException)
            {
                _gbk = Encoding.Default;
            }

            return _gbk;
        }
    }
}
