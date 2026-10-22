using Avalonia;
using GhNotify.App;
using GhNotify.Core;
using GhNotify.Platform.Mac;
using GhNotify.UI;
using SkiaSharp;
using Velopack;

namespace GhNotify.App.Mac;

public static class Program
{
    /// <summary>Iconset sizes iconutil expects: (file name, pixels).</summary>
    internal static readonly (string Name, int Px)[] IconsetSizes =
    [
        ("icon_16x16.png", 16), ("icon_16x16@2x.png", 32), ("icon_32x32.png", 32), ("icon_32x32@2x.png", 64),
        ("icon_128x128.png", 128), ("icon_128x128@2x.png", 256), ("icon_256x256.png", 256), ("icon_256x256@2x.png", 512),
        ("icon_512x512.png", 512), ("icon_512x512@2x.png", 1024),
    ];

    public static int Main(string[] args)
    {
        VelopackApp.Build().Run();
        if (args is ["--version"])
        {
            Console.WriteLine(AppVersion.Current);
            return 0;
        }
        if (args is ["--commit"])
        {
            Console.WriteLine(AppVersion.Commit);
            return 0;
        }
        if (args is ["--write-iconset", var dir])
        {
            WriteIconset(dir);
            return 0;
        }

        var paths = MacPaths.Current;
        using var instance = InstanceLock.TryAcquire(paths.InstanceLock);
        if (instance is null) return 0;
        return Run(args, paths, testNotification: args.Contains("--test-notification"), testTerminal: args.Contains("--test-terminal"));
    }

    static int Run(string[] args, MacPaths paths, bool testNotification, bool testTerminal)
    {
        Log.Configure(paths.Logs);
        Log.Info($"gh_notify {AppVersion.Current} (commit {AppVersion.Commit}) from {AppContext.BaseDirectory}");
        string? error = null;
        try
        {
            LoginShellPath.Merge(new ProcessRunner());
            var options = Composition.Build(paths);
            if (testNotification) ScheduleTestNotification(options.Platform.Notifier);
            if (testTerminal) ScheduleTestTerminal(options.Platform.Shell);
            var app = AppBuilder.Configure(() => new GhNotify.UI.App(options))
                .UsePlatformDetect()
                .With(new MacOSPlatformOptions { ShowInDock = false }) // a menu-bar app: no Dock icon, no app menu
                .LogToTrace();
            var exit = app.StartWithClassicDesktopLifetime(args.Where(a => a is not ("--test-notification" or "--test-terminal")).ToArray());
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
            if (error is not null) ShowStartupError(error, paths.Logs);
        }
    }

    /// <summary>Tester round 2 (build/mac-test.sh): one sample notification a few seconds after startup, to check the permission
    /// prompt and that a click opens the popup. A new id each run: macOS may replace one with the same id without a banner.</summary>
    static void ScheduleTestNotification(INotifier notifier) =>
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
            notifier.Apply(new ToastRequest(ToastAction.Show, $"ghn-test-{DateTime.UtcNow:HHmmss}",
                "gh_notify test notification", "Click to open the popup")));

    /// <summary>Tester round 2: a terminal action (display.terminal) in the home folder, to check the Automation permission prompt.</summary>
    static void ScheduleTestTerminal(IShell shell) =>
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ =>
            shell.OpenTerminal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "echo \"gh_notify terminal test\""));

    /// <summary>The app is already gone, so use a plain system alert (osascript) instead of an Avalonia window.</summary>
    static void ShowStartupError(string error, string logs)
    {
        try
        {
            var message = TerminalCommand.AppleScriptEscape($"{error}\n\nSee the logs in {logs}");
            new ProcessRunner().StartDetached(new ProcessSpec("osascript",
                ["-e", $"display alert \"gh_notify failed to start\" message \"{message}\" as critical"]));
        }
        catch (Exception ex)
        {
            Log.Error("could not show the startup error", ex);
        }
    }

    /// <summary>The app icon (the accent mark) at every iconset size; build/pack-mac.sh turns the folder into GhNotify.icns.</summary>
    internal static void WriteIconset(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var (name, px) in IconsetSizes)
        {
            using SKBitmap bmp = TrayIconRenderer.RenderAccent(px);
            File.WriteAllBytes(Path.Combine(dir, name), TrayIconRenderer.ToPng(bmp));
        }
    }
}
