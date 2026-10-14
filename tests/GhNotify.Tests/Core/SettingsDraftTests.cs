using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class SettingsDraftTests
{
    [Fact]
    public void Draft_is_a_deep_copy_and_tracks_dirty()
    {
        var current = new AppConfig();
        var d = new SettingsDraft(current, "h1");
        Assert.False(d.IsDirty);
        d.Config.Filters.Mute.Add("acme");
        Assert.True(d.IsDirty);
        Assert.Empty(current.Filters.Mute);
        Assert.Equal("h1", d.FileHashAtOpen);
        d.Config.Filters.Mute.Clear();
        Assert.False(d.IsDirty);
    }

    [Theory]
    [InlineData("pollSeconds", SettingsPage.General)]
    [InlineData("workHours.days", SettingsPage.General)]
    [InlineData("updates.checkHours", SettingsPage.General)]
    [InlineData("ado.orgs[0]", SettingsPage.Sources)]
    [InlineData("display.maxRepoChars", SettingsPage.Sources)]
    [InlineData("notifications.sounds[1].sound", SettingsPage.Notifications)]
    [InlineData("filters.hide[0]", SettingsPage.Filters)]
    [InlineData("rules[2].profile", SettingsPage.Workflows)]
    [InlineData("agentProfiles.x.command", SettingsPage.Workflows)]
    [InlineData("defaultProfiles.ReviewRequested", SettingsPage.Workflows)]
    [InlineData("hooks[0].run", SettingsPage.Workflows)]
    public void Errors_map_to_pages(string field, SettingsPage page) => Assert.Equal(page, SettingsDraft.PageFor(field));

    [Fact]
    public void Validate_returns_page_keyed_errors()
    {
        var d = new SettingsDraft(new AppConfig(), null);
        d.Config.PollSeconds = 1;
        d.Config.Filters.Hide.Add("x//y");
        var errors = d.Validate();
        Assert.Contains(errors, e => e.Page == SettingsPage.General && e.Field == "pollSeconds");
        Assert.Contains(errors, e => e.Page == SettingsPage.Filters && e.Field == "filters.hide[0]");
    }

    [Fact]
    public void Move_swaps_within_bounds()
    {
        var list = new List<string> { "a", "b", "c" };
        Assert.True(SettingsDraft.Move(list, 2, -1));
        Assert.Equal(["a", "c", "b"], list);
        Assert.False(SettingsDraft.Move(list, 0, -1));
        Assert.False(SettingsDraft.Move(list, 2, 1));
    }

    [Fact]
    public void Rule_and_sound_summaries_read_as_sentences()
    {
        var rule = new Rule { Match = new ItemMatch { Kind = ItemKind.ReviewRequested, Repo = "acme/*", MaxChangedLines = 400 }, Agent = AgentMode.Auto, Profile = "peer-review" };
        Assert.Equal("When Review requested in acme/* ≤ 400 lines → Pre-review · Auto", SettingsDraft.Summary(rule));
        Assert.Equal("When Any item in any repo → Diagnose · Button",
            SettingsDraft.Summary(new Rule { Profile = "diagnose-ci" }));
        Assert.Equal("When Approval pending in acme/* → Alarm · alert off-hours",
            SettingsDraft.Summary(new SoundRule { Match = new ItemMatch { Kind = ItemKind.ApprovalPending, Repo = "acme/*" }, Sound = "builtin:alarm", AlertOffHours = true }));
        Assert.Equal("ding.wav", SettingsDraft.SoundLabel(@"C:\sounds\ding.wav"));
        Assert.Equal("Windows default", SettingsDraft.SoundLabel("default"));
    }

    [Fact]
    public void Count_matches_for_filter_hints()
    {
        InboxItem I(string repo) => new("k" + repo, Provider.GitHub, ItemKind.ReviewRequested, repo, "T", "https://u", "a", DateTimeOffset.UnixEpoch, null, null, null);
        Assert.Equal(2, SettingsDraft.CountMatches("acme", [I("acme/a"), I("acme/b"), I("fabrikam/c")]));
        Assert.Equal(-1, SettingsDraft.CountMatches("acme//", [I("acme/a")]));
    }

    [Theory]
    [InlineData("{ \"a\": 1 }", false)]
    [InlineData("{ \"url\": \"https://x//y\" }", false)]
    [InlineData("{ // note\n \"a\": 1 }", true)]
    [InlineData("{ /* note */ \"a\": 1 }", true)]
    [InlineData("{ \"a\": \"\\\"//\" }", false)]
    public void Detects_comments_outside_strings(string json, bool expected) => Assert.Equal(expected, SettingsDraft.HasComments(json));

    [Fact]
    public void Reset_to_defaults_marks_dirty_but_keeps_unknown_keys()
    {
        var current = new AppConfig { PollSeconds = 300 };
        current.Extra = new() { ["futureThing"] = System.Text.Json.JsonDocument.Parse("1").RootElement };
        var d = new SettingsDraft(current, null);
        d.ResetToDefaults();
        Assert.Equal(120, d.Config.PollSeconds);
        Assert.True(d.IsDirty);
        Assert.NotNull(d.Config.Extra);
    }
}
