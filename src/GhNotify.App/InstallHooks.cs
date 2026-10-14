using GhNotify.Core;

namespace GhNotify.App;

/// <summary>Velopack install/update/uninstall hooks. Each runs in its own short-lived process and must never throw.</summary>
public static class InstallHooks
{
    public static void AfterInstall() => Run("after-install", () => StartupRegistration.Set(true));

    public static void AfterUpdate() => Run("after-update", () =>
    {
        if (StartupRegistration.IsEnabled()) StartupRegistration.Set(true); // rewrites the path
    });

    public static void BeforeUninstall() => Run("before-uninstall", () =>
    {
        StartupRegistration.Set(false);
        try { Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.Uninstall(); } catch { }
    });

    static void Run(string name, Action action)
    {
        try
        {
            Log.Configure(AppPaths.Logs);
            action();
        }
        catch (Exception ex)
        {
            try { Log.Error($"install hook {name} failed", ex); } catch { }
        }
    }
}
