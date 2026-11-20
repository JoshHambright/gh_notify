using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class PipelineSourceCatalogTests
{
    sealed class NamedSource(string name) : IInboxSource
    {
        public string Name => name;
        public Task<SourceResult> FetchAsync(CancellationToken ct) => Task.FromResult(SourceResult.Ok([]));
    }

    static PipelineSubscription Sub(Provider p, string? org = null, bool enabled = true) =>
        new() { Id = Guid.NewGuid().ToString(), Provider = p, Org = org, Project = "P", Repo = "a/b", PipelineId = 1, Enabled = enabled };

    static PipelineSourceCatalog Catalog(AppConfig c) =>
        new(() => c, org => new NamedSource(SourceRetirement.AdoPipelinesPrefix + org), () => new NamedSource(SourceRetirement.GitHubPipelines));

    static async Task<string[]> Names(PipelineSourceCatalog c) => (await c.GetSourcesAsync(default)).Select(s => s.Name).Order().ToArray();

    [Fact]
    public async Task No_subscriptions_means_no_sources() => Assert.Empty(await Names(Catalog(new AppConfig())));

    [Fact]
    public async Task One_source_per_ado_org_and_one_for_github()
    {
        var c = new AppConfig();
        c.PipelineSubscriptions.AddRange([Sub(Provider.Ado, "acme"), Sub(Provider.Ado, "ACME"), Sub(Provider.Ado, "other"), Sub(Provider.GitHub), Sub(Provider.GitHub)]);
        Assert.Equal(["ADO-Pipelines:acme", "ADO-Pipelines:other", "GitHub-Pipelines"], await Names(Catalog(c)));
    }

    [Fact]
    public async Task Disabled_subscriptions_and_disabled_providers_add_nothing()
    {
        var c = new AppConfig();
        c.PipelineSubscriptions.AddRange([Sub(Provider.Ado, "acme", enabled: false), Sub(Provider.GitHub)]);
        c.GitHub.Enabled = false;
        Assert.Empty(await Names(Catalog(c)));
        c.GitHub.Enabled = true;
        c.Ado.Enabled = false;
        c.PipelineSubscriptions.Add(Sub(Provider.Ado, "acme"));
        Assert.Equal(["GitHub-Pipelines"], await Names(Catalog(c)));
    }

    [Fact]
    public async Task A_source_is_created_once_and_reused()
    {
        var c = new AppConfig();
        c.PipelineSubscriptions.Add(Sub(Provider.Ado, "acme"));
        var catalog = Catalog(c);
        Assert.Same((await catalog.GetSourcesAsync(default)).Single(), (await catalog.GetSourcesAsync(default)).Single());
    }
}
