using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GhNotify.Core;
using H.NotifyIcon;
using Microsoft.Toolkit.Uwp.Notifications;

namespace GhNotify.App;

public partial class App : Application
{
    static readonly Brush Idle = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
    static readonly Brush Busy = new SolidColorBrush(Color.FromRgb(0x2F, 0x81, 0xF7));
    static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x40));

    Mutex? _mutex;
    TaskbarIcon? _tray;
    PopupWindow? _popup;
    AppController? _controller;
    FileSystemWatcher? _watcher;
    DispatcherTimer? _debounce;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Local\GhNotify.SingleInstance", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        Log.Configure(AppPaths.Logs);
        DispatcherUnhandledException += (_, a) => { Log.Error("unhandled UI exception", a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("unhandled exception", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("unobserved task exception", a.Exception); a.SetObserved(); };

        var config = new ConfigStore(AppPaths.ConfigFile);
        config.Load();
        _controller = new AppController(config, new StateStore(AppPaths.StateFile));
        _popup = new PopupWindow(_controller);

        _tray = new TaskbarIcon { ToolTipText = "gh_notify" };
        _tray.TrayLeftMouseUp += (_, _) => TogglePopup();
        UpdateTray();
        _tray.ForceCreate();

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
        else _popup.ShowNearTray();
    }

    void UpdateTray()
    {
        var (count, unhealthy) = _controller!.TrayState();
        _tray!.IconSource = new GeneratedIconSource
        {
            Text = count > 99 ? "99+" : count.ToString(),
            Foreground = Brushes.White,
            Background = unhealthy ? Warn : count == 0 ? Idle : Busy,
            FontSize = count > 9 ? 40 : 52,
        };
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
        _watcher?.Dispose();
        _controller?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
