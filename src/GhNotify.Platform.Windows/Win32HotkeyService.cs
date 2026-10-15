using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using GhNotify.Core;
using GhNotify.UI;

namespace GhNotify.Platform.Windows;

/// <summary>
/// RegisterHotKey on a message-only window (MOD_NOREPEAT, so holding the keys doesn't flicker the popup). The window is created on
/// the calling thread, which must pump messages (the UI thread does); WM_HOTKEY then arrives on that thread. No WPF or Avalonia types.
/// </summary>
public sealed class Win32HotkeyService : IHotkeyService, IDisposable
{
    const uint WmHotkey = 0x0312;
    const int HotkeyId = 0x6768; // "gh"
    const uint ModNoRepeat = 0x4000;
    const string ClassName = "GhNotifyHotkeyWindow";
    static readonly IntPtr MessageOnlyParent = new(-3); // HWND_MESSAGE

    // The delegate must outlive the window class, so it is held in a static.
    static readonly WndProc WndProcDelegate = StaticWndProc;
    static readonly ConcurrentDictionary<IntPtr, Win32HotkeyService> Windows = new();
    static bool _classRegistered;

    IntPtr _hwnd;
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

    public bool TryRegister(Hotkey hotkey, Action onPressed, out string? error)
    {
        Unregister();
        EnsureWindow();
        _pressed = onPressed;
        var (mods, vk) = Translate(hotkey);
        _registered = RegisterHotKey(_hwnd, HotkeyId, mods | ModNoRepeat, vk);
        error = _registered ? null : $"RegisterHotKey failed (Win32 error {Marshal.GetLastWin32Error()})";
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }

    void EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero) return;
        var hInstance = GetModuleHandle(null);
        if (!_classRegistered)
        {
            var cls = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
                hInstance = hInstance,
                lpszClassName = ClassName,
            };
            if (RegisterClassEx(ref cls) == 0 && Marshal.GetLastWin32Error() != 1410) // 1410 = already registered
                throw new InvalidOperationException($"could not register the hotkey window class (Win32 error {Marshal.GetLastWin32Error()})");
            _classRegistered = true;
        }
        _hwnd = CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, MessageOnlyParent, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"could not create the hotkey window (Win32 error {Marshal.GetLastWin32Error()})");
        Windows[_hwnd] = this;
    }

    static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId && Windows.TryGetValue(hwnd, out var self))
        {
            self._pressed?.Invoke();
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Unregister();
        if (_hwnd != IntPtr.Zero)
        {
            Windows.TryRemove(_hwnd, out _);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassEx(ref WndClassEx cls);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
