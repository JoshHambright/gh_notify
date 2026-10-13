using System.Collections.Concurrent;
using GhNotify.Core;

namespace GhNotify.Ado;

public sealed class AdoSourceCatalog(
    AdoDirectory directory,
    Func<AdoConfig> config,
    Func<string, IInboxSource> createOrgSource,
    TimeProvider? time = null) : ISourceCatalog
{
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly ConcurrentDictionary<string, IInboxSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    IReadOnlyList<string>? _discovered;
    DateTimeOffset _discoveredAt;
    string? _discoveryProblem;

    public async Task<IReadOnlyList<IInboxSource>> GetSourcesAsync(CancellationToken ct)
    {
        var cfg = config();
        if (!cfg.Enabled) return [];

        if (cfg.DiscoverOrgs && (_discovered is null || _time.GetUtcNow() - _discoveredAt > TimeSpan.FromHours(1)))
        {
            try
            {
                _discovered = await directory.DiscoverOrgsAsync(ct);
                _discoveredAt = _time.GetUtcNow();
                _discoveryProblem = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep the last good list (if any) so a token blip never drops orgs.
                _discoveryProblem = ex.Message;
                Log.Error("ADO org discovery failed", ex);
            }
        }

        var orgs = new HashSet<string>(cfg.Orgs, StringComparer.OrdinalIgnoreCase);
        if (cfg.DiscoverOrgs && _discovered is not null) orgs.UnionWith(_discovered);
        orgs.ExceptWith(cfg.ExcludeOrgs);

        if (orgs.Count == 0 && _discoveryProblem is not null)
            return [new FailedSource("ADO", _discoveryProblem)];
        return orgs.Order(StringComparer.OrdinalIgnoreCase).Select(o => _sources.GetOrAdd(o, createOrgSource)).ToList();
    }
}
