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
        var items = GitHubPrMapper.MyPrs(Data()).ToList();
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
}
