using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GhNotify.Platform.Mac;

/// <summary>P/Invoke into libGhNotifyMac.dylib (next to the executable). Callbacks go through static [UnmanagedCallersOnly] trampolines
/// to the delegates held here, so the native side never holds a managed object.</summary>
public sealed unsafe partial class NativeShim : IMacShim
{
    /// <summary>Bumped together with GHN_API_VERSION in GhNotifyMac.swift whenever the C API changes.</summary>
    public const int ApiVersion = 1;
    const string Lib = "GhNotifyMac";

    static Action<string, string?>? _onClick;
    static Action? _onHotkey;

    /// <summary>Loads the shim and checks its version. Throws DllNotFoundException / EntryPointNotFoundException / InvalidOperationException.</summary>
    public static NativeShim Load()
    {
        var shim = new NativeShim();
        var version = shim.Version();
        if (version != ApiVersion) throw new InvalidOperationException($"libGhNotifyMac API version {version}, expected {ApiVersion}");
        return shim;
    }

    public int Version() => ghn_version();

    public int NotifyInit(Action<string, string?> onClick)
    {
        _onClick = onClick;
        return ghn_notify_init(&OnClick);
    }

    public int NotifyShow(string tag, string title, string body, string? url, bool silent) =>
        ghn_notify_show(tag, title, body, url, silent ? 1 : 0);

    public void NotifyRemove(string tag) => ghn_notify_remove(tag);
    public void NotifyClearAll() => ghn_notify_clear_all();

    public int HotkeyRegister(uint keyCode, uint carbonModifiers, Action onPressed)
    {
        _onHotkey = onPressed;
        return ghn_hotkey_register(keyCode, carbonModifiers, &OnHotkey);
    }

    public void HotkeyUnregister()
    {
        ghn_hotkey_unregister();
        _onHotkey = null;
    }

    public int LoginItemStatus() => ghn_login_item_status();
    public int LoginItemSet(bool enabled) => ghn_login_item_set(enabled ? 1 : 0);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnClick(byte* tag, byte* url)
    {
        try { _onClick?.Invoke(Marshal.PtrToStringUTF8((nint)tag) ?? "", url is null ? null : Marshal.PtrToStringUTF8((nint)url)); }
        catch (Exception ex) { Core.Log.Error("notification click handler failed", ex); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnHotkey()
    {
        try { _onHotkey?.Invoke(); }
        catch (Exception ex) { Core.Log.Error("hotkey handler failed", ex); }
    }

    [LibraryImport(Lib)] private static partial int ghn_version();
    [LibraryImport(Lib)] private static partial int ghn_notify_init(delegate* unmanaged[Cdecl]<byte*, byte*, void> onClick);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int ghn_notify_show(string tag, string title, string body, string? url, int silent);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] private static partial void ghn_notify_remove(string tag);
    [LibraryImport(Lib)] private static partial void ghn_notify_clear_all();
    [LibraryImport(Lib)] private static partial int ghn_hotkey_register(uint keyCode, uint modifiers, delegate* unmanaged[Cdecl]<void> onPressed);
    [LibraryImport(Lib)] private static partial void ghn_hotkey_unregister();
    [LibraryImport(Lib)] private static partial int ghn_login_item_status();
    [LibraryImport(Lib)] private static partial int ghn_login_item_set(int enabled);
}
