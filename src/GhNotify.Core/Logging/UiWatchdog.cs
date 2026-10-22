namespace GhNotify.Core;

/// <summary>
/// Logs when the UI thread stops answering: every <see cref="Interval"/> it posts a ping to the UI thread, and a ping still
/// unanswered after the threshold is logged once, with another line when the thread recovers. A stuck UI thread looks like a
/// missing tray icon and a dead hotkey, so the log says which it was.
/// </summary>
public sealed class UiWatchdog : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    readonly Action<Action> _post;
    readonly TimeProvider _time;
    readonly TimeSpan _threshold;
    readonly object _gate = new();
    ITimer? _timer;
    DateTimeOffset? _pingSentAt;
    bool _reported;

    public UiWatchdog(Action<Action> postToUi, TimeSpan threshold, TimeProvider? time = null)
    {
        _post = postToUi;
        _threshold = threshold;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True from the moment a stall is logged until the UI thread answers.</summary>
    public bool Stalled { get { lock (_gate) return _reported; } }

    public void Start() => _timer ??= _time.CreateTimer(_ => Tick(), null, Interval, Interval);

    internal void Tick()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_pingSentAt is { } sent)
            {
                if (!_reported && now - sent >= _threshold)
                {
                    _reported = true;
                    Log.Error($"the UI thread has not responded for {(now - sent).TotalSeconds:0}s");
                }
                return;
            }
            _pingSentAt = now;
        }
        _post(Answer);
    }

    void Answer()
    {
        lock (_gate)
        {
            if (_reported && _pingSentAt is { } sent)
                Log.Info($"the UI thread responded again after {(_time.GetUtcNow() - sent).TotalSeconds:0}s");
            _pingSentAt = null;
            _reported = false;
        }
    }

    public void Dispose() => _timer?.Dispose();
}
