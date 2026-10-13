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
}
