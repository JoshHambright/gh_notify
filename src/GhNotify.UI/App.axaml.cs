using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>Everything the host (GhNotify.App) hands to the UI: platform services, folders and the update client.</summary>
public sealed record AppOptions(
    PlatformServices Platform,
    AppDirs Dirs,
    string ConfigFile,
    string StateFile,
    string Version,
    IUpdateClient UpdateClient);

/// <summary>The Avalonia application: composes the controller, the tray icon, the popup and settings windows, and the config watcher.</summary>
public partial class App : Application
{
    static readonly int[] TraySizes = [16, 20, 24, 32];
    internal const int MacTrayPx = 44;

    readonly AppOptions? _options;
    readonly CancellationTokenSource _cts = new();
    AppController? _controller;
    PopupWindow? _popup;
    SettingsWindow? _settings;
    TrayIcon? _tray;
    ThemeController? _theme;
    UpdateService? _updates;
    GlobalHotkey? _hotkey;
    FileSystemWatcher? _watcher;
    DispatcherTimer? _debounce;
    Action<string> _copy = _ => { };

    public App() { } // the XAML previewer / designer

    public App(AppOptions options) => _options = options;

    /// <summary>Set when startup failed; the host reports it after the lifetime ends.</summary>
    public string? StartupError { get; private set; }

    public AppController? Controller => _controller;
    public PopupWindow? Popup => _popup;
    public TrayIcon? Tray => _tray;
    public IReadOnlyList<Window> OpenWindows => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? (_settings is null ? [] : [_settings]);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (_options is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Cleanup();
            try
            {
                Start(() => desktop.Shutdown());
            }
            catch (Exception ex)
            {
                // Never leave a zombie process holding the single-instance mutex with no tray icon.
                Log.Error("gh_notify failed to start", ex);
                StartupError = ex.Message;
                desktop.Shutdown(1);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Composes and starts everything. The classic-desktop lifetime calls this; headless tests call it directly.</summary>
    public void Start(Action shutdown)
    {
        var o = _options ?? throw new InvalidOperationException("no AppOptions");
        var dispatcher = new AvaloniaUiDispatcher();
        Dispatcher.UIThread.UnhandledException += (_, a) => { Log.Error("unhandled UI exception", a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("unhandled exception", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("unobserved task exception", a.Exception); a.SetObserved(); };

        var config = new ConfigStore(o.ConfigFile);
        config.Load();
        var runtime = new AppRuntime(dispatcher, o.Dirs, () => dispatcher.Post(shutdown), t => _copy(t));
        var controller = _controller = new AppController(config, new StateStore(o.StateFile), o.Platform, runtime) { Version = o.Version };

        _theme = new ThemeController(() => controller.ThemeMode, o.Platform.Theme, dispatcher); // persisted in state.json; the popup's theme button cycles it
        _theme.Apply();
        controller.ThemeModeChanged += _theme.Apply;

        _updates = new UpdateService(o.UpdateClient, () => controller.Config.Updates);
        controller.AttachUpdates(_updates);
        _updates.Changed += () => dispatcher.Post(controller.RaiseChanged);

        _popup = new PopupWindow(controller);
        _copy = text => _ = CopyAsync(text);
        controller.SettingsRequested += () => dispatcher.Post(OpenSettings);

        _tray = new TrayIcon { ToolTipText = "gh_notify" };
        _tray.Clicked += (_, _) => TogglePopup();
        UpdateTray();
        TrayIcon.SetIcons(this, [_tray]);

        controller.Changed += () => dispatcher.Post(UpdateTray);
        o.Platform.Notifier.Activated += (tag, action) => dispatcher.Post(() =>
        {
            if (!controller.OnToastActivated(tag, action)) _popup.ShowNearTray();
        });

        StartHotkey(controller, o.Platform.Hotkey);
        WatchConfig(o.ConfigFile);
        controller.Start();
        _ = _updates.RunAsync(_cts.Token);
        Log.Info("gh_notify started");
    }

    async Task CopyAsync(string text)
    {
        try
        {
            if (_popup?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            Log.Error("could not copy to clipboard", ex);
        }
    }

    /// <summary>One settings window at a time: re-activates the open one, or opens a new one after the last closed.</summary>
    public void OpenSettings()
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
    void StartHotkey(AppController controller, IHotkeyService service)
    {
        try
        {
            _hotkey = new GlobalHotkey(service, () =>
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

    /// <summary>Left click on the tray icon.</summary>
    public void TogglePopup()
    {
        if (UiPlatform.IsMac) Log.Info($"toggle popup (visible {_popup!.IsVisible})");
        if (_popup!.IsVisible) _popup.Hide();
        else if ((DateTime.UtcNow - _popup.LastHiddenByDeactivateUtc).TotalMilliseconds < 300) return; // the tray click itself deactivated the popup
        else _popup.ShowNearTray();
    }

    void UpdateTray()
    {
        if (_tray is null || _controller is null) return;
        var (count, unhealthy) = _controller.TrayState();
        var scale = _popup?.Screens.Primary?.Scaling ?? 1.0;
        // macOS draws the status item at the menu-bar height (22 pt). The popup's screen scale is unknown until it is first shown,
        // so a scale-derived size came out 16 px and was blown up on Retina: always render 44 px (22 pt at 2x); macOS scales down.
        var size = UiPlatform.IsMac ? MacTrayPx : TraySizes.FirstOrDefault(s => s >= 16 * scale, TraySizes[^1]);
        var iconKey = (size, BadgeText.For(count), unhealthy);
        if (iconKey != _trayIconKey)
        {
            _trayIconKey = iconKey;
            // macOS: the calm icon is a template image, tinted by the menu bar; waiting and unhealthy keep their colours.
            var template = UiPlatform.IsMac && iconKey.Item2 is null && !unhealthy;
            using var bmp = template ? TrayIconRenderer.RenderTemplate(size) : TrayIconRenderer.Render(size, iconKey.Item2, unhealthy);
            if (UiPlatform.IsMac) MacOSProperties.SetIsTemplateIcon(_tray, template);
            _tray.Icon = new WindowIcon(new MemoryStream(TrayIconRenderer.ToPng(bmp)));
        }
        var problem = _controller.TrayProblem();
        var tip = count == 0 ? "gh_notify — nothing waiting" : $"gh_notify — {count} waiting";
        if (problem is not null) tip += "\n" + (problem.Length > 80 ? problem[..79] + "…" : problem);
        tip += "\nv" + (_options?.Version ?? "dev");
        if (_tray.ToolTipText != tip) _tray.ToolTipText = tip;
        _trayMenu ??= new TrayMenu(this, _controller);
        _trayMenu.Update(_controller.Update);
        if (_tray.Menu != _trayMenu.Menu) _tray.Menu = _trayMenu.Menu;
    }

    TrayMenu? _trayMenu;
    (int, string?, bool)? _trayIconKey;

    /// <summary>
    /// The right-click menu: Open, Settings, What's new, Check for updates, Restart to update (only when one is ready), Quit.
    /// Built once and updated in place, so a refresh never replaces (and closes) a menu the user has open.
    /// </summary>
    internal sealed class TrayMenu
    {
        readonly NativeMenuItem _check;
        readonly NativeMenuItem _restart;

        public NativeMenu Menu { get; } = new();

        public TrayMenu(App app, AppController controller)
        {
            var open = new NativeMenuItem("Open");
            open.Click += (_, _) => { if (!app._popup!.IsVisible) app._popup.ShowNearTray(); };
            Menu.Add(open);

            var settings = new NativeMenuItem("Settings…");
            settings.Click += (_, _) => app.OpenSettings();
            Menu.Add(settings);

            var whatsNew = new NativeMenuItem(UpdateUi.WhatsNewItem);
            whatsNew.Click += (_, _) => controller.OpenWhatsNew();
            Menu.Add(whatsNew);

            _check = new NativeMenuItem();
            _check.Click += (_, _) => _ = controller.CheckForUpdatesAsync();
            Menu.Add(_check);

            _restart = new NativeMenuItem();
            // The tray has no inline confirm: a restart that would interrupt running agents is refused here and offered in the popup.
            _restart.Click += (_, _) =>
            {
                if (controller.RestartToUpdate(confirmed: false) == RestartDecision.NeedsConfirm) app._popup!.ShowNearTray();
            };

            Menu.Add(new NativeMenuItemSeparator());
            var quit = new NativeMenuItem("Quit");
            quit.Click += (_, _) => controller.Quit();
            Menu.Add(quit);
        }

        public void Update(UpdateStatus update)
        {
            var (header, enabled, _) = UpdateUi.CheckItem(update);
            if (_check.Header != header) _check.Header = header;
            if (_check.IsEnabled != enabled) _check.IsEnabled = enabled;

            var restartText = UpdateUi.RestartItem(update);
            var shown = Menu.Items.Contains(_restart);
            if (restartText is null) { if (shown) Menu.Items.Remove(_restart); return; }
            if (_restart.Header != restartText) _restart.Header = restartText;
            if (!shown) Menu.Items.Insert(Menu.Items.IndexOf(_check) + 1, _restart);
        }
    }

    void WatchConfig(string configFile)
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _controller!.ReloadConfig();
        };
        var dir = Path.GetDirectoryName(configFile)!;
        Directory.CreateDirectory(dir);
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(configFile))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler restart = (_, _) => Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });
        _watcher.Changed += restart;
        _watcher.Created += restart;
        _watcher.Renamed += (s, a) => restart(s, a);
    }

    public void Cleanup()
    {
        _cts.Cancel();
        _theme?.Dispose();
        _watcher?.Dispose();
        _hotkey?.Apply("");
        _controller?.Dispose();
        _tray?.Dispose();
    }
}
