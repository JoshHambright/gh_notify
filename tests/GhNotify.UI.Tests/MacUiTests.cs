using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>The macOS paths of the shared UI, exercised on any OS by flipping <see cref="UiPlatform.IsMac"/>.</summary>
public class MacUiTests
{
    static T AsMac<T>(bool mac, Func<T> body)
    {
        var before = UiPlatform.IsMac;
        UiPlatform.IsMac = mac;
        try { return body(); }
        finally { UiPlatform.IsMac = before; }
    }

    [Fact]
    public void Popup_sits_bottom_right_on_Windows_and_top_right_under_the_menu_bar_on_macOS()
    {
        var area = new PixelRect(0, 25, 1440, 875); // macOS: the work area starts below the 25 px menu bar
        Assert.Equal(new PixelPoint(1440 - 376, 900 - 600), PopupWindow.TrayPlacement(area, 376, 600, mac: false));
        Assert.Equal(new PixelPoint(1440 - 376, 25), PopupWindow.TrayPlacement(area, 376, 600, mac: true));
    }

    [Theory]
    [InlineData(1000, 1000 + 15 - 188)] // centred under the icon
    [InlineData(1400, 1440 - 376)]      // near the right edge: kept on screen
    [InlineData(40, 0)]                 // near the left edge
    public void macOS_popup_opens_centred_under_the_menu_bar_icon(int iconX, int expectedX)
    {
        var area = new PixelRect(0, 25, 1440, 875);
        var icon = new PixelRect(iconX, 0, 30, 24);
        Assert.Equal(new PixelPoint(expectedX, 25), PopupWindow.TrayPlacement(area, 376, 600, mac: true, icon));
    }

    [Fact]
    public void Template_icon_is_black_glyph_on_transparent()
    {
        using var b = TrayIconRenderer.RenderTemplate(36);
        Assert.Equal(36, b.Width);
        var corner = b.GetPixel(1, 1);
        Assert.Equal(0, corner.Alpha); // no tile
        var trunk = b.GetPixel(9, 17); // the trunk (x 11 in the artwork) after the glyph is scaled to fill the icon
        Assert.True(trunk.Alpha > 200 && trunk.Red < 40 && trunk.Green < 40 && trunk.Blue < 40, $"trunk pixel {trunk}");
    }

    [AvaloniaFact]
    public void macOS_settings_say_Open_at_login_and_show_the_terminal_setting_and_login_item_hint()
    {
        SettingsTokens.Ensure();
        var host = new SampleSettingsHost { StartupProblem = "Approve gh_notify in System Settings → General → Login Items" };
        var (general, advanced, w) = AsMac(true, () =>
        {
            var win = new SettingsWindow(host);
            return ((GeneralPage)win.Page(SettingsPage.General), (AdvancedPage)win.Page(SettingsPage.Advanced), win);
        });
        Assert.Contains(general.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>(), t => t.Text == "Open at login");
        Assert.Equal(host.StartupProblem, general.StartupHint?.Text);
        Assert.NotNull(advanced.TerminalBox);
        Assert.Equal(DisplayConfig.DefaultTerminal, advanced.TerminalBox!.Text);
        advanced.TerminalBox.Text = "iterm";
        Assert.Equal("iterm", w.Draft.Config.Display.Terminal);
        w.Discard();
    }

    [AvaloniaFact]
    public void Windows_settings_hide_the_terminal_setting_and_keep_Start_with_Windows()
    {
        SettingsTokens.Ensure();
        var (general, advanced, w) = AsMac(false, () =>
        {
            var win = new SettingsWindow(new SampleSettingsHost());
            return ((GeneralPage)win.Page(SettingsPage.General), (AdvancedPage)win.Page(SettingsPage.Advanced), win);
        });
        Assert.Contains(general.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>(), t => t.Text == "Start with Windows");
        Assert.Null(general.StartupHint);
        Assert.Null(advanced.TerminalBox);
        w.Discard();
    }
}
