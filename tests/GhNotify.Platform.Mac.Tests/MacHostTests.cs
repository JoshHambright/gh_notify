using GhNotify.App.Mac;

namespace GhNotify.Platform.Mac.Tests;

public class MacHostTests
{
    [Fact]
    public void Paths_live_under_Library_and_honour_the_env_overrides()
    {
        var home = Path.Combine(Path.GetTempPath(), "alice");
        var defaults = new MacPaths(home, _ => null);
        var support = Path.Combine(home, "Library", "Application Support");
        Assert.Equal(Path.Combine(support, "GhNotify", "config.json"), defaults.ConfigFile);
        Assert.Equal(Path.Combine(support, "GhNotify-data", "state.json"), defaults.StateFile);
        Assert.Equal(Path.Combine(home, "Library", "Logs", "GhNotify"), defaults.Logs);

        var env = new Dictionary<string, string> { ["GHNOTIFY_CONFIG_DIR"] = "/tmp/cfg", ["GHNOTIFY_DATA_DIR"] = "/tmp/data" };
        var overridden = new MacPaths(home, n => env.GetValueOrDefault(n));
        Assert.Equal(Path.Combine("/tmp/cfg", "config.json"), overridden.ConfigFile);
        Assert.Equal(Path.Combine("/tmp/data", "logs"), overridden.Logs);
        Assert.Equal(Path.Combine("/tmp/data", "instance.lock"), overridden.InstanceLock);
        Assert.Equal(defaults.ConfigFile, new MacPaths(home, _ => "").ConfigFile); // empty = not set
    }

    [Fact]
    public void Only_one_instance_holds_the_lock_and_it_is_released_on_dispose()
    {
        var path = Path.Combine(Path.GetTempPath(), "ghn-lock-" + Guid.NewGuid().ToString("N"), "instance.lock");
        using (var first = InstanceLock.TryAcquire(path))
        {
            Assert.NotNull(first);
            Assert.Null(InstanceLock.TryAcquire(path));
        }
        using var again = InstanceLock.TryAcquire(path);
        Assert.NotNull(again);
    }

    [Fact]
    public void Iconset_has_every_size_iconutil_needs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ghn-iconset-" + Guid.NewGuid().ToString("N"), "GhNotify.iconset");
        try
        {
            Program.WriteIconset(dir);
            foreach (var (name, px) in Program.IconsetSizes)
            {
                using var bmp = SkiaSharp.SKBitmap.Decode(Path.Combine(dir, name));
                Assert.Equal(px, bmp.Width);
            }
            Assert.Equal(10, Directory.GetFiles(dir).Length);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch (IOException) { }
        }
    }
}
