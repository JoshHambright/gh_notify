using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Mac;

/// <summary>Open at login: SMAppService.mainApp, the System Settings → General → Login Items entry (macOS 13+).</summary>
public sealed class MacStartupService(IMacShim shim) : IStartupService
{
    internal const string ApprovalHint = "macOS needs your approval: System Settings → General → Login Items → allow gh_notify.";
    internal const string NotFoundHint = "macOS can't find gh_notify as a login item. Make sure it runs from Applications.";
    string? _lastError;

    public bool IsEnabled => shim.LoginItemStatus() == 1;

    public void SetEnabled(bool enabled)
    {
        var status = shim.LoginItemSet(enabled);
        _lastError = status == 0 ? null : $"Could not {(enabled ? "turn on" : "turn off")} open at login (status {status}); see the logs.";
        if (status != 0) Log.Error($"could not {(enabled ? "register" : "unregister")} the login item (status {status})");
    }

    /// <summary>A failed change wins (the checkbox flips back otherwise with no reason), then what the status says.</summary>
    public string? Problem => _lastError ?? shim.LoginItemStatus() switch
    {
        2 => ApprovalHint,
        3 => NotFoundHint,
        < 0 and var s => $"The login item status could not be read (status {s}).",
        _ => null,
    };
}
