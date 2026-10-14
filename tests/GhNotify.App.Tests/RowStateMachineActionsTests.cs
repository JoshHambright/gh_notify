using GhNotify.Core;

namespace GhNotify.App.Tests;

public class RowStateMachineActionsTests
{
    static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
    static readonly ActionPreview Ok = new(true, "stmt", "Go", null, null, null);

    static RowUiState SendingFor(ItemAction a) => RowStateMachine.Sending(RowStateMachine.WithPreview(RowStateMachine.Begin(a, T), Ok, T));

    [Theory]
    [InlineData(ItemAction.Approve, "Approving…", "Approved ✓")]
    [InlineData(ItemAction.ApprovePr, "Approving…", "Approved ✓")]
    [InlineData(ItemAction.Reject, "Rejecting…", "Rejected ✓")]
    [InlineData(ItemAction.Merge, "Merging…", "Merged ✓")]
    [InlineData(ItemAction.RerunFailed, "Re-running…", "Re-run started ✓")]
    public void Labels_per_action(ItemAction action, string sending, string done)
    {
        var s = SendingFor(action);
        Assert.Equal(sending, s.Message);
        var finished = RowStateMachine.Finished(s, new ActionResult(true, null));
        Assert.Equal(RowMode.Done, finished.Mode);
        Assert.Equal(done, finished.Message);
        Assert.True(RowStateMachine.HidesOnNextPoll(finished));
        Assert.Null(RowStateMachine.ToastFor(new ActionResult(true, done), action));
    }

    [Theory]
    [InlineData(ItemAction.Merge, "Merged, but the branch couldn't be deleted: 403")]
    [InlineData(ItemAction.RerunFailed, "Re-run started, but 1 failed: build")]
    public void Warnings_stay_and_are_toasted(ItemAction action, string warning)
    {
        var done = RowStateMachine.Finished(SendingFor(action), new ActionResult(true, warning));
        Assert.Equal(warning, done.Message);
        Assert.False(RowStateMachine.HidesOnNextPoll(done));
        Assert.Equal(warning, RowStateMachine.ToastFor(new ActionResult(true, warning), action));
    }

    [Fact]
    public void Failed_merge_is_not_hidden()
    {
        var failed = RowStateMachine.Finished(SendingFor(ItemAction.Merge), new ActionResult(false, "Blocked by branch protection"));
        Assert.Equal(RowMode.Failed, failed.Mode);
        Assert.False(RowStateMachine.HidesOnNextPoll(failed));
    }
}
