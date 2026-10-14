using GhNotify.Core;

namespace GhNotify.App;

public enum BatchPhase { Idle, Previewing, Ready, Running, Done }

public enum BatchLineStatus { Checking, Refused, Ready, Running, Ok, Stale, Failed }

/// <summary>One item in the batch panel: the live check result, whether it is ticked, and (after the run) its outcome.</summary>
public sealed record BatchLine(
    InboxItem Item, string RepoDisplay, BatchLineStatus Status, bool Checked, ActionPreview? Preview, string? Message)
{
    public string Key => Item.Key;
}

public sealed record BatchEntry(InboxItem Item, AgentRun? Run, string RepoDisplay);

/// <summary>
/// Batch approve/reject for the Approvals section (v1.3 spec §5). Previews every item (at most <see cref="MaxParallel"/> in flight),
/// then, on confirm, executes the ticked items one at a time in display order. Lines are keyed by item key and never shift when the
/// inbox refreshes mid-batch, so a result always lands on the line it belongs to.
/// </summary>
public sealed class BatchCoordinator(IItemActions actions, TimeSpan previewTimeout, Func<DateTimeOffset>? clock = null)
{
    public const int MaxParallel = 4;

    readonly object _gate = new();
    readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.Now);
    List<BatchLine> _lines = [];
    CancellationTokenSource? _previews;
    BatchPhase _phase;
    ItemAction _action = ItemAction.Approve;
    DateTimeOffset? _readyAt;
    string? _summary;
    int _generation;

    /// <summary>Raised (on any thread) whenever the panel's content changes.</summary>
    public event Action? Changed;

    public BatchPhase Phase { get { lock (_gate) return _phase; } }
    public ItemAction BatchAction { get { lock (_gate) return _action; } }
    public string? Summary { get { lock (_gate) return _summary; } }
    public DateTimeOffset? ReadyAt { get { lock (_gate) return _readyAt; } }
    /// <summary>The panel is open or running: row actions in the section stay disabled.</summary>
    public bool Active => Phase != BatchPhase.Idle;
    public IReadOnlyList<BatchLine> Lines { get { lock (_gate) return _lines.ToArray(); } }
    public int CheckedCount { get { lock (_gate) return _lines.Count(IsTicked); } }
    public string ConfirmLabel => BatchApproval.ConfirmLabel(BatchAction, CheckedCount);

    static bool IsTicked(BatchLine l) => l.Checked && l.Status == BatchLineStatus.Ready;

    /// <summary>Opens the panel and previews every entry in parallel. Completes when all previews are back (or the panel was cancelled).</summary>
    public async Task OpenAsync(ItemAction action, IReadOnlyList<BatchEntry> entries, CancellationToken ct)
    {
        CancellationTokenSource cts;
        int generation;
        lock (_gate)
        {
            if (_phase is BatchPhase.Previewing or BatchPhase.Running) return;
            _previews?.Dispose();
            cts = _previews = CancellationTokenSource.CreateLinkedTokenSource(ct);
            generation = ++_generation;
            _action = action;
            _phase = BatchPhase.Previewing;
            _readyAt = null;
            _summary = null;
            _lines = entries.Select(e => new BatchLine(e.Item, e.RepoDisplay, BatchLineStatus.Checking, false, null, null)).ToList();
        }
        Changed?.Invoke();

        using var gate = new SemaphoreSlim(MaxParallel);
        await Task.WhenAll(entries.Select(async e =>
        {
            await gate.WaitAsync(CancellationToken.None);
            try { await PreviewOneAsync(e, action, cts.Token, generation); }
            finally { gate.Release(); }
        }));

        lock (_gate)
        {
            if (generation != _generation || _phase != BatchPhase.Previewing) return;
            _phase = BatchPhase.Ready;
            _readyAt = _now(); // the 600 ms confirm guard starts when the last preview is back
        }
        Changed?.Invoke();
    }

    async Task PreviewOneAsync(BatchEntry e, ItemAction action, CancellationToken ct, int generation)
    {
        ActionPreview preview;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(previewTimeout);
            preview = await actions.PreviewAsync(e.Item, action, e.Run, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            preview = ActionText.Refuse("Timed out checking the current state — try again.");
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Log.Error($"batch preview {action} {e.Item.Key} failed", ex);
            preview = ActionText.Refuse(ex.Message);
        }
        lock (_gate)
        {
            if (generation != _generation) return;
            Replace(e.Item.Key, l => preview.Allowed
                ? l with { Status = BatchLineStatus.Ready, Checked = true, Preview = preview, Message = preview.Statement }
                : l with { Status = BatchLineStatus.Refused, Checked = false, Preview = preview, Message = preview.RefusalReason });
        }
        Changed?.Invoke();
    }

    void Replace(string key, Func<BatchLine, BatchLine> edit)
    {
        var i = _lines.FindIndex(l => l.Key == key);
        if (i >= 0) _lines[i] = edit(_lines[i]);
    }

    public void SetChecked(string key, bool value)
    {
        lock (_gate)
        {
            if (_phase != BatchPhase.Ready) return;
            Replace(key, l => l.Status == BatchLineStatus.Ready ? l with { Checked = value } : l);
        }
        Changed?.Invoke();
    }

    /// <summary>The confirm button may be enabled: previews are back, 600 ms have passed and at least one item is ticked.</summary>
    public bool CanConfirm(DateTimeOffset now)
    {
        lock (_gate)
            return _phase == BatchPhase.Ready && _readyAt is { } t && now - t >= ActionText.ConfirmDelay && _lines.Any(IsTicked);
    }

    /// <summary>The popup was re-shown: a panel left open gets a fresh 600 ms guard.</summary>
    public void Rearm()
    {
        lock (_gate)
        {
            if (_phase != BatchPhase.Ready) return;
            _readyAt = _now();
        }
        Changed?.Invoke();
    }

    /// <summary>Closes the panel (cancelling previews still in flight). Ignored while items are being sent: there is no cancel mid-run.</summary>
    public void Close()
    {
        lock (_gate)
        {
            if (_phase is BatchPhase.Running or BatchPhase.Idle) return;
            _generation++;
            _previews?.Cancel();
            _phase = BatchPhase.Idle;
            _lines = [];
            _readyAt = null;
            _summary = null;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Sends the ticked items sequentially in display order with the shared comment. Re-checks the guard itself. Each line gets its own
    /// outcome; <paramref name="afterRun"/> (a refresh) runs once everything has finished.
    /// </summary>
    public async Task ConfirmAsync(string? comment, CancellationToken ct, Func<Task>? afterRun = null)
    {
        List<string> keys;
        ItemAction action;
        lock (_gate)
        {
            if (!CanConfirm(_now())) return;
            _phase = BatchPhase.Running;
            action = _action;
            keys = _lines.Where(IsTicked).Select(l => l.Key).ToList();
        }
        Changed?.Invoke();

        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        var results = new List<BatchItemResult>();
        foreach (var key in keys)
        {
            BatchLine line;
            lock (_gate)
            {
                Replace(key, l => l with { Status = BatchLineStatus.Running, Message = "Sending…" });
                line = _lines.First(l => l.Key == key);
            }
            Changed?.Invoke();

            var (outcome, message) = await ExecuteOneAsync(line, action, comment, ct);
            results.Add(new BatchItemResult(key, outcome, message));
            lock (_gate)
            {
                Replace(key, l => l with
                {
                    Status = outcome switch { BatchOutcome.Ok => BatchLineStatus.Ok, BatchOutcome.Stale => BatchLineStatus.Stale, _ => BatchLineStatus.Failed },
                    Message = message,
                });
            }
            Changed?.Invoke();
        }

        lock (_gate)
        {
            _summary = BatchApproval.Summary(results, action);
            _phase = BatchPhase.Done;
        }
        Changed?.Invoke();
        if (afterRun is not null)
        {
            try { await afterRun(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.Error("refresh after batch failed", ex); }
        }
    }

    async Task<(BatchOutcome, string?)> ExecuteOneAsync(BatchLine line, ItemAction action, string? comment, CancellationToken ct)
    {
        try
        {
            var r = await actions.ExecuteAsync(line.Item, action, line.Preview!, new ActionInput(comment), ct);
            if (r.Ok) return (BatchOutcome.Ok, r.Error ?? RowStateMachine.DoneLabel(action));
            return (r.Stale ? BatchOutcome.Stale : BatchOutcome.Failed, r.Error ?? "The action failed");
        }
        catch (OperationCanceledException)
        {
            return (BatchOutcome.Failed, "Cancelled");
        }
        catch (Exception ex)
        {
            Log.Error($"batch {action} {line.Key}: failed (comment {comment?.Length ?? 0} chars)", ex);
            return (BatchOutcome.Failed, ex.Message);
        }
    }
}
