using System.Windows;

namespace GhNotify.App;

/// <summary>
/// Pointer half of the confirm guard. A mouse click on a confirm button only counts once the pointer has entered the button
/// while it was enabled and then actually moved (screen coordinates). This stops the click that opened the panel — or a
/// second click of a double-click — landing on a confirm button that appeared, or enabled, under a still cursor (WPF
/// re-syncs mouse-over after layout and raises MouseEnter without any movement). Keyboard activation is not affected.
/// </summary>
public sealed class ConfirmPointerGuard
{
    Point? _enteredAt;

    public bool IsArmed { get; private set; }

    /// <summary>MouseEnter. Only an entry while the button is enabled can lead to arming.</summary>
    public void Entered(Point screen, bool enabled)
    {
        IsArmed = false;
        _enteredAt = enabled ? screen : null;
    }

    /// <summary>MouseMove over the button. Arms on real movement after an enabled entry; disarms if the button is disabled.</summary>
    public void Moved(Point screen, bool enabled)
    {
        if (!enabled)
        {
            Reset();
            return;
        }
        if (_enteredAt is { } p && Math.Max(Math.Abs(screen.X - p.X), Math.Abs(screen.Y - p.Y)) >= MinMovePx) IsArmed = true;
    }

    /// <summary>Movement smaller than this (hand jitter during a double-click) doesn't count.</summary>
    public const double MinMovePx = 2;

    /// <summary>MouseLeave.</summary>
    public void Left() => Reset();

    /// <summary>Re-render (the button is rebuilt) or the button became disabled.</summary>
    public void Reset()
    {
        IsArmed = false;
        _enteredAt = null;
    }
}
