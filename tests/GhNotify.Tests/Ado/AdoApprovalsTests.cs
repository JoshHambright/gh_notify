using GhNotify.Ado;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Ado;

public class AdoApprovalsTests
{
    [Fact]
    public async Task Finds_pending_steps_assigned_to_me_or_my_groups()
    {
        var http = new FakeHttpHandler().On("/Core%20Platform/_apis/pipelines/approvals?state=pending", Fixture.Read("ado-approvals.json"));
        var items = await AdoApprovals.FetchAsync(new AdoClient(new HttpClient(http), new FakeTokenProvider()),
            "contoso", AdoPullRequestsTests.Core, AdoPullRequestsTests.Ctx, default);

        var item = Assert.Single(items);
        Assert.Equal("ado:approval:contoso/Core Platform/appr-1", item.Key);
        Assert.Equal(ItemKind.ApprovalPending, item.Kind);
        Assert.Equal("contoso/Core Platform", item.Repo);
        Assert.Equal("api-release 20261013.3", item.Title);
        Assert.Equal("https://dev.azure.com/contoso/Core%20Platform/_build/results?buildId=901", item.Url);
        Assert.Equal("Instructions: Check the release notes", item.Detail);
        Assert.Contains("$expand=steps", http.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Missing_web_link_falls_back_to_builds_page()
    {
        var json = """{ "value": [ { "id": "a", "status": "pending", "steps": [ { "assignedApprover": { "id": "me-id" }, "status": "pending" } ], "pipeline": { "name": "p" } } ] }""";
        var http = new FakeHttpHandler().On("pipelines/approvals", json);
        var item = Assert.Single(await AdoApprovals.FetchAsync(new AdoClient(new HttpClient(http), new FakeTokenProvider()),
            "contoso", AdoPullRequestsTests.Core, AdoPullRequestsTests.Ctx, default));
        Assert.Equal("https://dev.azure.com/contoso/Core%20Platform/_build", item.Url);
        Assert.Equal("p", item.Title);
        Assert.Null(item.Detail);
    }

    [Fact]
    public async Task Org_source_includes_approvals()
    {
        var http = AdoPullRequestsTests.Http()
            .On("connectionData", Fixture.Read("ado-connection.json"))
            .On("queryMembership=expanded", Fixture.Read("ado-identity-expanded.json"))
            .On("identities?descriptors=", Fixture.Read("ado-identities-groups.json"))
            .On("/_apis/projects", """{"value":[{"id":"proj-1","name":"Core Platform"}]}""");
        // Http() already has an empty approvals route, so wrap it and answer approvals first.
        var combined = new FakeHttpHandler().On(r => true, r =>
            r.RequestUri!.AbsoluteUri.Contains("pipelines/approvals")
                ? FakeHttpHandler.Respond(System.Net.HttpStatusCode.OK, Fixture.Read("ado-approvals.json"))
                : Forward(http, r));
        var client = new AdoClient(new HttpClient(combined), new FakeTokenProvider());
        var result = await new AdoOrgSource("contoso", client, new AdoDirectory(client), () => new AdoConfig()).FetchAsync(default);
        Assert.Contains(result.Items, i => i.Key == "ado:approval:contoso/Core Platform/appr-1");
    }

    static HttpResponseMessage Forward(FakeHttpHandler inner, HttpRequestMessage r) =>
        new HttpMessageInvoker(inner).SendAsync(r, default).GetAwaiter().GetResult();
}
