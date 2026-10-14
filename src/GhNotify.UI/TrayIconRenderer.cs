using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GhNotify.App;

public static class TrayIconRenderer
{
    static readonly Color Navy = Color.FromRgb(0x00, 0x0C, 0x1A);
    static readonly Color Red = Color.FromRgb(0xED, 0x35, 0x2F);
    static readonly Color Amber = Color.FromRgb(0xF9, 0xB7, 0x00);

    static Typeface? _heavy;
    static Typeface Heavy => _heavy ??= MakeHeavy();

    static Typeface MakeHeavy()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack; // registers the pack:// scheme when no Application exists (tests)
        return new Typeface(
        new FontFamily(new Uri("pack://application:,,,/GhNotify;component/Fonts/"), "./#Roboto, Segoe UI"),
        FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
    }

    static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static Pen P(Color c, double w, bool round = false)
    {
        var p = new Pen(B(c), w);
        if (round) { p.StartLineCap = PenLineCap.Round; p.EndLineCap = PenLineCap.Round; }
        p.Freeze();
        return p;
    }

    public static BitmapSource Render(int sizePx, string? badge, bool attention) => Draw(sizePx, badge, attention, accent: false);

    public static BitmapSource RenderAccent(int sizePx) => Draw(sizePx, null, false, accent: true);

    static BitmapSource Draw(int sizePx, string? badge, bool attention, bool accent)
    {
        var white = Colors.White;
        var red = accent || badge is not null;
        var dv = new DrawingVisual();
        RenderOptions.SetEdgeMode(dv, EdgeMode.Unspecified);
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(sizePx / 36.0, sizePx / 36.0));

            var tile = new RectangleGeometry(new Rect(0, 0, 36, 36), 9, 9);
            tile.Freeze();
            dc.DrawGeometry(B(Navy), null, tile);

            dc.PushClip(tile);
            if (red) dc.DrawEllipse(B(Red), null, new Point(34, 2), 19, 19);
            dc.Pop();

            // merge glyph
            dc.DrawLine(P(white, 2.6, true), new Point(11, 6.5), new Point(11, 25.5));
            var curve = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(25, 10.5) };
            fig.Segments.Add(new BezierSegment(new Point(25, 17.5), new Point(11, 15.5), new Point(11, 23.5), true));
            curve.Figures.Add(fig);
            curve.Freeze();
            dc.DrawGeometry(null, P(white, 2.6, true), curve);

            dc.DrawEllipse(B(white), null, new Point(11, 6.5), 3.2, 3.2);
            dc.DrawEllipse(B(white), null, new Point(11, 27), 3.2, 3.2);
            if (red) dc.DrawEllipse(B(Red), P(white, 2.2), new Point(25, 9), 3.6, 3.6);
            else dc.DrawEllipse(B(white), null, new Point(25, 9), 3.6, 3.6);

            if (badge is not null)
            {
                dc.DrawEllipse(B(Red), P(attention ? Amber : white, 1.2), new Point(30, 6), 5.5, 5.5);
                var size = badge.Length >= 3 ? 6.0 : 7.0;
                var ft = new FormattedText(badge, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Heavy, size, B(white), 1.0);
                dc.DrawText(ft, new Point(30 - ft.WidthIncludingTrailingWhitespace / 2, 6 - ft.Height / 2));
            }
            else if (attention)
            {
                dc.DrawEllipse(B(Amber), null, new Point(30, 6), 4, 4);
            }
            dc.Pop();
        }

        var bmp = new RenderTargetBitmap(sizePx, sizePx, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }
}
