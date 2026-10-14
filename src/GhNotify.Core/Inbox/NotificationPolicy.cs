namespace GhNotify.Core;

public enum ToastAction { Show, Remove }

public sealed record ToastRequest(ToastAction Action, string Tag, string? Title = null, string? Body = null, string? Url = null);

public static class NotificationPolicy
{
    public const string SummaryTag = "summary";

    public static IReadOnlyList<ToastRequest> Plan(InboxDiff diff, bool inWorkHours)
    {
        var plan = new List<ToastRequest>();
        foreach (var r in diff.Removed)
        {
            plan.Add(new ToastRequest(ToastAction.Remove, ItemTag.For(r.Key)));
            plan.Add(new ToastRequest(ToastAction.Remove, ItemTag.ForAgent(r.Key)));
        }
        if (!inWorkHours) return plan;
        foreach (var a in diff.Added.Where(i => i.Kind != ItemKind.MyPrWaiting))
            plan.Add(new ToastRequest(ToastAction.Show, ItemTag.For(a.Key), TitleFor(a), $"{a.Repo}: {a.Title}", a.Url));
        var initial = diff.InitialLoad.Where(i => i.Kind != ItemKind.MyPrWaiting).ToList();
        if (initial.Count > 0)
        {
            var n = initial.Count;
            plan.Add(new ToastRequest(ToastAction.Show, SummaryTag, $"{n} item{(n == 1 ? "" : "s")} waiting on you",
                string.Join("\n", initial.Take(3).Select(i => $"{i.Repo}: {i.Title}"))));
        }
        return plan;
    }

    public static string TitleFor(InboxItem i) => i.Kind switch
    {
        ItemKind.ReviewRequested => "Review requested",
        ItemKind.ApprovalPending => "Approval needed",
        ItemKind.MyPrCiFailing => "Your PR is failing",
        ItemKind.MyPrNeedsResponse => "Your PR needs a response",
        ItemKind.MyPrReadyToMerge => "Ready to merge",
        ItemKind.MyPrWaiting => "Waiting on others",
        _ => throw new ArgumentOutOfRangeException(nameof(i), i.Kind, null),
    };
}
