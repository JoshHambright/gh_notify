using System.Windows;
using GhNotify.Core;
using ThemeMode = GhNotify.Core.ThemeMode;
using Microsoft.Win32;

namespace GhNotify.App;

public sealed class ThemeService : IDisposable
{
    const string DarkSource = "Themes/Dark.xaml";
    const string LightSource = "Themes/Light.xaml";
    readonly Func<ThemeMode> _mode;
    volatile bool _disposed;

    public ThemeService(Func<ThemeMode> mode)
    {
        _mode = mode;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsDark { get; private set; } = true;
    public event Action? Changed;

    public static int? ReadAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") as int?;
        }
        catch { return null; }
    }

    public void Apply()
    {
        IsDark = ThemeResolver.IsDark(_mode(), ReadAppsUseLightTheme());
        var app = Application.Current;
        if (app is null) return;
        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var s = merged[i].Source?.OriginalString;
            if (s is not null && (s.EndsWith(DarkSource, StringComparison.OrdinalIgnoreCase) || s.EndsWith(LightSource, StringComparison.OrdinalIgnoreCase)))
                merged.RemoveAt(i);
        }
        merged.Add(new ResourceDictionary { Source = new Uri(IsDark ? DarkSource : LightSource, UriKind.Relative) });
        Changed?.Invoke();
    }

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_disposed || _mode() != ThemeMode.System) return;
            Apply();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
