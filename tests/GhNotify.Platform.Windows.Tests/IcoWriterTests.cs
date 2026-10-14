using GhNotify.Platform.Windows;
using GhNotify.UI;

namespace GhNotify.Platform.Windows.Tests;

public class IcoWriterTests
{
    [Fact]
    public void Writes_multi_size_ico_header()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ghn-{Guid.NewGuid()}.ico");
        try
        {
            IcoWriter.Write(path, 16, 32, 256);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));   // reserved
            Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));   // type = icon
            Assert.Equal(3, BitConverter.ToUInt16(bytes, 4));   // count
            Assert.Equal(16, bytes[6]);                         // first entry width
            Assert.Equal(0, bytes[6 + 16 * 2]);                 // 256 encoded as 0
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Frames_are_png_entries_at_the_recorded_offsets()
    {
        using var a = TrayIconRenderer.Render(16, "7", true);
        using var b = TrayIconRenderer.Render(32, "7", true);
        var ico = IcoWriter.Build(a, b);
        for (var i = 0; i < 2; i++)
        {
            var length = BitConverter.ToInt32(ico, 6 + 16 * i + 8);
            var offset = BitConverter.ToInt32(ico, 6 + 16 * i + 12);
            Assert.True(length > 0 && offset + length <= ico.Length);
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], ico[offset..(offset + 4)]); // PNG signature
        }
    }
}
