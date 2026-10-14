using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class KeyMapTests
{
    static readonly ItemAction[] Approvals = [ItemAction.Approve, ItemAction.Reject];

    static PopupContext List(RowMode? mode = RowMode.Collapsed, params ItemAction[] actions) =>
        new(SelectedRowMode: mode, SelectedRowActions: actions);

    static KeyCommand R(string key, PopupContext ctx, bool ctrl = false, bool shift = false) =>
        KeyMap.Resolve(new KeyInput(key, ctrl, shift), ctx);

    static KeyCommand K(KeyCommandKind kind) => new(kind);

    [Theory]
    [InlineData("Down", KeyCommandKind.MoveDown)]
    [InlineData("Up", KeyCommandKind.MoveUp)]
    [InlineData("Home", KeyCommandKind.First)]
    [InlineData("End", KeyCommandKind.Last)]
    [InlineData("Space", KeyCommandKind.ToggleExpand)]
    [InlineData("Enter", KeyCommandKind.Open)]
    [InlineData("O", KeyCommandKind.Open)]
    [InlineData("o", KeyCommandKind.Open)]
    [InlineData("S", KeyCommandKind.OpenSnoozeMenu)]
    [InlineData("/", KeyCommandKind.FocusSearch)]
    [InlineData("Slash", KeyCommandKind.FocusSearch)]
    [InlineData("Esc", KeyCommandKind.HidePopup)]
    [InlineData("Escape", KeyCommandKind.HidePopup)]
    public void List_keys(string key, KeyCommandKind expected) =>
        Assert.Equal(K(expected), R(key, List(RowMode.Collapsed, Approvals)));

    [Fact]
    public void Ctrl_F_focuses_search() => Assert.Equal(K(KeyCommandKind.FocusSearch), R("F", List(), ctrl: true));

    [Fact]
    public void Plain_F_does_nothing() => Assert.Equal(KeyCommand.None, R("F", List()));

    [Fact]
    public void Other_ctrl_chords_do_nothing()
    {
        Assert.Equal(KeyCommand.None, R("A", List(RowMode.Collapsed, ItemAction.Approve), ctrl: true));
        Assert.Equal(KeyCommand.None, R("Down", List(), ctrl: true));
    }

    [Fact]
    public void Space_toggles_even_without_a_row_for_section_headers() =>
        Assert.Equal(K(KeyCommandKind.ToggleExpand), R("Space", List(null)));

    [Theory]
    [InlineData("Enter")]
    [InlineData("O")]
    [InlineData("S")]
    public void Open_and_snooze_need_a_row(string key) => Assert.Equal(KeyCommand.None, R(key, List(null)));

    [Fact]
    public void A_begins_Approve_or_ApprovePr_whichever_is_offered()
    {
        Assert.Equal(KeyCommand.BeginAction(ItemAction.Approve), R("A", List(RowMode.Collapsed, Approvals)));
        Assert.Equal(KeyCommand.BeginAction(ItemAction.ApprovePr), R("A", List(RowMode.Expanded, ItemAction.ApprovePr)));
    }

    [Fact]
    public void R_M_C_begin_their_actions()
    {
        Assert.Equal(KeyCommand.BeginAction(ItemAction.Reject), R("R", List(RowMode.Collapsed, Approvals)));
        Assert.Equal(KeyCommand.BeginAction(ItemAction.Merge), R("M", List(RowMode.Collapsed, ItemAction.Merge)));
        Assert.Equal(KeyCommand.BeginAction(ItemAction.RerunFailed), R("C", List(RowMode.Collapsed, ItemAction.RerunFailed)));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("R")]
    [InlineData("M")]
    [InlineData("C")]
    public void Unavailable_action_does_nothing(string key)
    {
        Assert.Equal(KeyCommand.None, R(key, List(RowMode.Collapsed)));
        Assert.Equal(KeyCommand.None, R(key, new PopupContext(SelectedRowMode: RowMode.Collapsed)));
        Assert.Equal(KeyCommand.None, R(key, List(null, Approvals)));
    }

    [Fact]
    public void Action_not_offered_by_this_row_does_nothing()
    {
        Assert.Equal(KeyCommand.None, R("M", List(RowMode.Collapsed, Approvals)));
        Assert.Equal(KeyCommand.None, R("C", List(RowMode.Collapsed, ItemAction.ApprovePr)));
        Assert.Equal(KeyCommand.None, R("R", List(RowMode.Collapsed, ItemAction.ApprovePr)));
        Assert.Equal(KeyCommand.None, R("A", List(RowMode.Collapsed, ItemAction.Merge)));
    }

    [Theory]
    [InlineData(RowMode.Sending)]
    [InlineData(RowMode.Done)]
    public void Action_keys_ignored_while_row_is_busy(RowMode mode) =>
        Assert.Equal(KeyCommand.None, R("A", List(mode, ItemAction.Approve)));

    [Fact]
    public void Failed_row_can_retry() =>
        Assert.Equal(KeyCommand.BeginAction(ItemAction.Approve), R("A", List(RowMode.Failed, ItemAction.Approve)));

    [Fact]
    public void Batch_open_still_resolves_actions_so_the_ui_can_say_finish_the_batch_first() =>
        Assert.Equal(KeyCommand.BeginAction(ItemAction.Approve),
            R("A", List(RowMode.Collapsed, ItemAction.Approve) with { BatchOpen = true }));

    [Fact]
    public void Esc_cancels_an_open_batch() =>
        Assert.Equal(K(KeyCommandKind.CancelConfirm), R("Esc", List() with { BatchOpen = true }));

    [Fact]
    public void Enter_open_is_not_offered_while_a_row_is_confirming_but_enter_confirms()
    {
        var ctx = List(RowMode.Confirming, ItemAction.Approve) with { ConfirmOpen = true };
        Assert.Equal(K(KeyCommandKind.Confirm), R("Enter", ctx));
        Assert.Equal(K(KeyCommandKind.CancelConfirm), R("Esc", ctx));
        Assert.Equal(KeyCommand.None, R("O", ctx));
        Assert.Equal(KeyCommand.None, R("A", ctx));
        Assert.Equal(KeyCommand.None, R("Down", ctx));
        Assert.Equal(KeyCommand.None, R("/", ctx));
    }

    [Fact]
    public void Confirming_mode_alone_counts_as_confirm_open() =>
        Assert.Equal(K(KeyCommandKind.Confirm), R("Enter", List(RowMode.Confirming, ItemAction.Approve)));

    [Fact]
    public void Text_box_focused_swallows_everything_but_Esc()
    {
        var ctx = List(RowMode.Confirming, ItemAction.Approve) with { TextBoxFocused = true, ConfirmOpen = true };
        foreach (var key in new[] { "A", "R", "M", "C", "S", "O", "Enter", "Space", "/", "Down", "Up", "Home", "End" })
            Assert.Equal(KeyCommand.None, R(key, ctx));
        Assert.Equal(K(KeyCommandKind.CancelConfirm), R("Esc", ctx));
    }

    [Fact]
    public void Comment_box_without_confirm_state_is_still_text()
    {
        var ctx = List(RowMode.Collapsed, ItemAction.Approve) with { TextBoxFocused = true };
        Assert.Equal(KeyCommand.None, R("A", ctx));
        Assert.Equal(KeyCommand.None, R("Down", ctx));
        Assert.Equal(K(KeyCommandKind.HidePopup), R("Esc", ctx));
    }

    [Fact]
    public void Search_focused_letters_are_text_and_down_moves_to_the_list()
    {
        var ctx = List(RowMode.Collapsed, Approvals) with { TextBoxFocused = true, SearchFocused = true, SearchEmpty = false };
        foreach (var key in new[] { "A", "R", "M", "C", "S", "O", "Enter", "Space", "/", "Up", "Home", "End" })
            Assert.Equal(KeyCommand.None, R(key, ctx));
        Assert.Equal(K(KeyCommandKind.MoveDown), R("Down", ctx));
        Assert.Equal(KeyCommand.None, R("F", ctx, ctrl: true));
    }

    [Fact]
    public void Esc_in_search_clears_when_non_empty_and_hides_when_empty()
    {
        var ctx = new PopupContext(TextBoxFocused: true, SearchFocused: true, SearchEmpty: false);
        Assert.Equal(K(KeyCommandKind.ClearSearch), R("Esc", ctx));
        Assert.Equal(K(KeyCommandKind.HidePopup), R("Esc", ctx with { SearchEmpty = true }));
    }

    [Fact]
    public void Search_focused_flag_alone_is_enough_to_treat_keys_as_text() =>
        Assert.Equal(KeyCommand.None, R("A", new PopupContext(SearchFocused: true, SelectedRowMode: RowMode.Collapsed, SelectedRowActions: [ItemAction.Approve])));

    [Fact]
    public void Shifted_letters_and_question_mark_are_not_commands()
    {
        Assert.Equal(KeyCommand.None, R("A", List(RowMode.Collapsed, ItemAction.Approve), shift: true));
        Assert.Equal(KeyCommand.None, R("/", List(), shift: true));
    }

    [Fact]
    public void Unknown_key_does_nothing() => Assert.Equal(KeyCommand.None, R("Z", List(RowMode.Collapsed, Approvals)));
}
