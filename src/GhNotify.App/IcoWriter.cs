using System.Windows.Media.Imaging;

namespace GhNotify.App;

public static class IcoWriter
{
    public static void Write(string path, params int[] sizes) =>
        File.WriteAllBytes(path, Build(sizes.Select(s => TrayIconRenderer.RenderAccent(s)).ToArray()));

    /// <summary>Packs PNG-encoded frames into an .ico (Vista+ PNG entries).</summary>
    public static byte[] Build(params BitmapSource[] frames)
    {
        var blobs = frames.Select(f =>
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(f));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }).ToArray();

        using var output = new MemoryStream();
        using var w = new BinaryWriter(output);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)frames.Length);
        var offset = 6 + 16 * frames.Length;
        for (var i = 0; i < frames.Length; i++)
        {
            var size = frames[i].PixelWidth;
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);   // palette
            w.Write((byte)0);   // reserved
            w.Write((ushort)1); // planes
            w.Write((ushort)32);// bpp
            w.Write(blobs[i].Length);
            w.Write(offset);
            offset += blobs[i].Length;
        }
        foreach (var b in blobs) w.Write(b);
        w.Flush();
        return output.ToArray();
    }
}
