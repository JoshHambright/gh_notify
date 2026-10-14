using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class NotificationPlannerTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
    static InboxItem It(string key, ItemKind kind = ItemKind.ReviewRequested, string repo = "acme/api", int ageMin = 0) =>
        new(key, Provider.GitHub, kind, repo, "T " + key, "https://u/" + key, "alice", Now.AddMinutes(-ageMin), null, null, null);
    static InboxView View(IEnumerable<InboxItem> items, FiltersConfig? f = null, Snooze[]? sn = null) =>
        InboxView.Compose(items.Select(i => new InboxEntry(i, "GitHub", false)).ToList(), f ?? new FiltersConfig(), [], sn ?? [], Now);
    static InboxDiff Added(params InboxItem[] items) => new(items, [], [], []);
    static AppConfig Cfg(params SoundRule[] rules) => new() { Notifications = new NotificationsConfig { Sounds = rules.ToList() } };
    static IEnumerable<ToastRequest> Shown(NotificationPlan p) => p.Toasts.Where(t => t.Action == ToastAction.Show);

    [Fact]
    public void Default_sound_rules_toast_with_windows_sound()
    {
        var a = It("a");
        var p = NotificationPlanner.Plan(Added(a), [], View([a]), new AppConfig(), inWorkHours: true);
        var t = Assert.Single(Shown(p));
        Assert.False(t.Silent);
        Assert.Null(p.Sound);
    }

    [Fact]
    public void Custom_sound_plays_once_and_its_toast_is_silent()
    {
        var a = It("a", ItemKind.ApprovalPending);
        var b = It("b", ItemKind.ReviewRequested);
        var cfg = Cfg(new SoundRule { Match = new ItemMatch { Kind = ItemKind.ApprovalPending }, Sound = "builtin:alarm" },
                      new SoundRule { Sound = "builtin:chime" });
        var p = NotificationPlanner.Plan(Added(b, a), [], View([a, b]), cfg, true);
        Assert.Equal("builtin:alarm", p.Sound); // ApprovalPending outranks ReviewRequested
        Assert.All(Shown(p), t => Assert.True(t.Silent));
        Assert.Equal(2, Shown(p).Count());
    }

    [Fact]
    public void Only_one_default_sound_per_poll()
    {
        var a = It("a", ageMin: 5);
        var b = It("b", ageMin: 1);
        var p = NotificationPlanner.Plan(Added(a, b), [], View([a, b]), new AppConfig(), true);
        Assert.Equal(1, Shown(p).Count(t => !t.Silent));
        Assert.False(Shown(p).Single(t => t.Tag == ItemTag.For("a")).Silent); // oldest wins within the same priority
    }

    [Fact]
    public void Off_hours_only_alert_rules_toast()
    {
        var a = It("a", ItemKind.ApprovalPending);
        var b = It("b");
        var cfg = Cfg(new SoundRule { Match = new ItemMatch { Kind = ItemKind.ApprovalPending }, Sound = "builtin:alarm", AlertOffHours = true });
        var p = NotificationPlanner.Plan(Added(a, b), [], View([a, b]), cfg, inWorkHours: false);
        Assert.Equal([ItemTag.For("a")], Shown(p).Select(t => t.Tag));
        Assert.Equal("builtin:alarm", p.Sound);
    }

    [Fact]
    public void Muted_snoozed_hidden_and_waiting_never_toast_or_sound()
    {
        var m = It("m", repo: "acme/legacy");
        var s = It("s");
        var h = It("h", repo: "fabrikam/x");
        var w = It("w", ItemKind.MyPrWaiting);
        var view = View([m, s, h, w], new FiltersConfig { Mute = ["acme/legacy"], Hide = ["fabrikam"] }, [new Snooze("s", null, true)]);
        var p = NotificationPlanner.Plan(Added(m, s, h, w), [], view, Cfg(new SoundRule { Sound = "builtin:alarm", AlertOffHours = true }), true);
        Assert.Empty(Shown(p));
        Assert.Null(p.Sound);
    }

    [Fact]
    public void Woken_items_toast_with_back_prefix()
    {
        var a = It("a");
        var p = NotificationPlanner.Plan(InboxDiff.Empty, [a], View([a]), new AppConfig(), true);
        Assert.StartsWith("Back: ", Assert.Single(Shown(p)).Title);
    }

    [Fact]
    public void Unhide_does_not_retoast()
    {
        // The item has been in the reconciler all along; unhiding changes only the view, so the next poll's diff is empty.
        var a = It("a", repo: "fabrikam/x");
        var p = NotificationPlanner.Plan(InboxDiff.Empty, [], View([a]), new AppConfig(), true);
        Assert.Empty(Shown(p));
    }


    [Fact]
    public void Woken_item_that_is_hidden_does_not_toast()
    {
        var a = It("a", repo: "fabrikam/x");
        var p = NotificationPlanner.Plan(InboxDiff.Empty, [a], View([a], new FiltersConfig { Hide = ["fabrikam"] }),
            Cfg(new SoundRule { Sound = "builtin:alarm", AlertOffHours = true }), inWorkHours: true);
        Assert.Empty(Shown(p));
        Assert.Null(p.Sound);
    }

    [Fact]
    public void None_sound_rule_toasts_silently_without_sound()
    {
        var a = It("a");
        var p = NotificationPlanner.Plan(Added(a), [], View([a]), Cfg(new SoundRule { Sound = "none" }), true);
        Assert.True(Assert.Single(Shown(p)).Silent);
        Assert.Null(p.Sound);
    }
    [Fact]
    public void Removed_items_clear_both_toasts_even_off_hours()
    {
        var a = It("a");
        var p = NotificationPlanner.Plan(new InboxDiff([], [a], [], []), [], View([]), new AppConfig(), false);
        Assert.Equal([ItemTag.For("a"), ItemTag.ForAgent("a")], p.Toasts.Where(t => t.Action == ToastAction.Remove).Select(t => t.Tag));
    }

    [Fact]
    public void Initial_load_summary_counts_only_normal_items()
    {
        var a = It("a");
        var w = It("w", ItemKind.MyPrWaiting);
        var p = NotificationPlanner.Plan(new InboxDiff([], [], [], [a, w]), [], View([a, w]), new AppConfig(), true);
        Assert.Equal("1 item waiting on you", Assert.Single(Shown(p)).Title);
    }

    [Fact]
    public void Added_toast_carries_title_body_and_url()
    {
        var a = It("a", ItemKind.ApprovalPending);
        var t = Assert.Single(Shown(NotificationPlanner.Plan(Added(a), [], View([a]), new AppConfig(), true)));
        Assert.Equal("Approval needed", t.Title);
        Assert.Equal("acme/api: T a", t.Body);
        Assert.Equal("https://u/a", t.Url);
    }

    [Fact]
    public void Ready_to_merge_title() =>
        Assert.Equal("Ready to merge", NotificationPolicy.TitleFor(It("r", ItemKind.MyPrReadyToMerge)));

    [Fact]
    public void Changes_never_toast()
    {
        var a = It("a");
        var p = NotificationPlanner.Plan(new InboxDiff([], [], [new ItemChange(a, a)], []), [], View([a]), new AppConfig(), true);
        Assert.Empty(p.Toasts);
    }

    [Fact]
    public void Toast_body_includes_PR_number_only_when_present()
    {
        var a = It("a") with { Number = 7 };
        var b = It("b");
        var p = NotificationPlanner.Plan(Added(a, b), [a], View([a, b]), new AppConfig(), inWorkHours: true);
        var bodies = Shown(p).Select(t => t.Body).ToList();
        Assert.Equal(2, bodies.Count(x => x == "acme/api #7: T a"));
        Assert.Contains("acme/api: T b", bodies);
    }
}
