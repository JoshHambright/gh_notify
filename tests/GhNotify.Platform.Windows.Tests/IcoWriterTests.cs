using GhNotify.App;

namespace GhNotify.App.Tests;

public class IcoWriterTests
{
    [Fact]
    public void Writes_multi_size_ico_header()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ghn-{Guid.NewGuid()}.ico");
        var t = new Thread(() => IcoWriter.Write(path, 16, 32, 256));
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));   // reserved
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));   // type = icon
        Assert.Equal(3, BitConverter.ToUInt16(bytes, 4));   // count
        Assert.Equal(16, bytes[6]);                         // first entry width
        Assert.Equal(0, bytes[6 + 16 * 2]);                 // 256 encoded as 0
    }

    [Fact]
    public void Tray_icon_bytes_load_as_a_drawing_icon()
    {
        // The tray (H.NotifyIcon) rejects RenderTargetBitmap ImageSources; App.UpdateTray hands it a System.Drawing.Icon instead.
        (int W, int H) size = default;
        var t = new Thread(() =>
        {
            var ico = IcoWriter.Build(TrayIconRenderer.Render(16, "7", true), TrayIconRenderer.Render(32, "7", true));
            using var icon = new System.Drawing.Icon(new MemoryStream(ico), 16, 16);
            size = (icon.Width, icon.Height);
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        Assert.Equal((16, 16), size);
    }
}
