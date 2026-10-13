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
        foreach (var a in diff.Added)
            plan.Add(new ToastRequest(ToastAction.Show, ItemTag.For(a.Key), TitleFor(a), $"{a.Repo}: {a.Title}", a.Url));
        if (diff.InitialLoad.Count > 0)
        {
            var n = diff.InitialLoad.Count;
            plan.Add(new ToastRequest(ToastAction.Show, SummaryTag, $"{n} item{(n == 1 ? "" : "s")} waiting on you",
                string.Join("\n", diff.InitialLoad.Take(3).Select(i => $"{i.Repo}: {i.Title}"))));
        }
        return plan;
    }

    public static string TitleFor(InboxItem i) => i.Kind switch
    {
        ItemKind.ReviewRequested => "Review requested",
        ItemKind.ApprovalPending => "Approval needed",
        ItemKind.MyPrCiFailing => "Your PR is failing",
        _ => "Your PR needs a response",
    };
}
