using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class InboxPresenterTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");

    static InboxEntry E(string key, ItemKind kind, TimeSpan ago, bool stale = false, Provider p = Provider.GitHub) =>
        new(new InboxItem(key, p, kind, "acme/api", "T " + key, "https://u/" + key, "alice", Now - ago, "s", 5, "d"), "GitHub", stale);

    static AgentDecision Button(string profile = "peer-review") => new(AgentMode.Button, profile, new AgentProfile());

    static IReadOnlyList<InboxSection> Build(IReadOnlyList<InboxEntry> entries, Func<string, AgentRun?>? runs = null,
        Func<InboxItem, AgentDecision>? decide = null, string? why = null) =>
        InboxPresenter.Build(entries, runs ?? (_ => null), decide ?? (_ => Button()), (_, _) => why, Now);

    static InboxItem It(ItemKind kind) =>
        new("k1", Provider.GitHub, kind, "acme/api", "T", "https://u", "a", Now, "h", 5, null)
        { Details = [new ItemDetail("Repo", "acme/api")] };

    static InboxRow Row(InboxItem item, RowUiState? ui = null, bool stale = false, AgentRun? run = null, DateTimeOffset? now = null) =>
        InboxPresenter.Build([new InboxEntry(item, "GitHub", stale)], _ => run, _ => new AgentDecision(AgentMode.Button, "peer-review", new AgentProfile()),
            (_, _) => null, now ?? Now, ActionText.DefaultActions, _ => ui)[0].Rows[0];

    [Fact]
    public void Urgent_details_and_actions()
    {
        var r = Row(It(ItemKind.ApprovalPending));
        Assert.True(r.Urgent);
        Assert.Equal("acme/api", r.Details.Single().Value);
        Assert.Equal(["Approve…", "Reject…"], r.Actions.Select(a => a.Label));
        Assert.All(r.Actions, a => Assert.True(a.Enabled));
        Assert.Equal(RowMode.Collapsed, r.Mode);
        Assert.False(Row(It(ItemKind.ReviewRequested)).Urgent);
        Assert.Empty(Row(It(ItemKind.MyPrNeedsResponse)).Actions);
    }

    [Fact]
    public void Stale_rows_disable_actions()
    {
        var a = Row(It(ItemKind.ApprovalPending), stale: true).Actions[0];
        Assert.False(a.Enabled);
        Assert.Equal("Source is unavailable — can't act right now", a.DisabledReason);
    }

    [Fact]
    public void Confirm_button_waits_600ms()
    {
        var preview = new ActionPreview(true, "Approve deployment of acme/api run 1 to production", "Approve production", null, null, new long[] { 1 });
        var ui = new RowUiState(RowMode.Confirming, ItemAction.Approve, preview, ConfirmOpenedAt: Now);
        var early = Row(It(ItemKind.ApprovalPending), ui, now: Now.AddMilliseconds(599));
        Assert.False(early.ConfirmEnabled);
        Assert.Equal("Approve production", early.ConfirmLabel);
        Assert.Equal(preview.Statement, early.Statement);
        Assert.True(Row(It(ItemKind.ApprovalPending), ui, now: Now.AddMilliseconds(600)).ConfirmEnabled);
    }

    [Fact]
    public void Refused_preview_shows_reason_and_no_confirm()
    {
        var ui = new RowUiState(RowMode.Confirming, ItemAction.ApprovePr, ActionText.Refuse("New commits since you looked — refresh."), ConfirmOpenedAt: Now.AddSeconds(-5));
        var r = Row(It(ItemKind.ReviewRequested), ui);
        Assert.False(r.ConfirmEnabled);
        Assert.Equal("New commits since you looked — refresh.", r.Message);
    }

    [Fact]
    public void Sending_disables_actions()
    {
        var ui = new RowUiState(RowMode.Sending, ItemAction.Approve, new ActionPreview(true, "s", "Approve x", null, null, null), ConfirmOpenedAt: Now.AddSeconds(-5));
        var r = Row(It(ItemKind.ApprovalPending), ui);
        Assert.All(r.Actions, a => Assert.False(a.Enabled));
        Assert.False(r.ConfirmEnabled);
    }

    [Fact]
    public void Run_info_and_result_text()
    {
        var run = new AgentRun { Key = "k1", Profile = "pr-shepherd", State = AgentState.Ready, CostUsd = 0.23m, DurationSeconds = 125, ResultText = "full" };
        var r = Row(It(ItemKind.MyPrCiFailing), run: run);
        Assert.Equal("Shepherd · ready · $0.23 · 2m 05s", r.RunInfo);
        Assert.Equal("full", r.ResultText);
        Assert.Equal("Shepherd · ready", Row(It(ItemKind.MyPrCiFailing), run: run with { CostUsd = null, DurationSeconds = null }).RunInfo);
    }

    [Fact]
    public void Orders_sections_and_rows_and_omits_empty()
    {
        var sections = Build(
        [
            E("r-old", ItemKind.ReviewRequested, TimeSpan.FromHours(3)),
            E("a", ItemKind.ApprovalPending, TimeSpan.FromMinutes(5)),
            E("r-new", ItemKind.ReviewRequested, TimeSpan.FromMinutes(1)),
            E("f", ItemKind.MyPrCiFailing, TimeSpan.FromDays(3), stale: true, p: Provider.Ado),
        ]);
        Assert.Equal(["Approvals", "Review requests", "My PRs — failing"], sections.Select(s => s.Title));
        Assert.Equal(["r-new", "r-old"], sections[1].Rows.Select(r => r.Key));
        var f = sections[2].Rows.Single();
        Assert.True(f.Stale);
        Assert.Equal("ADO", f.ProviderGlyph);
        Assert.Equal("3d", f.Age);
        Assert.Equal("GH", sections[0].Rows.Single().ProviderGlyph);
    }

    [Theory]
    [InlineData(30, "now")]
    [InlineData(5 * 60, "5m")]
    [InlineData(3 * 3600, "3h")]
    [InlineData(47 * 3600, "47h")]
    [InlineData(3 * 86400, "3d")]
    public void Age_format(int seconds, string expected) => Assert.Equal(expected, InboxPresenter.Age(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Run_button_labels_and_states()
    {
        var entries = new[] { E("x", ItemKind.ReviewRequested, TimeSpan.Zero) };
        Assert.Equal("Pre-review", Build(entries)[0].Rows[0].RunButton);
        Assert.Equal("Shepherd", Build(entries, decide: _ => Button("pr-shepherd"))[0].Rows[0].RunButton);
        Assert.Equal("Run deploy-check", Build(entries, decide: _ => Button("deploy-check"))[0].Rows[0].RunButton);
        Assert.Null(Build(entries, decide: _ => new AgentDecision(AgentMode.None, null, null))[0].Rows[0].RunButton);

        var disabled = Build(entries, why: "No local clone under repoRoots")[0].Rows[0];
        Assert.False(disabled.CanRun);
        Assert.Equal("No local clone under repoRoots", disabled.RunDisabledReason);
    }

    [Fact]
    public void Badges_and_open_in_claude()
    {
        var entries = new[] { E("x", ItemKind.ReviewRequested, TimeSpan.Zero) };
        AgentRun Run(AgentState s) => new() { Key = "x", Profile = "peer-review", State = s, SessionId = "sid", WorktreePath = @"C:\wt", Summary = "2 findings", CostUsd = 0.42m };

        var running = Build(entries, _ => Run(AgentState.Running))[0].Rows[0];
        Assert.Equal("Running…", running.BadgeText);
        Assert.Null(running.RunButton);
        Assert.False(running.CanOpenInClaude);

        var ready = Build(entries, _ => Run(AgentState.Ready))[0].Rows[0];
        Assert.Equal("✓ Ready", ready.BadgeText);
        Assert.Equal("2 findings ($0.42)", ready.BadgeTooltip);
        Assert.Equal("Re-run", ready.RunButton);
        Assert.True(ready.CanOpenInClaude);

        var failed = Build(entries, _ => Run(AgentState.Failed) with { Summary = null, Error = "timed out", CostUsd = null })[0].Rows[0];
        Assert.Equal("✗ Failed", failed.BadgeText);
        Assert.Equal("timed out", failed.BadgeTooltip);

        Assert.Equal("Outdated", Build(entries, _ => Run(AgentState.Outdated))[0].Rows[0].BadgeText);
        Assert.Equal("Queued", Build(entries, _ => Run(AgentState.Queued))[0].Rows[0].BadgeText);
        Assert.False(Build(entries, _ => Run(AgentState.Ready) with { SessionId = null })[0].Rows[0].CanOpenInClaude);
    }

    [Fact]
    public void Repo_display_defaults_to_full_name_and_uses_formatter()
    {
        var entries = new[] { new InboxEntry(It(ItemKind.ReviewRequested), "GitHub", false) };
        var plain = InboxPresenter.Build(entries, _ => null, _ => Button(), (_, _) => null, Now)[0].Rows[0];
        Assert.Equal("acme/api", plain.RepoDisplay);
        var formatted = InboxPresenter.Build(entries, _ => null, _ => Button(), (_, _) => null, Now, repoDisplay: r => "x/" + r)[0].Rows[0];
        Assert.Equal("x/acme/api", formatted.RepoDisplay);
        Assert.Equal("acme/api", formatted.Repo);
    }
}
