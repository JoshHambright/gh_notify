using SkiaSharp;

namespace GhNotify.UI.Tests;

public class TrayIconRendererTests
{
    [Theory]
    [InlineData(16, null, false)]
    [InlineData(20, "3", false)]
    [InlineData(24, "99+", false)]
    [InlineData(32, "3", true)]
    [InlineData(32, null, true)]
    public void Renders_requested_size(int px, string? badge, bool attention)
    {
        using var b = TrayIconRenderer.Render(px, badge, attention);
        Assert.Equal(px, b.Width);
        Assert.Equal(px, b.Height);
    }

    [Fact]
    public void Top_left_is_navy_and_top_right_is_red_in_accent_mark()
    {
        using var b = TrayIconRenderer.RenderAccent(64);
        var navy = b.GetPixel(10, 40);
        var red = b.GetPixel(56, 10);
        Assert.Equal((0x00, 0x0C, 0x1A), (navy.Red, navy.Green, navy.Blue)); // navy body
        Assert.Equal((0xED, 0x35, 0x2F), (red.Red, red.Green, red.Blue));    // red sweep
    }

    static bool IsRed(SKColor c) => c.Alpha > 200 && c.Red > 200 && c.Green < 90 && c.Blue < 90;
    static bool IsAmber(SKColor c) => c.Alpha > 200 && c.Red > 220 && c.Green > 150 && c.Green < 210 && c.Blue < 60;

    static List<(int X, int Y, SKColor Color)> Pixels(Func<SKBitmap> render)
    {
        using var bmp = render();
        var list = new List<(int, int, SKColor)>();
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
                list.Add((x, y, bmp.GetPixel(x, y)));
        return list;
    }

    [Fact]
    public void Nothing_waiting_has_no_red() =>
        Assert.DoesNotContain(Pixels(() => TrayIconRenderer.Render(32, null, false)), p => IsRed(p.Color));

    [Fact]
    public void Waiting_items_show_red_badge_in_the_corner()
    {
        var corner = Pixels(() => TrayIconRenderer.Render(32, "3", false)).Where(p => p.X >= 21 && p.Y <= 11).ToList();
        Assert.True(corner.Count(p => IsRed(p.Color)) > 10);
    }

    [Fact]
    public void The_count_is_drawn_in_white_on_the_badge()
    {
        var badge = Pixels(() => TrayIconRenderer.Render(64, "3", false)).Where(p => p.X is >= 48 and <= 62 && p.Y <= 22).ToList();
        Assert.Contains(badge, p => p.Color.Alpha == 255 && p.Color.Red > 240 && p.Color.Green > 240 && p.Color.Blue > 240);
    }

    [Fact]
    public void Attention_without_items_is_an_amber_dot_and_no_red()
    {
        var px = Pixels(() => TrayIconRenderer.Render(32, null, true));
        Assert.Contains(px, p => IsAmber(p.Color));
        Assert.DoesNotContain(px, p => IsRed(p.Color));
    }

    [Fact]
    public void Accent_mark_keeps_the_red_sweep() =>
        Assert.True(Pixels(() => TrayIconRenderer.RenderAccent(32)).Count(p => IsRed(p.Color)) > 20);

    [Fact]
    public void Png_encoding_round_trips()
    {
        using var b = TrayIconRenderer.Render(32, "7", true);
        using var decoded = SKBitmap.Decode(TrayIconRenderer.ToPng(b));
        Assert.Equal((32, 32), (decoded.Width, decoded.Height));
    }
}
