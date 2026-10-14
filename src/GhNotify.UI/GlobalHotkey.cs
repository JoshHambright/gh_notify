using System.Runtime.InteropServices;
using System.Windows.Interop;
using GhNotify.Core;

namespace GhNotify.App;

/// <summary>The OS side of a global hotkey, behind a seam so the failure path can be tested without a window.</summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>Registers <paramref name="hotkey"/> (replacing any earlier registration); false when another app already owns it.</summary>
    bool Register(Hotkey hotkey, Action pressed);

    void Unregister();
}

/// <summary>
/// The global hotkey that toggles the popup (v1.3 spec §6). Registration never throws: a clash or any other failure is logged once
/// per combination and exposed as <see cref="Error"/> for Settings → General. There is no toast and startup is never blocked.
/// </summary>
public sealed class GlobalHotkey(IHotkeyRegistrar registrar, Action toggle, Action<string>? logError = null)
{
    readonly Action<string> _log = logError ?? (m => Log.Error(m));
    readonly HashSet<string> _logged = [];

    /// <summary>"Win+Shift+G is in use by another app — pick another", or null when registered / off.</summary>
    public string? Error { get; private set; }

    /// <summary>The combination that is currently registered, or null.</summary>
    public Hotkey? Registered { get; private set; }

    /// <summary>Registers the configured hotkey (re-registers after a config change). "" turns it off.</summary>
    public void Apply(string? text)
    {
        try { registrar.Unregister(); }
        catch (Exception ex) { LogOnce("unregister", $"could not release the global hotkey: {ex.Message}"); }
        Error = null;
        Registered = null;
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!HotkeyParser.TryParse(text, out var hotkey, out var problem))
        {
            Error = $"Hotkey {problem}";
            LogOnce(text, $"invalid hotkey '{text}' in config: {problem}");
            return;
        }
        var shown = text.Trim(); // as the user wrote it (e.g. "Win+Shift+G"), not the canonical modifier order
        bool ok;
        try { ok = registrar.Register(hotkey, OnPressed); }
        catch (Exception ex)
        {
            ok = false;
            LogOnce(hotkey.ToString(), $"global hotkey {hotkey} failed: {ex.Message}");
        }
        if (ok)
        {
            Registered = hotkey;
            return;
        }
        Error = $"{shown} is in use by another app — pick another";
        LogOnce(hotkey.ToString(), $"global hotkey {shown} is in use by another app");
    }

    void OnPressed()
    {
        try { toggle(); }
        catch (Exception ex) { _log($"global hotkey handler failed: {ex}"); }
    }

    void LogOnce(string key, string message)
    {
        if (_logged.Add(key)) _log(message);
    }
}

/// <summary>RegisterHotKey on a message-only window (MOD_NOREPEAT, so holding the keys doesn't flicker the popup).</summary>
public sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    const int WmHotkey = 0x0312;
    const int HotkeyId = 0x6768; // "gh"
    const uint ModNoRepeat = 0x4000;
    static readonly IntPtr MessageOnlyParent = new(-3); // HWND_MESSAGE

    HwndSource? _source;
    Action? _pressed;
    bool _registered;

    /// <summary>RegisterHotKey modifier flags (without MOD_NOREPEAT) and the virtual-key code for a parsed hotkey.</summary>
    public static (uint Modifiers, uint VirtualKey) Translate(Hotkey hotkey)
    {
        uint mods = 0;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= 0x1;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) mods |= 0x2;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= 0x4;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win)) mods |= 0x8;
        var key = hotkey.Key;
        var vk = key.Length == 1 ? key[0] : 0x70u + (uint)int.Parse(key.AsSpan(1)) - 1; // A-Z, 0-9 are their char codes; F1 = 0x70
        return (mods, vk);
    }

    public bool Register(Hotkey hotkey, Action pressed)
    {
        Unregister();
        _source ??= CreateSource();
        _pressed = pressed;
        var (mods, vk) = Translate(hotkey);
        _registered = RegisterHotKey(_source.Handle, HotkeyId, mods | ModNoRepeat, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source is not null) UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    HwndSource CreateSource()
    {
        var source = new HwndSource(new HwndSourceParameters("GhNotifyHotkey") { ParentWindow = MessageOnlyParent, Width = 0, Height = 0, WindowStyle = 0 });
        source.AddHook(Hook);
        return source;
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.Dispose();
        _source = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
