namespace GhNotify.Core;

public interface ISourceCatalog
{
    Task<IReadOnlyList<IInboxSource>> GetSourcesAsync(CancellationToken ct);
}

public sealed class StaticCatalog(Func<IReadOnlyList<IInboxSource>> sources) : ISourceCatalog
{
    public Task<IReadOnlyList<IInboxSource>> GetSourcesAsync(CancellationToken ct) => Task.FromResult(sources());
}

public sealed class CompositeCatalog(params ISourceCatalog[] parts) : ISourceCatalog
{
    public async Task<IReadOnlyList<IInboxSource>> GetSourcesAsync(CancellationToken ct)
    {
        var all = new List<IInboxSource>();
        foreach (var p in parts) all.AddRange(await p.GetSourcesAsync(ct));
        return all;
    }
}

public sealed class FailedSource(string name, string problem) : IInboxSource
{
    public string Name => name;
    public Task<SourceResult> FetchAsync(CancellationToken ct) => Task.FromResult(SourceResult.Fail(problem));
}
