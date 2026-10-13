using System.Net;

namespace GhNotify.Core;

public enum ItemAction { Approve, Reject, ApprovePr }

/// <summary>Result of the fresh live check; Token carries provider data from preview to execute (e.g. environment ids, commit sha).</summary>
public sealed record ActionPreview(bool Allowed, string Statement, string ConfirmLabel, string? Warning, string? RefusalReason, object? Token);

public sealed record ActionResult(bool Ok, string? Error, bool Stale = false);

public interface IItemActions
{
    IReadOnlyList<ItemAction> ActionsFor(InboxItem item);
    Task<ActionPreview> PreviewAsync(InboxItem item, ItemAction action, AgentRun? run, CancellationToken ct);
    Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, string? comment, CancellationToken ct);
}

public sealed class CompositeItemActions(IReadOnlyDictionary<Provider, IItemActions> byProvider) : IItemActions
{
    public IReadOnlyList<ItemAction> ActionsFor(InboxItem item) =>
        byProvider.TryGetValue(item.Provider, out var a) ? a.ActionsFor(item) : [];

    public Task<ActionPreview> PreviewAsync(InboxItem item, ItemAction action, AgentRun? run, CancellationToken ct) =>
        byProvider.TryGetValue(item.Provider, out var a)
            ? a.PreviewAsync(item, action, run, ct)
            : Task.FromResult(ActionText.Refuse("Not supported for this source"));

    public Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, string? comment, CancellationToken ct) =>
        byProvider.TryGetValue(item.Provider, out var a)
            ? a.ExecuteAsync(item, action, preview, comment, ct)
            : Task.FromResult(new ActionResult(false, "Not supported for this source"));
}

public static class ActionText
{
    public const string NoPreReview = "No pre-review at this commit.";

    /// <summary>The confirm button stays disabled this long after the confirm panel opens (or the popup is re-shown).</summary>
    public static readonly TimeSpan ConfirmDelay = TimeSpan.FromMilliseconds(600);

    public static IReadOnlyList<ItemAction> DefaultActions(InboxItem item) => item.Kind switch
    {
        ItemKind.ApprovalPending => [ItemAction.Approve, ItemAction.Reject],
        ItemKind.ReviewRequested => [ItemAction.ApprovePr],
        _ => [],
    };

    public static string ButtonLabel(ItemAction a) => a switch
    {
        ItemAction.Approve => "Approve…",
        ItemAction.Reject => "Reject…",
        _ => "Approve PR…",
    };

    public static string? PrWarning(InboxItem item, AgentRun? run) =>
        run is { State: AgentState.Ready } r && r.HeadSha == item.HeadSha ? null : NoPreReview;

    public static ActionResult FromHttpError(HttpRequestException ex) => ex.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new(false, "You don't have permission to do this" + (ex.Message.Length is > 0 and < 160 ? $": {ex.Message}" : "")),
        HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity =>
            new(false, "This changed since you looked — refreshing", Stale: true),
        _ => new(false, ex.Message),
    };

    public static ActionPreview Refuse(string reason) => new(false, "", "", null, reason, null);
}
