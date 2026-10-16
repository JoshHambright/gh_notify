using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Mac;

/// <summary>Open at login: SMAppService.mainApp, the System Settings → General → Login Items entry (macOS 13+).</summary>
public sealed class MacStartupService(IMacShim shim) : IStartupService
{
    internal const string ApprovalHint = "macOS needs your approval: System Settings → General → Login Items → allow gh_notify.";

    public bool IsEnabled => shim.LoginItemStatus() == 1;

    public void SetEnabled(bool enabled)
    {
        var status = shim.LoginItemSet(enabled);
        if (status != 0) Log.Error($"could not {(enabled ? "register" : "unregister")} the login item (status {status})");
    }

    public string? Problem => shim.LoginItemStatus() switch
    {
        2 => ApprovalHint,
        < 0 and var s => $"The login item status could not be read (status {s}).",
        _ => null,
    };
}
