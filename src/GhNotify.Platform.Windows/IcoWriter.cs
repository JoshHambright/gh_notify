using GhNotify.UI;
using SkiaSharp;

namespace GhNotify.Platform.Windows;

public static class IcoWriter
{
    public static void Write(string path, params int[] sizes)
    {
        var frames = sizes.Select(TrayIconRenderer.RenderAccent).ToArray();
        try { File.WriteAllBytes(path, Build(frames)); }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    /// <summary>Packs PNG-encoded frames into an .ico (Vista+ PNG entries).</summary>
    public static byte[] Build(params SKBitmap[] frames)
    {
        var blobs = frames.Select(TrayIconRenderer.ToPng).ToArray();

        using var output = new MemoryStream();
        using var w = new BinaryWriter(output);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)frames.Length);
        var offset = 6 + 16 * frames.Length;
        for (var i = 0; i < frames.Length; i++)
        {
            var size = frames[i].Width;
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
