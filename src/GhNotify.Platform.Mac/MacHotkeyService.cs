using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Mac;

/// <summary>A global hotkey through Carbon RegisterEventHotKey (the shim); the press arrives on the main (UI) thread.</summary>
public sealed class MacHotkeyService(IMacShim shim) : IHotkeyService
{
    internal const int EventHotKeyExistsErr = -9878;
    bool _registered;

    public bool TryRegister(Hotkey hotkey, Action onPressed, out string? error)
    {
        Unregister();
        var (mods, key) = MacKeyCodes.Translate(hotkey);
        var status = shim.HotkeyRegister(key, mods, onPressed);
        _registered = status == 0;
        error = status switch
        {
            0 => null,
            EventHotKeyExistsErr => "the combination is already registered by another app",
            _ => $"RegisterEventHotKey failed (OSStatus {status})",
        };
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        shim.HotkeyUnregister();
        _registered = false;
    }
}
