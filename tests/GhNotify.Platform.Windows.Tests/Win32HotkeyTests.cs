using GhNotify.Core;
using GhNotify.Platform.Windows;

namespace GhNotify.Platform.Windows.Tests;

public class Win32HotkeyTests
{
    [Theory]
    [InlineData("Win+Shift+G", 0x8u | 0x4u, (uint)'G')]
    [InlineData("Ctrl+Alt+5", 0x2u | 0x1u, (uint)'5')]
    [InlineData("F9", 0u, 0x78u)]
    [InlineData("Ctrl+F12", 0x2u, 0x7Bu)]
    public void Translates_to_win32_flags_and_virtual_keys(string text, uint mods, uint vk)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hotkey));
        Assert.Equal((mods, vk), Win32HotkeyService.Translate(hotkey));
    }

    [Fact]
    public void Registers_and_releases_a_combination_on_its_own_message_window()
    {
        // Ctrl+Alt+Shift+Win+F11 is not a chord anything uses.
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+Win+F11", out var hotkey, out _));
        string? error = "unset";
        var registered = false;
        var t = new Thread(() =>
        {
            using var service = new Win32HotkeyService();
            registered = service.TryRegister(hotkey, () => { }, out error);
            service.Unregister();
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        Assert.True(registered, error);
        Assert.Null(error);
    }

    [Fact]
    public void A_second_registration_of_the_same_combination_reports_the_clash()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+Win+F10", out var hotkey, out _));
        bool first = false, second = true;
        string? error = null;
        var t = new Thread(() =>
        {
            using var a = new Win32HotkeyService();
            using var b = new Win32HotkeyService();
            first = a.TryRegister(hotkey, () => { }, out _);
            second = b.TryRegister(hotkey, () => { }, out error);
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        Assert.True(first);
        Assert.False(second);
        Assert.Contains("1409", error); // ERROR_HOTKEY_ALREADY_REGISTERED
    }
}
