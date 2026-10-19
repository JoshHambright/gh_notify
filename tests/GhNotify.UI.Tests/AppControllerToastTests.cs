using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>Toast clicks on the real AppController: a finished pre-review resumes its Claude session, other toasts open their URL.</summary>
public sealed class AppControllerToastTests : IDisposable
{
    const string Key = "gh:review:acme/api#1";
    const string Url = "https://github.com/acme/api/pull/1";
    const string Sid = "0b7e4f0e-3c1a-4a52-9d39-1f2a3b4c5d6e";

    readonly string _root = Directory.CreateTempSubdirectory("ghn-toast").FullName;
    readonly FakePlatform _platform = new();

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    AppController Controller(AgentRun? run)
    {
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath, """{"github":{"enabled":false},"ado":{"enabled":false}}""");
        var config = new ConfigStore(configPath);
        config.Load();
        var state = new StateStore(Path.Combine(_root, "state.json"));
        var item = new InboxItem(Key, Provider.GitHub, ItemKind.ReviewRequested, "acme/api", "Add retries", Url, "alice",
            DateTimeOffset.Now.AddHours(-1), "abc1234", 10, null);
        state.Save(new AppState { Snapshot = new() { ["GitHub"] = [item] }, AgentRuns = run is null ? [] : [run] });
        var dirs = new AppDirs(_root, Path.Combine(_root, "logs"), Path.Combine(_root, "wt"), Path.Combine(_root, "scratch"));
        return new AppController(config, state, _platform.Services, new AppRuntime(new InlineDispatcher(), dirs, () => { }, _ => { }));
    }

    AgentRun Run(AgentState state, string? worktree) => new()
    {
        Key = Key,
        Profile = "peer-review",
        State = state,
        SessionId = Sid,
        WorktreePath = worktree,
        HeadSha = "abc1234",
        FinishedAt = DateTimeOffset.Now,
    };

    [Fact]
    public void Clicking_a_pre_review_ready_toast_resumes_the_claude_session_instead_of_opening_the_PR()
    {
        var wt = Directory.CreateDirectory(Path.Combine(_root, "wt", "abc")).FullName;
        using var controller = Controller(Run(AgentState.Ready, wt));

        Assert.True(controller.OnToastActivated(ItemTag.ForAgent(Key), ToastActions.ForUrl(Url)));

        var (dir, command) = Assert.Single(_platform.Shell.Terminals);
        Assert.Equal(wt, dir);
        Assert.Contains(Sid, command);
        Assert.Empty(_platform.Shell.Urls);
    }

    [Theory]
    [InlineData(AgentState.Failed, true)]  // a failed run: the PR, not a half-finished session
    [InlineData(AgentState.Ready, false)]  // the worktree was cleaned up
    public void Other_agent_toasts_still_open_the_PR(AgentState state, bool worktreeExists)
    {
        var wt = Path.Combine(_root, "wt", "abc");
        if (worktreeExists) Directory.CreateDirectory(wt);
        using var controller = Controller(Run(state, wt));

        Assert.True(controller.OnToastActivated(ItemTag.ForAgent(Key), ToastActions.ForUrl(Url)));

        Assert.Empty(_platform.Shell.Terminals);
        Assert.Equal([Url], _platform.Shell.Urls);
    }

    [Fact]
    public void An_item_toast_opens_its_URL_and_one_without_a_URL_opens_the_popup()
    {
        using var controller = Controller(run: null);

        Assert.True(controller.OnToastActivated(ItemTag.For(Key), ToastActions.ForUrl(Url)));
        Assert.Equal([Url], _platform.Shell.Urls);
        Assert.False(controller.OnToastActivated(NotificationPolicy.SummaryTag, ToastActions.Open));
        Assert.Empty(_platform.Shell.Terminals);
    }
}
