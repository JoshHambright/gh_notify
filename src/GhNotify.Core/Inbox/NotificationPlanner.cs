namespace GhNotify.Core;

public sealed record NotificationPlan(IReadOnlyList<ToastRequest> Toasts, string? Sound);

public static class NotificationPlanner
{
    static readonly ItemKind[] Priority =
        [ItemKind.ApprovalPending, ItemKind.MyPrCiFailing, ItemKind.ReviewRequested, ItemKind.MyPrNeedsResponse, ItemKind.MyPrReadyToMerge];

    public static SoundRule? RuleFor(InboxItem item, AppConfig cfg) =>
        cfg.Notifications.Sounds.FirstOrDefault(r => ItemMatcher.Matches(r.Match, item));

    public static NotificationPlan Plan(InboxDiff diff, IReadOnlyList<InboxItem> woken, InboxView view, AppConfig cfg, bool inWorkHours)
    {
        var toasts = new List<ToastRequest>();
        foreach (var r in diff.Removed)
        {
            toasts.Add(new ToastRequest(ToastAction.Remove, ItemTag.For(r.Key)));
            toasts.Add(new ToastRequest(ToastAction.Remove, ItemTag.ForAgent(r.Key)));
        }

        var candidates = diff.Added.Select(i => (Item: i, Back: false)).Concat(woken.Select(i => (Item: i, Back: true)))
            .Where(c => view.AttentionFor(c.Item.Key) == Attention.Normal && c.Item.Kind != ItemKind.MyPrWaiting)
            .Select(c => (c.Item, c.Back, Rule: RuleFor(c.Item, cfg)))
            .Where(c => inWorkHours || c.Rule?.AlertOffHours == true)
            .ToList();

        var winner = candidates.Where(c => (c.Rule?.Sound ?? "default") != "none")
            .OrderBy(c => Array.IndexOf(Priority, c.Item.Kind) is var i && i < 0 ? int.MaxValue : i)
            .ThenBy(c => c.Item.UpdatedAt)
            .Select(c => (c.Item.Key, Sound: c.Rule?.Sound ?? "default"))
            .FirstOrDefault();

        foreach (var c in candidates)
        {
            var carriesDefault = winner.Key == c.Item.Key && winner.Sound == "default";
            toasts.Add(new ToastRequest(ToastAction.Show, ItemTag.For(c.Item.Key),
                (c.Back ? "Back: " : "") + NotificationPolicy.TitleFor(c.Item), $"{ItemText.RepoAndNumber(c.Item)}: {c.Item.Title}", c.Item.Url)
            { Silent = !carriesDefault });
        }

        if (inWorkHours)
        {
            var initial = diff.InitialLoad.Where(i => view.AttentionFor(i.Key) == Attention.Normal && i.Kind != ItemKind.MyPrWaiting).ToList();
            if (initial.Count > 0)
            {
                var n = initial.Count;
                toasts.Add(new ToastRequest(ToastAction.Show, NotificationPolicy.SummaryTag, $"{n} item{(n == 1 ? "" : "s")} waiting on you",
                    string.Join("\n", initial.Take(3).Select(i => $"{i.Repo}: {i.Title}"))) { Silent = winner.Key is not null });
            }
        }

        var custom = winner.Key is not null && winner.Sound != "default" ? winner.Sound : null;
        return new NotificationPlan(toasts, custom);
    }
}
