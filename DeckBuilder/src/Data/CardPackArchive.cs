using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// The archive reader used by <see cref="CardPackInstaller"/>. ZIP stays on
    /// System.IO.Compression so the existing GBK file-name fallback and ZIP
    /// behavior do not change; RAR and 7z are handed to the vendored
    /// SharpCompress 0.24 build (<c>lib\SharpCompress.dll</c>).
    /// </summary>
    internal abstract class CardPackArchive : IDisposable
    {
        private static readonly string[] Extensions = { ".zip", ".rar", ".7z" };

        public abstract int Count { get; }

        public abstract CardPackEntry this[int index] { get; }

        public abstract void Dispose();

        public static bool IsSupported(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string extension;
            try
            {
                extension = Path.GetExtension(path);
            }
            catch (ArgumentException)
            {
                return false;
            }

            for (int i = 0; i < Extensions.Length; i++)
            {
                if (string.Equals(extension, Extensions[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static CardPackArchive Open(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".zip",
                StringComparison.OrdinalIgnoreCase))
            {
                return new ZipCardPackArchive(path);
            }

            return new SharpCompressCardPackArchive(path);
        }
    }

    /// <summary>
    /// One file inside a card pack. The installer only needs the entry name and
    /// a readable stream, so the ZIP and SharpCompress entries share this
    /// wrapper instead of leaking either API into the install logic.
    /// </summary>
    internal sealed class CardPackEntry
    {
        private readonly ZipArchiveEntry _zipEntry;
        private readonly IArchiveEntry _compressedEntry;
        private readonly string _stagedFullName;
        private readonly string _stagedPath;

        private CardPackEntry(ZipArchiveEntry zipEntry,
            IArchiveEntry compressedEntry, string stagedFullName,
            string stagedPath)
        {
            _zipEntry = zipEntry;
            _compressedEntry = compressedEntry;
            _stagedFullName = stagedFullName;
            _stagedPath = stagedPath;
        }

        public static CardPackEntry FromZip(ZipArchiveEntry entry)
        {
            return new CardPackEntry(entry, null, null, null);
        }

        public static CardPackEntry FromCompressed(IArchiveEntry entry)
        {
            return new CardPackEntry(null, entry, null, null);
        }

        /// <summary>
        /// A file that was already unpacked from a solid archive. The original
        /// entry key is kept for installation while the stream comes from the
        /// temporary file, because solid archives can only be decompressed in
        /// their own sequential order.
        /// </summary>
        public static CardPackEntry FromStaged(string fullName, string path)
        {
            return new CardPackEntry(null, null, fullName, path);
        }

        public string FullName
        {
            get
            {
                if (_zipEntry != null)
                    return _zipEntry.FullName;
                if (_compressedEntry != null)
                    return _compressedEntry.Key;
                return _stagedFullName;
            }
        }

        public bool IsDirectory
        {
            get
            {
                if (_zipEntry != null)
                    return string.IsNullOrEmpty(_zipEntry.Name);
                if (_compressedEntry != null)
                    return _compressedEntry.IsDirectory;
                return false;
            }
        }

        public Stream Open()
        {
            if (_zipEntry != null)
                return _zipEntry.Open();
            if (_compressedEntry != null)
                return _compressedEntry.OpenEntryStream();
            return File.OpenRead(_stagedPath);
        }
    }

    internal sealed class ZipCardPackArchive : CardPackArchive
    {
        private readonly ZipArchive _archive;
        private readonly List<CardPackEntry> _entries;

        public ZipCardPackArchive(string path)
        {
            _archive = OpenZip(path);
            try
            {
                _entries = new List<CardPackEntry>(_archive.Entries.Count);
                for (int i = 0; i < _archive.Entries.Count; i++)
                    _entries.Add(CardPackEntry.FromZip(_archive.Entries[i]));
            }
            catch
            {
                _archive.Dispose();
                throw;
            }
        }

        public override int Count
        {
            get { return _entries.Count; }
        }

        public override CardPackEntry this[int index]
        {
            get { return _entries[index]; }
        }

        public override void Dispose()
        {
            _archive.Dispose();
        }

        private static ZipArchive OpenZip(string path)
        {
            // Legacy Chinese packs write file names in GBK without setting the
            // UTF-8 flag. Trying code page 936 first keeps those names intact;
            // the flag-aware reader is the fallback when the code page is not
            // available on this machine.
            try
            {
                return ZipFile.Open(path, ZipArchiveMode.Read,
                    Encoding.GetEncoding(936));
            }
            catch (ArgumentException)
            {
                return ZipFile.OpenRead(path);
            }
            catch (NotSupportedException)
            {
                return ZipFile.OpenRead(path);
            }
        }
    }


    internal sealed class SharpCompressCardPackArchive : CardPackArchive
    {
        private readonly IArchive _archive;
        private readonly List<CardPackEntry> _entries;
        private string _tempDirectory;

        public SharpCompressCardPackArchive(string path)
        {
            var options = new ReaderOptions();
            try
            {
                options.ArchiveEncoding = new ArchiveEncoding
                {
                    Default = Encoding.GetEncoding(936)
                };
            }
            catch (ArgumentException)
            {
                // Code page 936 is unavailable on this machine. SharpCompress
                // then keeps its own UTF-8/Unicode handling.
            }

            try
            {
                _archive = ArchiveFactory.Open(path, options);
                _entries = new List<CardPackEntry>();

                if (_archive.IsSolid)
                {
                    StageSolidEntries();
                    return;
                }

                // Non-solid archives can be read entry by entry. Keep the entry
                // objects so the installer may read the same file more than once.
                foreach (IArchiveEntry entry in _archive.Entries)
                {
                    if (!entry.IsDirectory)
                        _entries.Add(CardPackEntry.FromCompressed(entry));
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public override int Count
        {
            get { return _entries.Count; }
        }

        public override CardPackEntry this[int index]
        {
            get { return _entries[index]; }
        }

        public override void Dispose()
        {
            try
            {
                if (_archive != null)
                    _archive.Dispose();
            }
            finally
            {
                DeleteTempDirectory();
            }
        }

        /// <summary>
        /// Solid RAR archives reject <c>OpenEntryStream</c> and must be
        /// decompressed in one pass. Stage every file to a temporary directory
        /// in archive order so the installer can still validate first and then
        /// extract the selected files. Files are named by index, not by entry
        /// path, so hostile entry names cannot escape the staging directory.
        /// </summary>
        private void StageSolidEntries()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(),
                "WeissDeckBuilder", "pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);

            int index = 0;
            using (IReader reader = _archive.ExtractAllEntries())
            {
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory)
                        continue;

                    string stagedPath = Path.Combine(_tempDirectory,
                        index.ToString("D8"));
                    using (var output = new FileStream(stagedPath,
                        FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        reader.WriteEntryTo(output);
                    }

                    _entries.Add(CardPackEntry.FromStaged(
                        reader.Entry.Key, stagedPath));
                    index++;
                }
            }
        }

        private void DeleteTempDirectory()
        {
            if (_tempDirectory == null)
                return;

            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch (IOException)
            {
                // A caller may still hold an entry stream open. The next run
                // can clean the temporary folder up; installation already
                // finished by this point.
            }
            catch (UnauthorizedAccessException)
            {
            }

            _tempDirectory = null;
        }
    }
}

