using System.Collections.Concurrent;
using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Ado;

public sealed class AdoOrgSource(string org, AdoClient client, AdoDirectory directory, Func<AdoConfig> config) : IInboxSource
{
    readonly ConcurrentDictionary<string, IReadOnlyList<InboxItem>> _lastByProject = new();

    public string Name => $"ADO:{org}";

    public async Task<SourceResult> FetchAsync(CancellationToken ct)
    {
        try
        {
            var cfg = config();
            var ctx = await directory.GetOrgContextAsync(org, ct);
            var projects = await directory.GetProjectsAsync(org, ct);
            var all = new ConcurrentBag<InboxItem>();
            await Parallel.ForEachAsync(projects, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (p, ct2) =>
            {
                IReadOnlyList<InboxItem> items;
                try
                {
                    items = await FetchProjectAsync(p, ctx, cfg, ct2);
                    _lastByProject[p.Id] = items;
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException)
                {
                    Log.Error($"ADO {org}/{p.Name} skipped this poll: {ex.Message}");
                    items = _lastByProject.GetValueOrDefault(p.Id) ?? [];
                }
                foreach (var i in items) all.Add(i);
            });
            return SourceResult.Ok(all.ToList());
        }
        catch (Exception ex) when (ex is SourceAuthException or RateLimitedException or HttpRequestException or JsonException)
        {
            return SourceResult.Fail(ex.Message);
        }
    }

    async Task<IReadOnlyList<InboxItem>> FetchProjectAsync(AdoProject p, AdoOrgContext ctx, AdoConfig cfg, CancellationToken ct)
    {
        var items = new List<InboxItem>();
        items.AddRange(await AdoPullRequests.FetchAsync(client, org, p, ctx, cfg.IncludeDrafts, ct));
        items.AddRange(await AdoApprovals.FetchAsync(client, org, p, ctx, ct));
        return items;
    }
}
