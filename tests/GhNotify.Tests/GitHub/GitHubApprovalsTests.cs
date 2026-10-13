using System.Net;
using GhNotify.Core;
using GhNotify.GitHub;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.GitHub;

public class GitHubApprovalsTests
{
    static FakeHttpHandler Http() => new FakeHttpHandler()
        .On("/notifications?", Fixture.Read("github-notifications.json"))
        .On("/repos/acme/deploy/actions/runs?status=waiting", Fixture.Read("github-runs-waiting.json"))
        .On("/repos/acme/deploy/actions/runs/9001/pending_deployments", Fixture.Read("github-pending-can.json"))
        .On("/repos/acme/api/actions/runs?status=waiting", Fixture.Read("github-runs-waiting.json").Replace("9001", "9002"))
        .On("/repos/acme/api/actions/runs/9002/pending_deployments", Fixture.Read("github-pending-cannot.json"))
        .On("/repos/acme/private/actions/runs?status=waiting", """{"message":"Not Found"}""", HttpStatusCode.NotFound);

    [Fact]
    public async Task Finds_runs_waiting_on_me_across_candidate_repos()
    {
        var http = Http();
        var approvals = new GitHubApprovals(new GitHubClient(new HttpClient(http), new FakeTokenProvider()));

        var items = await approvals.FetchAsync(["acme/api", "acme/private", "ACME/deploy"], CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("gh:approval:acme/deploy/runs/9001", item.Key);
        Assert.Equal(ItemKind.ApprovalPending, item.Kind);
        Assert.Equal("Release: Release v1.4.0", item.Title);
        Assert.Equal("Environment: production", item.Detail);
        Assert.Equal("https://github.com/acme/deploy/actions/runs/9001", item.Url);
        Assert.Equal("carol", item.Author);
        Assert.Equal("https://github.com/acme/deploy", item.CloneUrl);
        // acme/other only had a review_requested notification, so it is not a candidate.
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("acme/other"));
        // acme/deploy is queried once even though it came from two sets with different casing.
        Assert.Single(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/repos/acme/deploy/actions/runs?"));
    }

    [Fact]
    public async Task Source_includes_approvals_for_repos_of_my_prs()
    {
        var http = Http().On("/graphql", Fixture.Read("github-inbox.json"));
        var source = new GitHubSource(new GitHubClient(new HttpClient(http), new FakeTokenProvider()), () => new GitHubConfig());
        var result = await source.FetchAsync(CancellationToken.None);
        Assert.True(result.Healthy);
        Assert.Contains(result.Items, i => i.Key == "gh:approval:acme/deploy/runs/9001");
        Assert.Contains(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/repos/acme/api/actions/runs?status=waiting"));
    }

    [Fact]
    public async Task Pending_deployments_failure_keeps_last_known_approval_for_that_repo()
    {
        var pendingCalls = 0;
        var http = new FakeHttpHandler()
            .On("/notifications?", "[]")
            .On("/repos/acme/deploy/actions/runs?status=waiting", Fixture.Read("github-runs-waiting.json"))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/pending_deployments"), _ => ++pendingCalls == 1
                ? FakeHttpHandler.Respond(HttpStatusCode.OK, Fixture.Read("github-pending-can.json"))
                : FakeHttpHandler.Respond(HttpStatusCode.InternalServerError, """{"message":"boom"}"""));
        var approvals = new GitHubApprovals(new GitHubClient(new HttpClient(http), new FakeTokenProvider()));

        var first = await approvals.FetchAsync(["acme/deploy"], CancellationToken.None);
        var second = await approvals.FetchAsync(["acme/deploy"], CancellationToken.None);

        Assert.Equal("gh:approval:acme/deploy/runs/9001", Assert.Single(first).Key);
        Assert.Equal("gh:approval:acme/deploy/runs/9001", Assert.Single(second).Key);
    }

    [Fact]
    public async Task Notifications_failure_still_scans_extra_repos()
    {
        var http = new FakeHttpHandler()
            .On("/notifications?", "{}", HttpStatusCode.InternalServerError)
            .On("/repos/acme/deploy/actions/runs?status=waiting", Fixture.Read("github-runs-waiting.json"))
            .On("/repos/acme/deploy/actions/runs/9001/pending_deployments", Fixture.Read("github-pending-can.json"));
        var approvals = new GitHubApprovals(new GitHubClient(new HttpClient(http), new FakeTokenProvider()));

        var items = await approvals.FetchAsync(["acme/deploy"], CancellationToken.None);

        Assert.Equal("gh:approval:acme/deploy/runs/9001", Assert.Single(items).Key);
    }

    [Fact]
    public async Task Runs_server_error_for_one_repo_keeps_source_healthy_with_pr_items()
    {
        var http = new FakeHttpHandler()
            .On("/repos/acme/api/actions/runs?", """{"message":"boom"}""", HttpStatusCode.InternalServerError)
            .On("/notifications?", "[]")
            .On("/graphql", Fixture.Read("github-inbox.json"));
        var source = new GitHubSource(new GitHubClient(new HttpClient(http), new FakeTokenProvider()), () => new GitHubConfig());

        var result = await source.FetchAsync(CancellationToken.None);

        Assert.True(result.Healthy);
        Assert.Contains(result.Items, i => i.Kind == ItemKind.ReviewRequested);
    }

    static InboxItem RestoredApproval(string repo, long run) =>
        new($"gh:approval:{repo}/runs/{run}", Provider.GitHub, ItemKind.ApprovalPending, repo, "t",
            $"https://github.com/{repo}/actions/runs/{run}", "a", DateTimeOffset.UnixEpoch, null, null, null);

    [Fact]
    public async Task Failing_repo_on_first_poll_after_restart_keeps_last_known_approvals()
    {
        var http = new FakeHttpHandler()
            .On("/notifications?", "[]")
            .On("/actions/runs?status=waiting", """{"message":"boom"}""", HttpStatusCode.InternalServerError);
        IReadOnlyList<InboxItem> restored =
        [
            RestoredApproval("Acme/Deploy", 9001),
            RestoredApproval("acme/other", 1),
            new("gh:review:acme/deploy#3", Provider.GitHub, ItemKind.ReviewRequested, "acme/deploy", "t", "u", "a",
                DateTimeOffset.UnixEpoch, null, null, null),
        ];
        var approvals = new GitHubApprovals(new GitHubClient(new HttpClient(http), new FakeTokenProvider()), lastKnown: () => restored);

        var items = await approvals.FetchAsync(["acme/deploy"], CancellationToken.None);

        Assert.Contains(items, i => i.Key == "gh:approval:Acme/Deploy/runs/9001");
        Assert.DoesNotContain(items, i => i.Kind != ItemKind.ApprovalPending);
    }

    [Fact]
    public async Task Notifications_failure_on_first_poll_after_restart_scans_last_known_approval_repos()
    {
        var http = new FakeHttpHandler()
            .On("/notifications?", "{}", HttpStatusCode.InternalServerError)
            .On("/repos/acme/deploy/actions/runs?status=waiting", Fixture.Read("github-runs-waiting.json"))
            .On("/repos/acme/deploy/actions/runs/9001/pending_deployments", Fixture.Read("github-pending-can.json"));
        IReadOnlyList<InboxItem> restored = [RestoredApproval("acme/deploy", 9001)];
        var approvals = new GitHubApprovals(new GitHubClient(new HttpClient(http), new FakeTokenProvider()), lastKnown: () => restored);

        var items = await approvals.FetchAsync([], CancellationToken.None);

        Assert.Equal("gh:approval:acme/deploy/runs/9001", Assert.Single(items).Key);
        Assert.Contains(http.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/repos/acme/deploy/actions/runs?"));
    }

    [Fact]
    public async Task Source_passes_last_known_items_to_approvals()
    {
        var http = new FakeHttpHandler()
            .On("/graphql", Fixture.Read("github-inbox.json"))
            .On("/notifications?", "[]")
            .On("/actions/runs?status=waiting", """{"message":"boom"}""", HttpStatusCode.InternalServerError);
        IReadOnlyList<InboxItem> restored = [RestoredApproval("acme/api", 42)];
        var source = new GitHubSource(new GitHubClient(new HttpClient(http), new FakeTokenProvider()), () => new GitHubConfig(),
            () => restored);

        var result = await source.FetchAsync(CancellationToken.None);

        Assert.True(result.Healthy);
        Assert.Contains(result.Items, i => i.Key == "gh:approval:acme/api/runs/42");
    }
}
