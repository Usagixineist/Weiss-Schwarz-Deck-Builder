using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using WeissDeckBuilder.Models;
using WeissDeckBuilder.Search;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// Owner-drawn modal overlay that shows a card's artwork and every field
    /// available in <see cref="WeissCard"/>. Clicking outside the popup, the
    /// close button, or pressing Escape dismisses it.
    /// </summary>
    internal sealed class CardDetailOverlay : UserControl
    {
        private const int MaxPopupWidth = 1240;
        private const int MaxPopupHeight = 720;
        private const int PopupPadding = 18;
        private const int ColumnGap = 24;
        private const int InfoBoxHeight = 52;
        private const int InfoBoxCount = 8;
        private const int InfoColumns = 8;
        private const int PopupCornerRadius = 16;
        private const int EffectPadding = 14;
        private const int CardAspectWidth = 400;
        private const int CardAspectHeight = 559;

        private const int ImageFrameMargin = 16;
        private const int ImageFrameInset = 8;
        private const int NavButtonSize = 30;
        private const int NavButtonGap = 14;
        private const int SectionTitleHeight = 22;
        private const int SectionTitleGap = 6;
        private const int CardCellWidth = 78;
        private const int CardCellGap = 10;
        private const int CardNameHeight = 36;
        private const int CardImageGap = 4;
        private const int StripIndicatorHeight = 6;
        private const int BottomBandGap = 14;

        private const TextFormatFlags WrapFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        private const TextFormatFlags SingleLineFlags =
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis;
        private const TextFormatFlags MeasureFlags =
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        private const TextFormatFlags CenteredFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix;

        private enum SelectionArea
        {
            None,
            Eyebrow,
            Title,
            Effect
        }

        private sealed class TextLine
        {
            public int Start;
            public string Text;
            public int[] Offsets;
        }

        private sealed class ContentLayout
        {
            public Rectangle Eyebrow;
            public Rectangle Title;
            public Font TitleFont;
            public Rectangle[] InfoBoxes = new Rectangle[InfoBoxCount];
            public Rectangle InfoArea;
            public Rectangle Traits;
            public Rectangle EffectTitle;
            public Rectangle EffectBox;
            public Rectangle EffectViewport;
            public Rectangle AltTitle;
            public Rectangle AltStrip;
            public Rectangle AltIndicator;
            public Rectangle RelatedTitle;
            public Rectangle RelatedStrip;
            public Rectangle RelatedIndicator;
            public int BandDividerX;
            public bool HasAltSection;
            public bool HasRelatedSection;
            public bool HasBottomBand;
            public int SeparatorY;
            public int EffectTextHeight;
        }

        private readonly ThumbnailCache _thumbnails;
        private readonly Font _titleFont;
        private readonly Font _titleMediumFont;
        private readonly Font _titleSmallFont;
        private readonly Font _titleTinyFont;
        private readonly Font _nameFont;
        private readonly Font _sectionFont;
        private readonly Font _labelFont;
        private readonly Font _valueFont;
        private readonly Font _valueSmallFont;
        private readonly Font _bodyFont;
        private readonly Font _bodySmallFont;
        private readonly Font _stripNameFont;
        private readonly Font _hintFont;
        private readonly SolidBrush _panelBrush;
        private readonly SolidBrush _imageBackBrush;
        private readonly SolidBrush _imageFrameBrush;
        private readonly SolidBrush _boxBrush;
        private readonly SolidBrush _selectionBrush;
        private readonly SolidBrush _hoverBrush;
        private readonly Pen _borderPen;
        private readonly Pen _dividerPen;

        private WeissCard _card;
        private Bitmap _backdrop;
        private Image _cardImage;
        private ContentLayout _content;
        private Rectangle _popupBounds;
        private Rectangle _imageFrame;
        private Rectangle _imageBounds;
        private Rectangle _contentBounds;
        private Rectangle _closeBounds;
        private Rectangle _prevBounds;
        private Rectangle _nextBounds;
        private List<WeissCard> _altCards;
        private List<WeissCard> _relatedCards;
        private List<TextLine> _effectLines;
        private int _effectScroll;
        private int _effectMaxScroll;
        private int _altScroll;
        private int _altMaxScroll;
        private int _relatedScroll;
        private int _relatedMaxScroll;
        private SelectionArea _selectionArea;
        private int _selectionStart;
        private int _selectionEnd;
        private bool _selecting;
        private int _dragStrip;
        private int _dragStartX;
        private int _dragStartScroll;
        private bool _dragMoved;
        private WeissCard _hoverCard;
        private int _hoverSection;
        private bool _closeHover;
        private bool _prevHover;
        private bool _nextHover;
        private WeissCard _prevCard;
        private WeissCard _nextCard;

        public event EventHandler CloseRequested;

        /// <summary>
        /// Resolves a card name (as written inside 「」, “” or "" in an effect
        /// text) to the cards of the series that is currently open. The second
        /// argument is the folder of the open card; matches from that folder
        /// are preferred when several cards share the same name.
        /// </summary>
        public Func<string, string, IList<WeissCard>> NameResolver { get; set; }

        /// <summary>
        /// Returns the card that is <paramref name="step"/> entries away from
        /// the given card (-1 = previous, 1 = next) in the card list, or null
        /// when there is no such card.
        /// </summary>
        public Func<WeissCard, int, WeissCard> CardNavigation { get; set; }

        public CardDetailOverlay(ThumbnailCache thumbnails)
        {
            if (thumbnails == null)
                throw new ArgumentNullException("thumbnails");

            _thumbnails = thumbnails;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);

            BackColor = AppTheme.MainBackground;
            TabStop = true;
            Visible = false;

            _titleFont = CreateFont(20F, FontStyle.Bold);
            _titleMediumFont = CreateFont(18F, FontStyle.Bold);
            _titleSmallFont = CreateFont(16F, FontStyle.Bold);
            _titleTinyFont = CreateFont(14F, FontStyle.Bold);
            _nameFont = CreateFont(10.5F, FontStyle.Regular);
            _sectionFont = CreateFont(10.5F, FontStyle.Bold);
            _labelFont = CreateFont(8.5F, FontStyle.Regular);
            _valueFont = CreateFont(13.5F, FontStyle.Bold);
            _valueSmallFont = CreateFont(10F, FontStyle.Bold);
            _bodyFont = CreateFont(11F, FontStyle.Regular);
            _bodySmallFont = CreateFont(9.5F, FontStyle.Regular);
            _stripNameFont = CreateFont(8.5F, FontStyle.Regular);
            _hintFont = CreateFont(8.5F, FontStyle.Regular);

            _panelBrush = new SolidBrush(Color.FromArgb(23, 32, 49));
            _imageBackBrush = new SolidBrush(Color.FromArgb(10, 17, 31));
            _imageFrameBrush = new SolidBrush(Color.FromArgb(5, 8, 15));
            _boxBrush = new SolidBrush(Color.FromArgb(17, 27, 43));
            _selectionBrush = new SolidBrush(Color.FromArgb(96, 59, 130, 246));
            _hoverBrush = new SolidBrush(Color.FromArgb(38, 96, 165, 250));
            _borderPen = new Pen(Color.FromArgb(36, 48, 66), 1F);
            _dividerPen = new Pen(AppTheme.Divider);

            _dragStrip = -1;
            _hoverSection = -1;
        }

        public void ShowCard(WeissCard card, Bitmap backdrop)
        {
            if (card == null)
                return;

            ReplaceBackdrop(backdrop);
            NavigateTo(card);
        }

        public void Clear()
        {
            _card = null;
            _cardImage = null;
            _altCards = null;
            _relatedCards = null;
            _effectLines = null;
            _effectScroll = 0;
            _effectMaxScroll = 0;
            _altScroll = 0;
            _relatedScroll = 0;
            _altMaxScroll = 0;
            _relatedMaxScroll = 0;
            _selectionArea = SelectionArea.None;
            _selectionStart = 0;
            _selectionEnd = 0;
            _selecting = false;
            _dragStrip = -1;
            _dragMoved = false;
            _hoverCard = null;
            _hoverSection = -1;
            _closeHover = false;
            _prevHover = false;
            _nextHover = false;
            _prevCard = null;
            _nextCard = null;
            ReplaceBackdrop(null);
            Invalidate();
        }

        private void NavigateTo(WeissCard card)
        {
            if (card == null)
                return;

            _card = card;
            _effectScroll = 0;
            _altScroll = 0;
            _relatedScroll = 0;
            _selectionArea = SelectionArea.None;
            _selectionStart = 0;
            _selectionEnd = 0;
            _selecting = false;
            _dragStrip = -1;
            _dragMoved = false;
            _hoverCard = null;
            _hoverSection = -1;
            _prevHover = false;
            _nextHover = false;
            LoadCardSections();
            UpdateNavigationTargets();
            RecalculateLayout();
            LoadCardImage();
            Invalidate();
        }

        private void UpdateNavigationTargets()
        {
            _prevCard = ResolveNavigation(-1);
            _nextCard = ResolveNavigation(1);
        }

        private WeissCard ResolveNavigation(int step)
        {
            if (CardNavigation == null || _card == null)
                return null;

            try
            {
                return CardNavigation(_card, step);
            }
            catch
            {
                return null;
            }
        }

        private void LoadCardSections()
        {
            _altCards = new List<WeissCard>();
            _relatedCards = new List<WeissCard>();
            if (_card == null)
                return;

            WeissCard baseCard = _card.IsClone ? _card.CloneOf : _card;
            if (baseCard == null)
                baseCard = _card;

            AddAlternate(baseCard);
            for (int i = 0; i < baseCard.Clones.Count; i++)
                AddAlternate(baseCard.Clones[i]);

            if (NameResolver == null || string.IsNullOrEmpty(_card.Text))
                return;

            List<string> names = CardNameMatcher.ExtractNames(_card.Text);
            for (int i = 0; i < names.Count; i++)
            {
                IList<WeissCard> candidates =
                    NameResolver(names[i], baseCard.FolderPath);
                WeissCard match = PickRelatedCard(candidates, baseCard);
                if (match == null || _relatedCards.Contains(match))
                    continue;
                _relatedCards.Add(match);
            }
        }

        private void AddAlternate(WeissCard card)
        {
            if (card == null || _card == null)
                return;
            if (ReferenceEquals(card, _card))
                return;
            if (string.Equals(card.Id, _card.Id, StringComparison.OrdinalIgnoreCase))
                return;
            _altCards.Add(card);
        }

        /// <summary>
        /// Picks one card of the ordered candidate list. The resolver already
        /// ranks exact matches first and prefers the open card's own set.
        /// </summary>
        private static WeissCard PickRelatedCard(
            IList<WeissCard> candidates, WeissCard baseCard)
        {
            if (candidates == null)
                return null;

            for (int i = 0; i < candidates.Count; i++)
            {
                WeissCard candidate = candidates[i];
                if (candidate == null)
                    continue;

                WeissCard root = candidate.IsClone ? candidate.CloneOf : candidate;
                if (root == null || ReferenceEquals(root, baseCard))
                    continue;

                return root;
            }
            return null;
        }

        private void RecalculateLayout()
        {
            _content = new ContentLayout();
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            int width = Math.Min(MaxPopupWidth,
                Math.Max(360, ClientSize.Width - 96));
            int height = Math.Min(MaxPopupHeight,
                Math.Max(480, ClientSize.Height - 80));
            width = Math.Min(width, Math.Max(320, ClientSize.Width - 24));
            height = Math.Min(height, Math.Max(320, ClientSize.Height - 24));

            _popupBounds = new Rectangle(
                (ClientSize.Width - width) / 2,
                (ClientSize.Height - height) / 2,
                width, height);

            int frameHeight = Math.Max(80,
                _popupBounds.Height - ImageFrameMargin * 2);
            int imageHeight = Math.Max(1, frameHeight - ImageFrameInset * 2);
            int imageWidth = imageHeight * CardAspectWidth / CardAspectHeight;
            int frameWidth = imageWidth + ImageFrameInset * 2;

            int maxLeftWidth = Math.Max(120,
                _popupBounds.Width - PopupPadding - ColumnGap - 460);
            frameWidth = Math.Max(120, Math.Min(frameWidth, maxLeftWidth));

            _imageFrame = new Rectangle(
                _popupBounds.X + ImageFrameMargin,
                _popupBounds.Y + ImageFrameMargin,
                frameWidth, frameHeight);

            int drawWidth = Math.Min(
                Math.Max(1, _imageFrame.Width - ImageFrameInset * 2),
                imageWidth);
            int drawHeight = drawWidth * CardAspectHeight / CardAspectWidth;
            int maxDrawHeight = Math.Max(1,
                _imageFrame.Height - ImageFrameInset * 2);
            if (drawHeight > maxDrawHeight)
            {
                drawHeight = maxDrawHeight;
                drawWidth = Math.Max(1,
                    drawHeight * CardAspectWidth / CardAspectHeight);
            }

            _imageBounds = new Rectangle(
                _imageFrame.X + (_imageFrame.Width - drawWidth) / 2,
                _imageFrame.Y + (_imageFrame.Height - drawHeight) / 2,
                Math.Max(1, drawWidth),
                Math.Max(1, drawHeight));

            int contentLeft = _imageFrame.Right + ColumnGap;
            int contentRight = _popupBounds.Right - PopupPadding;
            _contentBounds = new Rectangle(
                contentLeft,
                _popupBounds.Y + PopupPadding,
                Math.Max(1, contentRight - contentLeft),
                Math.Max(1, _popupBounds.Height - PopupPadding * 2));

            _closeBounds = new Rectangle(
                _contentBounds.Right - 30,
                _contentBounds.Top,
                30, 30);

            int navY = _popupBounds.Y + (_popupBounds.Height - NavButtonSize) / 2;
            int prevX = _popupBounds.X - NavButtonGap - NavButtonSize;
            if (prevX < 6)
                prevX = 6;
            int nextX = _popupBounds.Right + NavButtonGap;
            if (nextX + NavButtonSize > ClientSize.Width - 6)
                nextX = ClientSize.Width - 6 - NavButtonSize;
            _prevBounds = new Rectangle(prevX, navY, NavButtonSize, NavButtonSize);
            _nextBounds = new Rectangle(nextX, navY, NavButtonSize, NavButtonSize);

            if (_card != null)
                BuildContentLayout();
        }

        private void BuildContentLayout()
        {
            int x = _contentBounds.X;
            int y = _contentBounds.Y;
            int width = _contentBounds.Width;

            string title = GetTitle();

            int headerWidth = Math.Max(100, width - 38);
            int eyebrowHeight = _nameFont.Height + 2;
            _content.Eyebrow = new Rectangle(x, y, headerWidth, eyebrowHeight);
            y += eyebrowHeight + 3;

            Font titleFont = SelectTitleFont(title, headerWidth);
            _content.TitleFont = titleFont;
            int titleHeight = titleFont.Height + 2;
            _content.Title = new Rectangle(x, y, headerWidth, titleHeight);
            y += titleHeight;

            _content.SeparatorY = y + 7;
            y += 18;

            int boxWidth = width / InfoColumns;
            for (int i = 0; i < InfoBoxCount; i++)
            {
                int column = i % InfoColumns;
                int row = i / InfoColumns;
                int left = x + column * boxWidth;
                int right = column == InfoColumns - 1
                    ? x + width
                    : left + boxWidth;
                int top = y + row * InfoBoxHeight;
                _content.InfoBoxes[i] = new Rectangle(
                    left, top, Math.Max(1, right - left), InfoBoxHeight);
            }
            _content.InfoArea = new Rectangle(
                x, y, width, InfoBoxHeight);
            y += InfoBoxHeight + 14;

            string traits = _card.TraitsDisplay;
            if (string.IsNullOrEmpty(traits))
                traits = "—";
            int traitLabelWidth = GetTraitLabelWidth();
            int traitHeight = Math.Max(18, MeasureWrappedHeight(
                traits, _bodySmallFont, Math.Max(20, width - traitLabelWidth)));
            _content.Traits = new Rectangle(x, y, width, traitHeight);
            y += traitHeight + 10;

            _content.EffectTitle = new Rectangle(x, y, width, SectionTitleHeight);
            y += SectionTitleHeight + 8;

            int bottom = _contentBounds.Bottom;
            int bandHeight = MeasureBottomBandHeight();
            if (bandHeight > 0 &&
                bottom - y - bandHeight - BottomBandGap < 80)
            {
                bandHeight = 0;
            }

            _content.HasBottomBand = bandHeight > 0;
            int effectBottom = _content.HasBottomBand
                ? bottom - bandHeight - BottomBandGap
                : bottom;

            int effectHeight = Math.Max(44, effectBottom - y);
            _content.EffectBox = new Rectangle(
                x, y, width, Math.Max(1, effectHeight));
            _content.EffectViewport = new Rectangle(
                _content.EffectBox.X + EffectPadding,
                _content.EffectBox.Y + EffectPadding,
                Math.Max(1, _content.EffectBox.Width - EffectPadding * 2 - 6),
                Math.Max(1, _content.EffectBox.Height - EffectPadding * 2));

            if (_content.HasBottomBand)
                BuildBottomBandLayout(effectBottom + BottomBandGap);

            BuildEffectTextLayout();
        }

        private bool HasAltCards
        {
            get { return _altCards != null && _altCards.Count > 0; }
        }

        private bool HasRelatedCards
        {
            get { return _relatedCards != null && _relatedCards.Count > 0; }
        }

        private int MeasureBottomBandHeight()
        {
            if (!HasAltCards && !HasRelatedCards)
                return 0;

            return SectionTitleHeight + SectionTitleGap +
                GetStripHeight() + StripIndicatorHeight;
        }

        private static int GetStripHeight()
        {
            return CardCellWidth * CardAspectHeight / CardAspectWidth +
                CardImageGap + CardNameHeight;
        }

        private void BuildBottomBandLayout(int top)
        {
            int left = _contentBounds.X;
            int width = _contentBounds.Width;
            int bandHeight = MeasureBottomBandHeight();
            bool hasAlt = _content.HasAltSection = HasAltCards;
            bool hasRelated = _content.HasRelatedSection = HasRelatedCards;

            int dividerGap = 18;
            Rectangle altArea;
            Rectangle relatedArea;
            if (hasAlt && hasRelated)
            {
                int altWidth = (width - dividerGap) / 2;
                altArea = new Rectangle(left, top, altWidth, bandHeight);
                relatedArea = new Rectangle(
                    left + altWidth + dividerGap, top,
                    Math.Max(1, width - altWidth - dividerGap), bandHeight);
                _content.BandDividerX = left + altWidth + dividerGap / 2;
            }
            else if (hasAlt)
            {
                altArea = new Rectangle(left, top, width, bandHeight);
                relatedArea = Rectangle.Empty;
            }
            else
            {
                altArea = Rectangle.Empty;
                relatedArea = new Rectangle(left, top, width, bandHeight);
            }

            int stripTop = top + SectionTitleHeight + SectionTitleGap;
            int stripHeight = GetStripHeight();
            _content.AltTitle = new Rectangle(
                altArea.X, top, altArea.Width, SectionTitleHeight);
            _content.AltStrip = new Rectangle(
                altArea.X, stripTop, altArea.Width, stripHeight);
            _content.AltIndicator = new Rectangle(
                altArea.X, stripTop + stripHeight, altArea.Width, 3);
            _content.RelatedTitle = new Rectangle(
                relatedArea.X, top, relatedArea.Width, SectionTitleHeight);
            _content.RelatedStrip = new Rectangle(
                relatedArea.X, stripTop, relatedArea.Width, stripHeight);
            _content.RelatedIndicator = new Rectangle(
                relatedArea.X, stripTop + stripHeight, relatedArea.Width, 3);
        }

        private void BuildEffectTextLayout()
        {
            int textWidth = Math.Max(20,
                _content.EffectViewport.Width - 10);
            _effectLines = BuildLines(GetEffectText(), _bodyFont, textWidth);

            int lineHeight = _bodyFont.Height + 2;
            _content.EffectTextHeight = Math.Max(lineHeight,
                _effectLines.Count * lineHeight);
            _effectMaxScroll = Math.Max(0,
                _content.EffectTextHeight -
                _content.EffectViewport.Height + 4);
            if (_effectScroll > _effectMaxScroll)
                _effectScroll = _effectMaxScroll;
            if (_effectMaxScroll <= 0)
                _effectScroll = 0;

            _altMaxScroll = MaxStripScroll(_content.AltStrip.Width,
                _altCards == null ? 0 : _altCards.Count);
            _relatedMaxScroll = MaxStripScroll(_content.RelatedStrip.Width,
                _relatedCards == null ? 0 : _relatedCards.Count);
            _altScroll = Math.Max(0, Math.Min(_altMaxScroll, _altScroll));
            _relatedScroll = Math.Max(0,
                Math.Min(_relatedMaxScroll, _relatedScroll));
        }

        private static int MaxStripScroll(int stripWidth, int cardCount)
        {
            if (stripWidth <= 0 || cardCount <= 0)
                return 0;
            int contentWidth = cardCount * (CardCellWidth + CardCellGap) -
                CardCellGap;
            return Math.Max(0, contentWidth - stripWidth);
        }

        private static List<TextLine> BuildLines(
            string text, Font font, int width)
        {
            var lines = new List<TextLine>();
            if (string.IsNullOrEmpty(text) || width <= 0)
                return lines;

            int index = 0;
            while (index < text.Length)
            {
                int start = index;
                int cursor = index;
                int lastSpace = -1;
                while (cursor < text.Length &&
                    text[cursor] != '\r' && text[cursor] != '\n')
                {
                    if (text[cursor] == ' ')
                        lastSpace = cursor;

                    int length = cursor - start + 1;
                    if (length > 1 && MeasureWidth(
                        text.Substring(start, length), font) > width)
                    {
                        cursor = lastSpace > start ? lastSpace : cursor;
                        break;
                    }
                    cursor++;
                }

                int end = cursor;
                if (end <= start && text[start] != '\r' && text[start] != '\n')
                    end = Math.Min(text.Length, start + 1);

                lines.Add(CreateLine(
                    text.Substring(start, end - start), start, font));
                index = end;

                if (index < text.Length && text[index] == '\r')
                {
                    index++;
                    if (index < text.Length && text[index] == '\n')
                        index++;
                }
                else if (index < text.Length && text[index] == '\n')
                {
                    index++;
                }

                if (end > start && index < text.Length && text[index] == ' ')
                    index++;
            }

            return lines;
        }

        private static TextLine CreateLine(
            string text, int start, Font font)
        {
            var line = new TextLine();
            line.Start = start;
            line.Text = text ?? string.Empty;
            line.Offsets = BuildOffsets(line.Text, font);
            return line;
        }

        private static int[] BuildOffsets(string text, Font font)
        {
            string value = text ?? string.Empty;
            int[] offsets = new int[value.Length + 1];
            for (int i = 1; i <= value.Length; i++)
                offsets[i] = MeasureWidth(value.Substring(0, i), font);
            return offsets;
        }

        private static int MeasureWidth(string text, Font font)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            return TextRenderer.MeasureText(text, font,
                new Size(int.MaxValue, int.MaxValue), MeasureFlags).Width;
        }

        private Font SelectTitleFont(string title, int width)
        {
            Font[] candidates =
            {
                _titleFont, _titleMediumFont,
                _titleSmallFont, _titleTinyFont
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                int textWidth = TextRenderer.MeasureText(
                    title ?? string.Empty, candidates[i],
                    new Size(int.MaxValue, int.MaxValue),
                    SingleLineFlags).Width;
                if (textWidth <= width)
                    return candidates[i];
            }

            return _titleTinyFont;
        }

        private static string ComposeEyebrow(WeissCard card)
        {
            string meta = ComposeMeta(card);
            string id = card.Id ?? string.Empty;
            if (meta.Length == 0)
                return id;
            if (id.Length == 0)
                return meta;
            return meta + "   ·   " + id;
        }

        private string GetTitle()
        {
            if (_card == null)
                return string.Empty;

            string title = string.IsNullOrEmpty(_card.JapaneseName)
                ? _card.Name
                : _card.JapaneseName;
            if (string.IsNullOrEmpty(title))
                title = _card.Id ?? string.Empty;
            return title ?? string.Empty;
        }

        private int GetTraitLabelWidth()
        {
            return TextRenderer.MeasureText("特征", _bodySmallFont).Width + 10;
        }

        private string GetEffectText()
        {
            if (_card == null || string.IsNullOrEmpty(_card.Text))
                return "此卡没有效果文本。";
            return _card.Text;
        }

        private void LoadCardImage()
        {
            _cardImage = null;
            if (_card == null || string.IsNullOrEmpty(_card.ImagePath))
                return;
            if (_imageBounds.Width <= 0 || _imageBounds.Height <= 0)
                return;

            _cardImage = _thumbnails.GetOrLoad(
                _card.ImagePath,
                _imageBounds.Width,
                _imageBounds.Height,
                true);
        }

        private void ReplaceBackdrop(Bitmap backdrop)
        {
            if (ReferenceEquals(_backdrop, backdrop))
                return;
            if (_backdrop != null)
                _backdrop.Dispose();
            _backdrop = backdrop;
        }

        private static Font CreateFont(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style,
                GraphicsUnit.Point, 134);
        }

        private static int MeasureWrappedHeight(
            string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0)
                return 0;
            Size size = TextRenderer.MeasureText(text, font,
                new Size(width, int.MaxValue), WrapFlags);
            return size.Height;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The overlay always paints the full backdrop itself.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            DrawBackdrop(graphics);
            if (_card == null)
                return;

            if (_popupBounds.Width <= 0 || _popupBounds.Height <= 0 ||
                _content == null)
            {
                RecalculateLayout();
            }

            DrawShadow(graphics);
            DrawPopup(graphics);
            DrawCardImage(graphics);
            DrawContent(graphics);
            DrawCloseButton(graphics);
            DrawPopupBorder(graphics);
            DrawNavigation(graphics);
            DrawHint(graphics);
        }

        private void DrawBackdrop(Graphics graphics)
        {
            Rectangle bounds = ClientRectangle;
            if (_backdrop != null)
            {
                graphics.DrawImage(_backdrop, bounds);
            }
            else
            {
                using (var background = new SolidBrush(AppTheme.MainBackground))
                    graphics.FillRectangle(background, bounds);
            }

            using (var dim = new SolidBrush(Color.FromArgb(178, 3, 8, 18)))
                graphics.FillRectangle(dim, bounds);
        }

        private void DrawShadow(Graphics graphics)
        {
            for (int i = 12; i >= 1; i--)
            {
                Rectangle shadow = new Rectangle(
                    _popupBounds.X - i,
                    _popupBounds.Y - i + 5,
                    _popupBounds.Width + i * 2,
                    _popupBounds.Height + i * 2);
                using (GraphicsPath path =
                    AppTheme.CreateRoundedRectangle(shadow, 16 + i))
                using (var brush = new SolidBrush(Color.FromArgb(7, 0, 0, 0)))
                {
                    graphics.FillPath(brush, path);
                }
            }
        }

        private void DrawPopup(Graphics graphics)
        {
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(_popupBounds, PopupCornerRadius))
            {
                graphics.FillPath(_panelBrush, path);
            }

            int dividerX = _imageFrame.Right + ColumnGap / 2;
            graphics.DrawLine(_dividerPen,
                dividerX, _contentBounds.Top,
                dividerX, _contentBounds.Bottom);
        }

        private void DrawPopupBorder(Graphics graphics)
        {
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(_popupBounds, PopupCornerRadius))
            {
                graphics.DrawPath(_borderPen, path);
            }
        }

        private void DrawCardImage(Graphics graphics)
        {
            Rectangle frame = _imageFrame;
            using (GraphicsPath framePath =
                AppTheme.CreateRoundedRectangle(frame, 10))
            {
                graphics.FillPath(_imageFrameBrush, framePath);
                graphics.DrawPath(_borderPen, framePath);
            }

            Rectangle imageBounds = _imageBounds;
            using (GraphicsPath imagePath =
                AppTheme.CreateRoundedRectangle(imageBounds, 6))
            {
                GraphicsState state = graphics.Save();
                graphics.SetClip(imagePath, CombineMode.Intersect);

                if (_cardImage != null)
                {
                    graphics.DrawImage(_cardImage, imageBounds);
                }
                else
                {
                    graphics.FillRectangle(_imageBackBrush, imageBounds);
                    DrawText(graphics, "暂无卡图", _bodyFont,
                        imageBounds, AppTheme.TextMuted, CenteredFlags);
                    string id = _card.Id ?? string.Empty;
                    Rectangle caption = new Rectangle(
                        imageBounds.X,
                        imageBounds.Bottom - 34,
                        imageBounds.Width, 22);
                    DrawText(graphics, id, _nameFont, caption,
                        AppTheme.Placeholder, CenteredFlags);
                }

                graphics.Restore(state);
                graphics.DrawPath(_borderPen, imagePath);
            }
        }

        private void DrawContent(Graphics graphics)
        {
            WeissCard card = _card;
            ContentLayout layout = _content;
            string title = GetTitle();

            DrawSelectableText(graphics, ComposeEyebrow(card), _nameFont,
                layout.Eyebrow, AppTheme.TextMuted, SingleLineFlags,
                SelectionArea.Eyebrow);
            DrawSelectableText(graphics, title, layout.TitleFont,
                layout.Title, AppTheme.Text, SingleLineFlags,
                SelectionArea.Title);

            graphics.DrawLine(_dividerPen,
                layout.Title.X, layout.SeparatorY,
                _contentBounds.Right, layout.SeparatorY);

            string power = card.Category == CardCategory.Character
                ? card.Power.ToString()
                : "—";
            string trigger = card.Trigger == TriggerIcon.None
                ? "无"
                : card.Trigger.ToString();
            string deckLimit = card.DeckLimit > 0
                ? card.DeckLimit.ToString()
                : "—";

            string[] labels = { "等级", "COST", "力量", "灵魂",
                "触发", "颜色", "类型", "卡组限制" };
            string[] values = { card.Level.ToString(), card.Cost.ToString(),
                power, card.Soul.ToString(), trigger, card.ColorDisplay,
                card.CategoryDisplay, deckLimit };

            DrawInfoArea(graphics, layout.InfoArea);
            for (int i = 0; i < InfoBoxCount; i++)
            {
                DrawInfoBox(graphics, layout.InfoBoxes[i],
                    labels[i], values[i]);
            }

            string traits = card.TraitsDisplay;
            if (string.IsNullOrEmpty(traits))
                traits = "—";
            int traitLabelWidth = GetTraitLabelWidth();
            DrawText(graphics, "特征", _bodySmallFont,
                new Rectangle(layout.Traits.X, layout.Traits.Y + 1,
                    traitLabelWidth, 18),
                AppTheme.Accent,
                TextFormatFlags.Left | TextFormatFlags.Top |
                TextFormatFlags.NoPrefix);
            DrawText(graphics, traits, _bodySmallFont,
                new Rectangle(
                    layout.Traits.X + traitLabelWidth,
                    layout.Traits.Y,
                    Math.Max(20, layout.Traits.Width - traitLabelWidth),
                    layout.Traits.Height),
                AppTheme.TextSoft, WrapFlags);

            DrawSectionTitle(graphics, layout.EffectTitle, "卡牌效果",
                _effectMaxScroll > 0 ? "滚轮 / ↑↓ 滚动" : null);

            using (GraphicsPath boxPath =
                AppTheme.CreateRoundedRectangle(layout.EffectBox, 10))
            {
                graphics.FillPath(_boxBrush, boxPath);
            }

            DrawEffectText(graphics);
            DrawEffectScrollbar(graphics);

            if (layout.HasBottomBand)
                DrawBottomBand(graphics);
        }

        private void DrawSectionTitle(Graphics graphics, Rectangle bounds,
            string text, string hint)
        {
            int hintWidth = string.IsNullOrEmpty(hint)
                ? 0
                : TextRenderer.MeasureText(hint, _hintFont).Width;
            int textWidth = TextRenderer.MeasureText(text, _sectionFont).Width;

            using (var accent = new SolidBrush(AppTheme.Accent))
            {
                graphics.FillRectangle(accent,
                    bounds.X, bounds.Y + 4, 3, 16);
            }

            DrawText(graphics, text, _sectionFont,
                new Rectangle(bounds.X + 10, bounds.Y,
                    Math.Max(20, bounds.Width - 10), bounds.Height),
                AppTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);

            int lineStart = bounds.X + 10 + textWidth + 12;
            int lineEnd = bounds.Right -
                (hintWidth > 0 ? hintWidth + 14 : 0);
            int lineY = bounds.Y + bounds.Height / 2;
            if (lineEnd > lineStart + 8)
            {
                graphics.DrawLine(_dividerPen,
                    lineStart, lineY, lineEnd, lineY);
            }

            if (hintWidth > 0)
            {
                DrawText(graphics, hint, _hintFont, bounds,
                    AppTheme.TextMuted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix);
            }
        }

        private void DrawEffectText(Graphics graphics)
        {
            if (_effectLines == null || _effectLines.Count == 0)
                return;

            ContentLayout layout = _content;
            bool hasText = _card != null &&
                !string.IsNullOrEmpty(_card.Text);
            Color effectColor = hasText
                ? AppTheme.TextSoft
                : AppTheme.TextMuted;

            int lineHeight = _bodyFont.Height + 2;
            int textX = layout.EffectViewport.X;
            int top = layout.EffectViewport.Y - _effectScroll;

            int selectionStart = 0;
            int selectionEnd = 0;
            if (hasText && _selectionArea == SelectionArea.Effect)
            {
                selectionStart = Math.Min(_selectionStart, _selectionEnd);
                selectionEnd = Math.Max(_selectionStart, _selectionEnd);
            }

            GraphicsState state = graphics.Save();
            graphics.SetClip(layout.EffectViewport);
            for (int i = 0; i < _effectLines.Count; i++)
            {
                TextLine line = _effectLines[i];
                int y = top + i * lineHeight;
                if (y + lineHeight <= layout.EffectViewport.Y ||
                    y >= layout.EffectViewport.Bottom)
                {
                    continue;
                }

                if (selectionEnd > selectionStart && line.Text.Length > 0)
                {
                    int lineEnd = line.Start + line.Text.Length;
                    int from = Math.Max(selectionStart, line.Start) - line.Start;
                    int to = Math.Min(selectionEnd, lineEnd) - line.Start;
                    if (to > from)
                    {
                        int x1 = line.Offsets[from];
                        int x2 = line.Offsets[to];
                        graphics.FillRectangle(_selectionBrush,
                            new Rectangle(textX + x1, y,
                                Math.Max(1, x2 - x1), lineHeight));
                    }
                }

                DrawText(graphics, line.Text, _bodyFont,
                    new Rectangle(textX, y, layout.EffectViewport.Width,
                        lineHeight),
                    effectColor,
                    TextFormatFlags.Left | TextFormatFlags.Top |
                    TextFormatFlags.NoPrefix);
            }
            graphics.Restore(state);
        }

        private void DrawSelectableText(Graphics graphics, string text,
            Font font, Rectangle bounds, Color color,
            TextFormatFlags flags, SelectionArea area)
        {
            if (_selectionArea == area && !string.IsNullOrEmpty(text))
            {
                int start = Math.Min(_selectionStart, _selectionEnd);
                int end = Math.Max(_selectionStart, _selectionEnd);
                if (end > start && end <= text.Length)
                {
                    int[] offsets = BuildOffsets(text, font);
                    int x1 = offsets[start];
                    int x2 = offsets[end];
                    graphics.FillRectangle(_selectionBrush,
                        new Rectangle(bounds.X + x1, bounds.Y + 2,
                            Math.Max(1, x2 - x1), bounds.Height - 4));
                }
            }

            DrawText(graphics, text, font, bounds, color, flags);
        }

        private void DrawBottomBand(Graphics graphics)
        {
            ContentLayout layout = _content;

            if (layout.HasAltSection)
            {
                DrawSectionTitle(graphics, layout.AltTitle, "异画",
                    _altMaxScroll > 0 ? "拖动查看" : null);
                DrawCardStrip(graphics, layout.AltStrip, _altCards,
                    _altScroll, 0);
                DrawStripIndicator(graphics, layout.AltIndicator,
                    _altScroll, _altMaxScroll);
            }

            if (layout.HasRelatedSection)
            {
                DrawSectionTitle(graphics, layout.RelatedTitle, "相关卡片",
                    _relatedMaxScroll > 0 ? "拖动查看" : null);
                DrawCardStrip(graphics, layout.RelatedStrip, _relatedCards,
                    _relatedScroll, 1);
                DrawStripIndicator(graphics, layout.RelatedIndicator,
                    _relatedScroll, _relatedMaxScroll);
            }

            if (layout.HasAltSection && layout.HasRelatedSection)
            {
                graphics.DrawLine(_dividerPen,
                    layout.BandDividerX, layout.AltTitle.Y + 2,
                    layout.BandDividerX,
                    layout.RelatedStrip.Bottom + 3);
            }
        }

        private void DrawCardStrip(Graphics graphics, Rectangle strip,
            IList<WeissCard> cards, int scroll, int section)
        {
            if (cards == null || cards.Count == 0 || strip.Width <= 0)
                return;

            int imageHeight = CardCellWidth * CardAspectHeight /
                CardAspectWidth;
            GraphicsState state = graphics.Save();
            graphics.SetClip(strip);

            for (int i = 0; i < cards.Count; i++)
            {
                int left = strip.X +
                    i * (CardCellWidth + CardCellGap) - scroll;
                if (left + CardCellWidth <= strip.X - CardCellGap ||
                    left >= strip.Right + CardCellGap)
                {
                    continue;
                }

                WeissCard card = cards[i];
                Rectangle imageBounds = new Rectangle(
                    left, strip.Y, CardCellWidth, imageHeight);
                bool hover = ReferenceEquals(_hoverCard, card) &&
                    _hoverSection == section;
                Image image = GetCellImage(card, imageBounds);

                using (GraphicsPath cellPath =
                    AppTheme.CreateRoundedRectangle(imageBounds, 8))
                {
                    if (image != null)
                    {
                        GraphicsState cellState = graphics.Save();
                        graphics.SetClip(cellPath, CombineMode.Intersect);
                        graphics.DrawImage(image, imageBounds);
                        graphics.Restore(cellState);
                    }
                    else
                    {
                        graphics.FillPath(_imageBackBrush, cellPath);
                    }

                    if (hover)
                    {
                        graphics.FillPath(_hoverBrush, cellPath);
                        using (var accent = new Pen(AppTheme.Accent))
                            graphics.DrawPath(accent, cellPath);
                    }
                    else
                    {
                        graphics.DrawPath(_borderPen, cellPath);
                    }
                }

                string name = string.IsNullOrEmpty(card.JapaneseName)
                    ? card.Name
                    : card.JapaneseName;
                if (string.IsNullOrEmpty(name))
                    name = card.Id ?? string.Empty;

                Rectangle nameBounds = new Rectangle(
                    left, imageBounds.Bottom + CardImageGap,
                    CardCellWidth, CardNameHeight);
                DrawText(graphics, name, _stripNameFont, nameBounds,
                    hover ? AppTheme.Text : AppTheme.TextSoft,
                    TextFormatFlags.WordBreak |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }

            graphics.Restore(state);
        }

        private Image GetCellImage(WeissCard card, Rectangle bounds)
        {
            if (card == null || string.IsNullOrEmpty(card.ImagePath))
                return null;
            return _thumbnails.GetOrLoad(card.ImagePath,
                Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), true);
        }

        private static void DrawStripIndicator(Graphics graphics,
            Rectangle bounds, int scroll, int maxScroll)
        {
            if (maxScroll <= 0 || bounds.Width <= 0)
                return;

            using (var track = new SolidBrush(AppTheme.Divider))
                graphics.FillRectangle(track,
                    bounds.X, bounds.Y, bounds.Width, 3);

            int thumbWidth = Math.Max(24,
                bounds.Width * bounds.Width / (bounds.Width + maxScroll));
            thumbWidth = Math.Min(bounds.Width, thumbWidth);
            int travel = Math.Max(1, bounds.Width - thumbWidth);
            int thumbX = bounds.X + travel * scroll /
                Math.Max(1, maxScroll);
            using (var thumb = new SolidBrush(AppTheme.TextMuted))
                graphics.FillRectangle(thumb,
                    thumbX, bounds.Y, thumbWidth, 3);
        }

        private void DrawInfoArea(Graphics graphics, Rectangle area)
        {
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(area, 10))
            {
                graphics.FillPath(_boxBrush, path);
            }

            int columnWidth = area.Width / InfoColumns;
            for (int column = 1; column < InfoColumns; column++)
            {
                int x = area.X + columnWidth * column;
                graphics.DrawLine(_dividerPen,
                    x, area.Y + 10, x, area.Bottom - 10);
            }
        }

        private void DrawInfoBox(Graphics graphics, Rectangle bounds,
            string label, string value)
        {
            DrawText(graphics, label, _labelFont,
                new Rectangle(bounds.X + 12, bounds.Y + 6,
                    Math.Max(1, bounds.Width - 20), 14),
                AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.Top |
                TextFormatFlags.NoPrefix);

            Font valueFont = _valueFont;
            if (TextRenderer.MeasureText(value, _valueFont).Width >
                bounds.Width - 24)
            {
                valueFont = _valueSmallFont;
            }

            DrawText(graphics, value, valueFont,
                new Rectangle(bounds.X + 12, bounds.Y + 20,
                    Math.Max(1, bounds.Width - 20), 24),
                AppTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private void DrawEffectScrollbar(Graphics graphics)
        {
            if (_effectMaxScroll <= 0)
                return;

            Rectangle box = _content.EffectBox;
            Rectangle track = new Rectangle(
                box.Right - 8, box.Y + 12, 3,
                Math.Max(1, box.Height - 24));
            using (var trackBrush = new SolidBrush(AppTheme.Divider))
                graphics.FillRectangle(trackBrush, track);

            int thumbHeight = Math.Max(24,
                track.Height * _content.EffectViewport.Height /
                Math.Max(1, _content.EffectTextHeight));
            thumbHeight = Math.Min(track.Height, thumbHeight);
            int travel = Math.Max(1, track.Height - thumbHeight);
            int thumbTop = track.Y +
                travel * _effectScroll / Math.Max(1, _effectMaxScroll);
            using (var thumbBrush = new SolidBrush(AppTheme.TextMuted))
                graphics.FillRectangle(thumbBrush,
                    new Rectangle(track.X, thumbTop, track.Width, thumbHeight));
        }

        private void DrawCloseButton(Graphics graphics)
        {
            Color fill = _closeHover
                ? Color.FromArgb(220, 68, 68)
                : Color.FromArgb(42, 56, 76);
            Color border = _closeHover
                ? Color.FromArgb(248, 113, 113)
                : AppTheme.Border;
            using (var fillBrush = new SolidBrush(fill))
                graphics.FillEllipse(fillBrush, _closeBounds);
            using (var borderPen = new Pen(border))
                graphics.DrawEllipse(borderPen, _closeBounds);

            int centerX = _closeBounds.Left + _closeBounds.Width / 2;
            int centerY = _closeBounds.Top + _closeBounds.Height / 2;
            using (var crossPen = new Pen(Color.White, 1.8F))
            {
                crossPen.StartCap = LineCap.Round;
                crossPen.EndCap = LineCap.Round;
                graphics.DrawLine(crossPen,
                    centerX - 5, centerY - 5,
                    centerX + 5, centerY + 5);
                graphics.DrawLine(crossPen,
                    centerX + 5, centerY - 5,
                    centerX - 5, centerY + 5);
            }
        }

        private void DrawNavigation(Graphics graphics)
        {
            DrawNavButton(graphics, _prevBounds, -1,
                _prevCard != null, _prevHover);
            DrawNavButton(graphics, _nextBounds, 1,
                _nextCard != null, _nextHover);
        }

        private void DrawNavButton(
            Graphics graphics, Rectangle bounds, int direction,
            bool enabled, bool hover)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            Color fill = !enabled
                ? Color.FromArgb(28, 38, 55)
                : hover
                    ? Color.FromArgb(59, 130, 246)
                    : Color.FromArgb(42, 56, 76);
            Color border = !enabled
                ? AppTheme.Divider
                : hover
                    ? AppTheme.AccentHover
                    : AppTheme.Border;
            using (var fillBrush = new SolidBrush(fill))
                graphics.FillEllipse(fillBrush, bounds);
            using (var borderPen = new Pen(border))
                graphics.DrawEllipse(borderPen, bounds);

            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            int tipX = centerX + (direction < 0 ? -2 : 2);
            int tailX = centerX + (direction < 0 ? 3 : -3);
            using (var glyphPen = new Pen(
                enabled ? Color.White : AppTheme.Placeholder, 1.8F))
            {
                glyphPen.StartCap = LineCap.Round;
                glyphPen.EndCap = LineCap.Round;
                graphics.DrawLine(glyphPen,
                    tailX, centerY - 5, tipX, centerY);
                graphics.DrawLine(glyphPen,
                    tipX, centerY, tailX, centerY + 5);
            }
        }

        private void DrawHint(Graphics graphics)
        {
            string hint = "点击空白区域或按ESC关闭";
            Size size = TextRenderer.MeasureText(hint, _hintFont);
            int top = _popupBounds.Bottom + 10;
            if (top + size.Height > ClientSize.Height - 2)
                return;

            Rectangle bounds = new Rectangle(
                (ClientSize.Width - size.Width) / 2,
                top, size.Width, size.Height);
            DrawText(graphics, hint, _hintFont, bounds,
                AppTheme.TextMuted, CenteredFlags);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecalculateLayout();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_selecting)
            {
                int index = HitTestSelectionIndex(_selectionArea, e.Location);
                if (index != _selectionEnd)
                {
                    _selectionEnd = index;
                    InvalidateSelectionArea();
                }
                Cursor = Cursors.IBeam;
                return;
            }

            if (_dragStrip >= 0)
            {
                int delta = e.X - _dragStartX;
                if (Math.Abs(delta) > 2)
                    _dragMoved = true;
                SetStripScroll(_dragStrip, _dragStartScroll - delta);
                Cursor = Cursors.SizeWE;
                return;
            }

            bool closeHover = _card != null &&
                _closeBounds.Contains(e.Location);
            if (closeHover != _closeHover)
            {
                _closeHover = closeHover;
                Invalidate(_closeBounds);
            }

            bool prevHover = _prevCard != null &&
                _prevBounds.Contains(e.Location);
            if (prevHover != _prevHover)
            {
                _prevHover = prevHover;
                Invalidate(_prevBounds);
            }

            bool nextHover = _nextCard != null &&
                _nextBounds.Contains(e.Location);
            if (nextHover != _nextHover)
            {
                _nextHover = nextHover;
                Invalidate(_nextBounds);
            }

            WeissCard hoverCard;
            int hoverSection;
            bool overCard = TryHitCard(e.Location,
                out hoverCard, out hoverSection);
            if (!ReferenceEquals(hoverCard, _hoverCard) ||
                hoverSection != _hoverSection)
            {
                Rectangle previous = GetStripBounds(_hoverSection);
                _hoverCard = hoverCard;
                _hoverSection = hoverSection;
                if (previous.Width > 0)
                    Invalidate(previous);

                Rectangle current = GetStripBounds(hoverSection);
                if (current.Width > 0)
                    Invalidate(current);
            }

            if (closeHover || overCard || prevHover || nextHover)
            {
                Cursor = Cursors.Hand;
            }
            else if (HitTestSelectionArea(e.Location) != SelectionArea.None)
            {
                Cursor = Cursors.IBeam;
            }
            else
            {
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_closeHover)
            {
                _closeHover = false;
                Invalidate(_closeBounds);
            }
            if (_prevHover)
            {
                _prevHover = false;
                Invalidate(_prevBounds);
            }
            if (_nextHover)
            {
                _nextHover = false;
                Invalidate(_nextBounds);
            }
            if (_hoverCard != null)
            {
                Rectangle previous = GetStripBounds(_hoverSection);
                _hoverCard = null;
                _hoverSection = -1;
                if (previous.Width > 0)
                    Invalidate(previous);
            }
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_card == null || e.Button != MouseButtons.Left)
                return;

            Focus();

            if (_content != null && _content.HasBottomBand)
            {
                if (_content.HasAltSection &&
                    _content.AltStrip.Contains(e.Location))
                {
                    _dragStrip = 0;
                    _dragStartX = e.X;
                    _dragStartScroll = _altScroll;
                    _dragMoved = false;
                    Cursor = Cursors.SizeWE;
                    return;
                }

                if (_content.HasRelatedSection &&
                    _content.RelatedStrip.Contains(e.Location))
                {
                    _dragStrip = 1;
                    _dragStartX = e.X;
                    _dragStartScroll = _relatedScroll;
                    _dragMoved = false;
                    Cursor = Cursors.SizeWE;
                    return;
                }
            }

            SelectionArea area = HitTestSelectionArea(e.Location);
            if (area != SelectionArea.None)
            {
                _selectionArea = area;
                _selectionStart = HitTestSelectionIndex(area, e.Location);
                _selectionEnd = _selectionStart;
                _selecting = true;
                InvalidateSelectionArea();
            }
            else if (_selectionArea != SelectionArea.None)
            {
                _selectionArea = SelectionArea.None;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || _card == null)
                return;

            if (_selecting)
            {
                _selecting = false;
                return;
            }

            if (_dragStrip >= 0)
            {
                int strip = _dragStrip;
                bool moved = _dragMoved;
                _dragStrip = -1;
                _dragMoved = false;
                Cursor = Cursors.Default;

                WeissCard card;
                int section;
                if (!moved && TryHitCard(e.Location, out card, out section) &&
                    section == strip)
                {
                    NavigateTo(card);
                }
                return;
            }

            if (_prevCard != null && _prevBounds.Contains(e.Location))
            {
                NavigateTo(_prevCard);
                return;
            }

            if (_nextCard != null && _nextBounds.Contains(e.Location))
            {
                NavigateTo(_nextCard);
                return;
            }

            if (_closeBounds.Contains(e.Location) ||
                !_popupBounds.Contains(e.Location))
            {
                RaiseCloseRequested();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_card == null)
            {
                base.OnMouseWheel(e);
                return;
            }

            if (_content != null && _content.HasBottomBand)
            {
                if (_content.HasAltSection && _altMaxScroll > 0 &&
                    _content.AltStrip.Contains(e.Location))
                {
                    SetStripScroll(0, _altScroll - Math.Sign(e.Delta) * 60);
                    return;
                }
                if (_content.HasRelatedSection && _relatedMaxScroll > 0 &&
                    _content.RelatedStrip.Contains(e.Location))
                {
                    SetStripScroll(1,
                        _relatedScroll - Math.Sign(e.Delta) * 60);
                    return;
                }
            }

            if (_effectMaxScroll <= 0)
            {
                base.OnMouseWheel(e);
                return;
            }

            int steps = Math.Max(1, Math.Abs(e.Delta) / 120);
            int direction = e.Delta > 0 ? -1 : 1;
            ScrollEffect(direction * steps * 3);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_card != null)
            {
                if (keyData == (Keys.Control | Keys.C))
                {
                    if (CopySelection())
                        return true;
                }
                if (keyData == Keys.Escape)
                {
                    RaiseCloseRequested();
                    return true;
                }
                if (keyData == Keys.Left && _prevCard != null)
                {
                    NavigateTo(_prevCard);
                    return true;
                }
                if (keyData == Keys.Right && _nextCard != null)
                {
                    NavigateTo(_nextCard);
                    return true;
                }
                if (keyData == Keys.PageDown)
                {
                    ScrollEffect(6);
                    return true;
                }
                if (keyData == Keys.PageUp)
                {
                    ScrollEffect(-6);
                    return true;
                }
                if (keyData == Keys.Down)
                {
                    ScrollEffect(2);
                    return true;
                }
                if (keyData == Keys.Up)
                {
                    ScrollEffect(-2);
                    return true;
                }
                if (keyData == Keys.Home)
                {
                    _effectScroll = 0;
                    Invalidate(_content.EffectBox);
                    return true;
                }
                if (keyData == Keys.End)
                {
                    _effectScroll = _effectMaxScroll;
                    Invalidate(_content.EffectBox);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SetStripScroll(int strip, int value)
        {
            if (strip == 0)
            {
                int next = Math.Max(0, Math.Min(_altMaxScroll, value));
                if (next == _altScroll)
                    return;
                _altScroll = next;
                Invalidate(_content.AltStrip);
                Invalidate(_content.AltIndicator);
            }
            else if (strip == 1)
            {
                int next = Math.Max(0, Math.Min(_relatedMaxScroll, value));
                if (next == _relatedScroll)
                    return;
                _relatedScroll = next;
                Invalidate(_content.RelatedStrip);
                Invalidate(_content.RelatedIndicator);
            }
        }

        private Rectangle GetStripBounds(int section)
        {
            if (_content == null)
                return Rectangle.Empty;
            if (section == 0 && _content.HasAltSection)
                return _content.AltStrip;
            if (section == 1 && _content.HasRelatedSection)
                return _content.RelatedStrip;
            return Rectangle.Empty;
        }

        private bool TryHitCard(Point location, out WeissCard card,
            out int section)
        {
            card = null;
            section = -1;
            if (_content == null || !_content.HasBottomBand)
                return false;

            int index;
            if (_content.HasAltSection && TryHitStripCell(
                _content.AltStrip, _altCards, _altScroll, location, out index))
            {
                card = _altCards[index];
                section = 0;
                return true;
            }

            if (_content.HasRelatedSection && TryHitStripCell(
                _content.RelatedStrip, _relatedCards, _relatedScroll,
                location, out index))
            {
                card = _relatedCards[index];
                section = 1;
                return true;
            }

            return false;
        }

        private static bool TryHitStripCell(Rectangle strip,
            IList<WeissCard> cards, int scroll, Point location,
            out int index)
        {
            index = -1;
            if (cards == null || cards.Count == 0 || !strip.Contains(location))
                return false;

            int stride = CardCellWidth + CardCellGap;
            int offset = location.X - strip.X + scroll;
            if (offset < 0)
                return false;

            int slot = offset / stride;
            if (offset - slot * stride >= CardCellWidth)
                return false;
            if (slot >= cards.Count)
                return false;

            index = slot;
            return true;
        }

        private SelectionArea HitTestSelectionArea(Point location)
        {
            if (_card == null || _content == null)
                return SelectionArea.None;
            if (_content.Title.Contains(location))
                return SelectionArea.Title;
            if (_content.Eyebrow.Contains(location))
                return SelectionArea.Eyebrow;
            if (_content.EffectViewport.Contains(location))
                return SelectionArea.Effect;
            return SelectionArea.None;
        }

        private int HitTestSelectionIndex(SelectionArea area, Point location)
        {
            switch (area)
            {
                case SelectionArea.Title:
                    return HitTestSingleLine(GetTitle(),
                        _content.TitleFont, _content.Title, location.X);
                case SelectionArea.Eyebrow:
                    return HitTestSingleLine(ComposeEyebrow(_card),
                        _nameFont, _content.Eyebrow, location.X);
                case SelectionArea.Effect:
                    return HitTestEffectText(location);
                default:
                    return 0;
            }
        }

        private static int HitTestSingleLine(string text, Font font,
            Rectangle bounds, int x)
        {
            int length = text == null ? 0 : text.Length;
            if (length == 0 || font == null)
                return 0;

            int[] offsets = BuildOffsets(text, font);
            return IndexFromOffsets(offsets,
                Math.Max(0, x - bounds.X), length);
        }

        private static int IndexFromOffsets(int[] offsets, int localX,
            int length)
        {
            if (localX <= 0 || length <= 0)
                return 0;
            if (localX >= offsets[length])
                return length;

            for (int i = 1; i <= length; i++)
            {
                if (localX < (offsets[i - 1] + offsets[i]) / 2)
                    return i - 1;
            }
            return length;
        }

        private int HitTestEffectText(Point location)
        {
            if (_effectLines == null || _effectLines.Count == 0 ||
                _card == null || string.IsNullOrEmpty(_card.Text))
            {
                return 0;
            }

            Rectangle viewport = _content.EffectViewport;
            int lineHeight = _bodyFont.Height + 2;
            int textY = location.Y - viewport.Y + _effectScroll;
            int lineIndex = textY / lineHeight;
            if (lineIndex < 0)
                return 0;
            if (lineIndex >= _effectLines.Count)
                return _card.Text.Length;

            TextLine line = _effectLines[lineIndex];
            int localX = location.X - viewport.X;
            return line.Start +
                IndexFromOffsets(line.Offsets, localX, line.Text.Length);
        }

        private void InvalidateSelectionArea()
        {
            if (_content == null)
            {
                Invalidate();
                return;
            }

            Rectangle bounds;
            switch (_selectionArea)
            {
                case SelectionArea.Title:
                    bounds = _content.Title;
                    break;
                case SelectionArea.Eyebrow:
                    bounds = _content.Eyebrow;
                    break;
                case SelectionArea.Effect:
                    bounds = _content.EffectViewport;
                    break;
                default:
                    Invalidate();
                    return;
            }

            Invalidate(Rectangle.Inflate(bounds, 4, 4));
        }

        private bool CopySelection()
        {
            string text = GetSelectedText();
            if (string.IsNullOrEmpty(text))
                return false;

            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string GetSelectedText()
        {
            int start = Math.Min(_selectionStart, _selectionEnd);
            int end = Math.Max(_selectionStart, _selectionEnd);
            if (_selectionArea == SelectionArea.None || end <= start ||
                _card == null)
            {
                return string.Empty;
            }

            string source;
            switch (_selectionArea)
            {
                case SelectionArea.Eyebrow:
                    source = ComposeEyebrow(_card);
                    break;
                case SelectionArea.Title:
                    source = GetTitle();
                    break;
                case SelectionArea.Effect:
                    source = _card.Text ?? string.Empty;
                    break;
                default:
                    return string.Empty;
            }

            if (source.Length < end)
                return string.Empty;
            return source.Substring(start, end - start);
        }

        private void ScrollEffect(int lines)
        {
            if (_effectMaxScroll <= 0)
                return;

            int step = Math.Max(24, _bodyFont.Height * 3);
            int next = _effectScroll + lines * step;
            next = Math.Max(0, Math.Min(_effectMaxScroll, next));
            if (next == _effectScroll)
                return;

            _effectScroll = next;
            Invalidate(_content.EffectBox);
        }

        private void RaiseCloseRequested()
        {
            EventHandler handler = CloseRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private static string ComposeMeta(WeissCard card)
        {
            string series = card.SeriesName ?? string.Empty;
            string set = card.SetName ?? string.Empty;
            string meta = series;
            if (set.Length > 0)
                meta = meta.Length > 0 ? meta + " · " + set : set;
            if (card.IsClone)
                meta = meta.Length > 0 ? "异画 · " + meta : "异画";
            return meta;
        }

        private static void DrawText(Graphics graphics, string text,
            Font font, Rectangle bounds, Color color,
            TextFormatFlags flags)
        {
            TextRenderer.DrawText(graphics, text ?? string.Empty,
                font, bounds, color, flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_backdrop != null)
                {
                    _backdrop.Dispose();
                    _backdrop = null;
                }

                _titleFont.Dispose();
                _titleMediumFont.Dispose();
                _titleSmallFont.Dispose();
                _titleTinyFont.Dispose();
                _nameFont.Dispose();
                _sectionFont.Dispose();
                _labelFont.Dispose();
                _valueFont.Dispose();
                _valueSmallFont.Dispose();
                _bodyFont.Dispose();
                _bodySmallFont.Dispose();
                _stripNameFont.Dispose();
                _hintFont.Dispose();
                _panelBrush.Dispose();
                _imageBackBrush.Dispose();
                _imageFrameBrush.Dispose();
                _boxBrush.Dispose();
                _selectionBrush.Dispose();
                _hoverBrush.Dispose();
                _borderPen.Dispose();
                _dividerPen.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
