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
        Assert.Equal("contoso", r.Org);
        Assert.Equal("Core Platform", r.Project);
        Assert.Equal("repo-1", r.RepoId);
        Assert.Equal("feature/retry → main", r.Details!.Single(x => x.Label == "Branch").Value);

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

    static string MyPr(string mergeStatus = "succeeded", bool draft = false, string reviewers = "") => $$"""
        { "pullRequestId": 200, "title": "Mine", "isDraft": {{(draft ? "true" : "false")}}, "mergeStatus": "{{mergeStatus}}",
          "creationDate": "2026-10-13T09:00:00Z", "createdBy": { "id": "me-id", "displayName": "Me" },
          "repository": { "id": "repo-1", "name": "api", "project": { "id": "proj-1", "name": "Core Platform" } },
          "sourceRefName": "refs/heads/mine", "lastMergeSourceCommit": { "commitId": "s200" },
          "reviewers": [ { "id": "me-id", "vote": 0 }{{reviewers}} ] }
        """;

    static async Task<IReadOnlyList<InboxItem>> FetchMine(string pr, string evaluations, bool includeDrafts = true)
    {
        var http = new FakeHttpHandler()
            .On("/Core%20Platform/_apis/git/pullrequests?", $$"""{ "value": [ {{pr}} ] }""")
            .On("/Core%20Platform/_apis/policy/evaluations?", evaluations)
            .On("/Core%20Platform/_apis/git/repositories/repo-1/pullRequests/200/threads", """{ "value": [] }""");
        return await AdoPullRequests.FetchAsync(new AdoClient(new HttpClient(http), new FakeTokenProvider()), "contoso", Core, Ctx, includeDrafts, default);
    }

    const string Reviewer = """, { "id": "bob-id", "vote": 10 }""";
    const string PoliciesPassing = """
        { "value": [ { "status": "approved", "configuration": { "isBlocking": true, "type": { "id": "0609b952-1397-4640-95ec-e00a01b2c241" } } },
                     { "status": "notApplicable", "configuration": { "isBlocking": true, "type": { "id": "x" } } },
                     { "status": "queued", "configuration": { "isBlocking": false, "type": { "id": "x" } } } ] }
        """;

    [Fact]
    public async Task Own_pr_with_passing_policies_is_ready_to_merge()
    {
        var item = Assert.Single(await FetchMine(MyPr(reviewers: Reviewer), PoliciesPassing));
        Assert.Equal(ItemKind.MyPrReadyToMerge, item.Kind);
        Assert.StartsWith("ado:ready:", item.Key);
        Assert.Equal("Approved · policies passing", item.Detail);
    }

    [Fact]
    public async Task Own_pr_with_queued_blocking_policy_is_waiting()
    {
        const string evals = """{ "value": [ { "status": "queued", "configuration": { "isBlocking": true, "type": { "id": "x" } } } ] }""";
        var item = Assert.Single(await FetchMine(MyPr(reviewers: Reviewer), evals));
        Assert.Equal(ItemKind.MyPrWaiting, item.Kind);
        Assert.StartsWith("ado:waiting:", item.Key);
        Assert.Equal("Waiting on policies", item.Detail);
    }

    [Fact]
    public async Task Own_pr_waits_on_required_reviewers()
    {
        const string two = """, { "id": "a-id", "vote": 0, "isRequired": true }, { "id": "b-id", "vote": 0, "isRequired": true }""";
        var item = Assert.Single(await FetchMine(MyPr(reviewers: two), PoliciesPassing));
        Assert.Equal(ItemKind.MyPrWaiting, item.Kind);
        Assert.Equal("Waiting on 2 reviewers", item.Detail);
    }

    [Fact]
    public async Task Own_pr_with_conflicts_is_waiting()
    {
        var item = Assert.Single(await FetchMine(MyPr(mergeStatus: "conflicts", reviewers: Reviewer), PoliciesPassing));
        Assert.Equal(ItemKind.MyPrWaiting, item.Kind);
        Assert.Equal("Merge conflicts", item.Detail);
    }

    [Fact]
    public async Task Own_draft_pr_is_waiting_only_when_drafts_included()
    {
        Assert.Empty(await FetchMine(MyPr(draft: true), PoliciesPassing, includeDrafts: false));
        var item = Assert.Single(await FetchMine(MyPr(draft: true), PoliciesPassing, includeDrafts: true));
        Assert.Equal(ItemKind.MyPrWaiting, item.Kind);
        Assert.Equal("Draft", item.Detail);
    }

    [Fact]
    public async Task Failing_build_policy_is_never_also_ready_or_waiting()
    {
        const string evals = """{ "value": [ { "status": "rejected", "configuration": { "isBlocking": true, "type": { "id": "0609b952-1397-4640-95ec-e00a01b2c241" } } } ] }""";
        var item = Assert.Single(await FetchMine(MyPr(reviewers: Reviewer), evals));
        Assert.Equal(ItemKind.MyPrCiFailing, item.Kind);
    }
}
