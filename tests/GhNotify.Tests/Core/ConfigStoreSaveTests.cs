using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ConfigStoreSaveTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ghn-save-" + Guid.NewGuid().ToString("N"));
    string PathJ => Path.Combine(_dir, "config.json");

    public ConfigStoreSaveTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Save_writes_backup_and_reloads()
    {
        File.WriteAllText(PathJ, "{ \"pollSeconds\": 60 }");
        var store = new ConfigStore(PathJ);
        store.Load();
        var hash = store.FileHash();
        var next = ConfigStore.Clone(store.Current);
        next.PollSeconds = 90;
        store.Save(next, hash);
        Assert.Equal(90, store.Current.PollSeconds);
        Assert.Equal("{ \"pollSeconds\": 60 }", File.ReadAllText(PathJ + ".bak"));
        Assert.Contains("\"pollSeconds\": 90", File.ReadAllText(PathJ));
    }

    [Fact]
    public void Save_refuses_when_file_changed_since_hash()
    {
        File.WriteAllText(PathJ, "{ \"pollSeconds\": 60 }");
        var store = new ConfigStore(PathJ);
        store.Load();
        var hash = store.FileHash();
        File.WriteAllText(PathJ, "{ \"pollSeconds\": 61 }"); // edited in a text editor meanwhile
        var next = ConfigStore.Clone(store.Current);
        next.PollSeconds = 90;
        Assert.Throws<ConfigConflictException>(() => store.Save(next, hash));
        Assert.Contains("61", File.ReadAllText(PathJ));
        store.Save(next, expectedHash: null); // Overwrite
        Assert.Contains("90", File.ReadAllText(PathJ));
    }

    [Fact]
    public void Save_rejects_invalid_config_and_writes_nothing()
    {
        File.WriteAllText(PathJ, "{}");
        var store = new ConfigStore(PathJ);
        store.Load();
        var next = ConfigStore.Clone(store.Current);
        next.PollSeconds = 1;
        Assert.Throws<InvalidDataException>(() => store.Save(next, store.FileHash()));
        Assert.Equal("{}", File.ReadAllText(PathJ));
        Assert.False(File.Exists(PathJ + ".bak"));
    }

    [Fact]
    public void Unknown_keys_survive_a_save()
    {
        File.WriteAllText(PathJ, "{ \"futureThing\": { \"a\": 1 } }");
        var store = new ConfigStore(PathJ);
        store.Load();
        store.Save(ConfigStore.Clone(store.Current), store.FileHash());
        using var doc = JsonDocument.Parse(File.ReadAllText(PathJ));
        Assert.Equal(1, doc.RootElement.GetProperty("futureThing").GetProperty("a").GetInt32());
    }

    [Fact]
    public void Clone_is_deep()
    {
        var a = new AppConfig();
        var b = ConfigStore.Clone(a);
        b.Filters.Mute.Add("acme");
        Assert.Empty(a.Filters.Mute);
    }
}
