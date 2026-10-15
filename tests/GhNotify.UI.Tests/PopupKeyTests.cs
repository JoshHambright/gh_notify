using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>Every row of the v1.3 key table (spec §6), driven through real key events on the popup.</summary>
public class PopupKeyTests
{
    static void SelectRow(PopupHarness h, string key)
    {
        h.Press(Key.Home);
        for (var i = 0; i < 30 && h.Window.SelectedKey != key; i++) h.Press(Key.Down);
        Assert.Equal(key, h.Window.SelectedKey);
    }

    static void ClearFocus(PopupHarness h) => TopLevel.GetTopLevel(h.Window)!.FocusManager!.ClearFocus();

    static async Task SettleAsync(PopupHarness h)
    {
        await Task.Yield();
        h.Pump();
    }

    static bool Did(PopupHarness h, string call) => h.Host.Calls.Contains(call);

    // ----- navigation -----

    [AvaloniaFact]
    public void Opening_the_popup_selects_the_first_row_and_shows_the_ring()
    {
        using var h = new PopupHarness();
        Assert.Equal(h.CardKeys()[0], h.Window.SelectedKey);
        Assert.True(h.Ring(h.CardKeys()[0]).IsVisible);
        Assert.Single(h.All<Border>(), b => b.Name == "SelectionRing" && b.IsVisible);
    }

    [AvaloniaFact]
    public void Down_and_Up_move_the_selection_and_do_not_wrap()
    {
        using var h = new PopupHarness();
        var keys = h.CardKeys();
        h.Press(Key.Up);
        Assert.Equal(keys[0], h.Window.SelectedKey);
        h.Press(Key.Down);
        Assert.Equal(keys[1], h.Window.SelectedKey);
        Assert.True(h.Ring(keys[1]).IsVisible);
        Assert.False(h.Ring(keys[0]).IsVisible);
        h.Press(Key.Up);
        Assert.Equal(keys[0], h.Window.SelectedKey);
    }

    [AvaloniaFact]
    public void Home_and_End_jump_to_the_first_and_last_row()
    {
        using var h = new PopupHarness();
        var keys = h.CardKeys();
        h.Press(Key.End);
        Assert.Equal(keys[^1], h.Window.SelectedKey);
        h.Press(Key.Down); // no wrap
        Assert.Equal(keys[^1], h.Window.SelectedKey);
        h.Press(Key.Home);
        Assert.Equal(keys[0], h.Window.SelectedKey);
    }

    [AvaloniaFact]
    public void Space_expands_and_collapses_the_selected_row()
    {
        using var h = new PopupHarness();
        var key = h.CardKeys()[0];
        h.Press(Key.Space);
        Assert.True(Did(h, $"toggle {key}"));
        Assert.True(h.Shows(h.Card(key), "Pipeline"));
        h.Press(Key.Space);
        Assert.False(h.Shows(h.Card(key), "Pipeline"));
    }

    [AvaloniaFact]
    public void Enter_and_O_open_the_selected_item()
    {
        using var h = new PopupHarness();
        const string url = "https://dev.azure.com/contoso/platform/_build/results?buildId=1042";
        h.Press(Key.Enter);
        h.Press(Key.O);
        Assert.Equal(2, h.Host.Calls.Count(c => c == "open " + url));
    }

    [AvaloniaFact]
    public void Clicking_a_row_selects_it_but_hovering_does_not()
    {
        using var h = new PopupHarness();
        var key = h.CardKeys()[3];
        var card = h.Card(key);
        h.Window.MouseMove(h.Center(card));
        h.Pump();
        Assert.NotEqual(key, h.Window.SelectedKey);

        var meta = h.All<TextBlock>(card).First(t => t.Inlines is { Count: > 0 });
        h.Click(meta);
        Assert.Equal(key, h.Window.SelectedKey);
        Assert.True(h.Ring(key).IsVisible);
    }

    // ----- action keys -----

    [AvaloniaFact]
    public async Task A_begins_approve_on_an_approval_and_approve_pr_on_a_review()
    {
        using var h = new PopupHarness();
        h.Press(Key.A);
        await SettleAsync(h);
        Assert.True(Did(h, $"begin {SampleHost.ApprovalKey} Approve"));
        h.Press(Key.Escape); // close the confirm so the arrows move the selection again

        SelectRow(h, SampleHost.ReviewKey);
        h.Press(Key.A);
        await SettleAsync(h);
        Assert.True(Did(h, $"begin {SampleHost.ReviewKey} ApprovePr"));
    }

    [AvaloniaFact]
    public async Task R_begins_reject_M_begins_merge_and_C_begins_re_run()
    {
        using var h = new PopupHarness();
        h.Press(Key.R);
        await SettleAsync(h);
        Assert.True(Did(h, $"begin {SampleHost.ApprovalKey} Reject"));
        h.Press(Key.Escape);

        SelectRow(h, SampleHost.ReadyKey);
        h.Press(Key.M);
        await SettleAsync(h);
        Assert.True(Did(h, $"begin {SampleHost.ReadyKey} Merge"));
        h.Press(Key.Escape);

        SelectRow(h, SampleHost.CiKey);
        h.Press(Key.C);
        await SettleAsync(h);
        Assert.True(Did(h, $"begin {SampleHost.CiKey} RerunFailed"));
    }

    [AvaloniaFact]
    public async Task An_action_key_the_row_does_not_offer_does_nothing()
    {
        using var h = new PopupHarness();
        h.Press(Key.M); // an approval has no merge
        h.Press(Key.C);
        SelectRow(h, SampleHost.CiKey);
        h.Press(Key.A);
        h.Press(Key.R);
        h.Press(Key.M);
        await SettleAsync(h);
        Assert.DoesNotContain(h.Host.Calls, c => c.StartsWith("begin "));
    }

    [AvaloniaFact]
    public void S_opens_the_snooze_menu_of_the_selected_row()
    {
        using var h = new PopupHarness();
        Assert.Null(h.Window.OpenMenu);
        h.Press(Key.S);
        Assert.True(h.Window.OpenMenu is { IsOpen: true });
        Assert.Equal("Snooze", ((MenuItem)h.Window.OpenMenu!.Items[0]!).Header);
    }

    // ----- confirm panel -----

    [AvaloniaFact]
    public async Task Esc_cancels_an_open_confirm_and_a_second_Esc_hides_the_popup()
    {
        using var h = new PopupHarness();
        h.Press(Key.A);
        await SettleAsync(h);
        h.Press(Key.Escape);
        Assert.True(Did(h, $"cancel {SampleHost.ApprovalKey}"));
        Assert.True(h.Window.IsVisible);
        h.Press(Key.Escape);
        Assert.False(h.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Enter_in_an_open_confirm_confirms_only_after_the_guard()
    {
        using var h = new PopupHarness();
        h.Press(Key.A);
        await SettleAsync(h);
        h.Press(Key.Enter);
        Assert.DoesNotContain(h.Host.Calls, c => c.StartsWith("confirm "));
        Assert.DoesNotContain(h.Host.Calls, c => c.StartsWith("open ")); // Enter does not fall through to "open"
        await h.WaitGuardAsync();
        h.Press(Key.Enter);
        Assert.Contains(h.Host.Calls, c => c.StartsWith($"confirm {SampleHost.ApprovalKey}"));
    }

    [AvaloniaFact]
    public async Task Other_keys_are_ignored_while_a_confirm_is_open()
    {
        using var h = new PopupHarness();
        h.Press(Key.A);
        await SettleAsync(h);
        var calls = h.Host.Calls.Count;
        h.Press(Key.Down);
        h.Press(Key.R);
        h.Press(Key.Space);
        h.Press(Key.O);
        Assert.Equal(calls, h.Host.Calls.Count);
    }

    [AvaloniaFact]
    public async Task Enter_on_a_focused_button_activates_that_button_not_the_confirm()
    {
        using var h = new PopupHarness();
        h.Press(Key.A);
        await SettleAsync(h);
        await h.WaitGuardAsync();
        h.Button("Cancel", h.Card(SampleHost.ApprovalKey)).Focus();
        h.Press(Key.Enter);
        Assert.True(Did(h, $"cancel {SampleHost.ApprovalKey}"));
        Assert.DoesNotContain(h.Host.Calls, c => c.StartsWith("confirm "));
    }

    // ----- search keys -----

    [AvaloniaFact]
    public void Slash_and_Ctrl_F_focus_the_search_box_without_typing_the_slash()
    {
        using var h = new PopupHarness();
        Assert.False(h.All<Border>().First(b => b.Name == "SearchBar").IsVisible);
        h.Press(Key.D7, KeyModifiers.Shift, symbol: "/"); // e.g. a German layout: "/" is Shift+7, the typed character is what counts
        h.Type("/");
        var box = h.SearchBox;
        Assert.True(box.IsEffectivelyVisible);
        Assert.True(box.IsFocused);
        Assert.True(string.IsNullOrEmpty(box.Text));

        h.Press(Key.Escape); // empty search: hides the popup
        Assert.False(h.Window.IsVisible);

        h.Show();
        h.Press(Key.F, KeyModifiers.Control);
        Assert.True(h.SearchBox.IsFocused);
    }

    [AvaloniaFact]
    public void A_slash_typed_after_Ctrl_F_is_kept_in_the_search_box()
    {
        using var h = new PopupHarness();
        h.Press(Key.F, KeyModifiers.Control);
        h.Type("/");
        Assert.Equal("/", h.SearchBox.Text);
    }

    [AvaloniaFact]
    public void Only_the_slash_character_opens_search_other_text_input_does_not()
    {
        using var h = new PopupHarness();
        h.Press(Key.OemQuestion, symbol: "#"); // the key that types "/" on a US layout types something else elsewhere
        h.Type("#");
        Assert.False(h.All<Border>().First(b => b.Name == "SearchBar").IsVisible);
    }

    [AvaloniaFact]
    public void Esc_clears_a_non_empty_search_then_hides_when_empty()
    {
        using var h = new PopupHarness();
        h.Press(Key.F, KeyModifiers.Control);
        h.Type("alice");
        Assert.Equal("alice", h.SearchBox.Text);
        h.Press(Key.Escape);
        Assert.Equal("", h.SearchBox.Text);
        Assert.True(h.Window.IsVisible);
        h.Press(Key.Escape);
        Assert.False(h.Window.IsVisible);
    }

    [AvaloniaFact]
    public void Down_from_the_search_box_moves_to_the_first_row()
    {
        using var h = new PopupHarness();
        h.Press(Key.End);
        h.Press(Key.F, KeyModifiers.Control);
        h.Press(Key.Down);
        Assert.False(h.SearchBox.IsFocused);
        Assert.Equal(h.CardKeys()[0], h.Window.SelectedKey);
    }

    [AvaloniaFact]
    public async Task Typing_in_the_search_box_never_triggers_an_action()
    {
        using var h = new PopupHarness();
        h.Press(Key.F, KeyModifiers.Control);
        h.Host.Calls.Clear();
        foreach (var (key, text) in new (Key, string)[]
                 {
                     (Key.A, "a"), (Key.R, "r"), (Key.M, "m"), (Key.C, "c"), (Key.S, "s"), (Key.O, "o"),
                     (Key.Space, " "), (Key.Enter, "\r"), (Key.OemQuestion, "/"),
                 })
        {
            h.Press(key, symbol: text);
            if (key != Key.Enter) h.Type(text);
        }
        await SettleAsync(h);
        Assert.Empty(h.Host.Calls);
        Assert.Equal("armcso /", h.SearchBox.Text);
        Assert.True(h.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Typing_in_the_comment_box_never_triggers_an_action()
    {
        using var h = new PopupHarness();
        h.Press(Key.A);
        await SettleAsync(h);
        var box = h.CommentBox(SampleHost.ApprovalKey);
        box.Focus();
        h.Host.Calls.Clear();
        foreach (var (key, text) in new (Key, string)[]
                 {
                     (Key.A, "a"), (Key.R, "r"), (Key.M, "m"), (Key.C, "c"), (Key.S, "s"), (Key.O, "o"),
                     (Key.Space, " "), (Key.OemQuestion, "/"), (Key.Home, ""), (Key.End, ""), (Key.Up, ""),
                 })
        {
            h.Press(key, symbol: text);
            if (text.Length > 0) h.Type(text);
        }
        h.Press(Key.Enter);
        h.Press(Key.F, KeyModifiers.Control);
        await SettleAsync(h);
        Assert.Empty(h.Host.Calls);
        Assert.Equal("armcso /", h.CommentBox(SampleHost.ApprovalKey).Text);
        Assert.False(h.All<Border>().First(b => b.Name == "SearchBar").IsVisible);
    }

    [AvaloniaFact]
    public async Task Esc_in_the_comment_box_still_cancels_the_confirm()
    {
        using var h = new PopupHarness();
        h.Press(Key.A);
        await SettleAsync(h);
        h.CommentBox(SampleHost.ApprovalKey).Focus();
        h.Press(Key.Escape);
        Assert.True(Did(h, $"cancel {SampleHost.ApprovalKey}"));
    }

    // ----- selection across refreshes -----

    [AvaloniaFact]
    public void The_selection_stays_on_the_same_item_when_rows_above_it_change()
    {
        using var h = new PopupHarness();
        var keys = h.CardKeys();
        SelectRow(h, keys[4]);

        h.Host.HiddenKeys.Add(keys[1]);
        h.Host.Raise();
        h.Pump();

        Assert.Equal(keys[4], h.Window.SelectedKey);
        Assert.True(h.Ring(keys[4]).IsVisible);
        Assert.Single(h.All<Border>(), b => b.Name == "SelectionRing" && b.IsVisible);
        h.Press(Key.Space); // a later key acts on the same row
        Assert.True(Did(h, $"toggle {keys[4]}"));
    }

    [AvaloniaFact]
    public void When_the_selected_item_leaves_the_selection_moves_to_the_row_at_the_same_index()
    {
        using var h = new PopupHarness();
        var keys = h.CardKeys();
        SelectRow(h, keys[3]);

        h.Host.HiddenKeys.Add(keys[3]);
        h.Host.Raise();
        h.Pump();

        Assert.Equal(keys[4], h.Window.SelectedKey);
        Assert.True(h.Ring(keys[4]).IsVisible);
    }

    [AvaloniaFact]
    public void When_the_last_row_leaves_the_selection_moves_to_the_new_last_row()
    {
        using var h = new PopupHarness();
        var keys = h.CardKeys();
        h.Press(Key.End);
        h.Host.HiddenKeys.Add(keys[^1]);
        h.Host.Raise();
        h.Pump();
        Assert.Equal(keys[^2], h.Window.SelectedKey);
    }

    [AvaloniaFact]
    public void Re_opening_the_popup_selects_the_first_row_again()
    {
        using var h = new PopupHarness();
        h.Press(Key.End);
        h.Window.Hide();
        h.Pump();
        h.Show();
        Assert.Equal(h.CardKeys()[0], h.Window.SelectedKey);
    }

    [AvaloniaFact]
    public void The_selection_survives_a_no_op_refresh_and_an_expand()
    {
        using var h = new PopupHarness();
        var keys = h.CardKeys();
        SelectRow(h, keys[2]);
        h.Host.Raise();
        h.Pump();
        Assert.Equal(keys[2], h.Window.SelectedKey);
        h.Press(Key.Space);
        Assert.Equal(keys[2], h.Window.SelectedKey);
        Assert.True(h.Ring(keys[2]).IsVisible);
    }
}
