using System.Net;
using GhNotify.Ado;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Ado;

public class AdoOrgSourceTests
{
    const string LegacyPrs = """
        { "value": [ { "pullRequestId": 201, "title": "Legacy fix", "isDraft": false, "creationDate": "2026-10-13T08:00:00Z",
          "createdBy": { "id": "alice-id", "displayName": "Alice" },
          "repository": { "id": "repo-9", "name": "old", "project": { "id": "proj-2", "name": "Legacy" } },
          "sourceRefName": "refs/heads/x", "reviewers": [ { "id": "me-id", "vote": 0 } ] } ] }
        """;

    static FakeHttpHandler Http(Func<bool> legacyFails) => AdoPullRequestsTests.Http()
        .On("connectionData", Fixture.Read("ado-connection.json"))
        .On("queryMembership=expanded", Fixture.Read("ado-identity-expanded.json"))
        .On("identities?descriptors=", Fixture.Read("ado-identities-groups.json"))
        .On("/_apis/projects", Fixture.Read("ado-projects.json"))
        .On(r => r.RequestUri!.AbsoluteUri.Contains("/Legacy/_apis/git/pullrequests?"),
            _ => legacyFails()
                ? FakeHttpHandler.Respond(HttpStatusCode.InternalServerError, "{}")
                : FakeHttpHandler.Respond(HttpStatusCode.OK, LegacyPrs))
        .On("/Legacy/_apis/pipelines/approvals", """{"value":[]}""");

    static AdoOrgSource Make(FakeHttpHandler http)
    {
        var client = new AdoClient(new HttpClient(http), new FakeTokenProvider());
        return new AdoOrgSource("contoso", client, new AdoDirectory(client), () => new AdoConfig());
    }

    [Fact]
    public async Task Collects_items_across_projects()
    {
        var source = Make(Http(() => false));
        Assert.Equal("ADO:contoso", source.Name);
        var result = await source.FetchAsync(default);
        Assert.True(result.Healthy);
        Assert.Contains(result.Items, i => i.Key == "ado:review:contoso/Legacy/old#201");
        Assert.Contains(result.Items, i => i.Key == "ado:review:contoso/Core Platform/api#101");
    }

    [Fact]
    public async Task Failing_project_keeps_its_previous_items()
    {
        var fail = false;
        var source = Make(Http(() => fail));
        await source.FetchAsync(default);
        fail = true;
        var result = await source.FetchAsync(default);
        Assert.True(result.Healthy);
        Assert.Contains(result.Items, i => i.Key == "ado:review:contoso/Legacy/old#201");
    }

    [Fact]
    public async Task Auth_failure_makes_source_unhealthy()
    {
        var http = new FakeHttpHandler().On("connectionData", "<html/>", HttpStatusCode.NonAuthoritativeInformation);
        var result = await Make(http).FetchAsync(default);
        Assert.False(result.Healthy);
        Assert.Contains("az login", result.Problem);
    }

    [Fact]
    public async Task Auth_failure_in_a_later_project_makes_source_unhealthy_with_az_login_hint()
    {
        // First registered route wins, so the 401 for Legacy must precede the shared routes.
        var http = new FakeHttpHandler()
            .On("/Legacy/_apis/git/pullrequests?", """{"message":"TF400813"}""", HttpStatusCode.Unauthorized)
            .On("connectionData", Fixture.Read("ado-connection.json"))
            .On("queryMembership=expanded", Fixture.Read("ado-identity-expanded.json"))
            .On("identities?descriptors=", Fixture.Read("ado-identities-groups.json"))
            .On("/_apis/projects", Fixture.Read("ado-projects.json"))
            .On("/Core%20Platform/_apis/git/pullrequests?", Fixture.Read("ado-prs.json"));
        var result = await Make(http).FetchAsync(default);
        Assert.False(result.Healthy);
        Assert.Contains("az login", result.Problem);
    }
}
