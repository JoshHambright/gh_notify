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
}
