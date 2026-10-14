using GhNotify.Core;

namespace GhNotify.App.Tests;

public class GlobalHotkeyTests
{
    sealed class FakeRegistrar : IHotkeyRegistrar
    {
        public bool Succeeds = true;
        public Exception? Throws;
        public readonly List<string> Calls = [];
        public Action? Pressed;

        public bool Register(Hotkey hotkey, Action pressed)
        {
            Calls.Add("register " + hotkey);
            if (Throws is not null) throw Throws;
            Pressed = pressed;
            return Succeeds;
        }

        public void Unregister() => Calls.Add("unregister");
        public void Dispose() { }
    }

    [Fact]
    public void Registered_hotkey_toggles_the_popup()
    {
        var reg = new FakeRegistrar();
        var toggles = 0;
        var hk = new GlobalHotkey(reg, () => toggles++, _ => { });
        hk.Apply("win+shift+g");
        Assert.Null(hk.Error);
        Assert.Equal("Shift+Win+G", hk.Registered?.ToString()); // canonical modifier order
        reg.Pressed!();
        Assert.Equal(1, toggles);
    }

    [Fact]
    public void Clash_is_exposed_and_logged_once_and_never_throws()
    {
        var reg = new FakeRegistrar { Succeeds = false };
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
        var reg = new FakeRegistrar { Succeeds = false };
        var hk = new GlobalHotkey(reg, () => { }, _ => { });
        hk.Apply("Win+Shift+G");
        Assert.NotNull(hk.Error);
        reg.Succeeds = true;
        hk.Apply("Ctrl+Alt+H");
        Assert.Null(hk.Error);
        Assert.Equal("Ctrl+Alt+H", hk.Registered?.ToString());
        Assert.Equal("unregister", reg.Calls[^2]); // released the old one first
    }

    [Fact]
    public void Empty_turns_it_off()
    {
        var reg = new FakeRegistrar();
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
        var reg = new FakeRegistrar { Throws = new InvalidOperationException("no window") };
        var log = new List<string>();
        var hk = new GlobalHotkey(reg, () => { }, log.Add);
        hk.Apply("Ctrl+Alt+H");
        Assert.Equal("Ctrl+Alt+H is in use by another app — pick another", hk.Error);
        Assert.Contains(log, l => l.Contains("no window"));
    }

    [Fact]
    public void A_toggle_that_throws_is_logged_not_propagated()
    {
        var reg = new FakeRegistrar();
        var log = new List<string>();
        var hk = new GlobalHotkey(reg, () => throw new InvalidOperationException("popup gone"), log.Add);
        hk.Apply("Ctrl+Alt+H");
        reg.Pressed!();
        Assert.Contains(log, l => l.Contains("popup gone"));
    }

    [Fact]
    public void An_invalid_config_value_is_an_error_line_not_a_crash()
    {
        var hk = new GlobalHotkey(new FakeRegistrar(), () => { }, _ => { });
        hk.Apply("G");
        Assert.NotNull(hk.Error);
        Assert.Null(hk.Registered);
    }

    [Theory]
    [InlineData("Win+Shift+G", 0x8u | 0x4u, (uint)'G')]
    [InlineData("Ctrl+Alt+5", 0x2u | 0x1u, (uint)'5')]
    [InlineData("F9", 0u, 0x78u)]
    [InlineData("Ctrl+F12", 0x2u, 0x7Bu)]
    public void Translates_to_win32_flags_and_virtual_keys(string text, uint mods, uint vk)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hotkey));
        Assert.Equal((mods, vk), Win32HotkeyRegistrar.Translate(hotkey));
    }
}
