using GhNotify.Core;

namespace GhNotify.App;

/// <summary>Pure row-state transitions for expand / two-step confirm, used by AppController.</summary>
public static class RowStateMachine
{

    public const string Checking = "Checking…";

    /// <summary>null/Collapsed → Expanded; Expanded → Collapsed; any action mode is left unchanged.</summary>
    public static RowUiState Toggle(RowUiState? s) => s?.Mode switch
    {
        null or RowMode.Collapsed => new RowUiState(RowMode.Expanded),
        RowMode.Expanded => new RowUiState(RowMode.Collapsed),
        _ => s,
    };

    public static RowUiState Begin(ItemAction a, DateTimeOffset now) =>
        new(RowMode.Confirming, a, Preview: null, Message: Checking, ConfirmOpenedAt: now);

    public static RowUiState WithPreview(RowUiState s, ActionPreview p) =>
        s with { Preview = p, Message = p.Allowed ? null : p.RefusalReason };

    public static bool CanConfirm(RowUiState? s, DateTimeOffset now) =>
        s is { Mode: RowMode.Confirming, Preview.Allowed: true, ConfirmOpenedAt: { } opened } && now - opened >= ActionText.ConfirmDelay;

    public static RowUiState Sending(RowUiState s) =>
        s with { Mode = RowMode.Sending, Message = s.Action == ItemAction.Reject ? "Rejecting…" : "Approving…" };

    public static RowUiState Finished(RowUiState s, ActionResult r) => r.Ok
        ? s with { Mode = RowMode.Done, Message = s.Action == ItemAction.Reject ? "Rejected ✓" : "Approved ✓" }
        : s with { Mode = RowMode.Failed, Message = r.Error ?? "The action failed" };

    public static RowUiState Cancel(RowUiState s) => new(RowMode.Expanded);

    /// <summary>Popup re-shown: a confirm panel left open gets a fresh 600 ms guard (its preview is kept).</summary>
    public static RowUiState Rearm(RowUiState s, DateTimeOffset now) =>
        s.Mode == RowMode.Confirming ? s with { ConfirmOpenedAt = now } : s;
}

public static class RefreshPolicy
{
    public static readonly TimeSpan MinAge = TimeSpan.FromSeconds(10);

    /// <summary>Opening/focusing the popup refreshes unless a poll finished &lt; 10 s ago or one is already running.</summary>
    public static bool ShouldRefreshOnOpen(DateTimeOffset? lastPolled, bool inFlight, DateTimeOffset now) =>
        !inFlight && (lastPolled is not { } t || now - t >= MinAge);
}
