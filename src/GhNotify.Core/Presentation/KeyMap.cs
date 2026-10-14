namespace GhNotify.Core;

/// <summary>
/// A key press as the popup sees it. <see cref="Key"/> is a name, case-insensitive: "Up", "Down", "Home", "End", "Space", "Enter",
/// "Esc" (or "Escape"), "/" (or "Slash"), or a single letter "A"-"Z".
/// </summary>
public sealed record KeyInput(string Key, bool Ctrl = false, bool Shift = false, bool Alt = false);

public sealed record PopupContext(
    bool TextBoxFocused = false,
    bool SearchFocused = false,
    bool SearchEmpty = true,
    RowMode? SelectedRowMode = null,
    IReadOnlyList<ItemAction>? SelectedRowActions = null,
    bool ConfirmOpen = false,
    bool BatchOpen = false);

public enum KeyCommandKind
{
    None, MoveUp, MoveDown, First, Last, ToggleExpand, Open, BeginAction, OpenSnoozeMenu,
    Confirm, CancelConfirm, FocusSearch, ClearSearch, HidePopup,
}

public readonly record struct KeyCommand(KeyCommandKind Kind, ItemAction? Action = null)
{
    public static readonly KeyCommand None = new(KeyCommandKind.None);
    public static KeyCommand BeginAction(ItemAction action) => new(KeyCommandKind.BeginAction, action);
}

/// <summary>Maps popup key presses to commands (v1.3 spec §6). Pure; PopupWindow only translates WPF key events.</summary>
public static class KeyMap
{
    public static KeyCommand Resolve(KeyInput input, PopupContext ctx)
    {
        var key = Normalize(input.Key);
        var confirm = ctx.ConfirmOpen || ctx.SelectedRowMode == RowMode.Confirming;
        var plain = !input.Ctrl && !input.Alt;

        if (key == "Esc")
        {
            if (!plain) return KeyCommand.None;
            if (confirm || ctx.BatchOpen) return new(KeyCommandKind.CancelConfirm);
            if (ctx.SearchFocused) return new(ctx.SearchEmpty ? KeyCommandKind.HidePopup : KeyCommandKind.ClearSearch);
            return new(KeyCommandKind.HidePopup);
        }

        if (ctx.TextBoxFocused || ctx.SearchFocused)
            return key == "Down" && plain && ctx.SearchFocused && !confirm ? new(KeyCommandKind.MoveDown) : KeyCommand.None;

        if (confirm) return key == "Enter" && plain && !input.Shift ? new(KeyCommandKind.Confirm) : KeyCommand.None;

        if (key == "F" && input.Ctrl && !input.Alt) return new(KeyCommandKind.FocusSearch);
        if (!plain) return KeyCommand.None;

        var row = ctx.SelectedRowMode is not null;
        var usable = ctx.SelectedRowMode is not (null or RowMode.Sending or RowMode.Done);
        switch (key)
        {
            case "Down": return new(KeyCommandKind.MoveDown);
            case "Up": return new(KeyCommandKind.MoveUp);
            case "Home": return new(KeyCommandKind.First);
            case "End": return new(KeyCommandKind.Last);
            case "Space": return new(KeyCommandKind.ToggleExpand);
            case "/" when !input.Shift: return new(KeyCommandKind.FocusSearch);
        }
        if (input.Shift) return KeyCommand.None;
        return key switch
        {
            "Enter" or "O" => row ? new(KeyCommandKind.Open) : KeyCommand.None,
            "A" => Begin(ctx, usable, ItemAction.Approve, ItemAction.ApprovePr),
            "R" => Begin(ctx, usable, ItemAction.Reject),
            "M" => Begin(ctx, usable, ItemAction.Merge),
            "C" => Begin(ctx, usable, ItemAction.RerunFailed),
            "S" => row ? new(KeyCommandKind.OpenSnoozeMenu) : KeyCommand.None,
            _ => KeyCommand.None,
        };
    }

    static KeyCommand Begin(PopupContext ctx, bool usable, params ItemAction[] candidates)
    {
        if (!usable || ctx.SelectedRowActions is not { } offered) return KeyCommand.None;
        foreach (var a in candidates)
            if (offered.Contains(a)) return KeyCommand.BeginAction(a);
        return KeyCommand.None;
    }

    static string Normalize(string key) => key.ToUpperInvariant() switch
    {
        "ESCAPE" or "ESC" => "Esc",
        "SLASH" or "/" or "OEMQUESTION" => "/",
        "SPACE" => "Space",
        "ENTER" or "RETURN" => "Enter",
        "DOWN" => "Down",
        "UP" => "Up",
        "HOME" => "Home",
        "END" => "End",
        var k => k,
    };
}
