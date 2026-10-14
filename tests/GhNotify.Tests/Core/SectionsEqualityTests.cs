using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class SectionsEqualityTests
{
    static InboxRow Row(string key = "k", RowMode mode = RowMode.Collapsed, string detail = "acme/api", bool enabled = true) =>
        new(key, "GH", "acme/api", "T", "https://u", "3h", false, null, null, null, "Pre-review", true, null, true,
            false, [new ItemDetail("Repo", detail)], null, null,
            [new RowActionButton(ItemAction.ApprovePr, "Approve PR…", enabled, null)], mode, null, null, false, null, null, "acme/api");

    static IReadOnlyList<InboxSection> S(params InboxRow[] rows) => [new InboxSection("REVIEW REQUESTS", rows.ToList())];

    [Fact]
    public void Rebuilt_but_identical_sections_are_equal()
    {
        // Every Build allocates fresh lists; reference equality on them must not force a re-render.
        Assert.True(InboxSections.SameContent(S(Row()), S(Row())));
    }

    [Fact]
    public void Any_visible_difference_is_not_equal()
    {
        Assert.False(InboxSections.SameContent(S(Row()), S(Row(mode: RowMode.Expanded))));
        Assert.False(InboxSections.SameContent(S(Row()), S(Row(detail: "acme/web"))));
        Assert.False(InboxSections.SameContent(S(Row()), S(Row(enabled: false))));
        Assert.False(InboxSections.SameContent(S(Row()), S(Row(), Row("k2"))));
        Assert.False(InboxSections.SameContent(S(Row()), [new InboxSection("APPROVALS", [Row()])]));
        Assert.False(InboxSections.SameContent(null, S(Row())));
    }

    [Fact]
    public void Section_header_changes_are_not_equal()
    {
        // A collapsed section has no rows, but its header shows the count and the expand state.
        InboxSection Waiting(int count, bool expanded) => new("Waiting on others", []) { Id = "waiting", Collapsible = true, Expanded = expanded, Count = count };
        Assert.True(InboxSections.SameContent([Waiting(2, false)], [Waiting(2, false)]));
        Assert.False(InboxSections.SameContent([Waiting(2, false)], [Waiting(3, false)]));
        Assert.False(InboxSections.SameContent([Waiting(2, false)], [Waiting(2, true)]));
    }
}
