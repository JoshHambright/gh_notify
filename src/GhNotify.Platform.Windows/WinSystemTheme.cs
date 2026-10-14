using GhNotify.UI;
using Microsoft.Win32;

namespace GhNotify.Platform.Windows;

/// <summary>The Windows apps light/dark setting (HKCU ...\Themes\Personalize\AppsUseLightTheme), with change notification.</summary>
public sealed class WinSystemTheme : ISystemTheme, IDisposable
{
    public WinSystemTheme() => SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

    public bool IsDark => ReadAppsUseLightTheme() == 0;

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

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color) Changed?.Invoke();
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
