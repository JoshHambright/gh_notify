using GhNotify.Core;

namespace GhNotify.App.Tests;

public class BatchCoordinatorTests
{
    static InboxItem Item(string id) => new($"ado:approval:{id}", Provider.Ado, ItemKind.ApprovalPending, "contoso/platform",
        $"release {id}", $"https://example.test/{id}", "", DateTimeOffset.Parse("2026-10-14T09:00:00Z"), null, null, null);

    static BatchEntry Entry(string id) => new(Item(id), null, "contoso/platform");

    sealed class FakeActions : IItemActions
    {
        readonly object _gate = new();
        int _inFlight;
        public int MaxInFlight;
        public readonly List<string> Executed = [];
        public readonly HashSet<string> Refuse = [];
        public readonly Dictionary<string, ActionResult> Results = [];
        public readonly HashSet<string> Throw = [];
        public TaskCompletionSource? Gate;
        public ActionInput? LastInput;

        public IReadOnlyList<ItemAction> ActionsFor(InboxItem item) => [ItemAction.Approve, ItemAction.Reject];

        public async Task<ActionPreview> PreviewAsync(InboxItem item, ItemAction action, AgentRun? run, CancellationToken ct)
        {
            lock (_gate) MaxInFlight = Math.Max(MaxInFlight, ++_inFlight);
            try
            {
                if (Gate is not null) await Gate.Task;
                await Task.Delay(20, CancellationToken.None);
            }
            finally { lock (_gate) _inFlight--; }
            return Refuse.Contains(item.Key)
                ? ActionText.Refuse("Already decided")
                : new ActionPreview(true, $"Approve {item.Title}", "Approve", null, null, item.Key);
        }

        public Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, ActionInput input, CancellationToken ct)
        {
            lock (_gate)
            {
                Assert.Equal(0, _inFlight); // sequential: nothing else is running
                Executed.Add(item.Key);
                LastInput = input;
            }
            if (Throw.Contains(item.Key)) throw new InvalidOperationException("boom");
            return Task.FromResult(Results.GetValueOrDefault(item.Key, new ActionResult(true, null)));
        }
    }

    static BatchCoordinator Make(FakeActions fake, Func<DateTimeOffset>? clock = null) =>
        new(fake, TimeSpan.FromSeconds(5), clock);

    [Fact]
    public async Task Previews_run_at_most_four_at_a_time_and_refused_items_are_unticked()
    {
        var fake = new FakeActions();
        fake.Refuse.Add(Item("c").Key);
        var batch = Make(fake);
        var entries = new[] { "a", "b", "c", "d", "e", "f", "g", "h" }.Select(Entry).ToList();

        await batch.OpenAsync(ItemAction.Approve, entries, CancellationToken.None);

        Assert.True(fake.MaxInFlight is > 1 and <= 4, $"max in flight was {fake.MaxInFlight}");
        Assert.Equal(BatchPhase.Ready, batch.Phase);
        Assert.Equal(entries.Select(e => e.Item.Key), batch.Lines.Select(l => l.Key)); // display order kept
        var refused = batch.Lines.Single(l => l.Key == Item("c").Key);
        Assert.Equal(BatchLineStatus.Refused, refused.Status);
        Assert.False(refused.Checked);
        Assert.Equal("Already decided", refused.Message);
        Assert.Equal(7, batch.CheckedCount);
        Assert.Equal("Approve 7", batch.ConfirmLabel);
    }

    [Fact]
    public async Task Confirm_guard_starts_when_all_previews_returned()
    {
        var now = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
        var fake = new FakeActions { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var batch = Make(fake, () => now);
        var open = batch.OpenAsync(ItemAction.Approve, [Entry("a"), Entry("b")], CancellationToken.None);
        Assert.Equal(BatchPhase.Previewing, batch.Phase);
        Assert.False(batch.CanConfirm(now.AddMinutes(5)));
        fake.Gate.SetResult();
        await open;

        Assert.Equal(now, batch.ReadyAt);
        Assert.False(batch.CanConfirm(now.AddMilliseconds(599)));
        Assert.True(batch.CanConfirm(now.AddMilliseconds(600)));
        batch.Rearm(); // popup re-shown: new guard from "now" (the clock is fixed here)
        Assert.False(batch.CanConfirm(now.AddMilliseconds(599)));
    }

    [Fact]
    public async Task Confirm_inside_the_guard_does_nothing()
    {
        var now = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
        var fake = new FakeActions();
        var batch = Make(fake, () => now);
        await batch.OpenAsync(ItemAction.Approve, [Entry("a")], CancellationToken.None);
        await batch.ConfirmAsync(null, CancellationToken.None); // the clock has not moved: 0 ms after ready
        Assert.Empty(fake.Executed);
        Assert.Equal(BatchPhase.Ready, batch.Phase);
    }

    [Fact]
    public async Task Executes_ticked_items_sequentially_in_order_with_the_shared_comment_and_summarises()
    {
        var t = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
        var clock = t;
        var fake = new FakeActions();
        fake.Results[Item("b").Key] = new ActionResult(false, "This changed since you looked — refreshing", Stale: true);
        fake.Results[Item("c").Key] = new ActionResult(false, "You don't have permission to do this");
        fake.Throw.Add(Item("d").Key);
        var batch = Make(fake, () => clock);
        await batch.OpenAsync(ItemAction.Approve, [Entry("a"), Entry("b"), Entry("c"), Entry("d"), Entry("e")], CancellationToken.None);
        batch.SetChecked(Item("e").Key, false);
        clock = t.AddSeconds(1);
        var refreshed = 0;

        await batch.ConfirmAsync("  ship it  ", CancellationToken.None, () => { refreshed++; return Task.CompletedTask; });

        Assert.Equal([Item("a").Key, Item("b").Key, Item("c").Key, Item("d").Key], fake.Executed);
        Assert.Equal("ship it", fake.LastInput?.Comment);
        Assert.Equal(BatchPhase.Done, batch.Phase);
        Assert.Equal(1, refreshed);
        Assert.Equal("1 approved · 1 changed since you looked · 2 failed", batch.Summary);
        var lines = batch.Lines.ToDictionary(l => l.Key);
        Assert.Equal(BatchLineStatus.Ok, lines[Item("a").Key].Status);
        Assert.Equal(BatchLineStatus.Stale, lines[Item("b").Key].Status);
        Assert.Equal(BatchLineStatus.Failed, lines[Item("c").Key].Status);
        Assert.Equal("You don't have permission to do this", lines[Item("c").Key].Message);
        Assert.Equal(BatchLineStatus.Failed, lines[Item("d").Key].Status);
        Assert.Equal("boom", lines[Item("d").Key].Message);
        Assert.Equal(BatchLineStatus.Ready, lines[Item("e").Key].Status); // unticked: never sent
    }

    [Fact]
    public async Task Reject_summary_and_label()
    {
        var t = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
        var clock = t;
        var fake = new FakeActions();
        var batch = Make(fake, () => clock);
        await batch.OpenAsync(ItemAction.Reject, [Entry("a"), Entry("b")], CancellationToken.None);
        Assert.Equal("Reject 2", batch.ConfirmLabel);
        clock = t.AddSeconds(1);
        await batch.ConfirmAsync(null, CancellationToken.None);
        Assert.Equal("2 rejected · 0 changed since you looked · 0 failed", batch.Summary);
    }

    [Fact]
    public async Task Cancel_closes_the_panel_and_a_done_panel_can_be_closed()
    {
        var t = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
        var clock = t;
        var batch = Make(new FakeActions(), () => clock);
        await batch.OpenAsync(ItemAction.Approve, [Entry("a")], CancellationToken.None);
        Assert.True(batch.Active);
        batch.Close();
        Assert.False(batch.Active);
        Assert.Empty(batch.Lines);

        await batch.OpenAsync(ItemAction.Approve, [Entry("a")], CancellationToken.None);
        clock = t.AddSeconds(1);
        await batch.ConfirmAsync(null, CancellationToken.None);
        Assert.Equal(BatchPhase.Done, batch.Phase);
        batch.Close();
        Assert.Equal(BatchPhase.Idle, batch.Phase);
    }

    [Fact]
    public async Task Nothing_ticked_cannot_confirm()
    {
        var t = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
        var batch = Make(new FakeActions(), () => t.AddSeconds(5));
        await batch.OpenAsync(ItemAction.Approve, [Entry("a")], CancellationToken.None);
        batch.SetChecked(Item("a").Key, false);
        Assert.Equal("Approve 0", batch.ConfirmLabel);
        Assert.False(batch.CanConfirm(t.AddMinutes(1)));
    }
}
