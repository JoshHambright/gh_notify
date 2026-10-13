using System.Net;
using GhNotify.Ado;
using GhNotify.Core;
using GhNotify.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace GhNotify.Tests.Ado;

public class AdoDirectoryTests
{
    static FakeHttpHandler Http() => new FakeHttpHandler()
        .On("/_apis/profile/profiles/me", """{"id":"profile-1"}""")
        .On("/_apis/accounts?memberId=profile-1", """{"count":2,"value":[{"accountName":"contoso"},{"accountName":"fabrikam"}]}""")
        .On("dev.azure.com/contoso/_apis/connectionData", Fixture.Read("ado-connection.json"))
        .On("queryMembership=expanded", Fixture.Read("ado-identity-expanded.json"))
        .On("identities?descriptors=", Fixture.Read("ado-identities-groups.json"))
        .On("dev.azure.com/contoso/_apis/projects", Fixture.Read("ado-projects.json"));

    [Fact]
    public async Task Discovers_orgs()
    {
        var dir = new AdoDirectory(new AdoClient(new HttpClient(Http()), new FakeTokenProvider()));
        Assert.Equal(["contoso", "fabrikam"], await dir.DiscoverOrgsAsync(default));
    }

    [Fact]
    public async Task Org_context_includes_me_and_groups_from_both_descriptor_shapes()
    {
        var http = Http();
        var dir = new AdoDirectory(new AdoClient(new HttpClient(http), new FakeTokenProvider()));
        var ctx = await dir.GetOrgContextAsync("contoso", default);
        Assert.Equal("me-id", ctx.MyId);
        Assert.Equal(["group-a", "group-b", "me-id"], ctx.MyIdentityIds.Order());
        var groupsCall = http.Requests.Single(r => r.RequestUri!.AbsoluteUri.Contains("descriptors="));
        Assert.Contains(Uri.EscapeDataString("Microsoft.TeamFoundation.Identity;S-1-9-2"), groupsCall.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Caches_context_24h_and_projects_1h()
    {
        var http = Http();
        var time = new FakeTimeProvider();
        var dir = new AdoDirectory(new AdoClient(new HttpClient(http), new FakeTokenProvider(), time), time);
        await dir.GetOrgContextAsync("contoso", default);
        await dir.GetProjectsAsync("contoso", default);
        time.Advance(TimeSpan.FromMinutes(59));
        await dir.GetOrgContextAsync("contoso", default);
        await dir.GetProjectsAsync("contoso", default);
        Assert.Single(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("connectionData"));
        Assert.Single(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("_apis/projects"));
        time.Advance(TimeSpan.FromMinutes(2));
        await dir.GetProjectsAsync("contoso", default);
        Assert.Equal(2, http.Requests.Count(r => r.RequestUri!.AbsoluteUri.Contains("_apis/projects")));
    }

    [Fact]
    public async Task Sign_in_page_203_is_an_auth_failure()
    {
        var http = new FakeHttpHandler().On("connectionData", "<html>sign in</html>", HttpStatusCode.NonAuthoritativeInformation);
        var dir = new AdoDirectory(new AdoClient(new HttpClient(http), new FakeTokenProvider()));
        var ex = await Assert.ThrowsAsync<SourceAuthException>(() => dir.GetOrgContextAsync("contoso", default));
        Assert.Contains("az login", ex.Message);
    }

    [Fact]
    public async Task Too_many_requests_is_rate_limited()
    {
        var http = new FakeHttpHandler().On("connectionData", "{}", HttpStatusCode.TooManyRequests);
        var dir = new AdoDirectory(new AdoClient(new HttpClient(http), new FakeTokenProvider()));
        await Assert.ThrowsAsync<RateLimitedException>(() => dir.GetOrgContextAsync("contoso", default));
    }
}
