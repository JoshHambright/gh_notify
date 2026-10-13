using GhNotify.App;
using GhNotify.Core;

namespace GhNotify.App.Tests;

public class ActionFlowTests
{
    static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
    static readonly ActionPreview Ok = new(true, "Approve deployment of x run 1 to production", "Approve production", null, null, new long[] { 1 });

    [Fact]
    public void Double_click_cannot_confirm()
    {
        var s = RowStateMachine.WithPreview(RowStateMachine.Begin(ItemAction.Approve, T), Ok, T);
        Assert.False(RowStateMachine.CanConfirm(s, T.AddMilliseconds(150)));
        Assert.True(RowStateMachine.CanConfirm(s, T.AddMilliseconds(600)));
    }

    [Fact]
    public void Refused_preview_never_confirms()
    {
        var s = RowStateMachine.WithPreview(RowStateMachine.Begin(ItemAction.ApprovePr, T), ActionText.Refuse("New commits since you looked — refresh."), T);
        Assert.False(RowStateMachine.CanConfirm(s, T.AddSeconds(5)));
        Assert.Equal("New commits since you looked — refresh.", s.Message);
    }

    [Fact]
    public void Finished_messages()
    {
        var sending = RowStateMachine.Sending(RowStateMachine.WithPreview(RowStateMachine.Begin(ItemAction.Reject, T), Ok, T));
        Assert.False(RowStateMachine.CanConfirm(sending, T.AddSeconds(5)));
        Assert.Equal("Rejected ✓", RowStateMachine.Finished(sending, new ActionResult(true, null)).Message);
        var failed = RowStateMachine.Finished(sending, new ActionResult(false, "You don't have permission to approve this"));
        Assert.Equal(RowMode.Failed, failed.Mode);
        Assert.Equal("You don't have permission to approve this", failed.Message);
    }

    [Fact]
    public void Toggle_and_cancel()
    {
        Assert.Equal(RowMode.Expanded, RowStateMachine.Toggle(null).Mode);
        Assert.Equal(RowMode.Collapsed, RowStateMachine.Toggle(new RowUiState(RowMode.Expanded)).Mode);
        var confirming = RowStateMachine.Begin(ItemAction.Approve, T);
        Assert.Equal(RowMode.Confirming, RowStateMachine.Toggle(confirming).Mode);
        Assert.Equal(RowMode.Expanded, RowStateMachine.Cancel(confirming).Mode);
    }

    [Fact]
    public void Approve_finishes_as_approved()
    {
        var begun = RowStateMachine.Begin(ItemAction.Approve, T);
        Assert.Equal("Checking…", begun.Message);
        Assert.False(RowStateMachine.CanConfirm(begun, T.AddSeconds(5))); // no preview yet
        var s = RowStateMachine.WithPreview(begun, Ok, T);
        Assert.Equal(T, s.ConfirmOpenedAt);
        Assert.Null(s.Message);
        Assert.Equal("Approved ✓", RowStateMachine.Finished(RowStateMachine.Sending(s), new ActionResult(true, null)).Message);
        Assert.False(RowStateMachine.CanConfirm(null, T.AddSeconds(5)));
    }

    [Fact]
    public void Confirm_guard_runs_from_when_the_preview_arrives()
    {
        var s = RowStateMachine.WithPreview(RowStateMachine.Begin(ItemAction.Approve, T), Ok, T.AddMilliseconds(900));
        Assert.Equal(T.AddMilliseconds(900), s.ConfirmOpenedAt);
        Assert.False(RowStateMachine.CanConfirm(s, T.AddMilliseconds(1400)));
        Assert.True(RowStateMachine.CanConfirm(s, T.AddMilliseconds(1500)));
    }

}

public class ConfirmPointerGuardTests
{
    static readonly System.Windows.Point A = new(100, 200), B = new(103, 201);

    [Fact]
    public void Entered_while_enabled_then_moved_arms()
    {
        var g = new ConfirmPointerGuard();
        g.Entered(A, enabled: true);
        Assert.False(g.IsArmed); // an enter with no movement may be WPF re-syncing under a still cursor
        g.Moved(B, enabled: true);
        Assert.True(g.IsArmed);
    }

    [Fact]
    public void Entered_while_disabled_then_enabled_without_new_enter_is_not_armed()
    {
        var g = new ConfirmPointerGuard();
        g.Entered(A, enabled: false);
        g.Moved(B, enabled: false);
        g.Moved(A, enabled: true);
        g.Moved(B, enabled: true);
        Assert.False(g.IsArmed);
    }

    [Fact]
    public void Stationary_cursor_never_arms()
    {
        var g = new ConfirmPointerGuard();
        g.Entered(A, enabled: true);
        g.Moved(A, enabled: true);
        Assert.False(g.IsArmed);
    }

    [Fact]
    public void Rerender_resets()
    {
        var g = new ConfirmPointerGuard();
        g.Entered(A, enabled: true);
        g.Moved(B, enabled: true);
        g.Reset();
        Assert.False(g.IsArmed);
        g.Moved(A, enabled: true); // movement alone after a reset (no fresh enter) does not arm
        Assert.False(g.IsArmed);
    }

    [Fact]
    public void Leaving_or_disabling_disarms()
    {
        var g = new ConfirmPointerGuard();
        g.Entered(A, enabled: true);
        g.Moved(B, enabled: true);
        g.Left();
        Assert.False(g.IsArmed);
        g.Entered(A, enabled: true);
        g.Moved(B, enabled: true);
        g.Moved(A, enabled: false);
        Assert.False(g.IsArmed);
    }
}

public class RearmTests
{
    static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
    static readonly ActionPreview Ok = new(true, "s", "Approve production", null, null, null);

    [Fact]
    public void Reshowing_the_popup_rearms_the_confirm_guard_and_keeps_the_preview()
    {
        var s = RowStateMachine.WithPreview(RowStateMachine.Begin(ItemAction.Approve, T), Ok, T);
        Assert.True(RowStateMachine.CanConfirm(s, T.AddSeconds(30)));
        var rearmed = RowStateMachine.Rearm(s, T.AddSeconds(30));
        Assert.Same(Ok, rearmed.Preview);
        Assert.False(RowStateMachine.CanConfirm(rearmed, T.AddSeconds(30).AddMilliseconds(100)));
        Assert.True(RowStateMachine.CanConfirm(rearmed, T.AddSeconds(30) + ActionText.ConfirmDelay));
    }

    [Fact]
    public void Rearm_leaves_other_modes_alone()
    {
        var expanded = new RowUiState(RowMode.Expanded);
        Assert.Same(expanded, RowStateMachine.Rearm(expanded, T));
    }
}

public class RefreshPolicyTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");

    [Fact] public void Never_polled_refreshes() => Assert.True(RefreshPolicy.ShouldRefreshOnOpen(null, false, Now));
    [Fact] public void Recent_poll_skips() => Assert.False(RefreshPolicy.ShouldRefreshOnOpen(Now.AddSeconds(-9), false, Now));
    [Fact] public void Ten_seconds_refreshes() => Assert.True(RefreshPolicy.ShouldRefreshOnOpen(Now.AddSeconds(-10), false, Now));
    [Fact] public void In_flight_skips() => Assert.False(RefreshPolicy.ShouldRefreshOnOpen(null, true, Now));
}
