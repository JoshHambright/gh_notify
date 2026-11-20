namespace GhNotify.Core;

public enum ToastAction { Show, Remove }

public sealed record ToastRequest(ToastAction Action, string Tag, string? Title = null, string? Body = null, string? Url = null)
{
    /// <summary>Show the toast without its own sound (a custom sound is played separately, or another toast carries the sound).</summary>
    public bool Silent { get; init; }
}

public static class NotificationPolicy
{
    public const string SummaryTag = "summary";

    public static string TitleFor(InboxItem i) => i.Kind switch
    {
        ItemKind.ReviewRequested => "Review requested",
        ItemKind.ApprovalPending => "Approval needed",
        ItemKind.MyPrCiFailing => "Your PR is failing",
        ItemKind.MyPrNeedsResponse => "Your PR needs a response",
        ItemKind.MyPrReadyToMerge => "Ready to merge",
        ItemKind.MyPrWaiting => "Waiting on others",
        ItemKind.PipelineRun => i.Detail switch
        {
            nameof(PipelineOutcome.Failed) => "Pipeline failed",
            nameof(PipelineOutcome.PartiallySucceeded) => "Pipeline succeeded with issues",
            _ => "Pipeline succeeded",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(i), i.Kind, null),
    };
}
