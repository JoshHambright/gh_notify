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
        var s = RowStateMachine.WithPreview(RowStateMachine.Begin(ItemAction.Approve, T), Ok);
        Assert.False(RowStateMachine.CanConfirm(s, T.AddMilliseconds(150)));
        Assert.True(RowStateMachine.CanConfirm(s, T.AddMilliseconds(600)));
    }

    [Fact]
    public void Refused_preview_never_confirms()
    {
        var s = RowStateMachine.WithPreview(RowStateMachine.Begin(ItemAction.ApprovePr, T), ActionText.Refuse("New commits since you looked — refresh."));
        Assert.False(RowStateMachine.CanConfirm(s, T.AddSeconds(5)));
        Assert.Equal("New commits since you looked — refresh.", s.Message);
    }

    [Fact]
    public void Finished_messages()
    {
        var sending = RowStateMachine.Sending(RowStateMachine.WithPreview(RowStateMachine.Begin(ItemAction.Reject, T), Ok));
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
    public void Approve_finishes_as_approved_and_preview_keeps_open_time()
    {
        var begun = RowStateMachine.Begin(ItemAction.Approve, T);
        Assert.Equal("Checking…", begun.Message);
        Assert.False(RowStateMachine.CanConfirm(begun, T.AddSeconds(5))); // no preview yet
        var s = RowStateMachine.WithPreview(begun, Ok);
        Assert.Equal(T, s.ConfirmOpenedAt);
        Assert.Null(s.Message);
        Assert.Equal("Approved ✓", RowStateMachine.Finished(RowStateMachine.Sending(s), new ActionResult(true, null)).Message);
        Assert.False(RowStateMachine.CanConfirm(null, T.AddSeconds(5)));
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
