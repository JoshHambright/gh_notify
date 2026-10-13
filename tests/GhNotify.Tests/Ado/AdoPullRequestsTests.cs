using GhNotify.Ado;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Ado;

public class AdoPullRequestsTests
{
    internal static readonly AdoOrgContext Ctx = new("contoso", "me-id", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "me-id", "group-a" });
    internal static readonly AdoProject Core = new("proj-1", "Core Platform");

    internal static FakeHttpHandler Http() => new FakeHttpHandler()
        .On("/Core%20Platform/_apis/git/pullrequests?", Fixture.Read("ado-prs.json"))
        .On("/Core%20Platform/_apis/policy/evaluations?", Fixture.Read("ado-evaluations-106.json"))
        .On("/Core%20Platform/_apis/git/repositories/repo-1/pullRequests/106/threads", Fixture.Read("ado-threads-106.json"))
        .On("/Core%20Platform/_apis/pipelines/approvals", """{"value":[]}""");

    [Fact]
    public async Task Maps_review_requests_and_my_pr_items()
    {
        var http = Http();
        var items = await AdoPullRequests.FetchAsync(new AdoClient(new HttpClient(http), new FakeTokenProvider()), "contoso", Core, Ctx, false, default);

        Assert.Equal(
            ["ado:cifail:contoso/Core Platform/api#106", "ado:respond:contoso/Core Platform/api#106",
             "ado:review:contoso/Core Platform/api#101", "ado:review:contoso/Core Platform/api#103"],
            items.Select(i => i.Key).Order().ToArray());

        var r = items.Single(i => i.Number == 101);
        Assert.Equal(Provider.Ado, r.Provider);
        Assert.Equal("contoso/Core Platform/api", r.Repo);
        Assert.Equal("https://dev.azure.com/contoso/Core%20Platform/_git/api/pullrequest/101", r.Url);
        Assert.Equal("https://dev.azure.com/contoso/Core%20Platform/_git/api", r.CloneUrl);
        Assert.Equal("refs/heads/feature/retry", r.SourceRef);
        Assert.Equal("s101", r.HeadSha);
        Assert.Equal("Alice", r.Author);
        Assert.Null(r.ChangedLines);

        Assert.Equal("1 build failing", items.Single(i => i.Kind == ItemKind.MyPrCiFailing).Detail);
        Assert.Equal("Waiting for author, 1 active thread", items.Single(i => i.Kind == ItemKind.MyPrNeedsResponse).Detail);

        var evalCall = http.Requests.Single(q => q.RequestUri!.AbsoluteUri.Contains("policy/evaluations"));
        Assert.Contains(Uri.EscapeDataString("vstfs:///CodeReview/CodeReviewId/proj-1/106"), evalCall.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Include_drafts_adds_draft_review()
    {
        var items = await AdoPullRequests.FetchAsync(new AdoClient(new HttpClient(Http()), new FakeTokenProvider()), "contoso", Core, Ctx, true, default);
        Assert.Contains(items, i => i.Number == 105);
    }
}
