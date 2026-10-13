using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class RepoNamesTests
{
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["contoso-engineering"] = "contoso",
        ["contoso-engineering/platform-project"] = "cp",
    };

    [Fact]
    public void Exact_org_alias() => Assert.Equal("contoso/api", RepoNames.Display("contoso-engineering/api", Aliases, 34));

    [Fact]
    public void Longer_prefix_wins() => Assert.Equal("cp/api", RepoNames.Display("contoso-engineering/platform-project/api", Aliases, 34));

    [Fact]
    public void Alias_match_is_case_insensitive() => Assert.Equal("contoso/api", RepoNames.Display("Contoso-Engineering/api", Aliases, 34));

    [Fact]
    public void Alias_requires_whole_segments() => Assert.Equal("contoso-engineering-x/api", RepoNames.Display("contoso-engineering-x/api", Aliases, 34));

    [Fact]
    public void Short_names_untouched() => Assert.Equal("acme/api", RepoNames.Display("acme/api", null, 34));

    [Fact]
    public void Middle_ellipsis_keeps_repo_name()
    {
        var d = RepoNames.Display("contoso-platform-engineering/payments-gateway-service", null, 34);
        Assert.Equal("contoso-…/payments-gateway-service", d);
        Assert.Equal(34, d.Length);
    }

    [Fact]
    public void Ado_path_keeps_repo()
    {
        var d = RepoNames.Display("fabrikam-industries/platform-services/billing-api", null, 30);
        Assert.Equal(30, d.Length);
        Assert.EndsWith("…/billing-api", d);
        Assert.StartsWith("fabrikam-indust", d);
    }

    [Fact]
    public void Overlong_last_segment_keeps_start_and_end()
    {
        var d = RepoNames.Display("acme/an-extremely-long-repository-name-that-goes-on", null, 20);
        Assert.Equal(20, d.Length);
        Assert.Contains("…", d);
        Assert.StartsWith("an-extrem", d);
        Assert.EndsWith("goes-on", d);
        Assert.DoesNotContain("/", d);
    }
}
