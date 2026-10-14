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

public class GitHubMergeAndRerunTests
{
    static InboxItem Mine(ItemKind kind, string sha = "aaa1111") => new("gh:mine:acme/api#12", Provider.GitHub, kind, "acme/api",
        "Add retry", "https://x", "alice", DateTimeOffset.UnixEpoch, sha, 10, null)
    { Number = 12 };
    static InboxItem Ready(string sha = "aaa1111") => Mine(ItemKind.MyPrReadyToMerge, sha);
    static InboxItem Failing(string sha = "aaa1111") => Mine(ItemKind.MyPrCiFailing, sha);

    static string PrJson(string state = "open", string sha = "aaa1111", string mergeable = "true", string mergeableState = "clean",
        string headRepo = "acme/api", string headRef = "feature/retry") => $$$"""
        {"state":"{{{state}}}","mergeable":{{{mergeable}}},"mergeable_state":"{{{mergeableState}}}",
         "head":{"sha":"{{{sha}}}","ref":"{{{headRef}}}","repo":{"full_name":"{{{headRepo}}}"}},
         "base":{"ref":"main","repo":{"full_name":"acme/api"}}
        }
        """;

    static string RepoJson(bool merge = true, bool squash = true, bool rebase = true, bool autoDelete = false) => $$"""
        {"allow_merge_commit":{{(merge ? "true" : "false")}},"allow_squash_merge":{{(squash ? "true" : "false")}},
         "allow_rebase_merge":{{(rebase ? "true" : "false")}},"delete_branch_on_merge":{{(autoDelete ? "true" : "false")}}}
        """;

    static bool Get(HttpRequestMessage r, string pathEnd) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith(pathEnd);

    static FakeHttpHandler MergeHttp(string pr, string repo) => new FakeHttpHandler()
        .On(r => Get(r, "/repos/acme/api/pulls/12"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, pr))
        .On(r => Get(r, "/repos/acme/api"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, repo));

    static (GitHubItemActions, FakeHttpHandler) Make(FakeHttpHandler http) =>
        (new GitHubItemActions(new GitHubClient(new HttpClient(http), new FakeTokenProvider())), http);

    static JsonDocument BodyOf(FakeHttpHandler http, HttpMethod method) =>
        JsonDocument.Parse(http.Bodies[http.Requests.FindIndex(q => q.Method == method)]!);

    [Fact]
    public void Actions_need_number_and_head_sha()
    {
        var (a, _) = Make(new FakeHttpHandler());
        Assert.Equal([ItemAction.Merge], a.ActionsFor(Ready()));
        Assert.Equal([ItemAction.RerunFailed], a.ActionsFor(Failing()));
        Assert.Empty(a.ActionsFor(Ready() with { Number = null }));
        Assert.Empty(a.ActionsFor(Failing() with { HeadSha = null }));
    }

    [Fact]
    public async Task Merge_preview_allowed_has_statement_label_and_options()
    {
        var (a, _) = Make(MergeHttp(PrJson(), RepoJson(rebase: false)));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.True(p.Allowed);
        Assert.Equal("Squash and merge PR #12 into main in acme/api at aaa1111", p.Statement);
        Assert.Equal("Merge PR #12", p.ConfirmLabel);
        Assert.NotNull(p.Options);
        Assert.Equal([MergeMethod.Merge, MergeMethod.Squash], p.Options.Allowed);
        Assert.Equal(MergeMethod.Squash, p.Options.Selected);
        Assert.True(p.Options.CanDeleteBranch);
        Assert.True(p.Options.DeleteBranch);
    }

    [Fact]
    public async Task Merge_preview_selects_first_allowed_when_squash_is_off()
    {
        var (a, _) = Make(MergeHttp(PrJson(), RepoJson(squash: false)));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.Equal(MergeMethod.Merge, p.Options!.Selected);
        Assert.StartsWith("Merge PR #12 into main", p.Statement);
    }

    [Fact]
    public async Task Merge_preview_does_not_default_delete_when_repo_auto_deletes()
    {
        var (a, _) = Make(MergeHttp(PrJson(), RepoJson(autoDelete: true)));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.True(p.Options!.CanDeleteBranch);
        Assert.False(p.Options.DeleteBranch);
    }

    [Fact]
    public async Task Merge_preview_for_fork_cannot_delete_branch()
    {
        var (a, _) = Make(MergeHttp(PrJson(headRepo: "bob/api"), RepoJson()));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.True(p.Allowed);
        Assert.False(p.Options!.CanDeleteBranch);
        Assert.False(p.Options.DeleteBranch);
    }

    [Theory]
    [InlineData("closed", "aaa1111", "true", "clean", "This PR is no longer open")]
    [InlineData("open", "bbb2222", "true", "clean", "New commits since you looked — refresh.")]
    [InlineData("open", "aaa1111", "null", "unknown", "GitHub is still checking mergeability — try again in a moment")]
    [InlineData("open", "aaa1111", "false", "blocked", "Blocked by branch protection")]
    [InlineData("open", "aaa1111", "true", "behind", "Behind the base branch")]
    [InlineData("open", "aaa1111", "false", "dirty", "Merge conflicts")]
    [InlineData("open", "aaa1111", "true", "unstable", "Some checks are not passing")]
    [InlineData("open", "aaa1111", "true", "draft", "Draft PR")]
    [InlineData("open", "aaa1111", "true", "weird", "Not mergeable right now")]
    public async Task Merge_preview_refusals(string state, string sha, string mergeable, string mergeableState, string reason)
    {
        var (a, _) = Make(MergeHttp(PrJson(state, sha, mergeable, mergeableState), RepoJson()));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.False(p.Allowed);
        Assert.Equal(reason, p.RefusalReason);
        Assert.Null(p.Options);
    }

    [Fact]
    public async Task Merge_preview_has_hooks_is_allowed()
    {
        var (a, _) = Make(MergeHttp(PrJson(mergeableState: "has_hooks"), RepoJson()));
        Assert.True((await a.PreviewAsync(Ready(), ItemAction.Merge, null, default)).Allowed);
    }

    [Fact]
    public async Task Merge_preview_refuses_when_no_method_allowed()
    {
        var (a, _) = Make(MergeHttp(PrJson(), RepoJson(false, false, false)));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.Equal("No merge method is allowed on this repo", p.RefusalReason);
    }

    [Fact]
    public async Task Merge_preview_refuses_without_number_or_sha()
    {
        var (a, http) = Make(new FakeHttpHandler());
        var p = await a.PreviewAsync(Ready() with { Number = null }, ItemAction.Merge, null, default);
        Assert.False(p.Allowed);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Merge_execute_puts_sha_and_method_then_deletes_branch()
    {
        var http = MergeHttp(PrJson(), RepoJson())
            .On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"merged":true}"""))
            .On(r => r.Method == HttpMethod.Delete, _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Rebase, true), default);
        Assert.True(r.Ok);
        Assert.Null(r.Error);
        var put = http.Requests.Single(q => q.Method == HttpMethod.Put);
        Assert.Equal("/repos/acme/api/pulls/12/merge", put.RequestUri!.AbsolutePath);
        Assert.Equal("""{"sha":"aaa1111","merge_method":"rebase"}""", http.Bodies[http.Requests.IndexOf(put)]);
        var del = http.Requests.Single(q => q.Method == HttpMethod.Delete);
        Assert.Equal("/repos/acme/api/git/refs/heads/feature/retry", del.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(MergeMethod.Merge, "merge")]
    [InlineData(MergeMethod.Squash, "squash")]
    public async Task Merge_execute_maps_method_names(MergeMethod method, string expected)
    {
        var http = MergeHttp(PrJson(), RepoJson()).On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, method), default);
        using var body = BodyOf(http, HttpMethod.Put);
        Assert.Equal(expected, body.RootElement.GetProperty("merge_method").GetString());
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task Merge_execute_never_deletes_a_fork_branch()
    {
        var http = MergeHttp(PrJson(headRepo: "bob/api"), RepoJson()).On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash, true), default);
        Assert.True(r.Ok);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task Merge_execute_branch_delete_failure_is_ok_with_warning()
    {
        var http = MergeHttp(PrJson(), RepoJson())
            .On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"))
            .On(r => r.Method == HttpMethod.Delete, _ => FakeHttpHandler.Respond(HttpStatusCode.UnprocessableEntity, """{"message":"Reference does not exist"}"""));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash, true), default);
        Assert.True(r.Ok);
        Assert.False(r.Stale);
        Assert.StartsWith("Merged, but the branch couldn't be deleted: ", r.Error);
    }

    [Fact]
    public async Task Merge_execute_with_method_not_allowed_or_missing_is_mismatch_and_sends_nothing()
    {
        var http = MergeHttp(PrJson(), RepoJson(rebase: false)).On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var notAllowed = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Rebase), default);
        var missing = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null), default);
        Assert.False(notAllowed.Ok);
        Assert.Equal("This action no longer matches what was shown — start again", notAllowed.Error);
        Assert.Equal("This action no longer matches what was shown — start again", missing.Error);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Merge_execute_token_mismatch()
    {
        var http = MergeHttp(PrJson(), RepoJson()).On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var input = new ActionInput(null, MergeMethod.Squash);
        Assert.False((await a.ExecuteAsync(Ready() with { Number = 13 }, ItemAction.Merge, p, input, default)).Ok);
        Assert.False((await a.ExecuteAsync(Ready() with { Repo = "acme/web" }, ItemAction.Merge, p, input, default)).Ok);
        Assert.False((await a.ExecuteAsync(Ready(), ItemAction.RerunFailed, p, input, default)).Ok);
        Assert.False((await a.ExecuteAsync(Ready(), ItemAction.Merge, ActionText.Refuse("x"), input, default)).Ok);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Merge_execute_stale_head_does_not_put()
    {
        var pr = PrJson();
        var http = new FakeHttpHandler()
            .On(r => Get(r, "/repos/acme/api/pulls/12"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, pr))
            .On(r => Get(r, "/repos/acme/api"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, RepoJson()))
            .On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        pr = PrJson(sha: "bbb2222");
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash), default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Put);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Merge_execute_409_and_422_are_stale(HttpStatusCode code)
    {
        var http = MergeHttp(PrJson(), RepoJson()).On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(code, """{"message":"Head branch was modified"}"""));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash, true), default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.Equal("This changed since you looked — refreshing", r.Error);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task Merge_execute_timeout_is_maybe_sent()
    {
        var http = MergeHttp(PrJson(), RepoJson()).On(r => r.Method == HttpMethod.Put, _ => throw new TaskCanceledException());
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash), default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.Contains("may have been sent", r.Error);
    }

    const string Runs = """
        {"workflow_runs":[
          {"id":501,"workflow_id":1,"run_number":7,"name":"build","conclusion":"failure"},
          {"id":490,"workflow_id":1,"run_number":6,"name":"build","conclusion":"failure"},
          {"id":502,"workflow_id":2,"run_number":3,"name":"e2e","conclusion":"timed_out"},
          {"id":503,"workflow_id":3,"run_number":9,"name":"lint","conclusion":"success"},
          {"id":480,"workflow_id":4,"run_number":1,"name":"docs","conclusion":"failure"},
          {"id":504,"workflow_id":4,"run_number":2,"name":"docs","conclusion":"success"}
        ]}
        """;

    static FakeHttpHandler RerunHttp(string runs = Runs, string prSha = "aaa1111") => new FakeHttpHandler()
        .On(r => Get(r, "/repos/acme/api/pulls/12"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, PrJson(sha: prSha)))
        .On(r => Get(r, "/repos/acme/api/actions/runs"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, runs));

    [Fact]
    public async Task Rerun_preview_keeps_latest_failed_run_per_workflow()
    {
        var (a, http) = Make(RerunHttp());
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.True(p.Allowed);
        Assert.Equal("Re-run failed jobs in 2 workflows on #12 (acme/api): build, e2e", p.Statement);
        Assert.Equal("Re-run 2 workflows", p.ConfirmLabel);
        Assert.Null(p.Options);
        var q = http.Requests.Single(x => x.RequestUri!.AbsolutePath.EndsWith("/actions/runs")).RequestUri!.Query;
        Assert.Equal("?head_sha=aaa1111&per_page=50", q);
    }

    [Fact]
    public async Task Rerun_preview_single_workflow_is_singular()
    {
        var (a, _) = Make(RerunHttp("""{"workflow_runs":[{"id":501,"workflow_id":1,"run_number":7,"name":"build","conclusion":"cancelled"}]}"""));
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.Equal("Re-run failed jobs in 1 workflow on #12 (acme/api): build", p.Statement);
        Assert.Equal("Re-run 1 workflow", p.ConfirmLabel);
    }

    [Fact]
    public async Task Rerun_preview_refusals()
    {
        var (a, _) = Make(RerunHttp("""{"workflow_runs":[{"id":503,"workflow_id":3,"run_number":9,"name":"lint","conclusion":"success"}]}"""));
        var none = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.False(none.Allowed);
        Assert.StartsWith("No failed GitHub Actions runs at this commit", none.RefusalReason);
        Assert.EndsWith("re-run them in the CI system that reported them", none.RefusalReason);

        var (b, _) = Make(RerunHttp(prSha: "bbb2222"));
        Assert.Equal("New commits since you looked — refresh.", (await b.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default)).RefusalReason);
    }

    [Fact]
    public async Task Rerun_execute_posts_each_run_with_empty_body()
    {
        var http = RerunHttp().On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.Created, "{}"));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.True(r.Ok);
        Assert.Null(r.Error);
        var posts = http.Requests.Where(q => q.Method == HttpMethod.Post).ToList();
        Assert.Equal(["/repos/acme/api/actions/runs/501/rerun-failed-jobs", "/repos/acme/api/actions/runs/502/rerun-failed-jobs"],
            posts.Select(q => q.RequestUri!.AbsolutePath));
        Assert.All(posts, q => Assert.Equal("{}", http.Bodies[http.Requests.IndexOf(q)]));
    }

    [Fact]
    public async Task Rerun_execute_partial_failure_is_ok_with_warning()
    {
        var http = RerunHttp()
            .On(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/runs/502/"), _ => FakeHttpHandler.Respond(HttpStatusCode.Forbidden, """{"message":"nope"}"""))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.Created, "{}"));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.True(r.Ok);
        Assert.Equal("Re-run started, but couldn't re-run: e2e", r.Error);
    }

    [Fact]
    public async Task Rerun_execute_all_failed_is_not_ok()
    {
        var http = RerunHttp().On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.Forbidden, """{"message":"nope"}"""));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.False(r.Ok);
        Assert.Contains("permission", r.Error);
    }

    [Fact]
    public async Task Rerun_execute_stale_head_posts_nothing()
    {
        var sha = "aaa1111";
        var http = new FakeHttpHandler()
            .On(r => Get(r, "/repos/acme/api/pulls/12"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, PrJson(sha: sha)))
            .On(r => Get(r, "/repos/acme/api/actions/runs"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, Runs))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.Created, "{}"));
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        sha = "bbb2222";
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.True(r.Stale);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Rerun_execute_token_mismatch_and_timeout()
    {
        var http = RerunHttp().On(r => r.Method == HttpMethod.Post, _ => throw new TaskCanceledException());
        var (a, _) = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.False((await a.ExecuteAsync(Failing() with { Number = 99 }, ItemAction.RerunFailed, p, default(ActionInput), default)).Ok);
        Assert.False((await a.ExecuteAsync(Failing(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash), default)).Ok);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Post);
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.Contains("may have been sent", r.Error);
    }
}
