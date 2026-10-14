using Avalonia;
using Avalonia.Controls;

namespace GhNotify.UI;

/// <summary>
/// The tray popup: borderless, topmost, hides when it loses focus. This is the foundation shell (placement, toggle, deactivate
/// bookkeeping); the popup task fills in the body and the keyboard/search/batch behaviour on top of <see cref="IPopupHost"/>.
/// </summary>
public partial class PopupWindow : Window
{
    readonly IPopupHost _host;

    public PopupWindow() : this(new SampleHost()) { }

    public PopupWindow(IPopupHost host)
    {
        _host = host;
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (!IsVisible) return;
            LastHiddenByDeactivateUtc = DateTime.UtcNow;
            Hide();
        };
    }

    public IPopupHost Host => _host;

    /// <summary>When the popup last hid itself because it lost focus; a tray click right after that must not re-open it.</summary>
    public DateTime LastHiddenByDeactivateUtc { get; private set; }

    /// <summary>Shows the popup at the bottom-right of the primary screen's work area and re-arms any open confirm panel.</summary>
    public void ShowNearTray()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var scale = screen.Scaling;
            var area = screen.WorkingArea;
            const int margin = 12;
            Position = new PixelPoint(
                area.Right - (int)(Width * scale) - margin,
                area.Bottom - (int)(Height * scale) - margin);
        }
        _host.OnPopupShown();
        Show();
        Activate();
    }
}
