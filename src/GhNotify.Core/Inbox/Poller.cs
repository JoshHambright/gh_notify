namespace GhNotify.Core;

public sealed class Poller(ISourceCatalog catalog, Reconciler reconciler, TimeSpan sourceTimeout, TimeProvider? time = null)
{
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly TimeProvider _time = time ?? TimeProvider.System;

    public event Action<InboxDiff>? Polled;
    public DateTimeOffset? LastPolledAt { get; private set; }

    public async Task<InboxDiff> PollOnceAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var sources = await catalog.GetSourcesAsync(ct);
            var fetched = await Task.WhenAll(sources.Select(async s => (s.Name, Result: await FetchOne(s, ct))));
            var results = new Dictionary<string, SourceResult>();
            foreach (var (name, result) in fetched) results.TryAdd(name, result);
            var now = _time.GetUtcNow();
            var diff = reconciler.Apply(results, now);
            LastPolledAt = now;
            Polled?.Invoke(diff);
            return diff;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RunAsync(Func<TimeSpan> interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("poll failed", ex);
            }
            try
            {
                await Task.Delay(interval(), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    async Task<SourceResult> FetchOne(IInboxSource source, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(sourceTimeout);
        try
        {
            return await source.FetchAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return SourceResult.Fail($"timed out after {sourceTimeout.TotalSeconds:0.#}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"source {source.Name} failed", ex);
            return SourceResult.Fail(ex.Message);
        }
    }
}
