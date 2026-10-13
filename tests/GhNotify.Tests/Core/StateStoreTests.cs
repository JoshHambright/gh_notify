using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class StateStoreTests
{
    [Fact]
    public void Round_trips_items_and_runs()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ghn-state").FullName, "x", "state.json");
        var store = new StateStore(path);
        var item = new InboxItem("gh:review:acme/api#12", Provider.GitHub, ItemKind.ReviewRequested, "acme/api", "T", "https://u", "alice",
            DateTimeOffset.Parse("2026-10-13T12:00:00Z"), "sha", 35, null)
        { Number = 12, SourceRef = "pull/12/head", CloneUrl = "https://github.com/acme/api", RunId = 9001, Org = "o", Project = "p" };
        var run = new AgentRun { Key = item.Key, Profile = "peer-review", State = AgentState.Ready, SessionId = "abc", CostUsd = 0.42m };
        store.Save(new AppState { Snapshot = new() { ["GitHub"] = [item] }, AgentRuns = [run], ThemeMode = ThemeMode.Light });

        var loaded = store.Load();
        Assert.Equal(item, loaded.Snapshot["GitHub"].Single());
        Assert.Equal(run, loaded.AgentRuns.Single());
        Assert.Equal(ThemeMode.Light, loaded.ThemeMode);
    }

    [Fact]
    public void Details_round_trip()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ghn-state").FullName, "state.json");
        var item = new InboxItem("k", Provider.Ado, ItemKind.ApprovalPending, "o/p", "t", "u", "", DateTimeOffset.UnixEpoch, null, null, null)
            { Details = [new ItemDetail("Pipeline", "api-release"), new ItemDetail("Run", "20261013.3")] };
        new StateStore(path).Save(new AppState { Snapshot = new() { ["ADO:o"] = [item] } });
        var loaded = new StateStore(path).Load().Snapshot["ADO:o"].Single();
        Assert.Equal(item.Details, loaded.Details!.ToArray());
    }

    [Fact]
    public void Missing_file_gives_empty_state() =>
        Assert.Empty(new StateStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "s.json")).Load().Snapshot);

    [Theory]
    [InlineData("""{ "snapshot": null, "agentRuns": null }""")]
    [InlineData("""{ "snapshot": null }""")]
    [InlineData("null")]
    public void Null_collections_default_to_empty(string json)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ghn-state").FullName, "state.json");
        File.WriteAllText(path, json);
        var state = new StateStore(path).Load();
        Assert.NotNull(state.Snapshot);
        Assert.NotNull(state.AgentRuns);
        Assert.Empty(state.Snapshot);
        Assert.Empty(state.AgentRuns);
    }

    [Fact]
    public void Locked_file_returns_usable_state_without_moving_it()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ghn-state").FullName, "state.json");
        File.WriteAllText(path, """{ "snapshot": {} }""");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var state = new StateStore(path).Load();
            Assert.Empty(state.Snapshot);
            Assert.Empty(state.AgentRuns);
        }
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Corrupt_file_is_set_aside_and_empty_state_returned()
    {
        var dir = Directory.CreateTempSubdirectory("ghn-state").FullName;
        var path = Path.Combine(dir, "state.json");
        File.WriteAllText(path, "{ broken");
        var state = new StateStore(path).Load();
        Assert.Empty(state.AgentRuns);
        Assert.True(File.Exists(path + ".bad"));
    }
}
