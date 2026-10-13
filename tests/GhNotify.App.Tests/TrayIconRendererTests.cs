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
    public void Top_left_is_navy_and_top_right_is_red_without_badge()
    {
        var px = Sta(() =>
        {
            var b = new FormatConvertedBitmap(TrayIconRenderer.Render(64, null, false), System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var buf = new byte[64 * 64 * 4]; b.CopyPixels(buf, 64 * 4, 0);
            return buf;
        });
        (byte b, byte g, byte r) At(int x, int y) { var i = (y * 64 + x) * 4; return (px[i], px[i + 1], px[i + 2]); }
        Assert.Equal((0x1A, 0x0C, 0x00), At(10, 40));   // navy body
        Assert.Equal((0x2F, 0x35, 0xED), At(56, 10));   // red sweep
    }
}
