namespace GhNotify.Platform.Mac;

/// <summary>The Swift shim (native/mac/GhNotifyMac.swift) as an interface, so the services can be tested with a fake.
/// Return codes: 0 is success; the meaning of anything else is per call.</summary>
public interface IMacShim
{
    /// <summary>The shim's API version; must equal <see cref="NativeShim.ApiVersion"/>.</summary>
    int Version();

    /// <summary>Installs the notification delegate and asks for permission (once). <paramref name="onClick"/> gets (tag, url or null),
    /// on any thread.</summary>
    int NotifyInit(Action<string, string?> onClick);
    int NotifyShow(string tag, string title, string body, string? url, bool silent);
    void NotifyRemove(string tag);
    void NotifyClearAll();

    /// <summary>Carbon RegisterEventHotKey; returns the OSStatus (-9878 = the combination is taken). <paramref name="onPressed"/> runs on
    /// the main thread.</summary>
    int HotkeyRegister(uint keyCode, uint carbonModifiers, Action onPressed);
    void HotkeyUnregister();

    /// <summary>SMAppService.mainApp: 0 off, 1 on, 2 requires approval, 3 not found, &lt;0 error.</summary>
    int LoginItemStatus();
    /// <summary>Registers or unregisters the login item; 0 on success, &lt;0 on error.</summary>
    int LoginItemSet(bool enabled);
}
