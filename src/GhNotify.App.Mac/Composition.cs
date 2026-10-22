using GhNotify.App;
using GhNotify.Core;
using GhNotify.Platform.Mac;
using GhNotify.UI;

namespace GhNotify.App.Mac;

/// <summary>Builds the macOS platform services and the options GhNotify.UI starts with.</summary>
static class Composition
{
    public static AppOptions Build(MacPaths paths)
    {
        var runner = new ProcessRunner();
        var (notifier, hotkey, startup) = NativeServices();
        var platform = new PlatformServices(
            notifier,
            hotkey,
            startup,
            new MacSoundService(runner, paths.Sounds),
            new AvaloniaSystemTheme(),
            new MacShell(runner, () => ReadTerminal(paths.ConfigFile)));
        var dirs = new AppDirs(paths.DataDir, paths.Logs, paths.Worktrees, paths.Scratch);
        return new AppOptions(platform, dirs, paths.ConfigFile, paths.StateFile, AppVersion.Current, new VelopackUpdateClient());
    }

    /// <summary>The shim-backed services, or no-ops that say why when libGhNotifyMac.dylib is missing or the wrong version.</summary>
    static (INotifier, IHotkeyService, IStartupService) NativeServices()
    {
        try
        {
            var shim = NativeShim.Load();
            UiPlatform.TrayIconFrame = () => shim.StatusItemFrame() is var (x, y, w, h) ? new Avalonia.Rect(x, y, w, h) : null;
            return (new MacNotifier(shim), new MacHotkeyService(shim), new MacStartupService(shim));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or BadImageFormatException)
        {
            var reason = $"libGhNotifyMac could not be loaded ({ex.Message})";
            Log.Error(reason + "; running without notifications, hotkey and open at login");
            return (new NoOpNotifier(), new NoOpHotkeyService(reason), new NoOpStartupService(reason));
        }
    }

    /// <summary>display.terminal, read when a terminal is opened (rare), so a Settings change applies without a restart.</summary>
    static string ReadTerminal(string configFile)
    {
        var store = new ConfigStore(configFile);
        store.Load();
        return store.Current.Display?.Terminal ?? DisplayConfig.DefaultTerminal;
    }
}
