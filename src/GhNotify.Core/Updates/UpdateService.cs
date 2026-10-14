namespace GhNotify.Core;

public sealed record UpdateCandidate(string Version, object Native);

public interface IUpdateClient
{
    bool IsInstalled { get; }
    string? CurrentVersion { get; }
    Task<UpdateCandidate?> CheckAsync(CancellationToken ct);
    Task DownloadAsync(UpdateCandidate candidate, CancellationToken ct);
    /// <summary>Arranges for the update to apply when this process exits, then restarts the app.</summary>
    void ApplyOnExit(UpdateCandidate candidate);
}

public enum UpdatePhase { Off, DevBuild, Idle, Checking, Downloading, Ready, Failed }

public sealed record UpdateStatus(UpdatePhase Phase, string? CurrentVersion, string? ReadyVersion, string? LastError, DateTimeOffset? LastChecked);

public enum RestartDecision { Restarting, NeedsConfirm, NothingToApply }

public sealed class UpdateService(IUpdateClient client, Func<UpdatesConfig> config, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly SemaphoreSlim _gate = new(1, 1);
    UpdateCandidate? _ready;

    public UpdateStatus Status { get; private set; } = new(client.IsInstalled ? UpdatePhase.Idle : UpdatePhase.DevBuild, client.CurrentVersion, null, null, null);
    public event Action? Changed;

    void Set(UpdateStatus s) { Status = s; Changed?.Invoke(); }

    public async Task CheckNowAsync(CancellationToken ct)
    {
        if (!client.IsInstalled) { Set(Status with { Phase = UpdatePhase.DevBuild }); return; }
        if (!config().Enabled) { Set(Status with { Phase = UpdatePhase.Off }); return; }
        if (_ready is not null) return; // already downloaded; waiting for the user to restart
        if (!await _gate.WaitAsync(0, ct)) return;
        try
        {
            Set(Status with { Phase = UpdatePhase.Checking, LastError = null });
            var next = await client.CheckAsync(ct);
            if (next is null) { Set(Status with { Phase = UpdatePhase.Idle, LastChecked = _time.GetUtcNow() }); return; }
            Set(Status with { Phase = UpdatePhase.Downloading });
            await client.DownloadAsync(next, ct);
            _ready = next;
            Set(Status with { Phase = UpdatePhase.Ready, ReadyVersion = next.Version, LastChecked = _time.GetUtcNow() });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Error("update check failed", ex);
            Set(Status with { Phase = UpdatePhase.Failed, LastError = ex.Message, LastChecked = _time.GetUtcNow() });
        }
        finally { _gate.Release(); }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), _time, ct);
            while (!ct.IsCancellationRequested)
            {
                await CheckNowAsync(ct);
                await Task.Delay(TimeSpan.FromHours(Math.Clamp(config().CheckHours, 1, 168)), _time, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    public RestartDecision RequestRestart(int runningAgents, bool confirmed)
    {
        if (_ready is null) return RestartDecision.NothingToApply;
        if (runningAgents > 0 && !confirmed) return RestartDecision.NeedsConfirm;
        client.ApplyOnExit(_ready);
        return RestartDecision.Restarting;
    }
}
