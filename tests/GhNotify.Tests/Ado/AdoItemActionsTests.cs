using System.Net;
using System.Text.Json;
using GhNotify.Ado;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Ado;

public class AdoItemActionsTests
{
    static InboxItem Approval() => new("ado:approval:contoso/Core Platform/appr-1", Provider.Ado, ItemKind.ApprovalPending, "contoso/Core Platform",
        "api-release 20261013.3", "https://x", "", DateTimeOffset.UnixEpoch, null, null, null) { Org = "contoso", Project = "Core Platform", ApprovalId = "appr-1" };
    static InboxItem Pr(string sha = "s101") => new("ado:review:contoso/Core Platform/api#101", Provider.Ado, ItemKind.ReviewRequested, "contoso/Core Platform/api",
        "Direct review", "https://x", "Alice", DateTimeOffset.UnixEpoch, sha, null, null) { Org = "contoso", Project = "Core Platform", RepoId = "repo-1", Number = 101 };

    static FakeHttpHandler Dir() => new FakeHttpHandler()
        .On("connectionData", Fixture.Read("ado-connection.json"))
        .On("queryMembership=expanded", Fixture.Read("ado-identity-expanded.json"))
        .On("identities?descriptors=", Fixture.Read("ado-identities-groups.json"));

    static AdoItemActions Make(FakeHttpHandler http)
    {
        var c = new AdoClient(new HttpClient(http), new FakeTokenProvider());
        return new AdoItemActions(c, new AdoDirectory(c));
    }

    const string PendingForGroup = """{"id":"appr-1","status":"pending","steps":[{"assignedApprover":{"id":"group-a"},"status":"pending"}]}""";

    [Fact]
    public async Task Approve_pipeline_patches_with_json_array()
    {
        var http = Dir()
            .On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsoluteUri.Contains("/pipelines/approvals/appr-1"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, PendingForGroup))
            .On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"value":[]}"""));
        var a = Make(http);
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        Assert.True(p.Allowed);
        Assert.Equal("Approve api-release 20261013.3 in contoso/Core Platform", p.Statement);
        var r = await a.ExecuteAsync(Approval(), ItemAction.Approve, p, "ok \"go\"", default);
        Assert.True(r.Ok);
        var patch = http.Requests.Single(q => q.Method == HttpMethod.Patch);
        Assert.Contains("/Core%20Platform/_apis/pipelines/approvals?api-version=7.1-preview.1", patch.RequestUri!.AbsoluteUri);
        using var body = JsonDocument.Parse(http.Bodies[http.Requests.IndexOf(patch)]!);
        var first = body.RootElement[0];
        Assert.Equal("appr-1", first.GetProperty("approvalId").GetString());
        Assert.Equal("approved", first.GetProperty("status").GetString());
        Assert.Equal("ok \"go\"", first.GetProperty("comment").GetString());
    }

    [Theory]
    [InlineData("""{"id":"appr-1","status":"approved","steps":[]}""")]
    [InlineData("""{"id":"appr-1","status":"pending","steps":[{"assignedApprover":{"id":"someone-else"},"status":"pending"}]}""")]
    [InlineData("""{"id":"appr-1","status":"pending","steps":[{"assignedApprover":{"id":"me-id"},"status":"approved"}]}""")]
    public async Task Approve_pipeline_refuses_when_not_mine_anymore(string json)
    {
        var a = Make(Dir().On("/pipelines/approvals/appr-1", json));
        var p = await a.PreviewAsync(Approval(), ItemAction.Reject, null, default);
        Assert.False(p.Allowed);
        Assert.Equal("No longer waiting on you", p.RefusalReason);
    }

    [Fact]
    public async Task Approve_pr_votes_10_as_me_then_comments()
    {
        var http = Dir()
            .On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsoluteUri.Contains("/pullRequests/101?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK,
                """{"status":"active","lastMergeSourceCommit":{"commitId":"s101"}}"""))
            .On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"vote":10}"""))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"id":1}"""));
        var a = Make(http);
        var p = await a.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default);
        Assert.Equal("Approve PR #101 in contoso/Core Platform/api at s101", p.Statement);
        var r = await a.ExecuteAsync(Pr(), ItemAction.ApprovePr, p, "looks good", default);
        Assert.True(r.Ok);
        var put = http.Requests.Single(q => q.Method == HttpMethod.Put);
        Assert.Contains("/repositories/repo-1/pullRequests/101/reviewers/me-id?api-version=7.1", put.RequestUri!.AbsoluteUri);
        using (var b = JsonDocument.Parse(http.Bodies[http.Requests.IndexOf(put)]!)) Assert.Equal(10, b.RootElement.GetProperty("vote").GetInt32());
        var post = http.Requests.Single(q => q.Method == HttpMethod.Post);
        Assert.Contains("/pullRequests/101/threads", post.RequestUri!.AbsoluteUri);
        using var c = JsonDocument.Parse(http.Bodies[http.Requests.IndexOf(post)]!);
        Assert.Equal("looks good", c.RootElement.GetProperty("comments")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Approve_pr_without_comment_does_not_post_thread()
    {
        var http = Dir()
            .On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsoluteUri.Contains("/pullRequests/101?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK,
                """{"status":"active","lastMergeSourceCommit":{"commitId":"s101"}}"""))
            .On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"vote":10}"""));
        var a = Make(http);
        var p = await a.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default);
        await a.ExecuteAsync(Pr(), ItemAction.ApprovePr, p, null, default);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Approve_pr_refuses_on_new_commits_or_completed()
    {
        var a = Make(Dir().On("/pullRequests/101?", """{"status":"active","lastMergeSourceCommit":{"commitId":"other"}}"""));
        Assert.Equal("New commits since you looked — refresh.", (await a.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default)).RefusalReason);
        var b = Make(Dir().On("/pullRequests/101?", """{"status":"completed","lastMergeSourceCommit":{"commitId":"s101"}}"""));
        Assert.Equal("This PR is no longer open", (await b.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default)).RefusalReason);
    }

    [Fact]
    public async Task Execute_403_maps_to_permission()
    {
        var http = Dir()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, PendingForGroup))
            .On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.Forbidden, "{}"));
        var a = Make(http);
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        var r = await a.ExecuteAsync(Approval(), ItemAction.Approve, p, null, default);
        Assert.False(r.Ok);
        Assert.Contains("permission", r.Error);
    }

    [Fact]
    public void Actions_require_ids()
    {
        var a = Make(new FakeHttpHandler());
        Assert.Equal([ItemAction.Approve, ItemAction.Reject], a.ActionsFor(Approval()));
        Assert.Empty(a.ActionsFor(Approval() with { ApprovalId = null }));
        Assert.Empty(a.ActionsFor(Pr() with { RepoId = null }));
    }
}
