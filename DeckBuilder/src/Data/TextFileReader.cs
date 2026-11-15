using System;
using System.IO;
using System.Text;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// Opens the game's text files with the encoding they were saved in. Most
    /// files are UTF-8, but a few older sets (for example Haruhi/WP02) are GBK
    /// (code page 936). Reading those as UTF-8 turns every Chinese name into
    /// replacement characters, so a byte order mark wins, otherwise the buffer
    /// is validated as UTF-8 and GBK is used as the fallback.
    /// </summary>
    public static class TextFileReader
    {
        private static Encoding _gbk;

        public static StreamReader Open(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return Open(bytes);
        }

        public static StreamReader Open(byte[] bytes)
        {
            return new StreamReader(
                new MemoryStream(bytes, false),
                DetectEncoding(bytes),
                true);
        }

        public static Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return Encoding.UTF8;

            if (bytes.Length >= 3 &&
                bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            return IsValidUtf8(bytes) ? Encoding.UTF8 : GetGbk();
        }

        private static bool IsValidUtf8(byte[] bytes)
        {
            try
            {
                var strict = new UTF8Encoding(false, true);
                strict.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static Encoding GetGbk()
        {
            if (_gbk != null)
                return _gbk;

            try
            {
                _gbk = Encoding.GetEncoding(936);
            }
            catch (ArgumentException)
            {
                try
                {
                    _gbk = Encoding.GetEncoding("gbk");
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
