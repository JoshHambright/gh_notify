namespace GhNotify.Core;

public enum RowMenuKind { Snooze, Mute, Unsnooze, Hide }
public sealed record RowMenuCommand(RowMenuKind Kind, SnoozeChoice? Snooze = null, MuteScope? Scope = null, MuteDuration? Duration = null);
public sealed record RowMenuEntry(string Header, RowMenuCommand? Command, IReadOnlyList<RowMenuEntry> Children);

/// <summary>The row ⋯ menu: snooze, mute and hide choices, or just Unsnooze for a snoozed row.</summary>
public static class RowMenus
{
    public static IReadOnlyList<RowMenuEntry> For(InboxRow row)
    {
        if (row.SnoozeText is not null) return [Leaf("Unsnooze", new(RowMenuKind.Unsnooze))];
        var menu = new List<RowMenuEntry>
        {
            new("Snooze", null,
            [
                Leaf("1 hour", new(RowMenuKind.Snooze, SnoozeChoice.OneHour)),
                Leaf("4 hours", new(RowMenuKind.Snooze, SnoozeChoice.FourHours)),
                Leaf("Until tomorrow", new(RowMenuKind.Snooze, SnoozeChoice.Tomorrow)),
                Leaf("Next week", new(RowMenuKind.Snooze, SnoozeChoice.NextWeek)),
                Leaf("Until it changes", new(RowMenuKind.Snooze, SnoozeChoice.UntilChange)),
            ]),
            Mute("Mute repo", MuteScope.Repo),
        };
        if (row.ProviderGlyph == "ADO") menu.Add(Mute("Mute project", MuteScope.Project));
        menu.Add(Mute("Mute org", MuteScope.Org));
        // Hide has no duration: the item is gone until its pattern is removed in Settings → Filters (or Undo in the footer).
        menu.Add(Leaf("Hide repo", new(RowMenuKind.Hide, Scope: MuteScope.Repo)));
        if (row.ProviderGlyph == "ADO") menu.Add(Leaf("Hide project", new(RowMenuKind.Hide, Scope: MuteScope.Project)));
        menu.Add(Leaf("Hide org", new(RowMenuKind.Hide, Scope: MuteScope.Org)));
        return menu;
    }

    static RowMenuEntry Leaf(string header, RowMenuCommand cmd) => new(header, cmd, []);

    static RowMenuEntry Mute(string header, MuteScope scope) => new(header, null,
    [
        Leaf("1 hour", new(RowMenuKind.Mute, Scope: scope, Duration: MuteDuration.OneHour)),
        Leaf("Until tomorrow", new(RowMenuKind.Mute, Scope: scope, Duration: MuteDuration.Tomorrow)),
        Leaf("Forever", new(RowMenuKind.Mute, Scope: scope, Duration: MuteDuration.Forever)),
    ]);
}
