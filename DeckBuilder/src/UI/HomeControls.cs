using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WeissDeckBuilder.Models;

namespace WeissDeckBuilder.UI
{
    /// <summary>How a fortune value should be coloured.</summary>
    internal enum FortuneTone
    {
        Great,
        Good,
        Neutral,
        Bad
    }

    /// <summary>
    /// The big daily card on the landing page. It keeps a fixed card aspect
    /// ratio, paints its own rounded border and shows a 异画 badge for alternate
    /// artworks. Clicking is handled by the page so navigation stays in
    /// MainForm.
    /// </summary>
    internal sealed class TodayCardView : Control
    {
        private const float CardAspect = 0.7142857F;
        private const int MaxCardWidth = 360;
        private const int MaxCardHeight = 520;
        private const int CardRadius = 12;

        private readonly ThumbnailCache _thumbnails;
        private readonly Font _badgeFont;
        private readonly Font _placeholderFont;
        private string _imagePath;
        private string _badgeText;
        private string _placeholderText;
        private bool _hover;

        public TodayCardView(ThumbnailCache thumbnails)
        {
            _thumbnails = thumbnails;
            _badgeFont = new Font("Microsoft YaHei UI", 8F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            _placeholderFont = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Regular, GraphicsUnit.Point, 134);
            _imagePath = string.Empty;
            _badgeText = string.Empty;
            _placeholderText = "正在抽取今日卡片…";

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = AppTheme.MainBackground;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        /// <summary>Replaces the image and its badge without touching layout.</summary>
        public void SetCard(WeissCard card, bool loading)
        {
            _imagePath = card == null ? string.Empty :
                (card.ImagePath ?? string.Empty);
            _badgeText = card != null && card.IsClone ? "异画" : string.Empty;
            if (loading)
                _placeholderText = "正在抽取今日卡片…";
            else if (card == null)
                _placeholderText = "今天没有抽到卡片";
            else
                _placeholderText = "卡图文件缺失";
            Cursor = card == null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Rectangle card = CalculateCardBounds();
            if (card.Width < 8 || card.Height < 8)
                return;

            Rectangle shadow = card;
            shadow.Offset(3, 5);
            using (GraphicsPath shadowPath =
                AppTheme.CreateRoundedRectangle(shadow, CardRadius))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(
                AppTheme.IsDark ? 80 : 32, 0, 0, 0)))
            {
                graphics.FillPath(shadowBrush, shadowPath);
            }

            using (GraphicsPath cardPath =
                AppTheme.CreateRoundedRectangle(card, CardRadius))
            {
                GraphicsState state = graphics.Save();
                graphics.SetClip(cardPath, CombineMode.Replace);

                Image image = null;
                if (!string.IsNullOrEmpty(_imagePath) && _thumbnails != null)
                    image = _thumbnails.GetOrLoad(_imagePath,
                        card.Width, card.Height, false);

                if (image != null)
                {
                    graphics.DrawImage(image, card);
                }
                else
                {
                    using (var fill = new SolidBrush(AppTheme.TileBackground))
                        graphics.FillRectangle(fill, card);

                    Rectangle textBounds = Rectangle.Inflate(card, -24, -24);
                    TextRenderer.DrawText(graphics, _placeholderText,
                        _placeholderFont, textBounds, AppTheme.TextMuted,
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.WordBreak |
                        TextFormatFlags.NoPrefix);
                }

                graphics.Restore(state);

                bool highlight = _hover && !string.IsNullOrEmpty(_imagePath) &&
                    image != null;
                using (var border = new Pen(
                    highlight ? AppTheme.Accent : AppTheme.Border,
                    highlight ? 2F : 1F))
                {
                    graphics.DrawPath(border, cardPath);
                }
            }

            DrawBadge(graphics, card);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _badgeFont.Dispose();
                _placeholderFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private Rectangle CalculateCardBounds()
        {
            int availableWidth = Math.Max(1, ClientSize.Width - 24);
            int availableHeight = Math.Max(1, ClientSize.Height - 16);

            int width = Math.Min(MaxCardWidth, availableWidth);
            int height = (int)Math.Round(width / CardAspect);
            if (height > MaxCardHeight)
            {
                height = MaxCardHeight;
                width = (int)Math.Round(height * CardAspect);
            }
            if (height > availableHeight)
            {
                height = availableHeight;
                width = (int)Math.Round(height * CardAspect);
            }
            if (width > availableWidth)
                width = availableWidth;
            if (height > availableHeight)
                height = availableHeight;

            width = Math.Max(1, width);
            height = Math.Max(1, height);
            return new Rectangle(
                (ClientSize.Width - width) / 2,
                (ClientSize.Height - height) / 2,
                width, height);
        }

        private void DrawBadge(Graphics graphics, Rectangle card)
        {
            if (string.IsNullOrEmpty(_badgeText))
                return;

            Size textSize = TextRenderer.MeasureText(_badgeText, _badgeFont);
            var badge = new Rectangle(card.X + 10, card.Y + 10,
                textSize.Width + 18, 24);
            using (GraphicsPath badgePath =
                AppTheme.CreateRoundedRectangle(badge, 8))
            using (var fill = new SolidBrush(AppTheme.Accent))
            {
                graphics.FillPath(fill, badgePath);
            }

            TextRenderer.DrawText(graphics, _badgeText, _badgeFont, badge,
                Color.White, TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }
    }
    /// <summary>
    /// A small rounded 宜 / 忌 chip. It measures itself from the text so a
    /// longer tag never gets clipped, and only draws with the current theme's
    /// colours at paint time.
    /// </summary>
    internal sealed class FortuneTagLabel : Control
    {
        private readonly FortuneTone _tone;
        private readonly Font _tagFont;

        public FortuneTagLabel(string text, FortuneTone tone)
        {
            _tone = tone;
            _tagFont = new Font("Microsoft YaHei UI", 9F,
                FontStyle.Bold, GraphicsUnit.Point, 134);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Text = text ?? string.Empty;
            Height = 30;
            UpdateWidth();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            UpdateWidth();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color accent = ToneColor();
            Color fill = AppTheme.IsDark
                ? Blend(AppTheme.TileBackground, accent, 0.20F)
                : Blend(AppTheme.TileBackground, accent, 0.12F);
            Color border = Blend(AppTheme.TileBackground, accent,
                AppTheme.IsDark ? 0.55F : 0.40F);
            Color text = AppTheme.IsDark ? accent : Darker(accent, 0.72F);

            Rectangle bounds = new Rectangle(0, 0,
                Math.Max(1, ClientSize.Width - 1),
                Math.Max(1, ClientSize.Height - 1));
            using (GraphicsPath path =
                AppTheme.CreateRoundedRectangle(bounds, 9))
            using (var fillBrush = new SolidBrush(fill))
            using (var borderPen = new Pen(border))
            {
                graphics.FillPath(fillBrush, path);
                graphics.DrawPath(borderPen, path);
            }

            TextRenderer.DrawText(graphics, Text, _tagFont, bounds, text,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _tagFont.Dispose();
            base.Dispose(disposing);
        }

        private void UpdateWidth()
        {
            if (_tagFont == null)
                return;

            Size size = TextRenderer.MeasureText(Text ?? string.Empty,
                _tagFont);
            Width = Math.Max(44, size.Width + 24);
        }

        private Color ToneColor()
        {
            if (_tone == FortuneTone.Bad)
                return AppTheme.Danger;
            return AppTheme.Success;
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                from.A + (int)((to.A - from.A) * amount),
                from.R + (int)((to.R - from.R) * amount),
                from.G + (int)((to.G - from.G) * amount),
                from.B + (int)((to.B - from.B) * amount));
        }

        private static Color Darker(Color color, float factor)
        {
            factor = Math.Max(0F, Math.Min(1F, factor));
            return Color.FromArgb(color.A,
                (int)(color.R * factor),
                (int)(color.G * factor),
                (int)(color.B * factor));
        }
    }


    /// <summary>
    /// A slim 今日人品 meter. The filled part uses the same colour as the
    /// large 吉凶 text so the number and the bar read as one value.
    /// </summary>
    internal sealed class FortuneScoreBar : Control
    {
        private int _score;
        private FortuneTone _tone;

        public FortuneScoreBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = AppTheme.MainBackground;
        }

        public int Score
        {
            get { return _score; }
            set
            {
                _score = Math.Max(0, Math.Min(100, value));
                Invalidate();
            }
        }

        public FortuneTone Tone
        {
            get { return _tone; }
            set
            {
                _tone = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int height = Math.Min(8, ClientSize.Height);
            var track = new Rectangle(0, (ClientSize.Height - height) / 2,
                Math.Max(1, ClientSize.Width - 1), height);
            using (GraphicsPath trackPath =
                AppTheme.CreateRoundedRectangle(track, height / 2))
            using (var trackBrush = new SolidBrush(AppTheme.Divider))
            {
                graphics.FillPath(trackBrush, trackPath);
            }

            int fillWidth = track.Width * _score / 100;
            if (fillWidth <= 0)
                return;

            var fill = new Rectangle(track.X, track.Y, fillWidth,
                track.Height);
            using (GraphicsPath fillPath =
                AppTheme.CreateRoundedRectangle(fill, height / 2))
            using (var fillBrush = new SolidBrush(ToneColor()))
            {
                graphics.FillPath(fillBrush, fillPath);
            }
        }

        private Color ToneColor()
        {
            if (_tone == FortuneTone.Great)
                return AppTheme.FortuneGold;
            if (_tone == FortuneTone.Bad)
            {
                return AppTheme.IsDark
                    ? AppTheme.Danger
                    : Color.FromArgb(185, 28, 28);
            }
            if (_tone == FortuneTone.Neutral)
                return AppTheme.TextSoft;
            return AppTheme.IsDark
                ? AppTheme.Success
                : Color.FromArgb(21, 128, 61);
        }
    }


}

