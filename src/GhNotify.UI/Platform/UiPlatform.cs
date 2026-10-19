namespace GhNotify.UI;

/// <summary>The few places the shared UI differs on macOS: popup placement, the template tray icon and macOS-only settings.
/// Tests set it to exercise the macOS paths on any OS.</summary>
public static class UiPlatform
{
    public static bool IsMac { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>macOS: the menu-bar icon's frame in screen points (Avalonia doesn't expose it), so the popup opens under it.
    /// Set by the macOS host; null, or returning null, falls back to the top-right corner.</summary>
    public static Func<Avalonia.Rect?>? TrayIconFrame { get; set; }

    /// <summary>The start-at-login toggle in Settings and the popup menu.</summary>
    public static string StartupLabel => IsMac ? "Open at login" : "Start with Windows";
}
