using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>Hide and Unhide on the real AppController: config.json, the view, toasts and Undo. Items come from a seeded state.json; nothing polls.</summary>
public sealed class AppControllerHideTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("ghn-hide").FullName;
    readonly FakePlatform _platform = new();
    readonly ConfigStore _config;
    readonly AppController _controller;

    static InboxItem Pr(string key, string repo) =>
        new(key, Provider.GitHub, ItemKind.ReviewRequested, repo, "T " + key, "https://github.com/" + repo + "/pull/1", "alice",
            DateTimeOffset.Now.AddHours(-1), "abc1234", 10, null);

    public AppControllerHideTests()
    {
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath, """{"github":{"enabled":false},"ado":{"enabled":false},"filters":{"hide":["gh:fabrikam/sandbox"]}}""");
        _config = new ConfigStore(configPath);
        _config.Load();
        var state = new StateStore(Path.Combine(_root, "state.json"));
        state.Save(new AppState
        {
            Snapshot = new() { ["GitHub"] = [Pr("gh:review:acme/api#1", "acme/api"), Pr("gh:review:acme/web#2", "acme/web"), Pr("gh:review:contoso/app#3", "contoso/app")] },
        });
        var dirs = new AppDirs(_root, Path.Combine(_root, "logs"), Path.Combine(_root, "wt"), Path.Combine(_root, "scratch"));
        _controller = new AppController(_config, state, _platform.Services, new AppRuntime(new InlineDispatcher(), dirs, () => { }, _ => { }));
    }

    public void Dispose()
    {
        _controller.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    IReadOnlyList<string> Keys() => _controller.BuildSections(null).SelectMany(s => s.Rows).Select(r => r.Key).ToList();

    [Fact]
    public void Hide_writes_the_pattern_removes_the_rows_and_their_toasts_and_undo_brings_them_back()
    {
        Assert.Equal(3, _controller.HeaderCount);
        _controller.HideMatching("gh:review:acme/api#1", MuteScope.Org);

        Assert.Equal(["gh:fabrikam/sandbox", "gh:acme"], _config.Current.Filters.Hide);
        Assert.Contains("gh:acme", File.ReadAllText(_config.Path));
        Assert.Equal(["gh:review:contoso/app#3"], Keys());
        Assert.Equal(1, _controller.HeaderCount);
        Assert.Equal("gh:acme", _controller.JustHidden);
        var removed = _platform.Notifier.Requests.Where(r => r.Action == ToastAction.Remove).Select(r => r.Tag).ToList();
        Assert.Contains(ItemTag.For("gh:review:acme/api#1"), removed);
        Assert.Contains(ItemTag.For("gh:review:acme/web#2"), removed);
        Assert.DoesNotContain(ItemTag.For("gh:review:contoso/app#3"), removed);
        Assert.Equal([new HiddenFilter("gh:fabrikam/sandbox", 0), new HiddenFilter("gh:acme", 2)], _controller.HiddenFilters());

        _controller.Unhide("GH:ACME"); // patterns match case-insensitively
        Assert.Equal(["gh:fabrikam/sandbox"], _config.Current.Filters.Hide);
        Assert.Null(_controller.JustHidden);
        Assert.Equal(3, Keys().Count);
    }

    [Fact]
    public void Undo_is_offered_until_the_popup_is_shown_again()
    {
        _controller.HideMatching("gh:review:acme/api#1", MuteScope.Repo);
        Assert.Equal("gh:acme/api", _controller.JustHidden);
        _controller.OnPopupShown();
        Assert.Null(_controller.JustHidden);
        Assert.Contains("gh:acme/api", _config.Current.Filters.Hide); // still hidden
    }

    [Fact]
    public void Hiding_a_pattern_that_is_already_listed_offers_no_undo_so_undo_cannot_remove_it()
    {
        File.WriteAllText(_config.Path, """{"github":{"enabled":false},"ado":{"enabled":false},"filters":{"hide":["GH:ACME/API"]}}""");
        _config.Load();
        _controller.HideMatching("gh:review:acme/api#1", MuteScope.Repo);
        Assert.Null(_controller.JustHidden);
        Assert.Equal(["GH:ACME/API"], _config.Current.Filters.Hide);
    }

    [Fact]
    public void Unhide_of_a_pattern_that_is_already_gone_does_not_rewrite_the_config()
    {
        var before = File.ReadAllText(_config.Path);
        var hash = _config.LoadedHash;
        _controller.Unhide("gh:nothing/here");
        Assert.Equal(before, File.ReadAllText(_config.Path));
        Assert.Equal(hash, _config.LoadedHash);
        Assert.False(File.Exists(_config.Path + ".bak"));
    }

    [Fact]
    public void Muted_rows_start_in_a_collapsed_Muted_section_and_the_toggle_is_remembered()
    {
        _controller.Mute("gh:review:acme/web#2", MuteScope.Repo, MuteDuration.OneHour);
        var muted = _controller.BuildSections(null).Single(s => s.Id == InboxPresenter.MutedSectionId);
        Assert.False(muted.Expanded);
        Assert.Equal(1, muted.Count);
        Assert.Equal(2, _controller.HeaderCount);
        Assert.DoesNotContain("gh:review:acme/web#2", Keys());

        _controller.ToggleSection(InboxPresenter.MutedSectionId);
        Assert.Contains("gh:review:acme/web#2", Keys());
        Assert.True(new StateStore(Path.Combine(_root, "state.json")).Load().MutedExpanded);
        // Searching opens it even when collapsed.
        _controller.ToggleSection(InboxPresenter.MutedSectionId);
        Assert.Contains("gh:review:acme/web#2", _controller.BuildSections("acme/web").SelectMany(s => s.Rows).Select(r => r.Key));
    }
}
