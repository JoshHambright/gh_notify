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
    SettingsWindow? _settings;
    AppController? _controller;
    FileSystemWatcher? _watcher;
    DispatcherTimer? _debounce;
    ThemeService? _theme;
    UpdateService? _updates;
    GlobalHotkey? _hotkey;
    readonly CancellationTokenSource _cts = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--write-icon", var iconPath])
        {
            IcoWriter.Write(iconPath, 16, 24, 32, 48, 256);
            Shutdown();
            return;
        }
        if (e.Args is ["--render-preview", var previewDir])
        {
            Shutdown(PreviewRenderer.Run(previewDir));
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
        if (Environment.GetEnvironmentVariable("GHNOTIFY_DATA_DIR") is null or "") DataMigration.Run(AppPaths.LegacyDataDir, AppPaths.DataDir);
        Log.Configure(AppPaths.Logs);
        DispatcherUnhandledException += (_, a) => { Log.Error("unhandled UI exception", a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("unhandled exception", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("unobserved task exception", a.Exception); a.SetObserved(); };

        var config = new ConfigStore(AppPaths.ConfigFile);
        config.Load();
        var controller = _controller = new AppController(config, new StateStore(AppPaths.StateFile));

        _theme = new ThemeService(() => controller.ThemeMode); // persisted in state.json; the popup's theme button cycles it
        _theme.Apply();
        controller.ThemeModeChanged += _theme.Apply;
        ToastPresenter.PrepareLogo(AppPaths.ToastLogo);

        _updates = new UpdateService(new VelopackUpdateClient(), () => controller.Config.Updates);
        controller.AttachUpdates(_updates);
        _updates.Changed += () => Dispatcher.InvokeAsync(controller.RaiseChanged);

        _popup = new PopupWindow(_controller);
        controller.SettingsRequested += () => Dispatcher.InvokeAsync(OpenSettings);

        _tray = new TaskbarIcon { ToolTipText = "gh_notify" };
        _tray.TrayLeftMouseUp += (_, _) => TogglePopup();
        UpdateTray();
        _tray.ForceCreate(enablesEfficiencyMode: false);

        _controller.Changed += () => Dispatcher.InvokeAsync(UpdateTray); // the popup re-renders itself on Changed
        ToastNotificationManagerCompat.OnActivated += a => Dispatcher.InvokeAsync(() =>
        {
            var args = ToastArguments.Parse(a.Argument);
            if (args.TryGetValue("url", out var url)) _controller.OpenUrl(url);
            else _popup.ShowNearTray();
        });

        StartHotkey(controller);
        WatchConfig();
        _controller.Start();
        _ = _updates.RunAsync(_cts.Token);
        Log.Info("gh_notify started");
    }

    /// <summary>One settings window at a time: re-activates the open one, or opens a new one after the last closed.</summary>
    void OpenSettings()
    {
        if (_settings is null)
        {
            var window = _settings = new SettingsWindow(_controller!);
            window.Closed += (_, _) => { if (_settings == window) _settings = null; };
            window.Show();
        }
        else if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    /// <summary>A hotkey clash or any failure here only shows up in Settings → General; it never blocks startup.</summary>
    void StartHotkey(AppController controller)
    {
        try
        {
            _hotkey = new GlobalHotkey(new Win32HotkeyRegistrar(), () =>
            {
                if (_popup!.IsVisible) _popup.Hide();
                else _popup.ShowNearTray();
            });
            controller.AttachHotkey(_hotkey);
            _hotkey.Apply(controller.Config.Hotkey);
        }
        catch (Exception ex)
        {
            Log.Error("could not set up the global hotkey", ex);
        }
    }

    void TogglePopup()
    {
        if (_popup!.IsVisible) _popup.Hide();
        else if ((DateTime.UtcNow - _popup.LastHiddenByDeactivateUtc).TotalMilliseconds < 300) return; // the tray click itself deactivated the popup
        else _popup.ShowNearTray();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    void UpdateTray()
    {
        var (count, unhealthy) = _controller!.TrayState();
        // H.NotifyIcon can't convert a RenderTargetBitmap ImageSource, so hand it a System.Drawing.Icon built from the rendered frames.
        var badge = BadgeText.For(count);
        var ico = IcoWriter.Build(TrayIconRenderer.Render(16, badge, unhealthy), TrayIconRenderer.Render(20, badge, unhealthy),
            TrayIconRenderer.Render(24, badge, unhealthy), TrayIconRenderer.Render(32, badge, unhealthy));
        var old = _tray!.Icon;
        var px = GetSystemMetrics(49); // SM_CXSMICON: the tray icon size in pixels at the system DPI
        using (var ms = new MemoryStream(ico)) _tray.Icon = new System.Drawing.Icon(ms, px > 0 ? px : 16, px > 0 ? px : 16);
        old?.Dispose();
        var problem = _controller.TrayProblem();
        var tip = count == 0 ? "gh_notify — nothing waiting" : $"gh_notify — {count} waiting";
        if (problem is not null) tip += "\n" + (problem.Length > 80 ? problem[..79] + "…" : problem);
        tip += "\nv" + AppVersion.Current;
        _tray.ToolTipText = tip;
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
        _cts.Cancel();
        _theme?.Dispose();
        _watcher?.Dispose();
        _hotkey?.Apply("");
        _controller?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
