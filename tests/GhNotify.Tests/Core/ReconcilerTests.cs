using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ReconcilerTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    static InboxItem I(string key, string? sha = "s1", string? detail = null, string title = "t", DateTimeOffset? updated = null) =>
        new(key, Provider.GitHub, ItemKind.ReviewRequested, "acme/api", title, "https://x/" + key, "alice", updated ?? T0, sha, 10, detail);

    static Dictionary<string, SourceResult> R(string source, params InboxItem[] items) => new() { [source] = SourceResult.Ok(items) };

    [Fact]
    public void ItemsFor_returns_a_sources_current_items_or_empty()
    {
        var r = new Reconciler(new Dictionary<string, IReadOnlyList<InboxItem>> { ["ADO:contoso"] = [I("x")] });
        r.Apply(R("GitHub", I("a"), I("b")), T0);
        Assert.Equal(["x"], r.ItemsFor("ADO:contoso").Select(i => i.Key));
        Assert.Equal(2, r.ItemsFor("GitHub").Count);
        Assert.Empty(r.ItemsFor("ADO:other"));
    }

    [Fact]
    public void First_poll_of_a_source_is_initial_load()
    {
        var r = new Reconciler();
        var d = r.Apply(R("GitHub", I("a"), I("b")), T0);
        Assert.Equal(2, d.InitialLoad.Count);
        Assert.Empty(d.Added);
        Assert.Equal(2, r.Entries.Count);
    }

    [Fact]
    public void Later_polls_report_added_removed_changed()
    {
        var r = new Reconciler();
        r.Apply(R("GitHub", I("a"), I("b"), I("c")), T0);
        var d = r.Apply(R("GitHub", I("a"), I("b", sha: "s2"), I("d")), T0);
        Assert.Equal(["d"], d.Added.Select(i => i.Key));
        Assert.Equal(["c"], d.Removed.Select(i => i.Key));
        var change = Assert.Single(d.Changed);
        Assert.Equal("b", change.New.Key);
        Assert.True(change.HeadChanged);
        Assert.Empty(d.InitialLoad);
    }

    [Fact]
    public void Detail_or_title_change_is_a_change_but_updatedAt_is_silent()
    {
        var r = new Reconciler();
        r.Apply(R("GitHub", I("a"), I("b")), T0);
        var d = r.Apply(R("GitHub", I("a", detail: "2 threads"), I("b", updated: T0.AddHours(1))), T0);
        var c = Assert.Single(d.Changed);
        Assert.Equal("a", c.New.Key);
        Assert.False(c.HeadChanged);
        Assert.Equal(T0.AddHours(1), r.Entries.Single(e => e.Item.Key == "b").Item.UpdatedAt);
    }

    [Fact]
    public void Unhealthy_source_keeps_items_as_stale_and_removes_nothing()
    {
        var r = new Reconciler();
        r.Apply(R("ADO:contoso", I("a")), T0);
        var d = r.Apply(new Dictionary<string, SourceResult> { ["ADO:contoso"] = SourceResult.Fail("run az login") }, T0.AddMinutes(2));
        Assert.True(d.IsEmpty);
        var e = Assert.Single(r.Entries);
        Assert.True(e.Stale);
        var s = Assert.Single(r.Statuses);
        Assert.False(s.Healthy);
        Assert.Equal("run az login", s.Problem);
        Assert.Equal(T0, s.LastSuccess);
    }

    [Fact]
    public void Absent_source_is_kept_and_marked_not_polled()
    {
        var r = new Reconciler();
        r.Apply(new Dictionary<string, SourceResult> { ["GitHub"] = SourceResult.Ok([I("a")]), ["ADO:contoso"] = SourceResult.Ok([I("b")]) }, T0);
        var d = r.Apply(R("GitHub", I("a")), T0);
        Assert.True(d.IsEmpty);
        Assert.Contains(r.Entries, e => e.Item.Key == "b" && e.Stale);
        Assert.Equal("not polled", r.Statuses.Single(s => s.Name == "ADO:contoso").Problem);
    }

    [Fact]
    public void Recovering_source_reconciles_against_items_kept_while_unhealthy()
    {
        var r = new Reconciler();
        r.Apply(R("GitHub", I("a"), I("b")), T0);
        r.Apply(new Dictionary<string, SourceResult> { ["GitHub"] = SourceResult.Fail("x") }, T0);
        var d = r.Apply(R("GitHub", I("b")), T0);
        Assert.Equal(["a"], d.Removed.Select(i => i.Key));
        Assert.False(r.Entries.Single().Stale);
    }

    [Fact]
    public void Restored_snapshot_means_no_initial_load_for_known_sources()
    {
        var snapshot = new Dictionary<string, IReadOnlyList<InboxItem>> { ["GitHub"] = [I("a"), I("b")] };
        var r = new Reconciler(snapshot);
        Assert.Equal(2, r.Entries.Count);
        var d = r.Apply(R("GitHub", I("b"), I("c")), T0);
        Assert.Equal(["c"], d.Added.Select(i => i.Key));
        Assert.Equal(["a"], d.Removed.Select(i => i.Key));
        Assert.Empty(d.InitialLoad);
    }

    [Fact]
    public void Retire_removes_matching_sources()
    {
        var r = new Reconciler();
        r.Apply(new Dictionary<string, SourceResult> { ["GitHub"] = SourceResult.Ok([I("a")]), ["ADO:x"] = SourceResult.Ok([I("b")]) }, T0);
        var d = r.RetireWhere(n => n.StartsWith("ADO:"));
        Assert.Equal(["b"], d.Removed.Select(i => i.Key));
        Assert.Equal(["GitHub"], r.Statuses.Select(s => s.Name));
        Assert.Equal(["a"], r.Snapshot()["GitHub"].Select(i => i.Key));
        Assert.False(r.Snapshot().ContainsKey("ADO:x"));
    }

    [Fact]
    public void Duplicate_keys_in_one_result_are_collapsed()
    {
        var r = new Reconciler();
        var d = r.Apply(R("GitHub", I("a"), I("a")), T0);
        Assert.Single(d.InitialLoad);
    }

    [Fact]
    public void Entries_are_newest_first()
    {
        var r = new Reconciler();
        r.Apply(R("GitHub", I("old", updated: T0), I("new", updated: T0.AddHours(1))), T0);
        Assert.Equal(["new", "old"], r.Entries.Select(e => e.Item.Key));
    }
}
