namespace GhNotify.Core;

/// <summary>
/// The popup's width (dragged from its left edge, saved in state.json) and text size (Settings → General). The width is in
/// unscaled pixels: the window is <c>width × text scale</c> wide, so a larger text size keeps the same number of characters per line.
/// </summary>
public static class PopupSize
{
    public const double DefaultWidth = 376;
    public const double MaxWidth = 760;
    public static readonly IReadOnlyList<double> TextScales = [0.9, 1.0, 1.15, 1.3];
    // Average width of a repo-line character (10.5 px Segoe UI), used to give a wider popup longer repo names.
    const double RepoCharWidth = 6;

    /// <summary>Clamps a width to [default, max], and to what fits on the screen (<paramref name="fits"/>, unscaled pixels) when that is wider than the default.</summary>
    public static double ClampWidth(double width, double? fits = null)
    {
        var max = fits is { } f ? Math.Min(MaxWidth, Math.Max(DefaultWidth, f)) : MaxWidth;
        return double.IsFinite(width) ? Math.Round(Math.Clamp(width, DefaultWidth, max)) : DefaultWidth;
    }

    /// <summary>The nearest supported text scale (a hand-edited or old state.json may hold anything).</summary>
    public static double NormalizeScale(double scale) =>
        double.IsFinite(scale) ? TextScales.MinBy(s => Math.Abs(s - scale)) : 1.0;

    /// <summary>display.maxRepoChars is for the default width; a wider popup shows proportionally longer repo names.</summary>
    public static int RepoChars(int configured, double width) =>
        configured + (int)Math.Max(0, (ClampWidth(width) - DefaultWidth) / RepoCharWidth);
}
