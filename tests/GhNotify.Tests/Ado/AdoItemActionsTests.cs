using System.Net;
using System.Text.Json;
using GhNotify.Ado;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Ado;

public class AdoItemActionsTests
{
    static InboxItem Approval() => new("ado:approval:contoso/Core Platform/appr-1", Provider.Ado, ItemKind.ApprovalPending, "contoso/Core Platform",
        "api-release 20261013.3", "https://x", "", DateTimeOffset.UnixEpoch, null, null, null)
    { Org = "contoso", Project = "Core Platform", ApprovalId = "appr-1" };
    static InboxItem Pr(string sha = "s101") => new("ado:review:contoso/Core Platform/api#101", Provider.Ado, ItemKind.ReviewRequested, "contoso/Core Platform/api",
        "Direct review", "https://x", "Alice", DateTimeOffset.UnixEpoch, sha, null, null)
    { Org = "contoso", Project = "Core Platform", RepoId = "repo-1", Number = 101 };

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

public class AdoItemActionsSafetyTests
{
    static InboxItem Approval() => new("ado:approval:contoso/Core Platform/appr-1", Provider.Ado, ItemKind.ApprovalPending, "contoso/Core Platform",
        "api-release 1", "https://x", "", DateTimeOffset.UnixEpoch, null, null, null)
    { Org = "contoso", Project = "Core Platform", ApprovalId = "appr-1" };
    static InboxItem Pr() => new("ado:review:contoso/Core Platform/api#101", Provider.Ado, ItemKind.ReviewRequested, "contoso/Core Platform/api",
        "Direct review", "https://x", "Alice", DateTimeOffset.UnixEpoch, "s101", null, null)
    { Org = "contoso", Project = "Core Platform", RepoId = "repo-1", Number = 101 };
    static FakeHttpHandler Dir() => new FakeHttpHandler()
        .On("connectionData", Fixture.Read("ado-connection.json"))
        .On("queryMembership=expanded", Fixture.Read("ado-identity-expanded.json"))
        .On("identities?descriptors=", Fixture.Read("ado-identities-groups.json"));
    static AdoItemActions Make(FakeHttpHandler http)
    {
        var c = new AdoClient(new HttpClient(http), new FakeTokenProvider());
        return new AdoItemActions(c, new AdoDirectory(c));
    }
    const string Pending = """{"id":"appr-1","status":"pending","steps":[{"assignedApprover":{"id":"group-a"},"status":"pending"}]}""";
    const string Completed = """{"id":"appr-1","status":"approved","steps":[]}""";
    const string ActivePr = """{"status":"active","lastMergeSourceCommit":{"commitId":"s101"}}""";

    [Fact]
    public async Task Preview_404_gives_stale_text()
    {
        var a = Make(Dir().On("/pullRequests/101?", "{}", HttpStatusCode.NotFound));
        var p = await a.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default);
        Assert.Equal("This changed since you looked — refreshing", p.RefusalReason);
    }

    [Fact]
    public async Task Comment_failure_after_vote_is_still_ok()
    {
        var http = Dir()
            .On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsoluteUri.Contains("/pullRequests/101?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, ActivePr))
            .On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"))
            .On(r => r.Method == HttpMethod.Post, _ => FakeHttpHandler.Respond(HttpStatusCode.InternalServerError, "{}"));
        var a = Make(http);
        var p = await a.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default);
        var r = await a.ExecuteAsync(Pr(), ItemAction.ApprovePr, p, "hi", default);
        Assert.True(r.Ok);
        Assert.StartsWith("Approved, but the comment couldn't be posted", r.Error);
    }

    [Fact]
    public async Task Execute_with_refused_preview_or_wrong_action_sends_nothing()
    {
        var http = Dir().On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, Pending))
            .On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var a = Make(http);
        var r1 = await a.ExecuteAsync(Approval(), ItemAction.Approve, ActionText.Refuse("nope"), null, default);
        Assert.False(r1.Ok);
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        var r2 = await a.ExecuteAsync(Approval(), ItemAction.Reject, p, null, default);
        Assert.False(r2.Ok);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Pr_head_changing_before_execute_is_stale_and_not_voted()
    {
        var pr = ActivePr;
        var http = Dir()
            .On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsoluteUri.Contains("/pullRequests/101?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, pr))
            .On(r => r.Method == HttpMethod.Put, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var a = Make(http);
        var p = await a.PreviewAsync(Pr(), ItemAction.ApprovePr, null, default);
        pr = """{"status":"active","lastMergeSourceCommit":{"commitId":"newer"}}""";
        var r = await a.ExecuteAsync(Pr(), ItemAction.ApprovePr, p, null, default);
        Assert.True(r.Stale);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Approval_completed_before_execute_is_stale_and_not_patched()
    {
        var json = Pending;
        var http = Dir()
            .On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsoluteUri.Contains("/pipelines/"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, json))
            .On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var a = Make(http);
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        json = Completed;
        var r = await a.ExecuteAsync(Approval(), ItemAction.Approve, p, null, default);
        Assert.True(r.Stale);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Patch_timeout_is_stale_with_may_have_been_sent()
    {
        var http = Dir()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, Pending))
            .On(r => r.Method == HttpMethod.Patch, _ => throw new TaskCanceledException("timeout"));
        var a = Make(http);
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        var r = await a.ExecuteAsync(Approval(), ItemAction.Approve, p, null, default);
        Assert.True(r.Stale);
        Assert.Contains("may have been sent", r.Error);
    }

    [Fact]
    public async Task Patch_2xx_with_unparseable_body_is_stale_with_may_have_been_sent()
    {
        var http = Dir()
            .On(r => r.Method == HttpMethod.Get, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, Pending))
            .On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "<html>proxy</html>"));
        var a = Make(http);
        var p = await a.PreviewAsync(Approval(), ItemAction.Approve, null, default);
        var r = await a.ExecuteAsync(Approval(), ItemAction.Approve, p, null, default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.Equal("Unexpected response — it may have been sent. Refresh to check.", r.Error);
    }
}

public class AdoMergeAndRerunTests
{
    static InboxItem Mine(ItemKind kind, string sha = "s42") => new("ado:mine:contoso/Core Platform/web#42", Provider.Ado, kind, "contoso/Core Platform/web",
        "Add retry", "https://x", "Alice", DateTimeOffset.UnixEpoch, sha, null, null)
    { Org = "contoso", Project = "Core Platform", RepoId = "repo-1", Number = 42 };
    static InboxItem Ready(string sha = "s42") => Mine(ItemKind.MyPrReadyToMerge, sha);
    static InboxItem Failing(string sha = "s42") => Mine(ItemKind.MyPrCiFailing, sha);

    static FakeHttpHandler Dir() => new FakeHttpHandler()
        .On("connectionData", Fixture.Read("ado-connection.json"))
        .On("queryMembership=expanded", Fixture.Read("ado-identity-expanded.json"))
        .On("identities?descriptors=", Fixture.Read("ado-identities-groups.json"));

    static AdoItemActions Make(FakeHttpHandler http)
    {
        var c = new AdoClient(new HttpClient(http), new FakeTokenProvider());
        return new AdoItemActions(c, new AdoDirectory(c));
    }

    static string PrJson(string status = "active", string sha = "s42", string mergeStatus = "succeeded", bool draft = false) => $$$"""
        {"status":"{{{status}}}","isDraft":{{{(draft ? "true" : "false")}}},"mergeStatus":"{{{mergeStatus}}}",
         "lastMergeSourceCommit":{"commitId":"{{{sha}}}"},"targetRefName":"refs/heads/main",
         "repository":{"id":"repo-1","project":{"id":"proj-1"}}
        }
        """;

    const string BuildType = "0609b952-1397-4640-95ec-e00a01b2c241";
    const string StrategyType = "fa4e907d-c16b-4a4c-9dfa-4916e5d171ab";
    const string NoPolicies = """{"value":[]}""";
    const string NoEvals = """{"value":[]}""";

    static string Strategy(bool noFf, bool squash, bool rebase, string refName = "refs/heads/main", string repo = "repo-1") => $$$"""
        {"value":[{"isEnabled":true,"isDeleted":false,"type":{"id":"{{{StrategyType}}}"},
          "settings":{"allowNoFastForward":{{{(noFf ? "true" : "false")}}},"allowSquash":{{{(squash ? "true" : "false")}}},"allowRebase":{{{(rebase ? "true" : "false")}}},
                      "scope":[{"repositoryId":"{{{repo}}}","refName":"{{{refName}}}","matchKind":"Exact"}]}}]}
        """;

    static bool Is(HttpRequestMessage r, HttpMethod m, string contains) => r.Method == m && r.RequestUri!.AbsoluteUri.Contains(contains, StringComparison.Ordinal);

    static FakeHttpHandler MergeHttp(string pr, string policies = NoPolicies, string evals = NoEvals) => Dir()
        .On(r => Is(r, HttpMethod.Get, "/pullRequests/42?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, pr))
        .On(r => Is(r, HttpMethod.Get, "/policy/evaluations?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, evals))
        .On(r => Is(r, HttpMethod.Get, "/policy/configurations?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, policies));

    [Fact]
    public void Actions_need_repo_number_and_head_sha()
    {
        var a = Make(new FakeHttpHandler());
        Assert.Equal([ItemAction.Merge], a.ActionsFor(Ready()));
        Assert.Equal([ItemAction.RerunFailed], a.ActionsFor(Failing()));
        Assert.Empty(a.ActionsFor(Ready() with { RepoId = null }));
        Assert.Empty(a.ActionsFor(Ready() with { Number = null }));
        Assert.Empty(a.ActionsFor(Failing() with { HeadSha = null }));
        Assert.Empty(a.ActionsFor(Ready() with { Org = null }));
    }

    [Fact]
    public async Task Merge_preview_allowed_with_no_strategy_policy_offers_all_three()
    {
        var a = Make(MergeHttp(PrJson()));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.True(p.Allowed);
        Assert.Equal("Squash and merge PR #42 into main in contoso/Core Platform/web at s42", p.Statement);
        Assert.Equal("Merge PR #42", p.ConfirmLabel);
        Assert.Equal([MergeMethod.Merge, MergeMethod.Squash, MergeMethod.Rebase], p.Options!.Allowed);
        Assert.Equal(MergeMethod.Squash, p.Options.Selected);
        Assert.True(p.Options.CanDeleteBranch);
        Assert.True(p.Options.DeleteBranch);
    }

    [Fact]
    public async Task Merge_preview_honours_the_require_merge_strategy_policy()
    {
        var a = Make(MergeHttp(PrJson(), Strategy(noFf: true, squash: false, rebase: true)));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.Equal([MergeMethod.Merge, MergeMethod.Rebase], p.Options!.Allowed);
        Assert.Equal(MergeMethod.Merge, p.Options.Selected);
        Assert.StartsWith("Merge PR #42 into main", p.Statement);
    }

    [Fact]
    public async Task Merge_preview_ignores_strategy_policies_scoped_elsewhere()
    {
        var a = Make(MergeHttp(PrJson(), Strategy(false, true, false, refName: "refs/heads/release")));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.Equal(3, p.Options!.Allowed.Count);
    }

    [Fact]
    public async Task Merge_preview_refuses_when_policy_allows_nothing()
    {
        var a = Make(MergeHttp(PrJson(), Strategy(false, false, false)));
        Assert.False((await a.PreviewAsync(Ready(), ItemAction.Merge, null, default)).Allowed);
    }

    [Theory]
    [InlineData("completed", "s42", "succeeded", false, "This PR is no longer open")]
    [InlineData("active", "other", "succeeded", false, "New commits since you looked — refresh.")]
    [InlineData("active", "s42", "conflicts", false, "Merge conflicts")]
    [InlineData("active", "s42", "queued", false, "Azure DevOps is still checking mergeability — try again in a moment")]
    [InlineData("active", "s42", "succeeded", true, "Draft PR")]
    public async Task Merge_preview_refusals(string status, string sha, string mergeStatus, bool draft, string reason)
    {
        var a = Make(MergeHttp(PrJson(status, sha, mergeStatus, draft)));
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.False(p.Allowed);
        Assert.Equal(reason, p.RefusalReason);
        Assert.Null(p.Options);
    }

    [Theory]
    [InlineData("""{"value":[{"status":"rejected","configuration":{"isBlocking":false}}]}""", "Some policies are not passing")]
    [InlineData("""{"value":[{"status":"running","configuration":{"isBlocking":true}}]}""", "Policies are still running")]
    [InlineData("""{"value":[{"status":"queued","configuration":{"isBlocking":true}}]}""", "Policies are still running")]
    public async Task Merge_preview_refuses_on_blocking_policy_state(string evals, string reason)
    {
        var a = Make(MergeHttp(PrJson(), evals: evals));
        Assert.Equal(reason, (await a.PreviewAsync(Ready(), ItemAction.Merge, null, default)).RefusalReason);
    }

    [Fact]
    public async Task Merge_preview_allows_non_blocking_running_policy()
    {
        var a = Make(MergeHttp(PrJson(), evals: """{"value":[{"status":"running","configuration":{"isBlocking":false}},{"status":"approved","configuration":{"isBlocking":true}}]}"""));
        Assert.True((await a.PreviewAsync(Ready(), ItemAction.Merge, null, default)).Allowed);
    }

    [Fact]
    public async Task Merge_preview_queries_evaluations_by_pr_artifact()
    {
        var http = MergeHttp(PrJson());
        await Make(http).PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.Contains(http.Requests, q => q.RequestUri!.AbsoluteUri.Contains(
            "/policy/evaluations?artifactId=vstfs%3A%2F%2F%2FCodeReview%2FCodeReviewId%2Fproj-1%2F42", StringComparison.Ordinal));
        Assert.Contains(http.Requests, q => q.RequestUri!.AbsoluteUri.Contains($"policyType={StrategyType}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MergeMethod.Merge, "noFastForward", true)]
    [InlineData(MergeMethod.Squash, "squash", false)]
    [InlineData(MergeMethod.Rebase, "rebase", true)]
    public async Task Merge_execute_patches_exact_body(MergeMethod method, string strategy, bool delete)
    {
        var http = MergeHttp(PrJson()).On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"status":"completed"}"""));
        var a = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, method, delete), default);
        Assert.True(r.Ok);
        Assert.Null(r.Error);
        var patch = http.Requests.Single(q => q.Method == HttpMethod.Patch);
        Assert.Contains("/Core%20Platform/_apis/git/repositories/repo-1/pullRequests/42?api-version=7.1", patch.RequestUri!.AbsoluteUri);
        Assert.Equal(
            """{"status":"completed","lastMergeSourceCommit":{"commitId":"s42"},"completionOptions":{"mergeStrategy":"""
            + $"\"{strategy}\",\"deleteSourceBranch\":{(delete ? "true" : "false")}" + "}}",
            http.Bodies[http.Requests.IndexOf(patch)]);
    }

    [Fact]
    public async Task Merge_execute_method_not_allowed_or_missing_is_mismatch()
    {
        var http = MergeHttp(PrJson(), Strategy(true, true, false)).On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"status":"completed"}"""));
        var a = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var rebase = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Rebase), default);
        var none = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null), default);
        Assert.Equal("This action no longer matches what was shown — start again", rebase.Error);
        Assert.False(none.Ok);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Merge_execute_token_mismatch()
    {
        var http = MergeHttp(PrJson()).On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, """{"status":"completed"}"""));
        var a = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var input = new ActionInput(null, MergeMethod.Squash);
        Assert.False((await a.ExecuteAsync(Ready() with { Number = 43 }, ItemAction.Merge, p, input, default)).Ok);
        Assert.False((await a.ExecuteAsync(Ready() with { HeadSha = "other" }, ItemAction.Merge, p, input, default)).Ok);
        Assert.False((await a.ExecuteAsync(Ready(), ItemAction.RerunFailed, p, input, default)).Ok);
        Assert.False((await a.ExecuteAsync(Ready(), ItemAction.Merge, ActionText.Refuse("x"), input, default)).Ok);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Patch);
    }

    [Theory]
    [InlineData("other", "succeeded")]
    [InlineData("s42", "conflicts")]
    public async Task Merge_execute_rechecks_head_and_merge_status(string sha, string mergeStatus)
    {
        var pr = PrJson();
        var http = MergeHttp(pr).On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        // Preview sees the good PR; the re-read before writing sees the changed one.
        var calls = 0;
        var changed = PrJson(sha: sha, mergeStatus: mergeStatus);
        var live = new FakeHttpHandler()
            .On("connectionData", Fixture.Read("ado-connection.json"))
            .On(r => Is(r, HttpMethod.Get, "/pullRequests/42?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, calls++ == 0 ? pr : changed))
            .On(r => Is(r, HttpMethod.Get, "/policy/evaluations?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, NoEvals))
            .On(r => Is(r, HttpMethod.Get, "/policy/configurations?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, NoPolicies))
            .On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var a = Make(live);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        Assert.True(p.Allowed);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash), default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.DoesNotContain(live.Requests, q => q.Method == HttpMethod.Patch);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Patch);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Merge_execute_409_and_422_are_stale(HttpStatusCode code)
    {
        var http = MergeHttp(PrJson()).On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(code, """{"message":"moved"}"""));
        var a = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash), default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.Equal("This changed since you looked — refreshing", r.Error);
    }

    [Fact]
    public async Task Merge_execute_timeout_is_maybe_sent()
    {
        var http = MergeHttp(PrJson()).On(r => r.Method == HttpMethod.Patch, _ => throw new TaskCanceledException("timeout"));
        var a = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash), default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.Contains("may have been sent", r.Error);
    }

    static string Eval(string id, string name, string status = "rejected", string type = BuildType) =>
        $$$"""{"evaluationId":"{{{id}}}","status":"{{{status}}}","configuration":{"type":{"id":"{{{type}}}"},"settings":{"displayName":"{{{name}}}"} } }""";

    static FakeHttpHandler RerunHttp(string evals, string pr = "") => Dir()
        .On(r => Is(r, HttpMethod.Get, "/pullRequests/42?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, pr.Length > 0 ? pr : PrJson()))
        .On(r => Is(r, HttpMethod.Get, "/policy/evaluations?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, evals));

    [Fact]
    public async Task Rerun_preview_lists_failed_build_policies_only()
    {
        var evals = $$"""{"value":[{{Eval("e-1", "CI")}},{{Eval("e-2", "Lint", "approved")}},{{Eval("e-3", "Reviewers", "rejected", "fd2167ab-b0be-447a-8ec8-39368250530e")}}]}""";
        var a = Make(RerunHttp(evals));
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.True(p.Allowed);
        Assert.Equal("Re-queue 1 failed build policy on PR #42 (contoso/Core Platform/web): CI", p.Statement);
        Assert.Equal("Re-queue 1 build", p.ConfirmLabel);
        Assert.Null(p.Options);
    }

    [Fact]
    public async Task Rerun_preview_names_several_policies()
    {
        var evals = $$"""{"value":[{{Eval("e-1", "CI")}},{{Eval("e-2", "E2E", "broken")}}]}""";
        var a = Make(RerunHttp(evals));
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.Equal("Re-queue 2 failed build policies on PR #42 (contoso/Core Platform/web): CI, E2E", p.Statement);
        Assert.Equal("Re-queue 2 builds", p.ConfirmLabel);
    }

    [Fact]
    public async Task Rerun_preview_refusals()
    {
        var none = await Make(RerunHttp($$"""{"value":[{{Eval("e-2", "Lint", "approved")}}]}""")).PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.False(none.Allowed);
        Assert.Equal("No failed build policies to re-queue", none.RefusalReason);
        var moved = await Make(RerunHttp(NoEvals, PrJson(sha: "other"))).PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.Equal("New commits since you looked — refresh.", moved.RefusalReason);
        var closed = await Make(RerunHttp(NoEvals, PrJson(status: "abandoned"))).PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.Equal("This PR is no longer open", closed.RefusalReason);
    }

    [Fact]
    public async Task Rerun_execute_patches_each_evaluation_without_body()
    {
        var evals = $$"""{"value":[{{Eval("e-1", "CI")}},{{Eval("e-2", "E2E")}}]}""";
        var http = RerunHttp(evals).On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var a = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.True(r.Ok);
        Assert.Null(r.Error);
        var patches = http.Requests.Where(q => q.Method == HttpMethod.Patch).ToList();
        Assert.Equal(
            ["https://dev.azure.com/contoso/Core%20Platform/_apis/policy/evaluations/e-1?api-version=7.1-preview.1",
             "https://dev.azure.com/contoso/Core%20Platform/_apis/policy/evaluations/e-2?api-version=7.1-preview.1"],
            patches.Select(q => q.RequestUri!.AbsoluteUri));
        Assert.All(patches, q => Assert.Null(http.Bodies[http.Requests.IndexOf(q)]));
    }

    [Fact]
    public async Task Rerun_execute_partial_failure_is_ok_with_warning()
    {
        var evals = $$"""{"value":[{{Eval("e-1", "CI")}},{{Eval("e-2", "E2E")}}]}""";
        var http = RerunHttp(evals)
            .On(r => Is(r, HttpMethod.Patch, "/evaluations/e-2"), _ => FakeHttpHandler.Respond(HttpStatusCode.InternalServerError, "{}"))
            .On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var a = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.True(r.Ok);
        Assert.Equal("Re-queue started, but couldn't re-queue: E2E", r.Error);
    }

    [Fact]
    public async Task Rerun_execute_rate_limit_after_a_success_is_ok_with_warning()
    {
        var evals = $$"""{"value":[{{Eval("e-1", "CI")}},{{Eval("e-2", "E2E")}}]}""";
        var http = RerunHttp(evals)
            .On(r => Is(r, HttpMethod.Patch, "/evaluations/e-2"), _ => FakeHttpHandler.Respond(HttpStatusCode.TooManyRequests, "{}"))
            .On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var a = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.True(r.Ok);
        Assert.Equal("Re-queue started, but couldn't re-queue: E2E", r.Error);
    }

    [Theory]
    [InlineData("""{"status":"completed","mergeStatus":"succeeded"}""", true, null)]
    [InlineData("""{"status":"active","mergeStatus":"succeeded"}""", true, "Merge queued — check the PR")]
    [InlineData("""{"status":"active","mergeStatus":"conflicts","mergeFailureMessage":"policy"}""", false, "Merge failed: policy")]
    [InlineData("""{"status":"active","mergeStatus":"rejectedByPolicy"}""", false, "Merge failed — check the PR")]
    public async Task Merge_execute_inspects_the_patch_response(string response, bool ok, string? error)
    {
        var http = MergeHttp(PrJson()).On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, response));
        var a = Make(http);
        var p = await a.PreviewAsync(Ready(), ItemAction.Merge, null, default);
        var r = await a.ExecuteAsync(Ready(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash), default);
        Assert.Equal(ok, r.Ok);
        Assert.Equal(error, r.Error);
    }

    [Fact]
    public async Task Rerun_execute_all_failed_is_not_ok()
    {
        var http = RerunHttp($$"""{"value":[{{Eval("e-1", "CI")}}]}""").On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.Forbidden, "{}"));
        var a = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.False(r.Ok);
        Assert.Contains("permission", r.Error);
    }

    [Fact]
    public async Task Rerun_execute_stale_head_patches_nothing()
    {
        var pr = PrJson();
        var http = Dir()
            .On(r => Is(r, HttpMethod.Get, "/pullRequests/42?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, pr))
            .On(r => Is(r, HttpMethod.Get, "/policy/evaluations?"), _ => FakeHttpHandler.Respond(HttpStatusCode.OK, $$"""{"value":[{{Eval("e-1", "CI")}}]}"""))
            .On(r => r.Method == HttpMethod.Patch, _ => FakeHttpHandler.Respond(HttpStatusCode.OK, "{}"));
        var a = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        pr = PrJson(sha: "newer");
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.True(r.Stale);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Rerun_execute_token_mismatch_and_timeout()
    {
        var http = RerunHttp($$"""{"value":[{{Eval("e-1", "CI")}}]}""").On(r => r.Method == HttpMethod.Patch, _ => throw new TaskCanceledException("timeout"));
        var a = Make(http);
        var p = await a.PreviewAsync(Failing(), ItemAction.RerunFailed, null, default);
        Assert.False((await a.ExecuteAsync(Failing() with { Number = 43 }, ItemAction.RerunFailed, p, default(ActionInput), default)).Ok);
        Assert.False((await a.ExecuteAsync(Failing(), ItemAction.Merge, p, new ActionInput(null, MergeMethod.Squash), default)).Ok);
        Assert.DoesNotContain(http.Requests, q => q.Method == HttpMethod.Patch);
        var r = await a.ExecuteAsync(Failing(), ItemAction.RerunFailed, p, default(ActionInput), default);
        Assert.False(r.Ok);
        Assert.True(r.Stale);
        Assert.Contains("may have been sent", r.Error);
    }
}
