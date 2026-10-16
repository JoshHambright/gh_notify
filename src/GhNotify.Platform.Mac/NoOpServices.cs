using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Mac;

/// <summary>Used when libGhNotifyMac.dylib can't be loaded: the app still runs, without notifications, hotkey or login item.</summary>
public sealed class NoOpNotifier : INotifier
{
    public void Apply(ToastRequest request) { }
    public event Action<string, string>? Activated { add { } remove { } }
    public void ClearAll() { }
}

/// <inheritdoc cref="NoOpNotifier"/>
public sealed class NoOpHotkeyService(string reason) : IHotkeyService
{
    public bool TryRegister(Hotkey hotkey, Action onPressed, out string? error)
    {
        error = reason;
        return false;
    }

    public void Unregister() { }
}

/// <inheritdoc cref="NoOpNotifier"/>
public sealed class NoOpStartupService(string reason) : IStartupService
{
    public bool IsEnabled => false;
    public void SetEnabled(bool enabled) => Log.Error($"open at login is unavailable: {reason}");
    public string? Problem => $"Open at login is unavailable: {reason}";
}
