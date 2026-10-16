using GhNotify.Core;

namespace GhNotify.Platform.Mac;

/// <summary>Carbon virtual key codes (kVK_ANSI_*, kVK_F*) and modifier masks for a parsed <see cref="Hotkey"/>.</summary>
public static class MacKeyCodes
{
    public const uint CmdKey = 0x0100, ShiftKey = 0x0200, OptionKey = 0x0800, ControlKey = 0x1000;

    static readonly Dictionary<string, uint> Codes = new()
    {
        ["A"] = 0x00, ["S"] = 0x01, ["D"] = 0x02, ["F"] = 0x03, ["H"] = 0x04, ["G"] = 0x05, ["Z"] = 0x06, ["X"] = 0x07,
        ["C"] = 0x08, ["V"] = 0x09, ["B"] = 0x0B, ["Q"] = 0x0C, ["W"] = 0x0D, ["E"] = 0x0E, ["R"] = 0x0F, ["Y"] = 0x10,
        ["T"] = 0x11, ["1"] = 0x12, ["2"] = 0x13, ["3"] = 0x14, ["4"] = 0x15, ["6"] = 0x16, ["5"] = 0x17, ["9"] = 0x19,
        ["7"] = 0x1A, ["8"] = 0x1C, ["0"] = 0x1D, ["O"] = 0x1F, ["U"] = 0x20, ["I"] = 0x22, ["P"] = 0x23, ["L"] = 0x25,
        ["J"] = 0x26, ["K"] = 0x28, ["N"] = 0x2D, ["M"] = 0x2E,
        ["F1"] = 0x7A, ["F2"] = 0x78, ["F3"] = 0x63, ["F4"] = 0x76, ["F5"] = 0x60, ["F6"] = 0x61, ["F7"] = 0x62,
        ["F8"] = 0x64, ["F9"] = 0x65, ["F10"] = 0x6D, ["F11"] = 0x67, ["F12"] = 0x6F,
    };

    /// <summary>Every key <see cref="HotkeyParser"/> accepts (A-Z, 0-9, F1-F12) has a code.</summary>
    public static (uint Modifiers, uint KeyCode) Translate(Hotkey hotkey)
    {
        uint mods = 0;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win)) mods |= CmdKey;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= ShiftKey;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= OptionKey;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) mods |= ControlKey;
        return (mods, Codes[hotkey.Key]);
    }

    internal static IReadOnlyCollection<string> KnownKeys => Codes.Keys;
}
