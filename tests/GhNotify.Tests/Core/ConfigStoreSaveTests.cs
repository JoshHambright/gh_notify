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

    [Fact]
    public void Loaded_hash_is_the_hash_of_the_bytes_current_came_from()
    {
        File.WriteAllText(PathJ, "{ \"pollSeconds\": 60 }");
        var store = new ConfigStore(PathJ);
        store.Load();
        var good = store.FileHash();
        Assert.Equal(good, store.LoadedHash);

        File.WriteAllText(PathJ, "{ \"pollSeconds\": 5 }"); // invalid: the last good config (and its hash) stay
        store.Load();
        Assert.NotNull(store.Error);
        Assert.Equal(good, store.LoadedHash);
        Assert.NotEqual(store.FileHash(), store.LoadedHash);

        File.WriteAllText(PathJ, "{ \"pollSeconds\": 90 }"); // edited, not reloaded yet
        Assert.Equal(good, store.LoadedHash);
    }

    [Fact]
    public void Loaded_hash_follows_saves_and_the_default_file()
    {
        var store = new ConfigStore(PathJ);
        Assert.Null(store.LoadedHash);
        store.Load(); // writes the default file
        Assert.Equal(store.FileHash(), store.LoadedHash);
        var next = ConfigStore.Clone(store.Current);
        next.PollSeconds = 90;
        store.Save(next, store.LoadedHash);
        Assert.Equal(store.FileHash(), store.LoadedHash);
    }

    [Fact]
    public void Is_current_until_the_file_changes_or_a_load_failed()
    {
        File.WriteAllText(PathJ, "{ \"pollSeconds\": 60 }");
        var store = new ConfigStore(PathJ);
        Assert.False(store.IsCurrent); // never loaded
        store.Load();
        Assert.True(store.IsCurrent);
        var next = ConfigStore.Clone(store.Current);
        next.PollSeconds = 90;
        store.Save(next, store.LoadedHash);
        Assert.True(store.IsCurrent); // our own save: the watcher's reload can be skipped
        File.WriteAllText(PathJ, "{ \"pollSeconds\": 5 }");
        Assert.False(store.IsCurrent);
        store.Load();
        Assert.False(store.IsCurrent); // failed load: keep trying
        File.WriteAllText(PathJ, JsonSerializer.Serialize(next, Json.Options));
        Assert.False(store.IsCurrent); // same bytes as the good load, but the error must still be cleared by a reload
    }
}
