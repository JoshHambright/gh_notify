using Avalonia;
using Avalonia.Platform;

namespace GhNotify.UI;

/// <summary>The OS light/dark setting as Avalonia reports it (used on macOS). The host builds it before the Avalonia app exists,
/// so it attaches to <see cref="Application.PlatformSettings"/> on first use.</summary>
public sealed class AvaloniaSystemTheme : ISystemTheme
{
    IPlatformSettings? _settings;

    public bool IsDark => Settings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;

    public event Action? Changed;

    IPlatformSettings? Settings
    {
        get
        {
            if (_settings is null && Application.Current?.PlatformSettings is { } s)
            {
                _settings = s;
                s.ColorValuesChanged += (_, _) => Changed?.Invoke();
            }
            return _settings;
        }
    }
}
