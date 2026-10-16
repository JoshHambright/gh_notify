using GhNotify.Core;

namespace GhNotify.UI.Tests;

public class GlobalHotkeyTests
{
    [Fact]
    public void Registered_hotkey_toggles_the_popup()
    {
        var reg = new FakeHotkeyService();
        var toggles = 0;
        var hk = new GlobalHotkey(reg, () => toggles++, _ => { });
        hk.Apply("win+shift+g");
        Assert.Null(hk.Error);
        Assert.Equal(Os.Hotkey("Shift+Win+G"), hk.Registered?.ToString()); // canonical modifier order
        reg.Pressed!();
        Assert.Equal(1, toggles);
    }

    [Fact]
    public void Clash_is_exposed_and_logged_once_and_never_throws()
    {
        var reg = new FakeHotkeyService { Succeeds = false };
        var log = new List<string>();
        var hk = new GlobalHotkey(reg, () => { }, log.Add);
        hk.Apply("Win+Shift+G");
        hk.Apply("Win+Shift+G"); // e.g. a config reload that changed something else
        Assert.Equal("Win+Shift+G is in use by another app — pick another", hk.Error);
        Assert.Null(hk.Registered);
        Assert.Single(log);
        Assert.DoesNotContain("toast", log[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Re_registering_a_free_combination_clears_the_error()
    {
        var reg = new FakeHotkeyService { Succeeds = false };
        var hk = new GlobalHotkey(reg, () => { }, _ => { });
        hk.Apply("Win+Shift+G");
        Assert.NotNull(hk.Error);
        reg.Succeeds = true;
        hk.Apply("Ctrl+Alt+H");
        Assert.Null(hk.Error);
        Assert.Equal(Os.Hotkey("Ctrl+Alt+H"), hk.Registered?.ToString());
        Assert.Equal("unregister", reg.Calls[^2]); // released the old one first
    }

    [Fact]
    public void Empty_turns_it_off()
    {
        var reg = new FakeHotkeyService();
        var hk = new GlobalHotkey(reg, () => { }, _ => { });
        hk.Apply("Ctrl+Alt+H");
        hk.Apply("");
        Assert.Null(hk.Error);
        Assert.Null(hk.Registered);
        Assert.Equal("unregister", reg.Calls[^1]);
    }

    [Fact]
    public void A_registrar_that_throws_is_a_failure_not_a_crash()
    {
        var reg = new FakeHotkeyService { Throws = new InvalidOperationException("no window") };
        var log = new List<string>();
        var hk = new GlobalHotkey(reg, () => { }, log.Add);
        hk.Apply("Ctrl+Alt+H");
        Assert.Equal("Ctrl+Alt+H is in use by another app — pick another", hk.Error);
        Assert.Contains(log, l => l.Contains("no window"));
    }

    [Fact]
    public void A_toggle_that_throws_is_logged_not_propagated()
    {
        var reg = new FakeHotkeyService();
        var log = new List<string>();
        var hk = new GlobalHotkey(reg, () => throw new InvalidOperationException("popup gone"), log.Add);
        hk.Apply("Ctrl+Alt+H");
        reg.Pressed!();
        Assert.Contains(log, l => l.Contains("popup gone"));
    }

    [Fact]
    public void An_invalid_config_value_is_an_error_line_not_a_crash()
    {
        var hk = new GlobalHotkey(new FakeHotkeyService(), () => { }, _ => { });
        hk.Apply("G");
        Assert.NotNull(hk.Error);
        Assert.Null(hk.Registered);
    }
}
