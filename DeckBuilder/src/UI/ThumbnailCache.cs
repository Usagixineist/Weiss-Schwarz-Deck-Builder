using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// LRU cache for resized card/cover thumbnails. Original images are never
    /// kept alive; only the small bitmaps used by the tile grid are cached.
    /// </summary>
    public sealed class ThumbnailCache : IDisposable
    {
        private sealed class CacheEntry
        {
            public string Key;
            public Image Image;
        }

        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entries;
        private readonly LinkedList<CacheEntry> _lru;
        private readonly object _sync;

        /// <summary>
        /// Decode statistics: how many images were resized and how long that
        /// took. Only the -profile log reads them; they cost two timestamp
        /// reads per decode, which is nothing next to the resize itself.
        /// </summary>
        private long _decodeCount;
        private long _decodeTicks;

        public ThumbnailCache(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            _entries = new Dictionary<string, LinkedListNode<CacheEntry>>(
                StringComparer.OrdinalIgnoreCase);
            _lru = new LinkedList<CacheEntry>();
            _sync = new object();
        }

        /// <summary>Images resized so far; cache hits are not counted.</summary>
        public long DecodeCount
        {
            get { return _decodeCount; }
        }

        /// <summary>Time spent resizing images so far, in milliseconds.</summary>
        public double DecodeMilliseconds
        {
            get { return _decodeTicks * 1000.0 / Stopwatch.Frequency; }
        }

        public Image GetOrLoad(
            string path,
            int width,
            int height,
            bool cropToFill)
        {
            if (string.IsNullOrEmpty(path) || width <= 0 || height <= 0)
                return null;

            string key = path + "|" + width + "x" + height +
                (cropToFill ? "|cover" : "|fit");

            lock (_sync)
            {
                LinkedListNode<CacheEntry> node;
                if (_entries.TryGetValue(key, out node))
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    return node.Value.Image;
                }
            }

            long decodeStart = Stopwatch.GetTimestamp();
            Image loaded = LoadAndResize(path, width, height, cropToFill);
            _decodeCount++;
            _decodeTicks += Stopwatch.GetTimestamp() - decodeStart;
            if (loaded == null)
                return null;

            lock (_sync)
            {
                LinkedListNode<CacheEntry> node;
                if (_entries.TryGetValue(key, out node))
                {
                    // Another paint could have loaded the same key while this
                    // thread was decoding. Keep the existing bitmap.
                    loaded.Dispose();
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    return node.Value.Image;
                }

                var entry = new CacheEntry();
                entry.Key = key;
                entry.Image = loaded;
                node = _lru.AddFirst(entry);
                _entries[key] = node;

                while (_entries.Count > _capacity)
                {
                    LinkedListNode<CacheEntry> last = _lru.Last;
                    if (last == null)
                        break;

                    _lru.RemoveLast();
                    _entries.Remove(last.Value.Key);
                    last.Value.Image.Dispose();
                }
            }

            return loaded;
        }

        private static Image LoadAndResize(
            string path,
            int width,
            int height,
            bool cropToFill)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite))
                using (Image source = Image.FromStream(stream, true, true))
                {
                    var bitmap = new Bitmap(width, height,
                        PixelFormat.Format32bppPArgb);
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.White);
                        // The card grid draws dozens of 140x196 tiles per frame,
                        // where bilinear is ~30 % cheaper than bicubic and looks
                        // the same at this size. Covers and the large detail view
                        // keep bicubic.
                        graphics.InterpolationMode = cropToFill
                            ? InterpolationMode.HighQualityBicubic
                            : InterpolationMode.HighQualityBilinear;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;

                        if (cropToFill)
                        {
                            float scale = Math.Max(
                                (float)width / source.Width,
                                (float)height / source.Height);
                            float sourceWidth = width / scale;
                            float sourceHeight = height / scale;
                            float sourceX = (source.Width - sourceWidth) / 2F;
                            float sourceY = (source.Height - sourceHeight) / 2F;
                            graphics.DrawImage(source,
                                new Rectangle(0, 0, width, height),
                                sourceX, sourceY, sourceWidth, sourceHeight,
                                GraphicsUnit.Pixel);
                        }
                        else
                        {
                            float scale = Math.Min(
                                (float)width / source.Width,
                                (float)height / source.Height);
                            int drawWidth = Math.Max(1,
                                (int)Math.Round(source.Width * scale));
                            int drawHeight = Math.Max(1,
                                (int)Math.Round(source.Height * scale));
                            int x = (width - drawWidth) / 2;
                            int y = (height - drawHeight) / 2;
                            graphics.DrawImage(source, new Rectangle(
                                x, y, drawWidth, drawHeight));
                        }
                    }
                    return bitmap;
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (ExternalException)
            {
                return null;
            }
            catch (OutOfMemoryException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                foreach (CacheEntry entry in _lru)
                    entry.Image.Dispose();
                _lru.Clear();
                _entries.Clear();
            }
        }
    }
}

