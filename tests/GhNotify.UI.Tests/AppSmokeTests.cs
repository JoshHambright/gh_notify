using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace GhNotify.UI.Tests;

public class AppSmokeTests
{
    static App StartedApp()
    {
        var app = (App)Application.Current!;
        if (app.Controller is null) app.Start(() => { });
        return app;
    }

    [AvaloniaFact]
    public void App_starts_with_fake_platform_services_and_sets_the_tray_icon()
    {
        var app = StartedApp();
        Assert.NotNull(app.Controller);
        Assert.NotNull(app.Tray);
        Assert.NotNull(app.Tray!.Icon);
        Assert.Equal("gh_notify — nothing waiting", app.Tray.ToolTipText!.Split('\n')[0]);
        var items = app.Tray.Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();
        Assert.Equal(["Open", "Settings…", "Check for updates", "-", "Quit"], items);
    }

    [AvaloniaFact]
    public void The_popup_toggles_from_the_tray_click()
    {
        var app = StartedApp();
        Assert.False(app.Popup!.IsVisible);
        app.TogglePopup();
        Assert.True(app.Popup.IsVisible);
        app.TogglePopup();
        Assert.False(app.Popup.IsVisible);
    }

    [AvaloniaFact]
    public void The_hotkey_is_registered_through_the_platform_service_and_toggles_the_popup()
    {
        var app = StartedApp();
        var hotkey = TestAppBuilder.Platform.Hotkey;
        Assert.Contains(hotkey.Calls, c => c.StartsWith("register "));
        hotkey.Pressed!();
        Assert.True(app.Popup!.IsVisible);
        hotkey.Pressed();
        Assert.False(app.Popup.IsVisible);
    }

    [AvaloniaFact]
    public void A_url_toast_activation_opens_the_url_through_the_shell()
    {
        StartedApp();
        TestAppBuilder.Platform.Notifier.Activate("gh:review", GhNotify.UI.ToastActions.ForUrl("https://github.com/acme/api/pull/5"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("https://github.com/acme/api/pull/5", TestAppBuilder.Platform.Shell.Urls);
    }

    [AvaloniaFact]
    public void Settings_opens_once()
    {
        var app = StartedApp();
        app.OpenSettings();
        app.OpenSettings();
        Assert.Single(app.OpenWindows.OfType<SettingsWindow>());
    }
}
