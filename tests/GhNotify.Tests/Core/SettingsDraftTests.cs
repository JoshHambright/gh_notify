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
    [InlineData("display.terminal", SettingsPage.Advanced)]
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
        Assert.Equal("ding.wav", SettingsDraft.SoundLabel(Path.Combine(Path.GetTempPath(), "sounds", "ding.wav")));
        Assert.Equal(OperatingSystem.IsMacOS() ? "macOS default" : "Windows default", SettingsDraft.SoundLabel("default"));
    }

    [Theory]
    [InlineData("builtin:", "builtin:")]
    [InlineData("builtin:a", "A")]
    [InlineData("BUILTIN:chime", "Chime")]
    [InlineData("", "")]
    public void Sound_label_never_throws_on_partial_input(string sound, string expected) =>
        Assert.Equal(expected, SettingsDraft.SoundLabel(sound));

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

    static AppConfig WithTriage() => new()
    {
        AgentProfiles = new(StringComparer.OrdinalIgnoreCase) { ["triage"] = new AgentProfile { Command = ["pwsh"] }, ["other"] = new AgentProfile { Prompt = "x" } },
        Rules = [new Rule { Profile = "triage" }, new Rule { Profile = "TRIAGE" }, new Rule { Profile = "ghost" }, new Rule()],
        DefaultProfiles = new(StringComparer.OrdinalIgnoreCase) { ["MyPrCiFailing"] = "triage", ["ReviewRequested"] = "ghost" },
    };

    [Fact]
    public void Rename_profile_rekeys_and_follows_references()
    {
        var d = new SettingsDraft(WithTriage(), null);
        var profile = d.Config.AgentProfiles["triage"];
        Assert.Null(d.RenameProfile("triage", "triage-ci"));
        Assert.False(d.Config.AgentProfiles.ContainsKey("triage"));
        Assert.Same(profile, d.Config.AgentProfiles["triage-ci"]);
        Assert.Equal(["triage-ci", "triage-ci", "ghost", null], d.Config.Rules.Select(r => r.Profile));
        Assert.Equal("triage-ci", d.Config.DefaultProfiles["MyPrCiFailing"]);
        Assert.Equal("ghost", d.Config.DefaultProfiles["ReviewRequested"]); // a dangling reference to another name is left alone
    }

    [Theory]
    [InlineData("", "The profile needs a name.")]
    [InlineData("  ", "The profile needs a name.")]
    [InlineData("other", "A profile named 'other' already exists.")]
    [InlineData("Review", "'Review' is a built-in profile name.")]
    public void Rename_profile_refuses_bad_names(string name, string error)
    {
        var d = new SettingsDraft(WithTriage(), null);
        Assert.Equal(error, d.RenameProfile("triage", name));
        Assert.True(d.Config.AgentProfiles.ContainsKey("triage"));
        Assert.False(d.IsDirty);
    }

    [Fact]
    public void Rename_profile_to_the_same_name_or_a_case_change()
    {
        var d = new SettingsDraft(WithTriage(), null);
        Assert.Null(d.RenameProfile("triage", "triage"));
        Assert.False(d.IsDirty);
        Assert.Null(d.RenameProfile("triage", "Triage"));
        Assert.Equal("Triage", d.Config.AgentProfiles.Keys.Single(k => k.Equals("triage", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("Triage", d.Config.Rules[0].Profile);
    }

    [Fact]
    public void Rename_of_an_unknown_or_built_in_profile_is_refused()
    {
        var d = new SettingsDraft(WithTriage(), null);
        Assert.Equal("No profile named 'nope'.", d.RenameProfile("nope", "x"));
        Assert.Equal("No profile named 'review'.", d.RenameProfile("review", "x"));
    }
}
