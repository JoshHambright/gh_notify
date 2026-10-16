using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Win+Shift+G", HotkeyModifiers.Win | HotkeyModifiers.Shift, "G")]
    [InlineData("ctrl+alt+g", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "G")]
    [InlineData("  Ctrl + Alt + 7 ", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "7")]
    [InlineData("F9", HotkeyModifiers.None, "F9")]
    [InlineData("f12", HotkeyModifiers.None, "F12")]
    [InlineData("Alt+F1", HotkeyModifiers.Alt, "F1")]
    [InlineData("Cmd+Shift+G", HotkeyModifiers.Win | HotkeyModifiers.Shift, "G")]
    [InlineData("command+option+g", HotkeyModifiers.Win | HotkeyModifiers.Alt, "G")]
    [InlineData("Ctrl+Opt+G", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "G")]
    [InlineData("WIN+ctrl+Shift+Alt+Z", HotkeyModifiers.Win | HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt, "Z")]
    public void Parses_valid(string text, HotkeyModifiers mods, string key)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hk));
        Assert.Equal(new Hotkey(mods, key), hk);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("G")]
    [InlineData("5")]
    [InlineData("Shift+G")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Meta+G")]
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F01")]
    [InlineData("Ctrl+Esc")]
    [InlineData("Ctrl+GG")]
    [InlineData("Ctrl+Ctrl+G")]
    [InlineData("Ctrl++G")]
    [InlineData("Ctrl+;")]
    public void Rejects_invalid(string text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("Ctrl+Alt+Shift+Win+X", "Ctrl+Option+Shift+Cmd+X")]
    [InlineData("Ctrl+Option+G", "Ctrl+Option+G")]
    public void Mac_format_uses_Option_and_Cmd_and_parses_back(string text, string mac)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hk));
        Assert.Equal(mac, hk.Format(mac: true));
        Assert.True(HotkeyParser.TryParse(mac, out var again));
        Assert.Equal(hk, again);
    }

    [Theory]
    [InlineData(false, "Win+Shift+G")]
    [InlineData(true, "Ctrl+Option+G")]
    public void Default_hotkey_depends_on_the_OS_and_is_valid(bool mac, string expected)
    {
        Assert.Equal(expected, AppConfig.DefaultHotkey(mac));
        Assert.True(HotkeyParser.TryParse(expected, out _));
    }

    [Fact]
    public void Null_is_rejected() => Assert.False(HotkeyParser.TryParse(null, out _));

    [Theory]
    [InlineData("shift+win+ctrl+alt+x", "Ctrl+Alt+Shift+Win+X")]
    [InlineData("win+shift+g", "Shift+Win+G")]
    [InlineData("f5", "F5")]
    [InlineData("Ctrl+Alt+G", "Ctrl+Alt+G")]
    public void ToString_is_canonical_and_round_trips(string text, string canonical)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hk));
        Assert.Equal(canonical, hk.Format(mac: false));
        Assert.True(HotkeyParser.TryParse(hk.ToString(), out var again));
        Assert.Equal(hk, again);
    }
}
