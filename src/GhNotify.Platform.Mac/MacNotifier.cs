using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Mac;

/// <summary>Notifications through UNUserNotificationCenter (the shim). The tag is the notification id, so Remove and ClearAll work.
/// A click is reported as (tag, "open") or (tag, "url:…"), the same strings as Windows; clicks that arrive before the first handler
/// attaches (a click that launched the app) are buffered and replayed to it.</summary>
public sealed class MacNotifier : INotifier
{
    readonly IMacShim _shim;
    readonly object _gate = new();
    readonly bool _ready;
    Action<string, string>? _activated;
    List<(string Tag, string Action)> _pending = [];

    public MacNotifier(IMacShim shim)
    {
        _shim = shim;
        var status = shim.NotifyInit((tag, url) => Raise(tag, url is null ? ToastActions.Open : ToastActions.ForUrl(url)));
        _ready = status == 0;
        if (!_ready) Log.Error($"notifications are unavailable (shim status {status}); the popup still works");
    }

    public event Action<string, string>? Activated
    {
        add
        {
            List<(string Tag, string Action)> replay;
            lock (_gate)
            {
                _activated += value;
                replay = _pending;
                _pending = [];
            }
            foreach (var (tag, action) in replay) value?.Invoke(tag, action);
        }
        remove { lock (_gate) _activated -= value; }
    }

    /// <summary>Delivers a click to the handlers, or holds it until the first one attaches.</summary>
    internal void Raise(string tag, string action)
    {
        Action<string, string>? handler;
        lock (_gate)
        {
            handler = _activated;
            if (handler is null) _pending.Add((tag, action));
        }
        handler?.Invoke(tag, action);
    }

    public void Apply(ToastRequest t)
    {
        if (!_ready) return;
        try
        {
            if (t.Action == ToastAction.Remove)
            {
                _shim.NotifyRemove(t.Tag);
                return;
            }
            var status = _shim.NotifyShow(t.Tag, t.Title ?? "gh_notify", t.Body ?? "", t.Url, t.Silent);
            if (status != 0) Log.Error($"notification {t.Tag} was not shown (shim status {status})");
        }
        catch (Exception ex)
        {
            Log.Error($"notification {t.Action} {t.Tag} failed", ex);
        }
    }

    public void ClearAll()
    {
        if (!_ready) return;
        try { _shim.NotifyClearAll(); }
        catch (Exception ex) { Log.Error("could not clear notifications", ex); }
    }
}
