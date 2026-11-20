using System.Runtime.InteropServices;
using GhNotify.UI;

namespace GhNotify.Platform.Windows;

/// <summary>
/// SHQueryUserNotificationState: true while Windows says a full-screen app, a D3D game or presentation mode has the screen.
/// A Teams window share does not always set it, so the tray's Pause toasts item is the dependable fallback.
/// </summary>
public sealed class WinPresenceService : IPresenceService
{
    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);

    public bool IsPresenting
    {
        get
        {
            try { return SHQueryUserNotificationState(out var state) == 0 && IsPresentingState(state); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException) { return false; }
        }
    }

    /// <summary>QUNS_BUSY (2), QUNS_RUNNING_D3D_FULL_SCREEN (3) and QUNS_PRESENTATION_MODE (4). Quiet time (6) is Windows' own hold, so not ours.</summary>
    public static bool IsPresentingState(int state) => state is 2 or 3 or 4;
}
