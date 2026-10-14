using System.Runtime.InteropServices;
using Avalonia;
using GhNotify.Core;
using GhNotify.Platform.Windows;
using GhNotify.UI;
using Velopack;

namespace GhNotify.App;

public static class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);

    [STAThread]
    public static int Main(string[] args)
    {
        var hookPaths = new AppDataPaths(AppPaths.LegacyDataDir, AppPaths.DataDir, AppPaths.Logs);
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => InstallHooks.AfterInstall(hookPaths))
            .OnAfterUpdateFastCallback(_ => InstallHooks.AfterUpdate(hookPaths))
            .OnBeforeUninstallFastCallback(_ => InstallHooks.BeforeUninstall(hookPaths))
            .Run();
        if (args is ["--version"])
        {
            AttachConsole(-1); // WinExe: print to the console that launched us, if any
            Console.WriteLine(AppVersion.Current);
            return 0;
        }
        if (args is ["--write-icon", var iconPath])
        {
            IcoWriter.Write(iconPath, 16, 24, 32, 48, 256);
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\GhNotify.SingleInstance", out var first);
        if (!first) return 0;
        try
        {
            return Run(args);
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
    }

    static int Run(string[] args)
    {
        if (Environment.GetEnvironmentVariable("GHNOTIFY_DATA_DIR") is null or "") DataMigration.Run(AppPaths.LegacyDataDir, AppPaths.DataDir);
        Log.Configure(AppPaths.Logs);

        string? error = null;
        try
        {
            var options = Composition.Build();
            var app = AppBuilder.Configure(() => new GhNotify.UI.App(options))
                .UsePlatformDetect()
                .LogToTrace();
            var exit = app.StartWithClassicDesktopLifetime(args);
            error = (Application.Current as GhNotify.UI.App)?.StartupError;
            return error is null ? exit : 1;
        }
        catch (Exception ex)
        {
            Log.Error("gh_notify failed to start", ex);
            error = ex.Message;
            return 1;
        }
        finally
        {
            if (error is not null)
                MessageBoxW(IntPtr.Zero, $"gh_notify failed to start: {error}. See logs at {AppPaths.Logs}", "gh_notify", 0x10);
        }
    }
}
