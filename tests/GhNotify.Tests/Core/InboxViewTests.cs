using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class InboxViewTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
    static InboxEntry E(string key, string repo, ItemKind kind = ItemKind.ReviewRequested, Provider p = Provider.GitHub) =>
        new(new InboxItem(key, p, kind, repo, "T", "https://u", "alice", Now, null, null, null), "GitHub", false);

    static InboxView V(InboxEntry[] entries, FiltersConfig? f = null, TimedMute[]? mutes = null, Snooze[]? snoozes = null) =>
        InboxView.Compose(entries, f ?? new FiltersConfig(), mutes ?? [], snoozes ?? [], Now);

    [Fact]
    public void Sorts_entries_into_active_waiting_snoozed_and_hidden()
    {
        var v = V([E("a", "acme/api"), E("w", "acme/web", ItemKind.MyPrWaiting), E("s", "acme/svc"), E("h", "fabrikam/sandbox"), E("m", "acme/legacy-x")],
            new FiltersConfig { Hide = ["gh:fabrikam/sandbox"], Mute = ["acme/legacy-*"] },
            snoozes: [new Snooze("s", null, true)]);
        Assert.Equal(["a", "m"], v.Active.Select(e => e.Entry.Item.Key));
        Assert.Equal("acme/legacy-*", v.Active.Single(e => e.Entry.Item.Key == "m").MutedBy);
        Assert.Equal(["w"], v.Waiting.Select(e => e.Entry.Item.Key));
        Assert.Equal(["s"], v.Snoozed.Select(e => e.Entry.Item.Key));
        Assert.Equal(Attention.Hidden, v.AttentionFor("h"));
        Assert.Equal(Attention.Muted, v.AttentionFor("m"));
        Assert.Equal(Attention.Snoozed, v.AttentionFor("s"));
        Assert.Equal(Attention.Waiting, v.AttentionFor("w"));
        Assert.Equal(Attention.Normal, v.AttentionFor("a"));
        Assert.Equal(1, v.BadgeCount); // muted, waiting, snoozed, hidden excluded
    }

    [Fact]
    public void Timed_mute_applies_until_it_expires()
    {
        Assert.Equal(Attention.Muted, V([E("a", "acme/api")], mutes: [new TimedMute("gh:acme", Now.AddMinutes(5))]).AttentionFor("a"));
        Assert.Equal(Attention.Normal, V([E("a", "acme/api")], mutes: [new TimedMute("gh:acme", Now.AddMinutes(-5))]).AttentionFor("a"));
    }

    [Fact]
    public void Hide_beats_snooze_and_mute() =>
        Assert.Equal(Attention.Hidden, V([E("a", "acme/api")], new FiltersConfig { Hide = ["acme"], Mute = ["acme"] }, snoozes: [new Snooze("a", null, true)]).AttentionFor("a"));

    [Fact]
    public void Hidden_items_still_reported_for_reconcile()
    {
        // The view never drops entries from the source list; it only classifies them. Unhiding brings the same entry back.
        var entries = new[] { E("h", "fabrikam/sandbox") };
        var hidden = V(entries, new FiltersConfig { Hide = ["fabrikam"] });
        Assert.Empty(hidden.Active);
        Assert.Single(entries);
        Assert.Equal(["h"], V(entries).Active.Select(e => e.Entry.Item.Key));
    }

    [Fact]
    public void Invalid_patterns_are_ignored_not_thrown() =>
        Assert.Equal(Attention.Normal, V([E("a", "acme/api")], new FiltersConfig { Mute = ["acme//"] }).AttentionFor("a"));
}
