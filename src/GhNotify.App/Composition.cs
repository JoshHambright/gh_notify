using GhNotify.Core;
using GhNotify.Platform.Windows;
using GhNotify.UI;

namespace GhNotify.App;

/// <summary>Builds the Windows platform services and the options GhNotify.UI starts with.</summary>
static class Composition
{
    public static AppOptions Build()
    {
        var notifier = new ToastNotifier();
        notifier.PrepareLogo(AppPaths.ToastLogo);
        var platform = new PlatformServices(
            notifier,
            new Win32HotkeyService(),
            new RunKeyStartupService(),
            new WinSoundService(),
            new WinSystemTheme(),
            new WinShell(new ProcessRunner()));
        var dirs = new AppDirs(AppPaths.DataDir, AppPaths.Logs, AppPaths.Worktrees, AppPaths.Scratch);
        return new AppOptions(platform, dirs, AppPaths.ConfigFile, AppPaths.StateFile, AppVersion.Current, new VelopackUpdateClient());
    }
}
