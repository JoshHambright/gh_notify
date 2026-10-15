using Avalonia;
using Avalonia.Styling;
using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>Resolves the effective light/dark theme from the configured mode and the OS theme, and applies it to the application.</summary>
public sealed class ThemeController : IDisposable
{
    readonly Func<ThemeMode> _mode;
    readonly ISystemTheme _system;
    readonly IUiDispatcher _dispatcher;
    volatile bool _disposed;

    public ThemeController(Func<ThemeMode> mode, ISystemTheme system, IUiDispatcher dispatcher)
    {
        _mode = mode;
        _system = system;
        _dispatcher = dispatcher;
        _system.Changed += OnSystemChanged;
    }

    public bool IsDark { get; private set; } = true;
    public event Action? Changed;

    public static bool Resolve(ThemeMode mode, bool systemIsDark) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => systemIsDark,
    };

    public void Apply()
    {
        IsDark = Resolve(_mode(), _system.IsDark);
        if (Application.Current is { } app) app.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        Changed?.Invoke();
    }

    void OnSystemChanged() => _dispatcher.Post(() =>
    {
        if (_disposed || _mode() != ThemeMode.System) return;
        Apply();
    });

    public void Dispose()
    {
        _disposed = true;
        _system.Changed -= OnSystemChanged;
    }
}
