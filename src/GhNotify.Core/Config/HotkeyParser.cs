namespace GhNotify.Core;

[Flags]
public enum HotkeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// <summary>A parsed global hotkey. <see cref="Key"/> is canonical upper case: A-Z, 0-9 or F1-F12.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>Canonical form, modifiers ordered Ctrl, Alt, Shift, Win (e.g. "Ctrl+Alt+G"); on macOS Alt and Win print as Option and Cmd.</summary>
    public override string ToString() => Format(OperatingSystem.IsMacOS());

    public string Format(bool mac)
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add(mac ? "Option" : "Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add(mac ? "Cmd" : "Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }
}

public static class HotkeyParser
{
    public static bool TryParse(string? text, out Hotkey hotkey) => TryParse(text, out hotkey, out _);

    public static bool TryParse(string? text, out Hotkey hotkey, out string? error)
    {
        hotkey = new Hotkey(HotkeyModifiers.None, "");
        if (string.IsNullOrWhiteSpace(text)) { error = "is empty"; return false; }

        var parts = text.Split('+').Select(p => p.Trim()).ToArray();
        var mods = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var m = parts[i].ToUpperInvariant() switch
            {
                "CTRL" => HotkeyModifiers.Ctrl,
                "ALT" or "OPTION" or "OPT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "CMD" or "COMMAND" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
            if (m == HotkeyModifiers.None) { error = $"unknown modifier '{parts[i]}' (use Ctrl, Alt/Option, Shift or Win/Cmd)"; return false; }
            if ((mods & m) != 0) { error = $"modifier '{parts[i]}' is repeated"; return false; }
            mods |= m;
        }

        var key = parts[^1].ToUpperInvariant();
        var isFunction = key.Length is 2 or 3 && key[0] == 'F' && key.Skip(1).All(char.IsAsciiDigit)
            && int.Parse(key.AsSpan(1)) is >= 1 and <= 12 && key[1] != '0';
        var isSingle = key.Length == 1 && (key[0] is >= 'A' and <= 'Z' || key[0] is >= '0' and <= '9');
        if (!isFunction && !isSingle) { error = $"unknown key '{parts[^1]}' (use A-Z, 0-9 or F1-F12)"; return false; }
        if (mods == HotkeyModifiers.None && !isFunction) { error = "needs a modifier (Ctrl, Alt, Shift or Win)"; return false; }
        if (mods == HotkeyModifiers.Shift) { error = "Shift alone is not enough — add Ctrl, Alt or Win"; return false; }

        hotkey = new Hotkey(mods, key);
        error = null;
        return true;
    }
}
