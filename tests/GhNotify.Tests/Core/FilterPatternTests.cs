using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class FilterPatternTests
{
    static InboxItem Gh(string repo) => new("k", Provider.GitHub, ItemKind.ReviewRequested, repo, "T", "https://u", "alice", DateTimeOffset.UnixEpoch, null, null, null);
    static InboxItem Ado(string path) => Gh(path) with { Provider = Provider.Ado };

    static bool M(string pattern, InboxItem item)
    {
        Assert.True(FilterPattern.TryParse(pattern, out var p, out var err), err);
        return p!.Matches(item);
    }

    [Fact]
    public void Prefix_segments_match_org_project_and_repo()
    {
        Assert.True(M("contoso", Ado("contoso/platform/api")));
        Assert.True(M("contoso/platform", Ado("contoso/platform/api")));
        Assert.False(M("contoso/plat", Ado("contoso/platform/api")));
        Assert.True(M("acme/legacy-*", Gh("acme/legacy-billing")));
        Assert.False(M("acme/legacy-*", Gh("acme/api")));
        Assert.True(M("ACME/API", Gh("acme/api")));
    }

    [Fact]
    public void Provider_prefix_limits_matches()
    {
        Assert.True(M("gh:acme", Gh("acme/api")));
        Assert.False(M("gh:acme", Ado("acme/p/api")));
        Assert.True(M("ado:acme", Ado("acme/p/api")));
    }

    [Fact]
    public void Pattern_longer_than_path_never_matches() => Assert.False(M("acme/api/extra/more", Gh("acme/api")));

    [Theory]
    [InlineData("")]
    [InlineData("acme//api")]
    [InlineData("acme/**")]
    [InlineData("xx:acme")]
    [InlineData("gh:")]
    public void Invalid_patterns_are_rejected(string pattern)
    {
        Assert.False(FilterPattern.TryParse(pattern, out _, out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }
}
