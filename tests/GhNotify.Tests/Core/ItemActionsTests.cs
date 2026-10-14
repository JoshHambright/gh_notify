using System.Net;
using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ItemActionsTests
{
    static InboxItem I(ItemKind k, Provider p = Provider.GitHub, string sha = "abc") =>
        new("k", p, k, "acme/api", "t", "https://u", "alice", DateTimeOffset.UnixEpoch, sha, null, null);

    [Fact]
    public void Default_actions_per_kind()
    {
        Assert.Equal([ItemAction.Approve, ItemAction.Reject], ActionText.DefaultActions(I(ItemKind.ApprovalPending)));
        Assert.Equal([ItemAction.ApprovePr], ActionText.DefaultActions(I(ItemKind.ReviewRequested)));
        Assert.Equal([ItemAction.RerunFailed], ActionText.DefaultActions(I(ItemKind.MyPrCiFailing)));
        Assert.Equal([ItemAction.Merge], ActionText.DefaultActions(I(ItemKind.MyPrReadyToMerge)));
        Assert.Empty(ActionText.DefaultActions(I(ItemKind.MyPrNeedsResponse)));
        Assert.Empty(ActionText.DefaultActions(I(ItemKind.MyPrWaiting)));
    }

    static readonly MergeMethod[] All = [MergeMethod.Merge, MergeMethod.Squash, MergeMethod.Rebase];

    [Fact]
    public void Merge_default_uses_configured_when_allowed()
    {
        Assert.Equal(MergeMethod.Rebase, MergeDefaults.Pick(All, MergeMethod.Rebase));
        Assert.Equal(MergeMethod.Merge, MergeDefaults.Pick(All, MergeMethod.Merge));
    }

    [Fact]
    public void Merge_default_falls_back_to_squash_when_configured_not_allowed_or_unset()
    {
        Assert.Equal(MergeMethod.Squash, MergeDefaults.Pick(All, null));
        Assert.Equal(MergeMethod.Squash, MergeDefaults.Pick([MergeMethod.Squash, MergeMethod.Merge], MergeMethod.Rebase));
    }

    [Fact]
    public void Merge_default_takes_first_allowed_in_squash_merge_rebase_order()
    {
        Assert.Equal(MergeMethod.Merge, MergeDefaults.Pick([MergeMethod.Rebase, MergeMethod.Merge], null));
        Assert.Equal(MergeMethod.Rebase, MergeDefaults.Pick([MergeMethod.Rebase], MergeMethod.Squash));
    }

    [Fact]
    public void Merge_and_rerun_button_labels() => Assert.Equal(["Merge…", "Re-run failed…"],
        new[] { ItemAction.Merge, ItemAction.RerunFailed }.Select(ActionText.ButtonLabel));

    [Fact]
    public void Labels() => Assert.Equal(["Approve…", "Reject…", "Approve PR…"],
        new[] { ItemAction.Approve, ItemAction.Reject, ItemAction.ApprovePr }.Select(ActionText.ButtonLabel));

    [Fact]
    public void Pr_warning_unless_ready_at_current_head()
    {
        var item = I(ItemKind.ReviewRequested, sha: "h2");
        AgentRun Run(AgentState s, string sha) => new() { Key = "k", Profile = "peer-review", State = s, HeadSha = sha };
        Assert.Equal("No pre-review at this commit.", ActionText.PrWarning(item, null));
        Assert.Equal("No pre-review at this commit.", ActionText.PrWarning(item, Run(AgentState.Ready, "h1")));
        Assert.Equal("No pre-review at this commit.", ActionText.PrWarning(item, Run(AgentState.Failed, "h2")));
        Assert.Null(ActionText.PrWarning(item, Run(AgentState.Ready, "h2")));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, false, "You don't have permission to do this")]
    [InlineData(HttpStatusCode.Unauthorized, false, "You don't have permission to do this")]
    [InlineData(HttpStatusCode.UnprocessableEntity, true, "changed since you looked")]
    [InlineData(HttpStatusCode.NotFound, true, "changed since you looked")]
    [InlineData(HttpStatusCode.Conflict, true, "changed since you looked")]
    [InlineData(HttpStatusCode.InternalServerError, false, "boom")]
    public void Maps_http_errors(HttpStatusCode code, bool stale, string contains)
    {
        var r = ActionText.FromHttpError(new HttpRequestException("boom", null, code));
        Assert.False(r.Ok);
        Assert.Equal(stale, r.Stale);
        Assert.Contains(contains, r.Error);
    }

    sealed class Fake(string tag) : IItemActions
    {
        public IReadOnlyList<ItemAction> ActionsFor(InboxItem item) => [ItemAction.Approve];
        public Task<ActionPreview> PreviewAsync(InboxItem i, ItemAction a, AgentRun? r, CancellationToken ct) =>
            Task.FromResult(new ActionPreview(true, tag, "Go", null, null, null));
        public Task<ActionResult> ExecuteAsync(InboxItem i, ItemAction a, ActionPreview p, ActionInput c, CancellationToken ct) =>
            Task.FromResult(new ActionResult(true, tag));
    }

    [Fact]
    public async Task Composite_routes_by_provider()
    {
        var c = new CompositeItemActions(new Dictionary<Provider, IItemActions> { [Provider.GitHub] = new Fake("gh") });
        Assert.Equal("gh", (await c.PreviewAsync(I(ItemKind.ApprovalPending), ItemAction.Approve, null, default)).Statement);
        var ado = await c.PreviewAsync(I(ItemKind.ApprovalPending, Provider.Ado), ItemAction.Approve, null, default);
        Assert.False(ado.Allowed);
        Assert.Empty(c.ActionsFor(I(ItemKind.ApprovalPending, Provider.Ado)));
    }
}
