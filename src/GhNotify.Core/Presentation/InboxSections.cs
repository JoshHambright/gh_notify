namespace GhNotify.Core;

public static class InboxSections
{
    static readonly IReadOnlyList<ItemDetail> NoDetails = [];
    static readonly IReadOnlyList<RowActionButton> NoActions = [];

    /// <summary>
    /// True when two builds would render identically. The popup uses this to skip rebuilding row visuals,
    /// because a rebuild between mouse-down and mouse-up swallows the click.
    /// </summary>
    public static bool SameContent(IReadOnlyList<InboxSection>? a, IReadOnlyList<InboxSection>? b)
    {
        if (a is null || b is null || a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Title != b[i].Title || a[i].Rows.Count != b[i].Rows.Count) return false;
            for (var j = 0; j < a[i].Rows.Count; j++)
                if (!SameRow(a[i].Rows[j], b[i].Rows[j])) return false;
        }
        return true;
    }

    // Record equality compares list members by reference; compare the lists by value and the rest via the record.
    static bool SameRow(InboxRow x, InboxRow y) =>
        x.Details.SequenceEqual(y.Details) && x.Actions.SequenceEqual(y.Actions) &&
        x with { Details = NoDetails, Actions = NoActions } == y with { Details = NoDetails, Actions = NoActions };
}
