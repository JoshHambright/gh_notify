using GhNotify.Core;

namespace GhNotify.Platform.Windows;

/// <summary>Velopack install/update/uninstall hooks. Each runs in its own short-lived process and must never throw.</summary>
public static class InstallHooks
{
    public static void AfterInstall(AppDataPaths paths) => Run("after-install", paths, () => new RunKeyStartupService().SetEnabled(true));

    public static void AfterUpdate(AppDataPaths paths) => Run("after-update", paths, () =>
    {
        var startup = new RunKeyStartupService();
        if (startup.IsEnabled) startup.SetEnabled(true); // rewrites the path
    });

    public static void BeforeUninstall(AppDataPaths paths) => Run("before-uninstall", paths, () =>
    {
        new RunKeyStartupService().SetEnabled(false);
        try { Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.Uninstall(); } catch { }
    });

    static void Run(string name, AppDataPaths paths, Action action)
    {
        try
        {
            // Same rule as app startup: move legacy data before logging creates the new data dir (the hooks can run before the first start).
            if (Environment.GetEnvironmentVariable("GHNOTIFY_DATA_DIR") is null or "") DataMigration.Run(paths.LegacyDataDir, paths.DataDir);
            Log.Configure(paths.Logs);
            action();
        }
        catch (Exception ex)
        {
            try { Log.Error($"install hook {name} failed", ex); } catch { }
        }
    }
}

/// <summary>The folders the install hooks need (the host owns the real ones, see AppPaths).</summary>
public sealed record AppDataPaths(string LegacyDataDir, string DataDir, string Logs);
