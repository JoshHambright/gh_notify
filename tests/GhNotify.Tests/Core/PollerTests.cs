using GhNotify.Core;
using Microsoft.Extensions.Time.Testing;

namespace GhNotify.Tests.Core;

public class PollerTests
{
    sealed class Src(string name, Func<CancellationToken, Task<SourceResult>> fetch) : IInboxSource
    {
        public string Name => name;
        public Task<SourceResult> FetchAsync(CancellationToken ct) => fetch(ct);
    }

    static InboxItem I(string key) =>
        new(key, Provider.GitHub, ItemKind.ReviewRequested, "r", "t", "u", "a", DateTimeOffset.UnixEpoch, null, null, null);

    [Fact]
    public async Task Polls_all_sources_and_raises_event()
    {
        var reconciler = new Reconciler();
        var catalog = new StaticCatalog(() =>
        [
            new Src("A", _ => Task.FromResult(SourceResult.Ok([I("a")]))),
            new Src("B", _ => Task.FromResult(SourceResult.Ok([I("b")]))),
        ]);
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-13T12:00:00Z"));
        var poller = new Poller(catalog, reconciler, TimeSpan.FromSeconds(30), time);
        InboxDiff? seen = null;
        poller.Polled += d => seen = d;

        var diff = await poller.PollOnceAsync(CancellationToken.None);

        Assert.Equal(2, diff.InitialLoad.Count);
        Assert.Same(diff, seen);
        Assert.Equal(time.GetUtcNow(), poller.LastPolledAt);
    }

    [Fact]
    public async Task Slow_source_times_out_as_unhealthy()
    {
        var reconciler = new Reconciler();
        var catalog = new StaticCatalog(() =>
            [new Src("Slow", async ct => { await Task.Delay(Timeout.Infinite, ct); return SourceResult.Ok([]); })]);
        var poller = new Poller(catalog, reconciler, TimeSpan.FromMilliseconds(100));

        await poller.PollOnceAsync(CancellationToken.None);

        var s = Assert.Single(reconciler.Statuses);
        Assert.False(s.Healthy);
        Assert.Contains("timed out", s.Problem);
    }

    [Fact]
    public async Task Throwing_source_becomes_unhealthy_with_message()
    {
        var reconciler = new Reconciler();
        var catalog = new StaticCatalog(() => [new Src("Boom", _ => throw new HttpRequestException("network down"))]);
        await new Poller(catalog, reconciler, TimeSpan.FromSeconds(5)).PollOnceAsync(CancellationToken.None);
        Assert.Equal("network down", reconciler.Statuses.Single().Problem);
    }

    [Fact]
    public async Task Composite_concatenates_and_failed_source_reports_problem()
    {
        var reconciler = new Reconciler();
        var catalog = new CompositeCatalog(
            new StaticCatalog(() => [new Src("A", _ => Task.FromResult(SourceResult.Ok([I("a")])))]),
            new StaticCatalog(() => [new FailedSource("ADO", "run az login")]));
        await new Poller(catalog, reconciler, TimeSpan.FromSeconds(5)).PollOnceAsync(CancellationToken.None);
        Assert.Equal(["A", "ADO"], reconciler.Statuses.Select(s => s.Name));
        Assert.Equal("run az login", reconciler.Statuses.Single(s => s.Name == "ADO").Problem);
    }

    [Fact]
    public async Task RunAsync_stops_on_cancellation()
    {
        // Deterministic: the source itself cancels on the 3rd poll, so no wall-clock timing decides the count.
        var polls = 0;
        using var cts = new CancellationTokenSource();
        var catalog = new StaticCatalog(() => [new Src("A", _ =>
        {
            if (++polls == 3) cts.Cancel();
            return Task.FromResult(SourceResult.Ok([]));
        })]);
        var poller = new Poller(catalog, new Reconciler(), TimeSpan.FromSeconds(5));
        var raised = 0;
        poller.Polled += _ => raised++;

        // The timeout is only a hang guard for a regression; it never decides the outcome.
        await poller.RunAsync(() => TimeSpan.Zero, cts.Token).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(3, polls);
        Assert.Equal(3, raised); // the cancelling poll still completes; the loop then stops before a 4th
    }
}
