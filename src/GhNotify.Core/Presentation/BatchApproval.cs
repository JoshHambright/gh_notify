namespace GhNotify.Core;

public enum BatchOutcome { Ok, Stale, Failed }

public sealed record BatchItemResult(string Key, BatchOutcome Outcome, string? Message = null);

/// <summary>Batch approve/reject rules for the Approvals section (v1.3 spec §5).</summary>
public static class BatchApproval
{
    public const string SectionId = "ApprovalPending";
    public const int MinItems = 2;

    /// <summary>Non-stale rows of the Approvals section that offer Approve (per the row's buttons), in display order.</summary>
    public static IReadOnlyList<InboxRow> Eligible(InboxSection section) =>
        Eligible(section, r => r.Actions.Select(a => a.Action).ToList());

    /// <summary>Same, with the offered actions supplied by the caller instead of the row's buttons.</summary>
    public static IReadOnlyList<InboxRow> Eligible(InboxSection section, Func<InboxRow, IReadOnlyList<ItemAction>> actionsFor) =>
        section.Id != SectionId ? [] : section.Rows.Where(r => !r.Stale && actionsFor(r).Contains(ItemAction.Approve)).ToList();

    /// <summary>"Approve all…" / "Reject all…" appear when at least two rows are eligible.</summary>
    public static bool ShowsButtons(InboxSection section) => Eligible(section).Count >= MinItems;

    public static bool ShowsButtons(InboxSection section, Func<InboxRow, IReadOnlyList<ItemAction>> actionsFor) =>
        Eligible(section, actionsFor).Count >= MinItems;

    /// <summary>"Approve 3" / "Reject 3" — counts the checked items.</summary>
    public static string ConfirmLabel(ItemAction action, int checkedCount) =>
        $"{(action == ItemAction.Reject ? "Reject" : "Approve")} {checkedCount}";

    /// <summary>"3 approved · 1 changed since you looked · 0 failed" (or "rejected" for <see cref="ItemAction.Reject"/>).</summary>
    public static string Summary(IEnumerable<BatchItemResult> results, ItemAction action = ItemAction.Approve)
    {
        var list = results.ToList();
        var ok = list.Count(r => r.Outcome == BatchOutcome.Ok);
        var stale = list.Count(r => r.Outcome == BatchOutcome.Stale);
        var failed = list.Count(r => r.Outcome == BatchOutcome.Failed);
        return $"{ok} {(action == ItemAction.Reject ? "rejected" : "approved")} · {stale} changed since you looked · {failed} failed";
    }
}
