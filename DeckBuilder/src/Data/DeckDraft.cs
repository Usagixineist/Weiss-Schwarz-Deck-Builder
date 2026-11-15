using System;
using System.Collections.Generic;

namespace WeissDeckBuilder.Data
{
    /// <summary>
    /// One card code of a <see cref="DeckDraft"/> together with the number of
    /// copies the player already placed into the new deck. The tray under the
    /// card grid draws one image per entry and prints <see cref="Count"/> in
    /// its corner, so duplicates collapse like they do in the 缩略 view.
    /// </summary>
    public sealed class DeckDraftCard
    {
        public string Code { get; private set; }
        public int Count { get; private set; }

        public DeckDraftCard(string code, int count)
        {
            Code = code ?? string.Empty;
            Count = count < 1 ? 1 : count;
        }

        internal DeckDraftCard WithCount(int count)
        {
            return new DeckDraftCard(Code, count);
        }

        public override string ToString()
        {
            return Count > 1 ? Code + " x" + Count : Code;
        }
    }

    /// <summary>
    /// The deck that is being assembled on the 编辑卡组 page. It only tracks
    /// card codes and copy counts: image lookup, painting and the rules that
    /// are not enforced yet stay in the UI, which keeps this model small
    /// enough for the smoke tests.
    /// </summary>
    public sealed class DeckDraft
    {
        /// <summary>Name the tray shows until a real deck name is set.</summary>
        public const string DefaultName = "新建卡组";

        /// <summary>
        /// Size a constructed Weiss Schwarz deck should have. It is the number
        /// the tray counts against; going past it is allowed so a player can
        /// draft a few candidates, the counter simply turns red.
        /// </summary>
        public const int MaxCards = 50;

        private readonly List<DeckDraftCard> _cards;

        public DeckDraft()
        {
            _cards = new List<DeckDraftCard>();
            Name = DefaultName;
        }

        /// <summary>Deck name, shown on the tab of the new deck tray.</summary>
        public string Name { get; set; }

        /// <summary>
        /// One entry per distinct code, in the order the player added them so
        /// the tray never reshuffles while cards are dropped in.
        /// </summary>
        public IList<DeckDraftCard> Cards
        {
            get { return _cards; }
        }

        /// <summary>Total number of cards, duplicates included.</summary>
        public int CardCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _cards.Count; i++)
                    total += _cards[i].Count;
                return total;
            }
        }

        public bool IsEmpty
        {
            get { return _cards.Count == 0; }
        }

        /// <summary>
        /// True once the draft reached the 50 张 target. Kept for the tray,
        /// which colours the counter green at exactly this point.
        /// </summary>
        public bool IsFull
        {
            get { return CardCount >= MaxCards; }
        }

        /// <summary>Copies of one code already in the draft, 0 when absent.</summary>
        public int CountOf(string code)
        {
            int index = IndexOf(code);
            return index < 0 ? 0 : _cards[index].Count;
        }

        /// <summary>
        /// Adds one copy of <paramref name="code"/>. Returns false for a blank
        /// code only: the 50 张 size is a target, not a hard limit, so the tray
        /// can hold as many cards as the player drops in.
        /// </summary>
        public bool Add(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Trim().Length == 0)
                return false;

            int index = IndexOf(code);
            if (index >= 0)
            {
                DeckDraftCard existing = _cards[index];
                _cards[index] = existing.WithCount(existing.Count + 1);
                return true;
            }

            _cards.Add(new DeckDraftCard(code.Trim(), 1));
            return true;
        }

        /// <summary>
        /// Removes one copy of <paramref name="code"/>, dropping the entry once
        /// its last copy is gone. Returns false when the draft has no such card.
        /// </summary>
        public bool Remove(string code)
        {
            int index = IndexOf(code);
            if (index < 0)
                return false;

            DeckDraftCard existing = _cards[index];
            if (existing.Count > 1)
                _cards[index] = existing.WithCount(existing.Count - 1);
            else
                _cards.RemoveAt(index);
            return true;
        }

        /// <summary>Empties the draft but keeps the name the player set.</summary>
        public void Clear()
        {
            _cards.Clear();
        }

        /// <summary>
        /// Sorts the entries by card code, the order the deck files use, so
        /// 整理 and every save produce the same readable sequence.
        /// </summary>
        public void Sort()
        {
            _cards.Sort(delegate(DeckDraftCard left, DeckDraftCard right)
            {
                return StringComparer.OrdinalIgnoreCase.Compare(
                    left.Code, right.Code);
            });
        }

        /// <summary>
        /// Every copy as a plain code list, the shape the deck loader and the
        /// statistics panel already understand.
        /// </summary>
        public IList<string> ToCardCodes()
        {
            var codes = new List<string>(CardCount);
            for (int i = 0; i < _cards.Count; i++)
            {
                DeckDraftCard card = _cards[i];
                for (int copy = 0; copy < card.Count; copy++)
                    codes.Add(card.Code);
            }
            return codes;
        }

        private int IndexOf(string code)
        {
            if (string.IsNullOrEmpty(code))
                return -1;

            string key = code.Trim();
            for (int i = 0; i < _cards.Count; i++)
            {
                if (string.Equals(_cards[i].Code, key,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
