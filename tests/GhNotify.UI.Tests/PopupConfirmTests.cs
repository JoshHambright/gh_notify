using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

public class PopupConfirmTests
{
    const string Key = SampleHost.ApprovalKey;

    static async Task<PopupHarness> OpenApproveAsync(Action<SampleHost>? setup = null)
    {
        var h = new PopupHarness(setup);
        h.Click(h.Button("Approve…", h.Card(Key)));
        await Task.Yield();
        h.Pump();
        return h;
    }

    static int Confirms(PopupHarness h) => h.Host.Calls.Count(c => c.StartsWith("confirm "));

    [AvaloniaFact]
    public async Task The_confirm_panel_shows_the_statement_and_a_disabled_confirm_button_for_600_ms()
    {
        using var h = await OpenApproveAsync();
        var card = h.Card(Key);
        Assert.Contains(h.Texts(card), t => t == $"Approve {Key}?");
        var confirm = h.ConfirmButton(Key);
        Assert.True(confirm.IsEffectivelyVisible);
        Assert.False(confirm.IsEnabled);

        // Even a deliberate click is ignored while the button is disabled.
        h.Hover(confirm);
        h.Click(confirm);
        Assert.Equal(0, Confirms(h));

        await h.WaitGuardAsync();
        Assert.True(h.ConfirmButton(Key).IsEnabled);
    }

    [AvaloniaFact]
    public async Task The_guard_opens_at_exactly_600_ms()
    {
        var now = DateTimeOffset.Now;
        using var h = await OpenApproveAsync(s => s.Clock = () => now);
        Assert.False(h.ConfirmButton(Key).IsEnabled);

        now += TimeSpan.FromMilliseconds(599);
        h.Window.Render();
        h.Pump();
        Assert.False(h.ConfirmButton(Key).IsEnabled);

        now += TimeSpan.FromMilliseconds(1);
        h.Window.Render();
        h.Pump();
        Assert.True(h.ConfirmButton(Key).IsEnabled);
    }

    [AvaloniaFact]
    public async Task Cancel_closes_the_panel_and_a_refusal_offers_no_confirm()
    {
        using var h = await OpenApproveAsync();
        h.Click(h.Button("Cancel", h.Card(Key)));
        Assert.Contains($"cancel {Key}", h.Host.Calls);
        Assert.DoesNotContain(h.All<Button>(h.Card(Key)), b => b.Name == "ConfirmButton" && b.IsEffectivelyVisible);

        h.Host.Previews[SampleHost.ReadyKey] = ActionText.Refuse("Blocked by branch protection");
        h.Click(h.Button("Merge…", h.Card(SampleHost.ReadyKey)));
        await Task.Yield();
        h.Pump();
        var ready = h.Card(SampleHost.ReadyKey);
        Assert.Contains(h.Texts(ready), t => t == "Blocked by branch protection");
        Assert.DoesNotContain(h.All<Button>(ready), b => b.Name == "ConfirmButton" && b.IsEffectivelyVisible);
        h.Click(h.Button("Open in browser", ready));
        Assert.Contains("open https://github.com/acme/web/pull/31", h.Host.Calls);
    }

    [AvaloniaFact]
    public async Task A_deliberate_click_confirms_with_the_typed_comment()
    {
        using var h = await OpenApproveAsync();
        var box = h.CommentBox(Key);
        Assert.True(box.IsEffectivelyVisible);
        box.Focus();
        h.Type("lgtm");
        await h.WaitGuardAsync();

        var confirm = h.ConfirmButton(Key);
        h.Hover(confirm);
        h.Click(confirm);

        Assert.Contains($"confirm {Key} comment=lgtm method= delete=False", h.Host.Calls);
        var card = h.Card(Key);
        Assert.Contains(h.Texts(card), t => t == "Approved ✓");
        Assert.NotNull(h.TryButton("Close", card));
    }

    [AvaloniaFact]
    public async Task A_click_with_no_pointer_movement_does_not_confirm()
    {
        using var h = await OpenApproveAsync();
        await h.WaitGuardAsync();
        var confirm = h.ConfirmButton(Key);
        Assert.True(confirm.IsEnabled);

        h.Click(confirm); // the click that opened the panel, or the second half of a double click, lands on a still cursor
        Assert.Equal(0, Confirms(h));
        h.Hover(confirm);
        h.Click(confirm);
        Assert.Equal(1, Confirms(h));
    }

    [AvaloniaFact]
    public async Task A_press_dragged_onto_confirm_does_not_fire()
    {
        using var h = await OpenApproveAsync();
        await h.WaitGuardAsync();
        var confirm = h.ConfirmButton(Key);
        var statement = h.All<TextBlock>(h.Card(Key)).First(t => t.Name == "StatementText");

        var from = h.Center(statement);
        var to = h.Center(confirm);
        h.Window.MouseDown(from, MouseButton.Left);
        h.Window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        h.Window.MouseMove(new Point(to.X - 2, to.Y));
        h.Window.MouseMove(to);
        h.Window.MouseMove(new Point(to.X + 4, to.Y));
        h.Window.MouseUp(new Point(to.X + 4, to.Y), MouseButton.Left);
        h.Pump();

        Assert.Equal(0, Confirms(h));
    }

    [AvaloniaFact]
    public async Task Re_showing_the_popup_re_arms_the_guard()
    {
        using var h = await OpenApproveAsync();
        await h.WaitGuardAsync();
        Assert.True(h.ConfirmButton(Key).IsEnabled);

        h.Window.Hide();
        h.Pump(); // the platform's deactivation for the hide is delivered while the popup is hidden
        h.Show();

        Assert.False(h.ConfirmButton(Key).IsEnabled);
        await h.WaitGuardAsync();
        Assert.True(h.ConfirmButton(Key).IsEnabled);
    }

    [AvaloniaFact]
    public async Task Keyboard_confirm_obeys_the_guard_too()
    {
        using var h = new PopupHarness();
        h.Press(Avalonia.Input.Key.A);
        await Task.Yield();
        h.Pump();
        Assert.Contains($"begin {Key} Approve", h.Host.Calls);

        h.Press(Avalonia.Input.Key.Enter); // still inside the 600 ms
        Assert.Equal(0, Confirms(h));
        await h.WaitGuardAsync();
        h.Press(Avalonia.Input.Key.Enter);
        Assert.Equal(1, Confirms(h));
    }

    [AvaloniaFact]
    public async Task Typing_a_comment_survives_a_refresh_with_focus_and_caret()
    {
        using var h = await OpenApproveAsync();
        var box = h.CommentBox(Key);
        box.Focus();
        h.Type("needs a look");
        box.CaretIndex = 5;

        h.Host.HiddenKeys.Add(SampleHost.MutedKey); // a visible change: every row visual is rebuilt
        h.Host.Raise();
        h.Pump();

        var rebuilt = h.CommentBox(Key);
        Assert.NotSame(box, rebuilt);
        Assert.Equal("needs a look", rebuilt.Text);
        Assert.True(rebuilt.IsFocused);
        Assert.Equal(5, rebuilt.CaretIndex);
    }

    [AvaloniaFact]
    public async Task An_open_panel_ignores_other_rows_being_acted_on_and_leaves_with_the_item()
    {
        using var h = await OpenApproveAsync();
        h.Host.HiddenKeys.Add(Key); // the approval was handled elsewhere
        h.Host.Raise();
        h.Pump();
        Assert.False(h.HasCard(Key));
    }

    // ----- merge options -----

    static ActionPreview MergePreview(IReadOnlyList<MergeMethod> allowed, MergeMethod selected, bool canDelete = true) =>
        new(true, MergeStatement.Build(selected, 31, "main", "acme/web", "beef001"), "Merge PR #31", null, null, null)
        {
            Options = new MergeOptions(allowed, selected, canDelete, false),
        };

    static async Task<PopupHarness> OpenMergeAsync(IReadOnlyList<MergeMethod> allowed, MergeMethod selected, bool canDelete = true)
    {
        var h = new PopupHarness(s => s.Previews[SampleHost.ReadyKey] = MergePreview(allowed, selected, canDelete));
        h.Click(h.Button("Merge…", h.Card(SampleHost.ReadyKey)));
        await Task.Yield();
        h.Pump();
        return h;
    }

    [AvaloniaFact]
    public async Task The_merge_picker_lists_only_the_allowed_methods()
    {
        using var h = await OpenMergeAsync([MergeMethod.Squash, MergeMethod.Rebase], MergeMethod.Squash);
        var card = h.Card(SampleHost.ReadyKey);
        var radios = h.All<RadioButton>(card).ToList();
        Assert.Equal(["Squash", "Rebase"], radios.Select(r => r.Content).ToList());
        Assert.True(radios[0].IsChecked);
        Assert.False(radios[1].IsChecked);
        Assert.Contains(h.Texts(card), t => t == "Method");
        Assert.NotNull(h.All<CheckBox>(card).FirstOrDefault(c => c.Content as string == "Delete branch"));
    }

    [AvaloniaFact]
    public async Task The_merge_statement_follows_the_selected_method()
    {
        using var h = await OpenMergeAsync([MergeMethod.Merge, MergeMethod.Squash, MergeMethod.Rebase], MergeMethod.Squash);
        var card = h.Card(SampleHost.ReadyKey);
        string Statement() => h.All<TextBlock>(h.Card(SampleHost.ReadyKey)).First(t => t.Name == "StatementText").Text!;
        Assert.Equal("Squash and merge PR #31 into main in acme/web at beef001", Statement());

        h.Click(h.All<RadioButton>(card).First(r => r.Content as string == "Rebase"));
        Assert.Equal("Rebase and merge PR #31 into main in acme/web at beef001", Statement());
        h.Click(h.All<RadioButton>(h.Card(SampleHost.ReadyKey)).First(r => r.Content as string == "Merge"));
        Assert.Equal("Merge PR #31 into main in acme/web at beef001", Statement());
    }

    [AvaloniaFact]
    public async Task Merge_sends_the_picked_method_and_delete_branch_and_takes_no_comment()
    {
        using var h = await OpenMergeAsync([MergeMethod.Squash, MergeMethod.Rebase], MergeMethod.Squash);
        var card = h.Card(SampleHost.ReadyKey);
        Assert.DoesNotContain(h.All<TextBox>(card), b => b.Name == "CommentBox" && b.IsEffectivelyVisible);

        h.Click(h.All<RadioButton>(card).First(r => r.Content as string == "Rebase"));
        h.Click(h.All<CheckBox>(h.Card(SampleHost.ReadyKey)).First(c => c.Content as string == "Delete branch"));
        await h.WaitGuardAsync();
        var confirm = h.ConfirmButton(SampleHost.ReadyKey);
        Assert.Equal("Merge PR #31", confirm.Content);
        h.Hover(confirm);
        h.Click(confirm);

        Assert.Contains($"confirm {SampleHost.ReadyKey} comment= method=Rebase delete=True", h.Host.Calls);
    }

    [AvaloniaFact]
    public async Task The_merge_choices_survive_a_refresh()
    {
        using var h = await OpenMergeAsync([MergeMethod.Squash, MergeMethod.Rebase], MergeMethod.Squash);
        h.Click(h.All<RadioButton>(h.Card(SampleHost.ReadyKey)).First(r => r.Content as string == "Rebase"));

        h.Host.HiddenKeys.Add(SampleHost.MutedKey);
        h.Host.Raise();
        h.Pump();

        var card = h.Card(SampleHost.ReadyKey);
        Assert.True(h.All<RadioButton>(card).First(r => r.Content as string == "Rebase").IsChecked);
        Assert.StartsWith("Rebase and merge", h.All<TextBlock>(card).First(t => t.Name == "StatementText").Text);
    }

    [AvaloniaFact]
    public async Task Delete_branch_is_hidden_when_the_provider_does_not_offer_it()
    {
        using var h = await OpenMergeAsync([MergeMethod.Squash], MergeMethod.Squash, canDelete: false);
        Assert.Empty(h.All<CheckBox>(h.Card(SampleHost.ReadyKey)));
    }
}
