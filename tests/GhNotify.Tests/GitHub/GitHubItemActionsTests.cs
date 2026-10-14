using System.Net;
using System.Text.Json;
using GhNotify.Core;
using GhNotify.GitHub;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.GitHub;

public class GitHubItemActionsTests
{
    static InboxItem Approval() => new("gh:approval:acme/deploy/runs/9001", Provider.GitHub, ItemKind.ApprovalPending, "acme/deploy",
        "Release", "https://x", "carol", DateTimeOffset.UnixEpoch, "abc999", null, null)
    { RunId = 9001 };
    static InboxItem Pr(string sha = "aaa1111") => new("gh:review:acme/api#12", Provider.GitHub, ItemKind.ReviewRequested, "acme/api",
        "Add retry", "https://x", "alice", DateTimeOffset.UnixEpoch, sha, 10, null)
    { Number = 12 };

    const string Pending = """
        [ { "environment": { "id": 11, "name": "production" }, "current_user_can_approve": true },
          { "environment": { "id": 12, "name": "staging" }, "current_user_can_approve": false },
          { "environment": { "id": 13, "name": "dr" }, "current_user_can_approve": true } ]
        """;

    static (GitHubItemActions, FakeHttpHandler) Make(FakeHttpHandler http) =>
        (new GitHubItemActions(new GitHubClient(new HttpClient(http), new FakeTokenProvider())), http);

    [Fact]
    public async Task Approve_preview_names_only_approvable_envs_and_execute_posts_their_ids()
    {
        var (a, http) = Make(new FakeHttpHandler()
            .On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/runs/9001/pending_deployments"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, Pending))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "[]")));
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        Assert.True(p.Allowed);
        Assert.Equal("Approve deployment of acme/deploy run 9001 to production, dr", p.Statement);
        Assert.Equal("Approve production, dr", p.ConfirmLabel);

        var r = await a.ExecuteAsync(Approval(), ItemAction.Approve, p, "LGTM \"ship\"\nit", default);
        Assert.True(r.Ok);
        var post = http.Requests.Single(q => q.Method == HttpMethod.Post);
        Assert.Equal("/repos/acme/deploy/actions/runs/9001/pending_deployments", post.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(http.Bodies[http.Requests.IndexOf(post)]!);
        Assert.Equal([11L, 13L], body.RootElement.GetProperty("environment_ids").EnumerateArray().Select(e => e.GetInt64()));
        Assert.Equal("approved", body.RootElement.GetProperty("state").GetString());
        Assert.Equal("LGTM \"ship\"\nit", body.RootElement.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task Reject_sends_rejected_with_empty_comment()
    {
        var (a, http) = Make(new FakeHttpHandler()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, Pending))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "[]")));
        var p = await a.PreviewAsync(Approval(), ItemAction.Reject, null, default);
        Assert.Equal("Reject production, dr", p.ConfirmLabel);
        await a.ExecuteAsync(Approval(), ItemAction.Reject, p, null, default);
        using var body = JsonDocument.Parse(http.Bodies[http.Requests.FindIndex(q => q.Method == HttpMethod.Post)]!);
        Assert.Equal("rejected", body.RootElement.GetProperty("state").GetString());
        Assert.Equal("", body.RootElement.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task Approve_preview_refuses_when_nothing_left()
    {
        var (a, _) = Make(new FakeHttpHandler().On("pending_deployments", """[ { "environment": { "id": 12, "name": "staging" }, "current_user_can_approve": false } ]"""));
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        Assert.False(p.Allowed);
        Assert.Equal("No longer waiting on you", p.RefusalReason);
    }

    [Fact]
    public async Task Approve_pr_checks_head_and_posts_review()
    {
        var (a, http) = Make(new FakeHttpHandler()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"state":"open","head":{"sha":"aaa1111"}}"""))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"id":1}""")));
        var p = await a.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default);
        Assert.True(p.Allowed);
        Assert.Equal("Approve PR #12 in acme/api at aaa1111", p.Statement);
        Assert.Equal("Approve PR #12", p.ConfirmLabel);
        Assert.Equal(ActionText.NoPreReview, p.Warning);
        var r = await a.ExecuteAsync(Pr(), ItemAction.ApprovePr, p, "  ", default);
        Assert.True(r.Ok);
        var post = http.Requests.Single(q => q.Method == HttpMethod.Post);
        Assert.Equal("/repos/acme/api/pulls/12/reviews", post.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(http.Bodies[http.Requests.IndexOf(post)]!);
        Assert.Equal("APPROVE", body.RootElement.GetProperty("event").GetString());
        Assert.Equal("aaa1111", body.RootElement.GetProperty("commit_id").GetString());
        Assert.False(body.RootElement.TryGetProperty("body", out _));
    }

    [Fact]
    public async Task Approve_pr_refuses_on_new_commits_or_closed()
    {
        var (a, _) = Make(new FakeHttpHandler().On("/pulls/12", """{"state":"open","head":{"sha":"bbb2222"}}"""));
        Assert.Equal("New commits since you looked — refresh.", (await a.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default)).RefusalReason);
        var (b, _) = Make(new FakeHttpHandler().On("/pulls/12", """{"state":"closed","head":{"sha":"aaa1111"}}"""));
        Assert.Equal("This PR is no longer open", (await b.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default)).RefusalReason);
    }

    [Fact]
    public async Task Ready_pre_review_at_head_has_no_warning()
    {
        var (a, _) = Make(new FakeHttpHandler().On("/pulls/12", """{"state":"open","head":{"sha":"aaa1111"}}"""));
        var run = new AgentRun { Key = "k", Profile = "peer-review", State = AgentState.Ready, HeadSha = "aaa1111" };
        Assert.Null((await a.PreviewAsync(Pr(), ItemAction.ApprovePr, run, default)).Warning);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, true)]
    public async Task Execute_maps_errors(HttpStatusCode code, bool stale)
    {
        var (a, _) = Make(new FakeHttpHandler()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, Pending))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(code, """{"message":"nope"}""")));
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        var r = await a.ExecuteAsync(Approval(), ItemAction.Approve, p, null, default);
        Assert.False(r.Ok);
        Assert.Equal(stale, r.Stale);
    }

    [Fact]
    public void Actions_require_ids()
    {
        var a = new GitHubItemActions(new GitHubClient(new HttpClient(new FakeHttpHandler()), new FakeTokenProvider()));
        Assert.Equal([ItemAction.Approve, ItemAction.Reject], a.ActionsFor(Approval()));
        Assert.Empty(a.ActionsFor(Approval() with { RunId = null }));
        Assert.Equal([ItemAction.ApprovePr], a.ActionsFor(Pr()));
    }

    [Fact]
    public async Task Execute_refuses_refused_preview_without_posting()
    {
        var (a, http) = Make(new FakeHttpHandler().On("/pulls/12", """{"state":"open","head":{"sha":"aaa1111"}}"""));
        var r = await a.ExecuteAsync(Pr(), ItemAction.ApprovePr, ActionText.Refuse("x"), null, default);
        Assert.False(r.Ok);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Execute_refuses_mismatched_action_or_item()
    {
        var (a, http) = Make(new FakeHttpHandler()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, Pending))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "[]")));
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        Assert.False((await a.ExecuteAsync(Approval(), ItemAction.Reject, p, null, default)).Ok);
        Assert.False((await a.ExecuteAsync(Approval() with { RunId = 9002 }, ItemAction.Approve, p, null, default)).Ok);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Execute_rechecks_pr_head()
    {
        var head = "aaa1111";
        var (a, http) = Make(new FakeHttpHandler()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, $$$"""{"state":"open","head":{"sha":"{{{head}}}"}}"""))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}")));
        var p = await a.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default);
        head = "bbb2222";
        var r = await a.ExecuteAsync(Pr(), ItemAction.ApprovePr, p, null, default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Execute_rechecks_deploy_envs()
    {
        var json = Pending;
        var (a, http) = Make(new FakeHttpHandler()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, json))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "[]")));
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        json = """[ { "environment": { "id": 11, "name": "production" }, "current_user_can_approve": true } ]""";
        var r = await a.ExecuteAsync(Approval(), ItemAction.Approve, p, null, default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Execute_timeout_is_reported_as_maybe_sent()
    {
        var (a, _) = Make(new FakeHttpHandler()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, Pending))
            .On(r => r.Method == HttpMethod.Post, _ => throw new TaskCanceledException()));
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        var r = await a.ExecuteAsync(Approval(), ItemAction.Approve, p, null, default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.Contains("may have been sent", r.Error);
    }
}
