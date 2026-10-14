using System.Linq;
using System.Windows.Media.Imaging;
using GhNotify.App;

namespace GhNotify.App.Tests;

public class TrayIconRendererTests
{
    static T Sta<T>(Func<T> f)
    {
        T result = default!; Exception? err = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err is not null) throw err;
        return result;
    }

    [Theory]
    [InlineData(16, null, false)]
    [InlineData(20, "3", false)]
    [InlineData(24, "99+", false)]
    [InlineData(32, "3", true)]
    [InlineData(32, null, true)]
    public void Renders_requested_size(int px, string? badge, bool attention)
    {
        var (w, h) = Sta(() => { var b = TrayIconRenderer.Render(px, badge, attention); return (b.PixelWidth, b.PixelHeight); });
        Assert.Equal(px, w);
        Assert.Equal(px, h);
    }

    [Fact]
    public void Top_left_is_navy_and_top_right_is_red_in_accent_mark()
    {
        var px = Sta(() =>
        {
            var b = new FormatConvertedBitmap(TrayIconRenderer.RenderAccent(64), System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var buf = new byte[64 * 64 * 4]; b.CopyPixels(buf, 64 * 4, 0);
            return buf;
        });
        (byte b, byte g, byte r) At(int x, int y) { var i = (y * 64 + x) * 4; return (px[i], px[i + 1], px[i + 2]); }
        Assert.Equal((0x1A, 0x0C, 0x00), At(10, 40));   // navy body
        Assert.Equal((0x2F, 0x35, 0xED), At(56, 10));   // red sweep
    }

    static bool IsRed(byte b, byte g, byte r, byte a) => a > 200 && r > 200 && g < 90 && b < 90;
    static bool IsAmber(byte b, byte g, byte r, byte a) => a > 200 && r > 220 && g > 150 && g < 210 && b < 60;

    static List<(int X, int Y, byte B, byte G, byte R, byte A)> Pixels(Func<BitmapSource> render) => Sta(() =>
    {
        var bmp = new FormatConvertedBitmap(render(), System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var px = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
        bmp.CopyPixels(px, bmp.PixelWidth * 4, 0);
        var list = new List<(int, int, byte, byte, byte, byte)>();
        for (var y = 0; y < bmp.PixelHeight; y++)
            for (var x = 0; x < bmp.PixelWidth; x++)
            {
                var i = (y * bmp.PixelWidth + x) * 4;
                list.Add((x, y, px[i], px[i + 1], px[i + 2], px[i + 3]));
            }
        return list;
    });

    [Fact]
    public void Nothing_waiting_has_no_red() =>
        Assert.DoesNotContain(Pixels(() => TrayIconRenderer.Render(32, null, false)), p => IsRed(p.B, p.G, p.R, p.A));

    [Fact]
    public void Waiting_items_show_red_badge_in_the_corner()
    {
        var corner = Pixels(() => TrayIconRenderer.Render(32, "3", false)).Where(p => p.X >= 21 && p.Y <= 11).ToList();
        Assert.True(corner.Count(p => IsRed(p.B, p.G, p.R, p.A)) > 10);
    }

    [Fact]
    public void Attention_without_items_is_an_amber_dot_and_no_red()
    {
        var px = Pixels(() => TrayIconRenderer.Render(32, null, true));
        Assert.Contains(px, p => IsAmber(p.B, p.G, p.R, p.A));
        Assert.DoesNotContain(px, p => IsRed(p.B, p.G, p.R, p.A));
    }

    [Fact]
    public void Accent_mark_keeps_the_red_sweep() =>
        Assert.True(Pixels(() => TrayIconRenderer.RenderAccent(32)).Count(p => IsRed(p.B, p.G, p.R, p.A)) > 20);
}
