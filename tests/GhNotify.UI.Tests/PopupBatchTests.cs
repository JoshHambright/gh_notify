using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

public class PopupBatchTests
{
    static async Task<PopupHarness> OpenBatchAsync(ItemAction action = ItemAction.Approve, Action<SampleHost>? setup = null)
    {
        var h = new PopupHarness(setup);
        h.Click(h.Button(action == ItemAction.Reject ? "Reject all…" : "Approve all…"));
        await Task.Yield();
        h.Pump();
        return h;
    }

    static Border Panel(PopupHarness h) => h.All<Border>().First(b => b.Name == "BatchPanel");

    static IReadOnlyList<string> PanelTexts(PopupHarness h) => h.Texts(Panel(h));

    static void ClearFocus(PopupHarness h) => TopLevel.GetTopLevel(h.Window)!.FocusManager!.ClearFocus();

    // ----- the header buttons -----

    [AvaloniaFact]
    public void The_header_offers_Approve_all_and_Reject_all_with_two_or_more_approvals()
    {
        using var h = new PopupHarness();
        Assert.NotNull(h.TryButton("Approve all…"));
        Assert.NotNull(h.TryButton("Reject all…"));
    }

    [AvaloniaFact]
    public void A_single_approval_gets_no_batch_buttons()
    {
        using var h = new PopupHarness(s => s.HiddenKeys.Add(SampleHost.Approval2Key));
        Assert.Contains(SampleHost.ApprovalKey, h.CardKeys());
        Assert.Null(h.TryButton("Approve all…"));
        Assert.Null(h.TryButton("Reject all…"));
    }

    [AvaloniaFact]
    public void Other_sections_never_get_batch_buttons()
    {
        using var h = new PopupHarness();
        Assert.Single(h.All<Button>(), b => b.IsEffectivelyVisible && b.Content as string == "Approve all…");
    }

    // ----- the panel -----

    [AvaloniaFact]
    public async Task Opening_the_panel_lists_one_line_per_item_with_a_count_label()
    {
        using var h = await OpenBatchAsync();
        var texts = PanelTexts(h);
        Assert.Contains("Approve all — 2 waiting", texts);
        Assert.Contains(texts, t => t == "contoso/platform · platform-release 20261013.4");
        Assert.Contains(texts, t => t == "contoso/platform · platform-hotfix 20261013.5");
        Assert.Equal(2, h.All<CheckBox>(Panel(h)).Count(c => c.IsChecked == true));
        Assert.Contains(h.All<TextBlock>(Panel(h)), t => t.Text == "Comment for all (optional)");
        Assert.Equal("Approve 2", h.BatchConfirm.Content);
        Assert.Equal("Cancel", h.Button("Cancel", Panel(h)).Content);
    }

    [AvaloniaFact]
    public async Task The_panel_sits_under_the_Approvals_header()
    {
        using var h = await OpenBatchAsync();
        var slot = h.All<Border>().First(b => string.IsNullOrEmpty(b.Name) && b.Tag as string == BatchApproval.SectionId);
        Assert.Same(slot, Panel(h).Parent);
        Assert.True(Panel(h).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Reject_all_opens_the_reject_variant()
    {
        using var h = await OpenBatchAsync(ItemAction.Reject);
        Assert.Contains("Reject all — 2 waiting", PanelTexts(h));
        Assert.Equal("Reject 2", h.BatchConfirm.Content);
    }

    [AvaloniaFact]
    public async Task Confirm_is_disabled_for_600_ms_and_follows_the_ticked_count()
    {
        using var h = await OpenBatchAsync();
        Assert.False(h.BatchConfirm.IsEnabled);
        await h.WaitGuardAsync();
        Assert.True(h.BatchConfirm.IsEnabled);

        var boxes = h.All<CheckBox>(Panel(h)).ToList();
        h.Click(boxes[0]);
        Assert.Equal("Approve 1", h.BatchConfirm.Content);
        Assert.True(h.BatchConfirm.IsEnabled);
        h.Click(h.All<CheckBox>(Panel(h)).ToList()[1]);
        Assert.Equal("Approve 0", h.BatchConfirm.Content);
        Assert.False(h.BatchConfirm.IsEnabled);
    }

    [AvaloniaFact]
    public async Task A_press_dragged_onto_batch_confirm_or_a_click_without_movement_does_not_fire()
    {
        using var h = await OpenBatchAsync();
        await h.WaitGuardAsync();
        var confirm = h.BatchConfirm;

        h.Click(confirm);
        Assert.DoesNotContain(h.Host.Calls, c => c.StartsWith("batch-confirm"));

        var title = h.All<TextBlock>(Panel(h)).First(t => t.Text == "Approve all — 2 waiting");
        var from = h.Center(title);
        var to = h.Center(confirm);
        h.Window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        h.Window.MouseMove(new Avalonia.Point(to.X - 3, to.Y));
        h.Window.MouseMove(to);
        h.Window.MouseMove(new Avalonia.Point(to.X + 4, to.Y));
        h.Window.MouseUp(new Avalonia.Point(to.X + 4, to.Y), Avalonia.Input.MouseButton.Left);
        h.Pump();
        Assert.DoesNotContain(h.Host.Calls, c => c.StartsWith("batch-confirm"));
    }

    [AvaloniaFact]
    public async Task Confirming_sends_the_shared_comment_and_shows_a_result_per_item_and_a_summary()
    {
        using var h = await OpenBatchAsync();
        await h.WaitGuardAsync();
        var comment = h.BatchComment!;
        comment.Focus();
        h.Type("ship it");

        h.Hover(h.BatchConfirm);
        h.Click(h.BatchConfirm);

        Assert.Contains("batch-confirm comment=ship it", h.Host.Calls);
        var texts = PanelTexts(h);
        Assert.Contains("Approve all — done", texts);
        Assert.Equal(2, texts.Count(t => t == "✓ Approved ✓"));
        Assert.Contains("2 approved · 0 changed since you looked · 0 failed", texts);
        Assert.Equal("Close", h.Button("Close", Panel(h)).Content);
        Assert.False(h.BatchConfirm.IsEffectivelyVisible);

        h.Click(h.Button("Close", Panel(h)));
        Assert.Contains("batch-cancel", h.Host.Calls);
        Assert.DoesNotContain(h.All<Border>(), b => b.Name == "BatchPanel" && b.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_stale_and_a_failed_item_each_show_on_their_own_line()
    {
        using var h = await OpenBatchAsync(setup: s => s.Outcome = item => item.Key == SampleHost.ApprovalKey
            ? new ActionResult(false, "You don't have permission to do this")
            : new ActionResult(false, "This changed since you looked — refreshing", Stale: true));
        await h.WaitGuardAsync();
        h.Hover(h.BatchConfirm);
        h.Click(h.BatchConfirm);

        var texts = PanelTexts(h);
        Assert.Contains("✗ You don't have permission to do this", texts);
        Assert.Contains("⚠ This changed since you looked — refreshing", texts);
        Assert.Contains("0 approved · 1 changed since you looked · 1 failed", texts);
        var lines = h.Host.Batch.Lines;
        Assert.Equal(BatchLineStatus.Failed, lines.First(l => l.Key == SampleHost.ApprovalKey).Status);
        Assert.Equal(BatchLineStatus.Stale, lines.First(l => l.Key == SampleHost.Approval2Key).Status);
    }

    [AvaloniaFact]
    public async Task Esc_cancels_the_batch_and_Enter_confirms_it_after_the_guard()
    {
        using var h = await OpenBatchAsync();
        ClearFocus(h);
        h.Press(Key.Enter); // inside the 600 ms
        Assert.DoesNotContain(h.Host.Calls, c => c.StartsWith("batch-confirm"));
        await h.WaitGuardAsync();
        ClearFocus(h);
        h.Press(Key.Enter);
        Assert.Contains(h.Host.Calls, c => c.StartsWith("batch-confirm"));
        Assert.Contains("Approve all — done", PanelTexts(h));
        h.Press(Key.Escape);
        Assert.Contains("batch-cancel", h.Host.Calls);
    }

    [AvaloniaFact]
    public async Task Esc_before_confirming_closes_the_panel()
    {
        using var h = await OpenBatchAsync();
        ClearFocus(h);
        h.Press(Key.Escape);
        Assert.Contains("batch-cancel", h.Host.Calls);
        Assert.Equal(BatchPhase.Idle, h.Host.Batch.Phase);
        Assert.DoesNotContain(h.All<Border>(), b => b.Name == "BatchPanel" && b.IsEffectivelyVisible);
        Assert.True(h.Window.IsVisible);
    }

    // ----- refreshes while the panel is open -----

    [AvaloniaFact]
    public async Task The_comment_keeps_focus_text_and_caret_across_a_refresh()
    {
        using var h = await OpenBatchAsync();
        await h.WaitGuardAsync();
        var comment = h.BatchComment!;
        comment.Focus();
        h.Type("hello there");
        comment.CaretIndex = 5;
        var panel = Panel(h);

        h.Host.HiddenKeys.Add(SampleHost.MutedKey); // a visible change: every row visual is rebuilt
        h.Host.Raise();
        h.Pump();

        Assert.Same(panel, Panel(h)); // one panel instance, moved to the new section header
        var slot = h.All<Border>().First(b => string.IsNullOrEmpty(b.Name) && b.Tag as string == BatchApproval.SectionId);
        Assert.Same(slot, panel.Parent);
        var after = h.BatchComment!;
        Assert.Equal("hello there", after.Text);
        Assert.True(after.IsFocused);
        Assert.Equal(5, after.CaretIndex);
    }

    [AvaloniaFact]
    public async Task Results_stay_on_their_own_lines_when_the_inbox_refreshes_mid_batch()
    {
        using var h = await OpenBatchAsync(setup: s => s.Outcome = item => item.Key == SampleHost.Approval2Key
            ? new ActionResult(false, "boom")
            : new ActionResult(true, null));
        await h.WaitGuardAsync();
        h.Hover(h.BatchConfirm);
        h.Click(h.BatchConfirm);

        h.Host.HiddenKeys.Add(SampleHost.ApprovalKey); // the approved item leaves the inbox
        h.Host.Raise();
        h.Pump();

        var lines = h.All<Grid>(Panel(h)).Where(g => g.Children.OfType<CheckBox>().Any()).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains(h.Texts(lines[0]), t => t == "✓ Approved ✓");
        Assert.Contains(h.Texts(lines[1]), t => t == "✗ boom");
    }

    [AvaloniaFact]
    public async Task When_the_Approvals_section_is_gone_the_panel_moves_to_the_fallback_slot()
    {
        using var h = await OpenBatchAsync();
        await h.WaitGuardAsync();
        h.Host.HiddenKeys.Add(SampleHost.ApprovalKey);
        h.Host.HiddenKeys.Add(SampleHost.Approval2Key);
        h.Host.Raise();
        h.Pump();
        Assert.Equal("BatchFallbackSlot", ((Border)Panel(h).Parent!).Name);
        Assert.True(Panel(h).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Re_showing_the_popup_re_arms_the_batch_guard()
    {
        using var h = await OpenBatchAsync();
        await h.WaitGuardAsync();
        Assert.True(h.BatchConfirm.IsEnabled);
        h.Window.Hide();
        h.Pump();
        h.Show();
        Assert.False(h.BatchConfirm.IsEnabled);
        await h.WaitGuardAsync();
        Assert.True(h.BatchConfirm.IsEnabled);
    }

    [AvaloniaFact]
    public async Task Opening_the_batch_twice_is_refused_with_a_notice()
    {
        using var h = await OpenBatchAsync();
        h.Click(h.Button("Approve all…"));
        await Task.Yield();
        h.Pump();
        var notice = h.All<TextBlock>().First(t => t.Name == "NoticeText");
        Assert.True(notice.IsVisible);
        Assert.Equal("Finish the batch first", notice.Text);
    }
}
