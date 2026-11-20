using System.Collections.Concurrent;

namespace GhNotify.Core;

/// <summary>One pipeline source per ADO org with an enabled subscription, plus one for GitHub; none when nothing is subscribed.</summary>
public sealed class PipelineSourceCatalog(Func<AppConfig> config, Func<string, IInboxSource> adoOrgSource, Func<IInboxSource> gitHubSource) : ISourceCatalog
{
    readonly ConcurrentDictionary<string, IInboxSource> _sources = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<IInboxSource>> GetSourcesAsync(CancellationToken ct)
    {
        var c = config();
        var sources = new List<IInboxSource>();
        var subs = c.PipelineSubscriptions.Where(s => s.Enabled).ToList();
        if (c.Ado.Enabled)
            foreach (var org in subs.Where(s => s.Provider == Provider.Ado && !string.IsNullOrWhiteSpace(s.Org))
                         .Select(s => s.Org!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
                sources.Add(_sources.GetOrAdd(SourceRetirement.AdoPipelinesPrefix + org, _ => adoOrgSource(org)));
        if (c.GitHub.Enabled && subs.Any(s => s.Provider == Provider.GitHub))
            sources.Add(_sources.GetOrAdd(SourceRetirement.GitHubPipelines, _ => gitHubSource()));
        return Task.FromResult<IReadOnlyList<IInboxSource>>(sources);
    }
}
