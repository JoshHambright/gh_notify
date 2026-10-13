using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GhNotify.Core;
using H.NotifyIcon;
using Microsoft.Toolkit.Uwp.Notifications;

namespace GhNotify.App;

public partial class App : Application
{

    Mutex? _mutex;
    TaskbarIcon? _tray;
    PopupWindow? _popup;
    AppController? _controller;
    FileSystemWatcher? _watcher;
    DispatcherTimer? _debounce;
    ThemeService? _theme;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--write-icon", var iconPath])
        {
            IcoWriter.Write(iconPath, 16, 24, 32, 48, 256);
            Shutdown();
            return;
        }
        _mutex = new Mutex(true, @"Local\GhNotify.SingleInstance", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        try
        {
            StartUp();
        }
        catch (Exception ex)
        {
            // Never leave a zombie process holding the single-instance mutex with no tray icon.
            Log.Error("gh_notify failed to start", ex);
            MessageBox.Show($"gh_notify failed to start: {ex.Message}. See logs at {AppPaths.Logs}", "gh_notify",
                MessageBoxButton.OK, MessageBoxImage.Error);
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
        }
    }

    void StartUp()
    {
        Log.Configure(AppPaths.Logs);
        DispatcherUnhandledException += (_, a) => { Log.Error("unhandled UI exception", a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("unhandled exception", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("unobserved task exception", a.Exception); a.SetObserved(); };

        _theme = new ThemeService(() => GhNotify.Core.ThemeMode.System); // Task 7 wires the persisted mode
        _theme.Apply();

        var config = new ConfigStore(AppPaths.ConfigFile);
        config.Load();
        _controller = new AppController(config, new StateStore(AppPaths.StateFile));
        _popup = new PopupWindow(_controller);

        _tray = new TaskbarIcon { ToolTipText = "gh_notify" };
        _tray.TrayLeftMouseUp += (_, _) => TogglePopup();
        UpdateTray();
        _tray.ForceCreate(enablesEfficiencyMode: false);

        _controller.Changed += () => Dispatcher.InvokeAsync(() =>
        {
            UpdateTray();
            if (_popup.IsVisible) _popup.Render();
        });
        ToastNotificationManagerCompat.OnActivated += a => Dispatcher.InvokeAsync(() =>
        {
            var args = ToastArguments.Parse(a.Argument);
            if (args.TryGetValue("url", out var url)) _controller.OpenUrl(url);
            else _popup.ShowNearTray();
        });

        WatchConfig();
        _controller.Start();
        Log.Info("gh_notify started");
    }

    void TogglePopup()
    {
        if (_popup!.IsVisible) _popup.Hide();
        else if ((DateTime.UtcNow - _popup.LastHiddenByDeactivateUtc).TotalMilliseconds < 300) return; // the tray click itself deactivated the popup
        else _popup.ShowNearTray();
    }

    void UpdateTray()
    {
        var (count, unhealthy) = _controller!.TrayState();
        _tray!.IconSource = TrayIconRenderer.Render(32, BadgeText.For(count), unhealthy);
        _tray.ToolTipText = unhealthy
            ? $"gh_notify — {count} waiting (a source needs attention)"
            : $"gh_notify — {count} waiting";
    }

    void WatchConfig()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _controller!.ReloadConfig();
        };
        Directory.CreateDirectory(AppPaths.ConfigDir);
        _watcher = new FileSystemWatcher(AppPaths.ConfigDir, "config.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler restart = (_, _) => Dispatcher.InvokeAsync(() => { _debounce.Stop(); _debounce.Start(); });
        _watcher.Changed += restart;
        _watcher.Created += restart;
        _watcher.Renamed += (s, a) => restart(s, a);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _theme?.Dispose();
        _watcher?.Dispose();
        _controller?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
