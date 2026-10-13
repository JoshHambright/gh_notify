using System.Net;
using GhNotify.Ado;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Ado;

public class AdoSourceCatalogTests
{
    sealed class Stub(string name) : IInboxSource
    {
        public string Name => name;
        public Task<SourceResult> FetchAsync(CancellationToken ct) => Task.FromResult(SourceResult.Ok([]));
    }

    static (AdoSourceCatalog, FakeHttpHandler) Make(AdoConfig cfg, FakeHttpHandler http)
    {
        var dir = new AdoDirectory(new AdoClient(new HttpClient(http), new FakeTokenProvider()));
        return (new AdoSourceCatalog(dir, () => cfg, org => new Stub($"ADO:{org}")), http);
    }

    static FakeHttpHandler Accounts() => new FakeHttpHandler()
        .On("/profiles/me", """{"id":"p"}""")
        .On("/_apis/accounts", """{"value":[{"accountName":"contoso"},{"accountName":"fabrikam"}]}""");

    [Fact]
    public async Task Discovered_plus_listed_minus_excluded()
    {
        var (catalog, _) = Make(new AdoConfig { Orgs = ["extra"], ExcludeOrgs = ["Fabrikam"] }, Accounts());
        var sources = await catalog.GetSourcesAsync(default);
        Assert.Equal(["ADO:contoso", "ADO:extra"], sources.Select(s => s.Name).Order());
    }

    [Fact]
    public async Task Disabled_returns_nothing()
    {
        var (catalog, http) = Make(new AdoConfig { Enabled = false }, Accounts());
        Assert.Empty(await catalog.GetSourcesAsync(default));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Reuses_source_instances_and_rediscovers_hourly_only()
    {
        var (catalog, http) = Make(new AdoConfig(), Accounts());
        var a = await catalog.GetSourcesAsync(default);
        var b = await catalog.GetSourcesAsync(default);
        Assert.Same(a[0], b[0]);
        Assert.Single(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/_apis/accounts"));
    }

    [Fact]
    public async Task Discovery_failure_without_cache_yields_failed_ADO_source()
    {
        var http = new FakeHttpHandler().On("/profiles/me", "<html/>", HttpStatusCode.NonAuthoritativeInformation);
        var (catalog, _) = Make(new AdoConfig(), http);
        var s = Assert.Single(await catalog.GetSourcesAsync(default));
        Assert.Equal("ADO", s.Name);
        var r = await s.FetchAsync(default);
        Assert.False(r.Healthy);
        Assert.Contains("az login", r.Problem);
    }

    [Fact]
    public async Task Discovery_failure_with_listed_orgs_still_returns_them()
    {
        var http = new FakeHttpHandler().On("/profiles/me", "{}", HttpStatusCode.InternalServerError);
        var (catalog, _) = Make(new AdoConfig { Orgs = ["contoso"] }, http);
        Assert.Equal(["ADO:contoso"], (await catalog.GetSourcesAsync(default)).Select(s => s.Name));
    }
}
