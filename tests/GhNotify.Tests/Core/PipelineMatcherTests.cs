using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class PipelineMatcherTests
{
    static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-20T09:00:00Z");
    static readonly DateTimeOffset After = Created.AddHours(1);

    static PipelineSubscription Sub(Action<PipelineSubscription>? edit = null)
    {
        var s = new PipelineSubscription
        {
            Id = "s1",
            Provider = Provider.Ado,
            Org = "acme",
            Project = "P",
            PipelineId = 42,
            Pipeline = "Deploy",
            On = [PipelineOutcome.Failed],
            CreatedAt = Created,
        };
        edit?.Invoke(s);
        return s;
    }

    static PipelineRunInfo Run(PipelineOutcome? outcome = PipelineOutcome.Failed, DateTimeOffset? finished = null, string? branch = "main",
        bool? mine = true, int attempt = 1, params PipelineStageInfo[] stages) =>
        new(Provider.Ado, "acme/P", 42, "Deploy", "1001", "20261120.1", attempt, branch, "Alice", mine, "https://run/1001", "abc123",
            outcome is null ? null : finished ?? After, outcome, stages);

    static PipelineStageInfo Stage(string name, PipelineOutcome? outcome, DateTimeOffset? finished = null) =>
        new(name, outcome, outcome is null ? null : finished ?? After);

    [Theory]
    [InlineData(PipelineOutcome.Failed, PipelineOutcome.Failed, 1)]
    [InlineData(PipelineOutcome.Failed, PipelineOutcome.Succeeded, 0)]
    public void Whole_run_outcome_must_be_one_the_subscription_wants(PipelineOutcome run, PipelineOutcome wants, int hits) =>
        Assert.Equal(hits, PipelineMatcher.Match(Sub(s => s.On = [wants]), Run(run)).Count);

    [Fact]
    public void An_in_progress_run_has_no_whole_run_hit() =>
        Assert.Empty(PipelineMatcher.Match(Sub(), Run(outcome: null)));

    [Fact]
    public void A_finished_stage_alerts_even_while_the_run_is_in_progress()
    {
        var run = Run(outcome: null, stages: [Stage("QA", PipelineOutcome.Succeeded), Stage("PROD", PipelineOutcome.Failed)]);
        var hit = Assert.Single(PipelineMatcher.Match(Sub(s => s.Stages = ["PROD*"]), run));
        Assert.Equal("PROD", hit.Stage);
        Assert.Equal(PipelineOutcome.Failed, hit.Outcome);
    }

    [Fact]
    public void A_stage_without_a_final_result_never_matches() =>
        Assert.Empty(PipelineMatcher.Match(Sub(s => s.Stages = ["PROD*"]), Run(stages: [Stage("PROD", null)])));

    [Fact]
    public void Every_matching_stage_is_its_own_hit() =>
        Assert.Equal(["Prod-EU", "Prod-US"], PipelineMatcher.Match(Sub(s => s.Stages = ["prod*"]),
            Run(stages: [Stage("Prod-EU", PipelineOutcome.Failed), Stage("Prod-US", PipelineOutcome.Failed), Stage("QA", PipelineOutcome.Failed)]))
            .Select(h => h.Stage));

    [Fact]
    public void Stage_judging_ignores_the_overall_run_result() =>
        Assert.Empty(PipelineMatcher.Match(Sub(s => s.Stages = ["PROD"]),
            Run(PipelineOutcome.Failed, stages: [Stage("PROD", PipelineOutcome.Succeeded)])));

    [Fact]
    public void Anything_that_finished_before_the_subscription_was_created_is_dropped()
    {
        Assert.Empty(PipelineMatcher.Match(Sub(), Run(finished: Created.AddSeconds(-1))));
        Assert.Single(PipelineMatcher.Match(Sub(), Run(finished: Created)));
        Assert.Empty(PipelineMatcher.Match(Sub(s => s.Stages = ["PROD"]), Run(stages: [Stage("PROD", PipelineOutcome.Failed, Created.AddMinutes(-5))])));
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("release/1.2", true)]
    [InlineData("feature/x", false)]
    [InlineData(null, false)]
    public void Branches_are_matched_by_glob(string? branch, bool matches) =>
        Assert.Equal(matches, PipelineMatcher.Match(Sub(s => s.Branches = ["main", "release/*"]), Run(branch: branch)).Count == 1);

    [Fact]
    public void No_branch_filter_accepts_any_branch() =>
        Assert.Single(PipelineMatcher.Match(Sub(), Run(branch: null)));

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(null, 0)]
    public void Only_mine_fails_closed(bool? mine, int hits) =>
        Assert.Equal(hits, PipelineMatcher.Match(Sub(s => s.OnlyMine = true), Run(mine: mine)).Count);

    [Fact]
    public void Keys_carry_attempt_and_stage_so_a_rerun_is_a_new_row()
    {
        Assert.Equal("pipe:ado:acme/P:42:1001:1", PipelineMatcher.Key(Run(), null));
        Assert.Equal("pipe:ado:acme/P:42:1001:1:PROD", PipelineMatcher.Key(Run(), "PROD"));
        Assert.NotEqual(PipelineMatcher.Key(Run(attempt: 1), null), PipelineMatcher.Key(Run(attempt: 2), null));
        var gh = Run() with { Provider = Provider.GitHub, Scope = "acme/shop" };
        Assert.Equal("pipe:gh:acme/shop:42:1001:1", PipelineMatcher.Key(gh, null));
    }

    [Fact]
    public void ToItem_fills_a_pipeline_row()
    {
        var run = Run();
        var hit = Assert.Single(PipelineMatcher.Match(Sub(), run));
        var item = PipelineMatcher.ToItem(Sub(), run, hit);
        Assert.Equal(ItemKind.PipelineRun, item.Kind);
        Assert.Equal(Provider.Ado, item.Provider);
        Assert.Equal("acme/P", item.Repo);
        Assert.Equal("Deploy", item.Title);
        Assert.Equal("Failed", item.Detail);
        Assert.Equal("https://run/1001", item.Url);
        Assert.Equal("Alice", item.Author);
        Assert.Equal(After, item.UpdatedAt);
        Assert.Equal(After, item.Finished);
        Assert.Equal("s1", item.SubscriptionId);
        Assert.Equal("abc123", item.HeadSha);
        Assert.Equal(PipelineMatcher.Key(run, null), item.Key);
        Assert.Contains(item.Details!, d => d.Label == "Pipeline" && d.Value == "Deploy");
        Assert.Contains(item.Details!, d => d.Label == "Branch" && d.Value == "main");
    }

    [Fact]
    public void A_staged_item_names_the_stage_in_its_title_and_key()
    {
        var run = Run(stages: [Stage("PROD", PipelineOutcome.Failed)]);
        var sub = Sub(s => s.Stages = ["PROD"]);
        var item = PipelineMatcher.ToItem(sub, run, PipelineMatcher.Match(sub, run).Single());
        Assert.Equal("Deploy · PROD", item.Title);
        Assert.EndsWith(":PROD", item.Key);
    }

    [Fact]
    public void A_retried_stage_gets_its_own_key_while_other_stages_keep_theirs()
    {
        var run = Run(stages: [new PipelineStageInfo("PROD", PipelineOutcome.Failed, After, 2), new PipelineStageInfo("QA", PipelineOutcome.Failed, After, 1)]);
        Assert.Equal("pipe:ado:acme/P:42:1001:2:PROD", PipelineMatcher.Key(run, "PROD"));
        Assert.Equal("pipe:ado:acme/P:42:1001:1:QA", PipelineMatcher.Key(run, "QA"));
        Assert.Equal("pipe:ado:acme/P:42:1001:1", PipelineMatcher.Key(run, null));
    }
}
