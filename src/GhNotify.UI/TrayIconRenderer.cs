using GhNotify.Core;
using SkiaSharp;

namespace GhNotify.UI;

/// <summary>
/// Draws the tray icon (and the accent mark used for the .ico and the toast logo) with SkiaSharp, so it needs no window or
/// platform backend. Same artwork as v1.3: navy tile, merge glyph, red sweep and count badge when items wait, amber when unhealthy.
/// </summary>
public static class TrayIconRenderer
{
    static readonly SKColor Navy = new(0x00, 0x0C, 0x1A);
    static readonly SKColor Red = new(0xED, 0x35, 0x2F);
    static readonly SKColor Amber = new(0xF9, 0xB7, 0x00);
    static readonly SKColor White = SKColors.White;

    static SKTypeface? _heavy;
    static SKTypeface Heavy => _heavy ??= LoadHeavy();

    static SKTypeface LoadHeavy()
    {
        try
        {
            using var stream = typeof(TrayIconRenderer).Assembly.GetManifestResourceStream("Fonts.Roboto-Black.ttf")
                               ?? throw new FileNotFoundException("Fonts.Roboto-Black.ttf is not embedded");
            return SKTypeface.FromStream(stream) ?? SKTypeface.Default;
        }
        catch (Exception ex)
        {
            Log.Error("could not load the bundled tray font", ex);
            return SKTypeface.Default;
        }
    }

    /// <summary>The tray icon: <paramref name="badge"/> is the count text (null = nothing waiting), <paramref name="attention"/> marks a source or config problem.</summary>
    public static SKBitmap Render(int sizePx, string? badge, bool attention) => Draw(sizePx, badge, attention, accent: false);

    /// <summary>The calm macOS menu-bar icon: the merge glyph alone in black on transparent, for a template image that macOS tints
    /// for light and dark menu bars. Waiting and unhealthy states use the full-colour <see cref="Render"/>.</summary>
    public static SKBitmap RenderTemplate(int sizePx)
    {
        var bitmap = new SKBitmap(new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        // Without the Windows tile there is padding to spare: centre the glyph (x 7.8–28.6, y 3.3–30.2 in the 36-unit artwork)
        // and let it fill the height, with a heavier stroke, so it reads like the system menu-bar icons.
        canvas.Scale(sizePx / 36f);
        canvas.Translate(18, 18);
        canvas.Scale(1.26f);
        canvas.Translate(-18.2f, -16.75f);
        DrawGlyph(canvas, SKColors.Black, stroke: 3.1f);
        using (var p = Fill(SKColors.Black)) canvas.DrawCircle(25, 9, 3.6f, p);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>The app mark (red sweep, no badge): .ico frames and the toast logo.</summary>
    public static SKBitmap RenderAccent(int sizePx) => Draw(sizePx, null, false, accent: true);

    public static byte[] ToPng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static SKPaint Fill(SKColor c) => new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Fill };

    static SKPaint Stroke(SKColor c, float w, bool round = false) => new()
    {
        Color = c,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = w,
        StrokeCap = round ? SKStrokeCap.Round : SKStrokeCap.Butt,
    };

    static SKBitmap Draw(int sizePx, string? badge, bool attention, bool accent)
    {
        var red = accent || badge is not null;
        var bitmap = new SKBitmap(new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(sizePx / 36f);

        var tile = new SKRoundRect(new SKRect(0, 0, 36, 36), 9, 9);
        using (var p = Fill(Navy)) canvas.DrawRoundRect(tile, p);

        canvas.Save();
        canvas.ClipRoundRect(tile, antialias: true);
        if (red) using (var p = Fill(Red)) canvas.DrawCircle(34, 2, 19, p);
        canvas.Restore();

        DrawGlyph(canvas, White);
        if (red)
        {
            using (var p = Fill(Red)) canvas.DrawCircle(25, 9, 3.6f, p);
            using (var p = Stroke(White, 2.2f)) canvas.DrawCircle(25, 9, 3.6f, p);
        }
        else using (var p = Fill(White)) canvas.DrawCircle(25, 9, 3.6f, p);

        if (badge is not null)
        {
            using (var p = Fill(Red)) canvas.DrawCircle(30, 6, 5.5f, p);
            using (var p = Stroke(attention ? Amber : White, 1.2f)) canvas.DrawCircle(30, 6, 5.5f, p);
            using var text = new SKPaint { Typeface = Heavy, TextSize = badge.Length >= 3 ? 6f : 7f, IsAntialias = true, SubpixelText = true, Color = White };
            var metrics = text.FontMetrics;
            var width = text.MeasureText(badge);
            var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
            canvas.DrawText(badge, 30 - width / 2, 6 - lineHeight / 2 - metrics.Ascent, text);
        }
        else if (attention)
        {
            using var p = Fill(Amber);
            canvas.DrawCircle(30, 6, 4, p);
        }
        canvas.Flush();
        return bitmap;
    }

    /// <summary>The merge glyph: trunk, curve and two dots (the third dot is drawn by the caller).</summary>
    static void DrawGlyph(SKCanvas canvas, SKColor color, float stroke = 2.6f)
    {
        using (var p = Stroke(color, stroke, round: true))
        {
            canvas.DrawLine(11, 6.5f, 11, 25.5f, p);
            using var curve = new SKPath();
            curve.MoveTo(25, 10.5f);
            curve.CubicTo(25, 17.5f, 11, 15.5f, 11, 23.5f);
            canvas.DrawPath(curve, p);
        }
        using (var p = Fill(color))
        {
            canvas.DrawCircle(11, 6.5f, 3.2f, p);
            canvas.DrawCircle(11, 27, 3.2f, p);
        }
    }
}
