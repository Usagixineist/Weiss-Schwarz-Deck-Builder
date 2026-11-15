using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// One staged deck ("暂存卡组") read back from StreamingAssets\DeckDrafts.
    /// The file keeps the same plain code list a deck file uses plus a
    /// <c>#name</c> header, so the deck name typed into the tray survives a
    /// restart without turning the codes unreadable.
    /// </summary>
    public sealed class DeckDraftSlot
    {
        private readonly List<string> _cardCodes;

        public DeckDraftSlot(int index, string deckName, string filePath,
            IList<string> cardCodes)
        {
            Index = index;
            DeckName = string.IsNullOrEmpty(deckName)
                ? DeckDraft.DefaultName
                : deckName;
            FilePath = filePath ?? string.Empty;
            _cardCodes = new List<string>();
            if (cardCodes != null)
                _cardCodes.AddRange(cardCodes);
        }

        /// <summary>Slot number, 1 to <see cref="DeckDraftStore.SlotCount"/>.</summary>
        public int Index { get; private set; }

        /// <summary>Deck name the tray carried when the deck was staged.</summary>
        public string DeckName { get; private set; }

        public string FilePath { get; private set; }

        /// <summary>Card codes in file order, duplicates included.</summary>
        public IList<string> CardCodes
        {
            get { return _cardCodes; }
        }

        public int CardCount
        {
            get { return _cardCodes.Count; }
        }

        public override string ToString()
        {
            return DeckDraftStore.SlotCaption(Index);
        }
    }

    /// <summary>
    /// Stores the decks the 编辑卡组 band stages instead of saving. They live in
    /// StreamingAssets\DeckDrafts, a folder next to Decks, so the game (and the
    /// deck sync plugin) never picks them up: only 保存 writes into Decks.
    /// There are five fixed slots and the file names are fixed too, so staging
    /// never asks the player for a name.
    /// </summary>
    public sealed class DeckDraftStore
    {
        /// <summary>Folder holding the staged decks, next to <c>Decks</c>.</summary>
        public const string LocalFolderName = "DeckDrafts";

        /// <summary>Number of staged decks the band can keep.</summary>
        public const int SlotCount = 5;

        /// <summary>Caption prefix of a slot in the 导入卡组 window.</summary>
        public const string SlotCaptionPrefix = "暂存卡组";

        /// <summary>First line of a staged deck: the name of the tray.</summary>
        public const string NameHeaderPrefix = "#name ";

        private readonly string _dataRoot;

        public DeckDraftStore(string dataRoot)
        {
            _dataRoot = dataRoot ?? string.Empty;
        }

        public string DataRoot
        {
            get { return _dataRoot; }
        }

        public string Root
        {
            get { return Path.Combine(_dataRoot, LocalFolderName); }
        }

        /// <summary>Fixed file name of a slot, e.g. 新建卡组3.txt.</summary>
        public static string GetSlotFileName(int index)
        {
            if (index < 1 || index > SlotCount)
                return string.Empty;
            return DeckDraft.DefaultName + index + DeckLibrary.DeckExtension;
        }

        /// <summary>Full path of a slot, null when the index is out of range.</summary>
        public string GetSlotPath(int index)
        {
            string fileName = GetSlotFileName(index);
            return fileName.Length == 0 ? null : Path.Combine(Root, fileName);
        }

        /// <summary>Caption the 导入卡组 window prints under a slot.</summary>
        public static string SlotCaption(int index)
        {
            return SlotCaptionPrefix + " " + index;
        }

        /// <summary>
        /// Every staged deck that currently exists, slot 1 first. Slots that
        /// were never used are simply absent, which is what the 导入卡组 window
        /// needs: an empty slot is not drawn.
        /// </summary>
        public IList<DeckDraftSlot> Load()
        {
            var slots = new List<DeckDraftSlot>();
            for (int index = 1; index <= SlotCount; index++)
            {
                DeckDraftSlot slot = Read(index);
                if (slot != null)
                    slots.Add(slot);
            }
            return slots;
        }

        /// <summary>Reads one slot, null when the file is missing or unreadable.</summary>
        public DeckDraftSlot Read(int index)
        {
            string path = GetSlotPath(index);
            if (path == null || !File.Exists(path))
                return null;

            try
            {
                string name;
                IList<string> codes;
                if (!TryParse(File.ReadAllText(path, Encoding.UTF8),
                    out name, out codes) || codes.Count == 0)
                {
                    return null;
                }
                return new DeckDraftSlot(index, name, path, codes);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Number of the first unused slot, 0 when all five are taken. The
        /// band uses it so 暂存 never overwrites another staged deck.
        /// </summary>
        public static int FirstFreeSlot(IList<DeckDraftSlot> slots)
        {
            for (int index = 1; index <= SlotCount; index++)
            {
                bool taken = false;
                if (slots != null)
                {
                    for (int i = 0; i < slots.Count; i++)
                    {
                        if (slots[i] != null && slots[i].Index == index)
                        {
                            taken = true;
                            break;
                        }
                    }
                }

                if (!taken)
                    return index;
            }
            return 0;
        }

        /// <summary>
        /// Writes one staged deck into its fixed slot file. Returns false with
        /// a message instead of throwing, because the band reports the failure
        /// in the status line.
        /// </summary>
        public bool Save(int index, string deckName, IList<string> cardCodes,
            out string path, out string error)
        {
            path = GetSlotPath(index);
            error = null;
            if (path == null)
            {
                error = "暂存位不存在: " + index;
                return false;
            }
            if (cardCodes == null || cardCodes.Count == 0)
            {
                error = "卡组里没有卡片";
                path = null;
                return false;
            }

            try
            {
                if (!Directory.Exists(Root))
                    Directory.CreateDirectory(Root);

                File.WriteAllText(path, BuildFileContent(deckName, cardCodes),
                    new UTF8Encoding(false));
                return true;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (NotSupportedException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Removes one staged deck, true when a file was deleted.</summary>
        public bool Delete(int index)
        {
            string path = GetSlotPath(index);
            if (path == null || !File.Exists(path))
                return false;

            try
            {
                File.Delete(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// File body of a staged deck: the optional <c>#name</c> header and one
        /// code per line — the layout <see cref="DeckLibrary.ParseCardCodes"/>
        /// already understands.
        /// </summary>
        public static string BuildFileContent(string deckName,
            IList<string> cardCodes)
        {
            var builder = new StringBuilder();
            string name = (deckName ?? string.Empty).Trim();
            if (name.Length > 0)
                builder.Append(NameHeaderPrefix).Append(name).Append("\r\n");

            if (cardCodes != null)
            {
                for (int i = 0; i < cardCodes.Count; i++)
                    builder.Append(cardCodes[i]).Append("\r\n");
            }
            return builder.ToString();
        }

        /// <summary>
        /// Reads a staged deck file back. A missing header falls back to the
        /// default deck name, so a hand edited file still loads.
        /// </summary>
        public static bool TryParse(string text, out string deckName,
            out IList<string> cardCodes)
        {
            deckName = DeckDraft.DefaultName;
            cardCodes = new List<string>();
            if (text == null)
                return false;

            string[] lines = SplitLines(text);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim().Trim('\uFEFF').Trim();
                if (!line.StartsWith(NameHeaderPrefix, StringComparison.Ordinal))
                    continue;

                string name = line.Substring(NameHeaderPrefix.Length).Trim();
                if (name.Length > 0)
                    deckName = name;
                break;
            }

            cardCodes = DeckLibrary.ParseCardCodes(lines);
            return true;
        }

        /// <summary>
        /// Card codes of a pasted 卡组代码 block. Every line is one code, and
        /// blank lines, comments and text that cannot be a code are dropped,
        /// exactly like a deck file.
        /// </summary>
        public static IList<string> ParseCodes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();
            return DeckLibrary.ParseCardCodes(SplitLines(text));
        }

        private static string[] SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
        }
    }
}
