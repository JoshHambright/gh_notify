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
        { Number = 12, SourceRef = "pull/12/head", CloneUrl = "https://github.com/acme/api" };
        var run = new AgentRun { Key = item.Key, Profile = "peer-review", State = AgentState.Ready, SessionId = "abc", CostUsd = 0.42m };
        store.Save(new AppState { Snapshot = new() { ["GitHub"] = [item] }, AgentRuns = [run] });

        var loaded = store.Load();
        Assert.Equal(item, loaded.Snapshot["GitHub"].Single());
        Assert.Equal(run, loaded.AgentRuns.Single());
    }

    [Fact]
    public void Missing_file_gives_empty_state() =>
        Assert.Empty(new StateStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "s.json")).Load().Snapshot);

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
