using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class NotificationPolicyTests
{
    static InboxItem I(string key, ItemKind kind = ItemKind.ReviewRequested) =>
        new(key, Provider.GitHub, kind, "acme/api", "Title " + key, "https://x/" + key, "alice", DateTimeOffset.UnixEpoch, null, null, null);

    [Fact]
    public void Outside_work_hours_only_removals_happen()
    {
        var diff = new InboxDiff([I("a")], [I("b")], [], [I("c")]);
        var plan = NotificationPolicy.Plan(diff, inWorkHours: false);
        Assert.All(plan, t => Assert.Equal(ToastAction.Remove, t.Action));
        Assert.Contains(plan, t => t.Tag == ItemTag.For("b"));
        Assert.Contains(plan, t => t.Tag == ItemTag.ForAgent("b"));
    }

    [Fact]
    public void Inside_work_hours_shows_added_and_one_summary()
    {
        var diff = new InboxDiff([I("a", ItemKind.ApprovalPending)], [], [], [I("c"), I("d")]);
        var plan = NotificationPolicy.Plan(diff, inWorkHours: true);
        var added = plan.Single(t => t.Tag == ItemTag.For("a"));
        Assert.Equal("Approval needed", added.Title);
        Assert.Equal("acme/api: Title a", added.Body);
        Assert.Equal("https://x/a", added.Url);
        var summary = plan.Single(t => t.Tag == NotificationPolicy.SummaryTag);
        Assert.Equal("2 items waiting on you", summary.Title);
        Assert.Null(summary.Url);
    }

    [Fact]
    public void Summary_is_singular_for_one_item() =>
        Assert.Equal("1 item waiting on you",
            NotificationPolicy.Plan(new InboxDiff([], [], [], [I("c")]), true).Single().Title);

    [Fact]
    public void Waiting_items_never_toast()
    {
        var plan = NotificationPolicy.Plan(new InboxDiff([I("w", ItemKind.MyPrWaiting)], [], [], [I("w2", ItemKind.MyPrWaiting)]), true);
        Assert.Empty(plan);
    }

    [Fact]
    public void Initial_load_summary_excludes_waiting_items()
    {
        var plan = NotificationPolicy.Plan(new InboxDiff([], [], [], [I("a"), I("w", ItemKind.MyPrWaiting), I("r", ItemKind.MyPrReadyToMerge)]), true);
        Assert.Equal("2 items waiting on you", plan.Single(t => t.Tag == NotificationPolicy.SummaryTag).Title);
    }

    [Fact]
    public void Ready_to_merge_title() =>
        Assert.Equal("Ready to merge", NotificationPolicy.TitleFor(I("r", ItemKind.MyPrReadyToMerge)));

    [Fact]
    public void Changes_never_toast() =>
        Assert.Empty(NotificationPolicy.Plan(new InboxDiff([], [], [new ItemChange(I("a"), I("a"))], []), true));
}
