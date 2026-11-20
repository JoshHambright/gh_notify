using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class InboxDiffTests
{
    static InboxItem It(string key, ItemKind kind) =>
        new(key, Provider.Ado, kind, "acme/P", "T", "https://u", "a", DateTimeOffset.UnixEpoch, null, null, null);

    [Fact]
    public void Pipeline_rows_found_on_a_sources_first_poll_count_as_added_so_they_toast_one_by_one()
    {
        var pipe = It("pipe", ItemKind.PipelineRun);
        var approval = It("appr", ItemKind.ApprovalPending);
        var added = It("new", ItemKind.ReviewRequested);
        var diff = new InboxDiff([added], [], [], [pipe, approval]).WithPipelineInitialAsAdded();
        Assert.Equal([approval], diff.InitialLoad);
        Assert.Equal([added, pipe], diff.Added);
    }

    [Fact]
    public void A_diff_without_initial_pipeline_rows_is_returned_unchanged()
    {
        var diff = new InboxDiff([], [], [], [It("appr", ItemKind.ApprovalPending)]);
        Assert.Same(diff, diff.WithPipelineInitialAsAdded());
    }
}
