using System.Net;
using Microsoft.Extensions.Time.Testing;
using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class PipelineSourceTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-20T12:00:00Z");
    static readonly DateTimeOffset Created = Now.AddHours(-2);

    sealed class FakeFeed : IPipelineRunFeed
    {
        public Dictionary<string, List<PipelineRunInfo>> Runs { get; } = new();
        public Dictionary<string, Exception> Throws { get; } = new();
        public List<(string Sub, DateTimeOffset Since, bool InProgress)> Calls { get; } = [];

        public Task<IReadOnlyList<PipelineRunInfo>> RecentRunsAsync(PipelineSubscription sub, DateTimeOffset since, bool includeInProgress, CancellationToken ct)
        {
            Calls.Add((sub.Id, since, includeInProgress));
            if (Throws.TryGetValue(sub.Id, out var ex)) throw ex;
            return Task.FromResult<IReadOnlyList<PipelineRunInfo>>(Runs.GetValueOrDefault(sub.Id) ?? []);
        }
    }

    static PipelineSubscription Sub(string id = "s1", long pipelineId = 42, Action<PipelineSubscription>? edit = null)
    {
        var s = new PipelineSubscription
        {
            Id = id, Provider = Provider.Ado, Org = "acme", Project = "P", PipelineId = pipelineId, Pipeline = "Deploy",
            On = [PipelineOutcome.Failed], CreatedAt = Created,
        };
        edit?.Invoke(s);
        return s;
    }

    static PipelineRunInfo Run(string id, PipelineOutcome outcome = PipelineOutcome.Failed, long pipelineId = 42, DateTimeOffset? finished = null) =>
        new(Provider.Ado, "acme/P", pipelineId, "Deploy", id, "n" + id, 1, "main", "Alice", true, "https://run/" + id, "sha",
            finished ?? Now.AddMinutes(-10), outcome, []);

    static PipelineSource Source(FakeFeed feed, IReadOnlyList<PipelineSubscription> subs, IReadOnlySet<string>? dismissed = null,
        IReadOnlyList<InboxItem>? last = null) =>
        new("ADO-Pipelines:acme", feed, () => subs, () => dismissed ?? new HashSet<string>(), () => last ?? [], new FakeTimeProvider(Now));

    [Fact]
    public async Task A_matching_new_run_becomes_an_item()
    {
        var feed = new FakeFeed();
        feed.Runs["s1"] = [Run("1001")];
        var r = await Source(feed, [Sub()]).FetchAsync(default);
        Assert.True(r.Healthy);
        var item = Assert.Single(r.Items);
        Assert.Equal(ItemKind.PipelineRun, item.Kind);
        Assert.Equal("s1", item.SubscriptionId);
    }

    [Fact]
    public async Task Runs_that_finished_before_the_subscription_never_appear()
    {
        var feed = new FakeFeed();
        feed.Runs["s1"] = [Run("1001", finished: Created.AddMinutes(-1))];
        Assert.Empty((await Source(feed, [Sub()]).FetchAsync(default)).Items);
    }

    [Fact]
    public async Task Disabled_subscriptions_are_not_polled()
    {
        var feed = new FakeFeed();
        await Source(feed, [Sub(edit: s => s.Enabled = false)]).FetchAsync(default);
        Assert.Empty(feed.Calls);
    }

    [Fact]
    public async Task The_feed_is_asked_for_in_progress_runs_only_when_stages_are_set()
    {
        var feed = new FakeFeed();
        await Source(feed, [Sub("plain"), Sub("staged", edit: s => s.Stages = ["PROD"])]).FetchAsync(default);
        Assert.False(feed.Calls.Single(c => c.Sub == "plain").InProgress);
        Assert.True(feed.Calls.Single(c => c.Sub == "staged").InProgress);
    }

    [Fact]
    public async Task The_window_starts_a_day_before_creation_but_never_more_than_three_days_back()
    {
        var feed = new FakeFeed();
        await Source(feed, [Sub("new"), Sub("old", edit: s => s.CreatedAt = Now.AddDays(-10))]).FetchAsync(default);
        Assert.Equal(Created.AddDays(-1), feed.Calls.Single(c => c.Sub == "new").Since);
        Assert.Equal(Now.AddDays(-3), feed.Calls.Single(c => c.Sub == "old").Since);
    }

    [Fact]
    public async Task Rows_are_carried_forward_when_the_feed_no_longer_returns_the_run()
    {
        var feed = new FakeFeed();
        feed.Runs["s1"] = [Run("1001")];
        var first = (await Source(feed, [Sub()]).FetchAsync(default)).Items;
        feed.Runs["s1"] = [];
        var again = await Source(feed, [Sub()], last: first).FetchAsync(default);
        Assert.Equal(first.Single().Key, again.Items.Single().Key);
    }

    [Fact]
    public async Task A_dismissed_key_is_not_returned_even_when_fresh_or_carried()
    {
        var feed = new FakeFeed();
        feed.Runs["s1"] = [Run("1001"), Run("1002")];
        var key = PipelineMatcher.Key(Run("1001"), null);
        var r = await Source(feed, [Sub()], dismissed: new HashSet<string> { key }).FetchAsync(default);
        Assert.Equal(PipelineMatcher.Key(Run("1002"), null), r.Items.Single().Key);

        var carried = PipelineMatcher.ToItem(Sub(), Run("1001"), new PipelineHit(null, PipelineOutcome.Failed, Now));
        var r2 = await Source(new FakeFeed(), [Sub()], dismissed: new HashSet<string> { key }, last: [carried]).FetchAsync(default);
        Assert.Empty(r2.Items);
    }

    [Fact]
    public async Task One_bad_subscription_keeps_its_carried_rows_and_does_not_fail_the_source()
    {
        var feed = new FakeFeed();
        feed.Throws["a"] = new HttpRequestException("403", null, HttpStatusCode.Forbidden);
        feed.Runs["b"] = [Run("2001", pipelineId: 43)];
        var carried = PipelineMatcher.ToItem(Sub("a"), Run("1001"), new PipelineHit(null, PipelineOutcome.Failed, Now));
        var r = await Source(feed, [Sub("a"), Sub("b", 43)], last: [carried]).FetchAsync(default);
        Assert.True(r.Healthy);
        Assert.Equal(2, r.Items.Count);
        Assert.Contains(r.Items, i => i.SubscriptionId == "a");
        Assert.Contains(r.Items, i => i.SubscriptionId == "b");
    }

    [Fact]
    public async Task Auth_and_rate_limit_failures_fail_the_source()
    {
        var feed = new FakeFeed();
        feed.Throws["s1"] = new SourceAuthException("az login");
        Assert.False((await Source(feed, [Sub()]).FetchAsync(default)).Healthy);
        feed.Throws["s1"] = new RateLimitedException(Now.AddMinutes(5));
        Assert.False((await Source(feed, [Sub()]).FetchAsync(default)).Healthy);
    }

    [Fact]
    public async Task Carried_rows_of_a_removed_subscription_disappear_and_others_stay()
    {
        var gone = PipelineMatcher.ToItem(Sub("gone"), Run("1001"), new PipelineHit(null, PipelineOutcome.Failed, Now));
        var kept = PipelineMatcher.ToItem(Sub("s1"), Run("1002"), new PipelineHit(null, PipelineOutcome.Failed, Now));
        var r = await Source(new FakeFeed(), [Sub("s1")], last: [gone, kept]).FetchAsync(default);
        Assert.Equal(kept.Key, r.Items.Single().Key);
    }

    [Fact]
    public async Task Non_pipeline_items_in_last_known_are_ignored()
    {
        var other = new InboxItem("k", Provider.Ado, ItemKind.ApprovalPending, "acme/P", "t", "u", "", Now, null, null, null);
        Assert.Empty((await Source(new FakeFeed(), [Sub()], last: [other]).FetchAsync(default)).Items);
    }

    [Fact]
    public async Task One_run_matched_by_two_subscriptions_is_one_row_owned_by_the_first()
    {
        var feed = new FakeFeed();
        feed.Runs["first"] = [Run("1001")];
        feed.Runs["second"] = [Run("1001")];
        var r = await Source(feed, [Sub("first"), Sub("second")]).FetchAsync(default);
        Assert.Equal("first", r.Items.Single().SubscriptionId);
    }

    [Fact]
    public void Dismissed_entries_older_than_fourteen_days_are_pruned()
    {
        var kept = new DismissedRun("a", Now.AddDays(-13));
        var old = new DismissedRun("b", Now.AddDays(-15));
        Assert.Equal([kept], PipelineSource.PruneDismissed([kept, old], Now));
    }

    [Fact]
    public async Task A_new_failure_toasts_once_survives_a_restart_without_a_second_toast_and_goes_when_dismissed()
    {
        var feed = new FakeFeed();
        feed.Runs["s1"] = [Run("1001")];
        var subs = new[] { Sub() };
        var reconciler = new Reconciler();
        var dismissed = new HashSet<string>();
        PipelineSource Make(Reconciler r) => new("ADO-Pipelines:acme", feed, () => subs, () => dismissed, () => r.ItemsFor("ADO-Pipelines:acme"), new FakeTimeProvider(Now));
        async Task<InboxDiff> Poll(Reconciler r) =>
            r.Apply(new Dictionary<string, SourceResult> { ["ADO-Pipelines:acme"] = await Make(r).FetchAsync(default) }, Now).WithPipelineInitialAsAdded();
        static InboxView ViewOf(Reconciler r) => InboxView.Compose(r.Entries, new FiltersConfig(), [], [], Now);

        var first = await Poll(reconciler);
        var plan = NotificationPlanner.Plan(first, [], ViewOf(reconciler), new AppConfig(), true);
        var toast = Assert.Single(plan.Toasts, t => t.Action == ToastAction.Show);
        Assert.Equal("Pipeline failed", toast.Title);

        var restarted = new Reconciler(reconciler.Snapshot().ToDictionary(kv => kv.Key, kv => (IReadOnlyList<InboxItem>)kv.Value));
        feed.Runs["s1"] = [];
        Assert.True((await Poll(restarted)).IsEmpty); // the row is carried forward: no removal, no new toast
        Assert.Single(restarted.Entries);

        dismissed.Add(PipelineMatcher.Key(Run("1001"), null));
        var gone = await Poll(restarted);
        Assert.Single(gone.Removed);
        Assert.Empty(restarted.Entries);
    }

    [Fact]
    public async Task A_subscription_without_a_createdAt_is_skipped_and_does_not_fail_the_source_or_its_neighbours()
    {
        var feed = new FakeFeed();
        feed.Runs["ok"] = [Run("1001", pipelineId: 43)];
        var r = await Source(feed, [Sub("bad", edit: s => s.CreatedAt = default), Sub("ok", 43)]).FetchAsync(default);
        Assert.True(r.Healthy);
        Assert.Equal("ok", r.Items.Single().SubscriptionId);
        Assert.DoesNotContain(feed.Calls, c => c.Sub == "bad");
    }
}
