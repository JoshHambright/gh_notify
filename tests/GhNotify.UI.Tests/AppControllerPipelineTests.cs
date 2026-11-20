using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>Dismissing pipeline rows on the real AppController. Items come from a seeded state.json; nothing polls.</summary>
public sealed class AppControllerPipelineTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("ghn-pipe").FullName;
    readonly FakePlatform _platform = new();
    readonly ConfigStore _config;
    readonly StateStore _state;
    AppController _controller;
    static readonly DateTimeOffset Finished = DateTimeOffset.Now.AddMinutes(-30);

    static InboxItem Pipe(string key, string outcome) =>
        new(key, Provider.Ado, ItemKind.PipelineRun, "acme/P", "Deploy", "https://dev.azure.com/acme/P/_build/results?buildId=1", "alice",
            Finished, null, null, outcome)
        { SubscriptionId = "s1", Finished = Finished };

    static InboxItem Pr() =>
        new("gh:review:acme/api#1", Provider.GitHub, ItemKind.ReviewRequested, "acme/api", "T", "https://github.com/acme/api/pull/1", "alice",
            DateTimeOffset.Now.AddHours(-1), "abc1234", 10, null);

    public AppControllerPipelineTests()
    {
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath, """
            {"github":{"enabled":false},"ado":{"enabled":true,"discoverOrgs":false,"orgs":["acme"]},
             "pipelineSubscriptions":[{"id":"s1","provider":"ado","org":"acme","project":"P","pipelineId":42,"pipeline":"Deploy","on":["failed","succeeded"],"createdAt":"2026-01-01T00:00:00Z"}]}
            """);
        _config = new ConfigStore(configPath);
        _config.Load();
        _state = new StateStore(Path.Combine(_root, "state.json"));
        _state.Save(new AppState
        {
            Snapshot = new()
            {
                ["ADO-Pipelines:acme"] = [Pipe("pipe:ado:acme/P:42:1:1", "Failed"), Pipe("pipe:ado:acme/P:42:2:1", "Failed"), Pipe("pipe:ado:acme/P:42:3:1", "Succeeded")],
                ["GitHub"] = [Pr()],
            },
        });
        _controller = Make();
    }

    AppController Make()
    {
        var dirs = new AppDirs(_root, Path.Combine(_root, "logs"), Path.Combine(_root, "wt"), Path.Combine(_root, "scratch"));
        return new AppController(_config, _state, _platform.Services, new AppRuntime(new InlineDispatcher(), dirs, () => { }, _ => { }));
    }

    public void Dispose()
    {
        _controller.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    IReadOnlyList<string> Keys() => _controller.BuildSections(null).SelectMany(s => s.Rows).Select(r => r.Key).ToList();

    [Fact]
    public void Dismissing_a_row_removes_it_and_its_toast_and_it_stays_gone_after_a_restart()
    {
        Assert.Equal(3, _controller.HeaderCount); // two failed pipelines + the PR; the green run does not count
        _controller.DismissPipeline("pipe:ado:acme/P:42:1:1");

        Assert.DoesNotContain("pipe:ado:acme/P:42:1:1", Keys());
        Assert.Equal(2, _controller.HeaderCount);
        Assert.Contains(_platform.Notifier.Requests, r => r.Action == ToastAction.Remove && r.Tag == ItemTag.For("pipe:ado:acme/P:42:1:1"));
        Assert.Contains("pipe:ado:acme/P:42:1:1", _controller.DismissedPipelineKeys);
        var saved = Assert.Single(_state.Load().DismissedPipelineRuns);
        Assert.Equal("pipe:ado:acme/P:42:1:1", saved.Key);
        Assert.Equal(Finished, saved.Finished);

        _controller.Dispose();
        _controller = Make();
        Assert.DoesNotContain("pipe:ado:acme/P:42:1:1", Keys());
    }

    [Fact]
    public void Dismiss_all_clears_every_pipeline_row_and_leaves_other_rows()
    {
        _controller.DismissAllPipelines();
        Assert.Equal(["gh:review:acme/api#1"], Keys());
        Assert.Equal(3, _controller.DismissedPipelineKeys.Count);
    }

    [Fact]
    public void Dismissing_an_unknown_key_changes_nothing()
    {
        _controller.DismissPipeline("pipe:nope");
        Assert.Empty(_controller.DismissedPipelineKeys);
        Assert.Equal(4, Keys().Count);
    }

    [Fact]
    public void Old_dismissals_are_forgotten_when_the_app_starts()
    {
        _controller.Dispose(); // may save; seed the file only afterwards
        _state.Save(new AppState
        {
            Snapshot = _state.Load().Snapshot,
            DismissedPipelineRuns = [new DismissedRun("old", DateTimeOffset.Now.AddDays(-20)), new DismissedRun("recent", DateTimeOffset.Now.AddDays(-1))],
        });
        _controller = Make();
        Assert.Equal(["recent"], _controller.DismissedPipelineKeys.Order());
    }

    [Fact]
    public void The_pipelines_section_offers_dismiss_all_and_rows_offer_dismiss()
    {
        var section = _controller.BuildSections(null).Single(s => s.Id == "PipelineRun");
        Assert.True(section.DismissAll);
        Assert.All(section.Rows, r => Assert.True(r.CanDismiss));
        Assert.False(_controller.BuildSections("deploy").Single(s => s.Id == "PipelineRun").DismissAll); // not while searching
    }
}
