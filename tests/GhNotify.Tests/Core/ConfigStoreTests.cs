using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ConfigStoreTests
{
    static string TempPath() => Path.Combine(Directory.CreateTempSubdirectory("ghn-cfg").FullName, "sub", "config.json");

    [Fact]
    public void Missing_file_writes_defaults()
    {
        var path = TempPath();
        var store = new ConfigStore(path);
        store.Load();
        Assert.Null(store.Error);
        Assert.Equal(120, store.Current.PollSeconds);
        Assert.True(store.Current.Ado.DiscoverOrgs);
        var text = File.ReadAllText(path);
        Assert.Contains("\"github\"", text);
        Assert.Contains("\"agentAllowedTools\"", text);
    }

    [Fact]
    public void Reads_comments_trailing_commas_and_enums()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              // poll faster
              "pollSeconds": 60,
              "github": { "watchRepos": ["acme/deploy"], },
              "rules": [ { "match": { "kind": "ReviewRequested", "repo": "acme/*" }, "agent": "auto" } ],
              "hooks": [ { "event": "ItemAdded", "run": ["pwsh", "-File", "x.ps1"] } ],
            }
            """);
        var store = new ConfigStore(path);
        store.Load();
        Assert.Null(store.Error);
        Assert.Equal(60, store.Current.PollSeconds);
        Assert.Equal(["acme/deploy"], store.Current.GitHub.WatchRepos);
        Assert.Equal(ItemKind.ReviewRequested, store.Current.Rules[0].Match!.Kind);
        Assert.Equal(AgentMode.Auto, store.Current.Rules[0].Agent);
        Assert.Equal(HookEvent.ItemAdded, store.Current.Hooks[0].Event);
    }

    [Fact]
    public void Invalid_json_keeps_last_good_config()
    {
        var path = TempPath();
        var store = new ConfigStore(path);
        store.Load();
        File.WriteAllText(path, """{ "pollSeconds": 90 }""");
        store.Load();
        var good = store.Current;
        File.WriteAllText(path, "{ not json");
        store.Load();
        Assert.Same(good, store.Current);
        Assert.NotNull(store.Error);
    }

    [Fact]
    public void Rejects_poll_seconds_below_30()
    {
        var path = TempPath();
        var store = new ConfigStore(path);
        store.Load();
        File.WriteAllText(path, """{ "pollSeconds": 5 }""");
        store.Load();
        Assert.Equal(120, store.Current.PollSeconds);
        Assert.Contains("pollSeconds", store.Error);
    }

    [Fact]
    public void Rejects_hook_without_command()
    {
        var path = TempPath();
        var store = new ConfigStore(path);
        store.Load();
        File.WriteAllText(path, """{ "hooks": [ { "event": "ItemAdded", "run": [] } ] }""");
        store.Load();
        Assert.Contains("hooks[0].run", store.Error);
    }
}
