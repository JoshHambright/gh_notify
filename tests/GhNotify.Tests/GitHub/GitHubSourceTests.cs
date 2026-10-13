using System.Net;
using GhNotify.Core;
using GhNotify.GitHub;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.GitHub;

public class GitHubSourceTests
{
    static (GitHubSource, FakeHttpHandler, FakeTokenProvider) Make(FakeHttpHandler http, GitHubConfig? cfg = null)
    {
        var tokens = new FakeTokenProvider();
        var client = new GitHubClient(new HttpClient(http), tokens);
        return (new GitHubSource(client, () => cfg ?? new GitHubConfig()), http, tokens);
    }

    static FakeHttpHandler Happy() => new FakeHttpHandler()
        .On("/graphql", Fixture.Read("github-inbox.json"))
        .On("/notifications", "[]")
        .On("/actions/runs?status=waiting", """{"total_count":0,"workflow_runs":[]}""");

    [Fact]
    public async Task Fetches_items_with_bearer_token_and_draft_filter()
    {
        var (source, http, _) = Make(Happy());
        var result = await source.FetchAsync(CancellationToken.None);
        Assert.True(result.Healthy);
        Assert.Equal(5, result.Items.Count);
        var gql = http.Requests.First(r => r.RequestUri!.AbsolutePath == "/graphql");
        Assert.Equal("Bearer", gql.Headers.Authorization!.Scheme);
        Assert.Equal("tok", gql.Headers.Authorization.Parameter);
        Assert.Contains("draft:false", http.Bodies[http.Requests.IndexOf(gql)]);
    }

    [Fact]
    public async Task Include_drafts_drops_draft_filter()
    {
        var (source, http, _) = Make(Happy(), new GitHubConfig { IncludeDrafts = true });
        await source.FetchAsync(CancellationToken.None);
        Assert.DoesNotContain("draft:false", http.Bodies[0]);
    }

    [Fact]
    public async Task Unauthorized_retries_once_with_forced_token_refresh()
    {
        var calls = 0;
        var http = new FakeHttpHandler()
            .On(r => r.RequestUri!.AbsolutePath == "/graphql", _ => ++calls == 1
                ? FakeHttpHandler.Respond(HttpStatusCode.Unauthorized, "{}")
                : FakeHttpHandler.Respond(HttpStatusCode.OK, Fixture.Read("github-inbox.json")))
            .On("/notifications", "[]")
            .On("/actions/runs?status=waiting", """{"workflow_runs":[]}""");
        var (source, _, tokens) = Make(http);
        var result = await source.FetchAsync(CancellationToken.None);
        Assert.True(result.Healthy);
        Assert.Equal(1, tokens.ForcedCalls);
    }

    [Fact]
    public async Task Persistent_unauthorized_is_unhealthy_with_login_hint()
    {
        var (source, _, _) = Make(new FakeHttpHandler().On("/graphql", "{}", HttpStatusCode.Unauthorized));
        var result = await source.FetchAsync(CancellationToken.None);
        Assert.False(result.Healthy);
        Assert.Contains("gh auth login", result.Problem);
    }

    [Fact]
    public async Task Token_failure_is_unhealthy()
    {
        var (source, _, tokens) = Make(Happy());
        tokens.Throw = new SourceAuthException("gh is not logged in — run `gh auth login`");
        var result = await source.FetchAsync(CancellationToken.None);
        Assert.False(result.Healthy);
        Assert.Contains("gh auth login", result.Problem);
    }

    [Fact]
    public async Task Rate_limit_backs_off_without_calling_again()
    {
        var http = new FakeHttpHandler().On(r => true, _ =>
        {
            var resp = FakeHttpHandler.Respond(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""");
            resp.Headers.Add("x-ratelimit-remaining", "0");
            resp.Headers.Add("x-ratelimit-reset", DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString());
            return resp;
        });
        var (source, _, _) = Make(http);
        var first = await source.FetchAsync(CancellationToken.None);
        var second = await source.FetchAsync(CancellationToken.None);
        Assert.Contains("rate limited", first.Problem);
        Assert.Contains("rate limited", second.Problem);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task GraphQL_errors_without_data_are_unhealthy()
    {
        var (source, _, _) = Make(new FakeHttpHandler().On("/graphql", """{"errors":[{"message":"Something broke"}]}"""));
        var result = await source.FetchAsync(CancellationToken.None);
        Assert.Contains("Something broke", result.Problem);
    }
}
