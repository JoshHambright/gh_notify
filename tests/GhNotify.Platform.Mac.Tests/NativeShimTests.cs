namespace GhNotify.Platform.Mac.Tests;

/// <summary>The real libGhNotifyMac.dylib, on the macOS CI job (native/mac/build.sh runs first). Notifications need a bundled
/// .app, so here they only check that the shim declines safely; the packaged app is covered by the install test and tester round 2.</summary>
public class NativeShimTests
{
    [MacFact]
    public void Loads_and_the_API_version_matches() => Assert.Equal(NativeShim.ApiVersion, NativeShim.Load().Version());

    [MacFact]
    public void Notifications_decline_without_an_app_bundle_instead_of_crashing()
    {
        var shim = NativeShim.Load();
        Assert.Equal(-1, shim.NotifyInit((_, _) => { }));
        Assert.Equal(-1, shim.NotifyShow("t", "title", "body", null, silent: true));
        shim.NotifyRemove("t");
        shim.NotifyClearAll();
    }

    [MacFact]
    public void Hotkey_registers_and_unregisters()
    {
        var shim = NativeShim.Load();
        var status = shim.HotkeyRegister(MacKeyCodes.Translate(new Core.Hotkey(Core.HotkeyModifiers.Ctrl | Core.HotkeyModifiers.Alt, "F12")).KeyCode,
            MacKeyCodes.ControlKey | MacKeyCodes.OptionKey, () => { });
        Assert.Equal(0, status);
        shim.HotkeyUnregister();
        shim.HotkeyUnregister(); // twice is harmless
    }

    [MacFact]
    public void Login_item_status_is_readable() =>
        Assert.InRange(NativeShim.Load().LoginItemStatus(), -1, 3);
}
