namespace GhNotify.Core;

/// <summary>
/// When toasts and sounds are held back: paused by hand, or (with <c>notifications.holdWhilePresenting</c>) while the OS says the
/// user is presenting. Held items still reach the popup and badge; nothing replays afterwards.
/// </summary>
public static class ToastHold
{
    public static bool IsHeld(DateTimeOffset? pausedUntil, bool holdWhilePresenting, bool presenting, DateTimeOffset now) =>
        (pausedUntil is { } until && now < until) || (holdWhilePresenting && presenting);

    /// <summary>Drops the toasts to show and the sound; removals still go through so a stale toast is cleared.</summary>
    public static NotificationPlan Apply(NotificationPlan plan, bool held) =>
        held ? new NotificationPlan(plan.Toasts.Where(t => t.Action != ToastAction.Show).ToList(), null) : plan;
}
