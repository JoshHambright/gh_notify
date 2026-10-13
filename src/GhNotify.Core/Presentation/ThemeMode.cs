namespace GhNotify.Core;

public enum ThemeMode { System, Dark, Light }

public static class ThemeResolver
{
    public static bool IsDark(ThemeMode mode, int? appsUseLightTheme) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => appsUseLightTheme == 0,
    };

    public static ThemeMode Next(ThemeMode mode) => mode switch
    {
        ThemeMode.System => ThemeMode.Dark,
        ThemeMode.Dark => ThemeMode.Light,
        _ => ThemeMode.System,
    };
}
