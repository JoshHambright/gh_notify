using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class RulesEngineTests
{
    static InboxItem Item(ItemKind kind = ItemKind.ReviewRequested, string repo = "acme/api", string author = "alice", int? lines = 100, Provider provider = Provider.GitHub) =>
        new($"k:{repo}", provider, kind, repo, "Title", "https://x", author, DateTimeOffset.UnixEpoch, "sha", lines, null);

    [Fact]
    public void No_rules_defaults_to_button_with_kind_profile()
    {
        var d = RulesEngine.Decide(Item(), new AppConfig());
        Assert.Equal(AgentMode.Button, d.Mode);
        Assert.Equal("review", d.ProfileName);
        Assert.Null(d.Profile!.RequiresSkill);
    }

    [Fact]
    public void Ci_failing_defaults_to_diagnose() =>
        Assert.Equal("diagnose-ci", RulesEngine.Decide(Item(ItemKind.MyPrCiFailing), new AppConfig()).ProfileName);

    [Theory]
    [InlineData(ItemKind.ReviewRequested, "review")]
    [InlineData(ItemKind.MyPrCiFailing, "diagnose-ci")]
    [InlineData(ItemKind.MyPrNeedsResponse, "diagnose-ci")]
    [InlineData(ItemKind.ApprovalPending, null)]
    [InlineData(ItemKind.MyPrReadyToMerge, null)]
    [InlineData(ItemKind.MyPrWaiting, null)]
    public void Built_in_defaults_are_generic(ItemKind kind, string? profile) =>
        Assert.Equal(profile, RulesEngine.DefaultProfileFor(kind, new AppConfig()));

    [Fact]
    public void DefaultProfiles_override_case_insensitively()
    {
        var cfg = new AppConfig();
        cfg.DefaultProfiles["reviewRequested"] = "peer-review";
        Assert.Equal("peer-review", RulesEngine.DefaultProfileFor(ItemKind.ReviewRequested, cfg));
        Assert.Equal("peer-review", RulesEngine.Decide(Item(ItemKind.ReviewRequested), cfg).ProfileName);
    }

    [Fact]
    public void Generic_profiles_need_no_skill()
    {
        Assert.Null(ProfileCatalog.BuiltIn["review"].RequiresSkill);
        Assert.Null(ProfileCatalog.BuiltIn["diagnose-ci"].RequiresSkill);
        Assert.Contains("{url}", ProfileCatalog.BuiltIn["review"].Prompt);
        Assert.True(ProfileCatalog.BuiltIn["review"].NeedsWorktree);
    }

    [Fact]
    public void Approvals_have_no_default_profile()
    {
        var d = RulesEngine.Decide(Item(ItemKind.ApprovalPending), new AppConfig());
        Assert.Equal(AgentMode.None, d.Mode);
        Assert.Null(d.Profile);
    }

    [Fact]
    public void First_matching_rule_wins()
    {
        var cfg = new AppConfig
        {
            Rules =
            [
                new Rule { Match = new ItemMatch { Author = "dependabot[bot]" }, Agent = AgentMode.None },
                new Rule { Match = new ItemMatch { Repo = "acme/*" }, Agent = AgentMode.Auto },
                new Rule { Match = new ItemMatch { Repo = "acme/api" }, Agent = AgentMode.Button },
            ],
        };
        Assert.Equal(AgentMode.Auto, RulesEngine.Decide(Item(), cfg).Mode);
        Assert.Equal(AgentMode.None, RulesEngine.Decide(Item(author: "Dependabot[bot]"), cfg).Mode);
        Assert.Equal(AgentMode.Button, RulesEngine.Decide(Item(repo: "other/x"), cfg).Mode);
    }

    [Fact]
    public void Max_changed_lines_requires_known_size()
    {
        var m = new ItemMatch { MaxChangedLines = 20 };
        Assert.True(ItemMatcher.Matches(m, Item(lines: 20)));
        Assert.False(ItemMatcher.Matches(m, Item(lines: 21)));
        Assert.False(ItemMatcher.Matches(m, Item(lines: null)));
    }

    [Fact]
    public void Match_on_provider_and_kind()
    {
        var m = new ItemMatch { Provider = Provider.Ado, Kind = ItemKind.ReviewRequested };
        Assert.True(ItemMatcher.Matches(m, Item(provider: Provider.Ado)));
        Assert.False(ItemMatcher.Matches(m, Item()));
        Assert.True(ItemMatcher.Matches(null, Item()));
    }

    [Theory]
    [InlineData("acme/*", "acme/api", true)]
    [InlineData("acme/*", "ACME/Api", true)]
    [InlineData("acme/a?i", "acme/api", true)]
    [InlineData("acme/*", "other/acme", false)]
    [InlineData("contoso/Core Platform/*", "contoso/Core Platform/api", true)]
    [InlineData("a.b/*", "axb/c", false)]
    public void Glob(string pattern, string text, bool expected) =>
        Assert.Equal(expected, ItemMatcher.Glob(pattern, text));

    [Fact]
    public void Rule_can_name_custom_profile_and_override_builtin()
    {
        var cfg = new AppConfig
        {
            AgentProfiles = new()
            {
                ["deploy-check"] = new AgentProfile { Prompt = "/post-deploy-verify {url}", NeedsWorktree = false },
                ["REVIEW"] = new AgentProfile { Prompt = "custom {url}" },
            },
            Rules = [new Rule { Match = new ItemMatch { Kind = ItemKind.ApprovalPending }, Agent = AgentMode.Auto, Profile = "deploy-check" }],
        };
        var approval = RulesEngine.Decide(Item(ItemKind.ApprovalPending), cfg);
        Assert.Equal(AgentMode.Auto, approval.Mode);
        Assert.False(approval.Profile!.NeedsWorktree);
        Assert.Equal("custom {url}", RulesEngine.Decide(Item(), cfg).Profile!.Prompt);
    }

    [Fact]
    public void Validation_rejects_unknown_profile_name()
    {
        var cfg = new AppConfig { Rules = [new Rule { Profile = "nope" }] };
        var ex = Assert.Throws<InvalidDataException>(() => ConfigStore.Validate(cfg));
        Assert.Contains("rules[0].profile", ex.Message);
    }

    [Fact]
    public void Validation_rejects_empty_profile_prompt()
    {
        var cfg = new AppConfig { AgentProfiles = new() { ["x"] = new AgentProfile { Prompt = " " } } };
        var ex = Assert.Throws<InvalidDataException>(() => ConfigStore.Validate(cfg));
        Assert.Contains("agentProfiles.x.prompt", ex.Message);
    }
}
