using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Hanglish.Core;

namespace Hanglish;

public enum BubbleStyle { White, Dark, Minimal }

/// <summary>
/// Draws the caret preview — a small one-line pill — into a transparent bitmap with a soft shadow.
/// Shared by the bubble above the caret and the previews in the settings window.
/// </summary>
static class BubbleRenderer
{
    static readonly Color Korean = Color.FromArgb(232, 87, 58), English = Color.FromArgb(22, 150, 212);

    sealed record Palette(Color Card, Color Border, Color Text, Color KoTint, Color EnTint);

    static Palette PaletteOf(BubbleStyle style) => style == BubbleStyle.Dark
        ? new(Color.FromArgb(27, 29, 41), Color.FromArgb(48, 52, 70), Color.White,
              Color.FromArgb(70, 232, 87, 58), Color.FromArgb(70, 22, 150, 212))
        : new(Color.White, Color.FromArgb(228, 231, 239), Color.FromArgb(25, 27, 38),
              Color.FromArgb(255, 237, 232), Color.FromArgb(228, 245, 254));

    /// <summary>Space around the pill for the shadow, in pixels at the given scale.</summary>
    public static int Margin(float scale) => (int)(8 * scale);

    public static Bitmap Render(Guess guess, BubbleStyle style, float scale)
    {
        var palette = PaletteOf(style);
        // Pixel units: the bitmap has no display DPI of its own, so sizes are scaled by hand.
        using var main = new Font("Malgun Gothic", 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tagFont = new Font("Malgun Gothic", 9.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var tag = guess.Korean ? "한" : "EN";
        var accent = guess.Korean ? Korean : English;

        using var probe = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(probe);
        measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        SizeF Measure(string text, Font font) => measure.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        var mainSize = Measure(guess.Text, main);
        var tagSize = Measure(tag, tagFont);

        // Minimal drops the language pill for a small colored dot.
        bool pill = style != BubbleStyle.Minimal;
        float padX = 10 * scale, padY = 5 * scale, gap = 6 * scale;
        float markW = pill ? tagSize.Width + 9 * scale : 6 * scale, markH = pill ? tagSize.Height + 3 * scale : 6 * scale;
        float width = (pill ? mainSize.Width + gap + markW : markW + gap + mainSize.Width) + padX * 2;
        float height = mainSize.Height + padY * 2;
        int margin = Margin(scale);
        var bitmap = new Bitmap((int)Math.Ceiling(width) + margin * 2, (int)Math.Ceiling(height) + margin * 2);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        var card = new RectangleF(margin, margin, width, height);
        float radius = height / 2;

        // Soft shadow: stacked translucent pills, slightly lower than the card.
        for (int i = 4; i >= 1; i--)
        {
            var spread = card;
            spread.Inflate(i * scale, i * scale);
            spread.Offset(0, 2 * scale);
            using var shadowPath = Rounded(spread, radius + i * scale);
            using var shadow = new SolidBrush(Color.FromArgb(style == BubbleStyle.Dark ? 18 : 10, 20, 24, 45));
            g.FillPath(shadow, shadowPath);
        }
        using (var path = Rounded(card, radius))
        {
            using var fill = new SolidBrush(palette.Card);
            g.FillPath(fill, path);
            using var border = new Pen(palette.Border, Math.Max(1, scale * 0.8f));
            g.DrawPath(border, path);
        }

        float x = card.Left + padX, y = card.Top + padY;
        using var text = new SolidBrush(palette.Text);
        if (pill)
        {
            g.DrawString(guess.Text, main, text, x, y, StringFormat.GenericTypographic);
            var mark = new RectangleF(x + mainSize.Width + gap, card.Top + (height - markH) / 2, markW, markH);
            using var markPath = Rounded(mark, markH / 2);
            using var tint = new SolidBrush(guess.Korean ? palette.KoTint : palette.EnTint);
            g.FillPath(tint, markPath);
            using var tagBrush = new SolidBrush(accent);
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(tag, tagFont, tagBrush, mark, center);
        }
        else
        {
            using var dot = new SolidBrush(accent);
            g.FillEllipse(dot, x, card.Top + (height - markH) / 2, markW, markH);
            g.DrawString(guess.Text, main, text, x + markW + gap, y, StringFormat.GenericTypographic);
        }
        return bitmap;
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
