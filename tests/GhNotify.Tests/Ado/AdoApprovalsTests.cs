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
        Assert.Equal("appr-1", item.ApprovalId);
        Assert.Equal("contoso", item.Org);
        Assert.Equal("Core Platform", item.Project);
        Assert.Equal(["Pipeline", "Run", "Project", "Instructions", "Created"], item.Details!.Select(x => x.Label));
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

    [Fact]
    public async Task Forbidden_approvals_keep_project_pr_items_and_do_not_carry_over_stale_ones()
    {
        var emptyPrs = false;
        var http = AdoPullRequestsTests.Http()
            .On("connectionData", Fixture.Read("ado-connection.json"))
            .On("queryMembership=expanded", Fixture.Read("ado-identity-expanded.json"))
            .On("identities?descriptors=", Fixture.Read("ado-identities-groups.json"))
            .On("/_apis/projects", """{"value":[{"id":"proj-1","name":"Core Platform"}]}""");
        var combined = new FakeHttpHandler()
            .On(r => r.RequestUri!.AbsoluteUri.Contains("/Core%20Platform/_apis/pipelines/approvals"),
                _ => FakeHttpHandler.Respond(System.Net.HttpStatusCode.Forbidden, """{"message":"no pipeline access"}"""))
            .On(r => r.RequestUri!.AbsoluteUri.Contains("/Core%20Platform/_apis/git/pullrequests?"),
                _ => emptyPrs
                    ? FakeHttpHandler.Respond(System.Net.HttpStatusCode.OK, """{"value":[]}""")
                    : FakeHttpHandler.Respond(System.Net.HttpStatusCode.OK, Fixture.Read("ado-prs.json")))
            .On(r => true, r => Forward(http, r));
        var client = new AdoClient(new HttpClient(combined), new FakeTokenProvider());
        var source = new AdoOrgSource("contoso", client, new AdoDirectory(client), () => new AdoConfig());

        var first = await source.FetchAsync(default);
        Assert.True(first.Healthy);
        Assert.Contains(first.Items, i => i.Key == "ado:review:contoso/Core Platform/api#101");

        emptyPrs = true;
        var second = await source.FetchAsync(default);
        Assert.True(second.Healthy);
        Assert.DoesNotContain(second.Items, i => i.Key == "ado:review:contoso/Core Platform/api#101");
    }

    static HttpResponseMessage Forward(FakeHttpHandler inner, HttpRequestMessage r) =>
        new HttpMessageInvoker(inner).SendAsync(r, default).GetAwaiter().GetResult();
}
