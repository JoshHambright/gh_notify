using System.Text.Json;
using GhNotify.Core;
using GhNotify.GitHub;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.GitHub;

public class GitHubPrMapperTests
{
    static JsonElement Data() => JsonDocument.Parse(Fixture.Read("github-inbox.json")).RootElement.GetProperty("data");

    [Fact]
    public void Maps_review_requests()
    {
        var items = GitHubPrMapper.ReviewRequests(Data()).ToList();
        Assert.Equal(2, items.Count);
        var pr = items.Single(i => i.Number == 12);
        Assert.Equal("gh:review:acme/api#12", pr.Key);
        Assert.Equal(ItemKind.ReviewRequested, pr.Kind);
        Assert.Equal("acme/api", pr.Repo);
        Assert.Equal("alice", pr.Author);
        Assert.Equal(35, pr.ChangedLines);
        Assert.Equal("aaa111", pr.HeadSha);
        Assert.Equal("pull/12/head", pr.SourceRef);
        Assert.Equal("https://github.com/acme/api", pr.CloneUrl);
        Assert.Equal("https://github.com/acme/api/pull/12", pr.Url);
    }

    [Fact]
    public void Bot_authors_get_bot_suffix_so_rules_match()
    {
        var bot = GitHubPrMapper.ReviewRequests(Data()).Single(i => i.Number == 7);
        Assert.Equal("dependabot[bot]", bot.Author);
        Assert.True(ItemMatcher.Matches(new ItemMatch { Author = "dependabot[bot]" }, bot));
    }

    [Fact]
    public void Maps_my_prs_failing_and_needing_response()
    {
        var items = GitHubPrMapper.MyPrs(Data()).Where(i => i.Kind is ItemKind.MyPrCiFailing or ItemKind.MyPrNeedsResponse).ToList();
        Assert.Equal(
            ["gh:cifail:acme/api#20", "gh:respond:acme/api#20", "gh:respond:acme/web#22"],
            items.Select(i => i.Key).Order().ToArray());
        Assert.Equal("Checks failing", items.Single(i => i.Key == "gh:cifail:acme/api#20").Detail);
        Assert.Equal("Changes requested", items.Single(i => i.Key == "gh:respond:acme/api#20").Detail);
        // One thread by bob plus one by a deleted user (author null) both count as "not mine".
        Assert.Equal("2 unresolved threads", items.Single(i => i.Key == "gh:respond:acme/web#22").Detail);
        Assert.All(items, i => Assert.Equal("me", i.Author));
    }
    [Fact]
    public void Pr_details()
    {
        var pr = GitHubPrMapper.ReviewRequests(Data()).Single(i => i.Number == 12);
        var d = pr.Details!.ToDictionary(x => x.Label, x => x.Value);
        Assert.Equal(["Repo", "Author", "Branch", "Commit", "Created"], pr.Details!.Select(x => x.Label));
        Assert.Equal("feature/retry → main", d["Branch"]);
        Assert.Equal("aaa111", d["Commit"]);
    }

    static JsonElement Data(string mineNode) =>
        JsonDocument.Parse($$"""{ "viewer": { "login": "alice" }, "review": { "nodes": [] }, "mine": { "nodes": [ {{mineNode}} ] } }""").RootElement;

    static string MinePr(string? decision = null, string rollup = "SUCCESS", string mergeState = "CLEAN", bool draft = false) => $$"""
        { "number": 5, "title": "Mine", "url": "https://github.com/acme/api/pull/5", "isDraft": {{(draft ? "true" : "false")}},
          "updatedAt": "2026-10-13T10:00:00Z", "headRefOid": "abc123",
          "author": { "__typename": "User", "login": "alice" },
          "repository": { "nameWithOwner": "acme/api", "url": "https://github.com/acme/api" },
          "reviewDecision": {{(decision is null ? "null" : $"\"{decision}\"")}}, "mergeStateStatus": "{{mergeState}}",
          "commits": { "nodes": [ { "commit": { "statusCheckRollup": { "state": "{{rollup}}" } } } ] },
          "reviewThreads": { "nodes": [] } }
        """;

    [Theory]
    [InlineData("APPROVED", "SUCCESS", "CLEAN", false, ItemKind.MyPrReadyToMerge, "Approved · checks green")]
    [InlineData("APPROVED", "SUCCESS", "HAS_HOOKS", false, ItemKind.MyPrReadyToMerge, "Approved · checks green")]
    [InlineData("APPROVED", "SUCCESS", "BEHIND", false, ItemKind.MyPrWaiting, "Behind base branch")]
    [InlineData("APPROVED", "SUCCESS", "DIRTY", false, ItemKind.MyPrWaiting, "Merge conflicts")]
    [InlineData("APPROVED", "PENDING", "BLOCKED", false, ItemKind.MyPrWaiting, "Checks running")]
    [InlineData("REVIEW_REQUIRED", "SUCCESS", "BLOCKED", false, ItemKind.MyPrWaiting, "Waiting on review")]
    [InlineData(null, "SUCCESS", "CLEAN", false, ItemKind.MyPrWaiting, "Waiting on review")]
    [InlineData("APPROVED", "SUCCESS", "CLEAN", true, ItemKind.MyPrWaiting, "Draft")]
    public void Own_pr_without_problems_is_ready_or_waiting(string? decision, string rollup, string merge, bool draft, ItemKind kind, string detail)
    {
        var items = GitHubPrMapper.MyPrs(Data(MinePr(decision: decision, rollup: rollup, mergeState: merge, draft: draft))).ToList();
        var item = Assert.Single(items);
        Assert.Equal(kind, item.Kind);
        Assert.Equal(detail, item.Detail);
        Assert.StartsWith(kind == ItemKind.MyPrReadyToMerge ? "gh:ready:" : "gh:waiting:", item.Key);
    }

    [Fact]
    public void Failing_or_needs_response_pr_is_never_also_ready_or_waiting()
    {
        var items = GitHubPrMapper.MyPrs(Data(MinePr(decision: "CHANGES_REQUESTED", rollup: "FAILURE", mergeState: "BLOCKED"))).ToList();
        Assert.Equal([ItemKind.MyPrCiFailing, ItemKind.MyPrNeedsResponse], items.Select(i => i.Kind));
    }

    [Fact]
    public void Draft_waiting_pr_is_skipped_when_drafts_excluded() =>
        Assert.Empty(GitHubPrMapper.MyPrs(Data(MinePr(decision: null, rollup: "SUCCESS", mergeState: "CLEAN", draft: true)), includeDrafts: false));
}
