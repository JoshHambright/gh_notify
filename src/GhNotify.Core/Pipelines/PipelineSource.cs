using System.Text.Json;

namespace GhNotify.Core;

/// <summary>Reads recent runs of one subscribed pipeline; implemented per provider.</summary>
public interface IPipelineRunFeed
{
    /// <param name="since">Only runs that could have finished at or after this moment need to be returned.</param>
    /// <param name="includeInProgress">Also return runs that are still going (needed to alert on a finished stage of a running pipeline).</param>
    /// <exception cref="SourceAuthException">Sign-in problem; fails the whole source.</exception>
    /// <exception cref="RateLimitedException">Fails the whole source.</exception>
    /// <exception cref="HttpRequestException">Fails only this subscription (for example 403/404 on a deleted pipeline).</exception>
    Task<IReadOnlyList<PipelineRunInfo>> RecentRunsAsync(PipelineSubscription sub, DateTimeOffset since, bool includeInProgress, CancellationToken ct);
}

/// <summary>
/// Turns subscribed pipeline runs into inbox rows. Rows are events, so they are carried forward from the reconciler's last-known items
/// until the user dismisses them; a dismissed key is simply not returned, which the reconciler sees as a removal.
/// </summary>
public sealed class PipelineSource(
    string name,
    IPipelineRunFeed feed,
    Func<IReadOnlyList<PipelineSubscription>> subscriptions,
    Func<IReadOnlySet<string>> dismissedKeys,
    Func<IReadOnlyList<InboxItem>> lastKnown,
    TimeProvider? time = null) : IInboxSource
{
    public static readonly TimeSpan DismissedRetention = TimeSpan.FromDays(14);
    static readonly TimeSpan MaxLookback = TimeSpan.FromDays(3);
    static readonly TimeSpan BaselineSlack = TimeSpan.FromDays(1);

    readonly TimeProvider _time = time ?? TimeProvider.System;

    public string Name => name;

    public async Task<SourceResult> FetchAsync(CancellationToken ct)
    {
        try
        {
            var now = _time.GetUtcNow();
            var subs = subscriptions().Where(s => s.Enabled).ToList();
            var owners = subs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            var items = new Dictionary<string, InboxItem>();

            foreach (var sub in subs)
            {
                if (sub.CreatedAt == default)
                {
                    Log.Error($"pipeline {sub.Scope}/{sub.Pipeline} skipped: createdAt is missing");
                    continue;
                }
                var since = Max(sub.CreatedAt - BaselineSlack, now - MaxLookback);
                IReadOnlyList<PipelineRunInfo> runs;
                try
                {
                    runs = await feed.RecentRunsAsync(sub, since, includeInProgress: sub.Stages.Count > 0, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException)
                {
                    Log.Error($"pipeline {sub.Scope}/{sub.Pipeline} skipped this poll: {ex.Message}");
                    continue;
                }
                foreach (var run in runs)
                    foreach (var hit in PipelineMatcher.Match(sub, run))
                        items.TryAdd(PipelineMatcher.Key(run, hit.Stage), PipelineMatcher.ToItem(sub, run, hit));
            }

            foreach (var old in lastKnown())
                if (old.Kind == ItemKind.PipelineRun && old.SubscriptionId is { } id && owners.Contains(id))
                    items.TryAdd(old.Key, old);

            var dismissed = dismissedKeys();
            return SourceResult.Ok(items.Values.Where(i => !dismissed.Contains(i.Key)).ToList());
        }
        catch (Exception ex) when (ex is SourceAuthException or RateLimitedException or HttpRequestException or JsonException)
        {
            return SourceResult.Fail(ex.Message);
        }
    }

    /// <summary>Forgets dismissals whose run finished more than <see cref="DismissedRetention"/> ago (it can no longer come back).</summary>
    public static IReadOnlyList<DismissedRun> PruneDismissed(IReadOnlyList<DismissedRun> dismissed, DateTimeOffset now, IReadOnlySet<string>? stillHeld = null) =>
        dismissed.Where(d => now - d.Finished <= DismissedRetention || stillHeld?.Contains(d.Key) == true).ToList();

    static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
