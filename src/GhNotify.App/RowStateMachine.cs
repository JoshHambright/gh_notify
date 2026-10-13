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

    /// <summary>
    /// Applies the live check. An allowed preview starts the 600 ms confirm guard at <paramref name="now"/> — the moment the
    /// confirm button can first appear — not when the panel opened (a slow preview must not let the button arrive enabled).
    /// </summary>
    public static RowUiState WithPreview(RowUiState s, ActionPreview p, DateTimeOffset now) => p.Allowed
        ? s with { Preview = p, Message = null, ConfirmOpenedAt = now }
        : s with { Preview = p, Message = p.RefusalReason };

    public static bool CanConfirm(RowUiState? s, DateTimeOffset now) =>
        s is { Mode: RowMode.Confirming, Preview.Allowed: true, ConfirmOpenedAt: { } opened } && now - opened >= ActionText.ConfirmDelay;

    public static RowUiState Sending(RowUiState s) =>
        s with { Mode = RowMode.Sending, Message = s.Action == ItemAction.Reject ? "Rejecting…" : "Approving…" };

    public const string Approved = "Approved ✓";
    public const string Rejected = "Rejected ✓";

    /// <summary>Success shows "Approved ✓"/"Rejected ✓", or the provider's warning when it succeeded with one (e.g. comment not posted).</summary>
    public static RowUiState Finished(RowUiState s, ActionResult r) => r.Ok
        ? s with { Mode = RowMode.Done, Message = r.Error ?? (s.Action == ItemAction.Reject ? Rejected : Approved) }
        : s with { Mode = RowMode.Failed, Message = r.Error ?? "The action failed" };

    /// <summary>
    /// A plain-success Done row is hidden on the next poll. A Done row carrying a warning stays until the user closes it,
    /// so the warning can't vanish unread.
    /// </summary>
    public static bool HidesOnNextPoll(RowUiState s) => s is { Mode: RowMode.Done, Message: Approved or Rejected };

    /// <summary>
    /// A successful action that came back with a warning is also toasted: the success refresh usually removes the row
    /// (e.g. the PR leaves the inbox once voted), which would take the inline warning with it unread.
    /// </summary>
    public static string? ToastFor(ActionResult r, ItemAction action) =>
        r is { Ok: true, Error: { Length: > 0 } warning } && warning is not (Approved or Rejected) ? warning : null;

    public static RowUiState Cancel(RowUiState s) => new(RowMode.Expanded);

    /// <summary>
    /// Popup re-shown: a confirm panel left open gets a fresh 600 ms guard (its preview is kept). A row still awaiting its
    /// preview is returned unchanged — BeginActionAsync swaps the preview in by reference, and WithPreview starts the guard anyway.
    /// </summary>
    public static RowUiState Rearm(RowUiState s, DateTimeOffset now) =>
        s is { Mode: RowMode.Confirming, Preview: not null } ? s with { ConfirmOpenedAt = now } : s;
}

public static class RefreshPolicy
{
    public static readonly TimeSpan MinAge = TimeSpan.FromSeconds(10);

    /// <summary>Opening/focusing the popup refreshes unless a poll finished &lt; 10 s ago or one is already running.</summary>
    public static bool ShouldRefreshOnOpen(DateTimeOffset? lastPolled, bool inFlight, DateTimeOffset now) =>
        !inFlight && (lastPolled is not { } t || now - t >= MinAge);
}
