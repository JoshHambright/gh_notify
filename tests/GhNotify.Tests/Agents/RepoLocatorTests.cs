using GhNotify.Agents;

namespace GhNotify.Tests.Agents;

public class RepoLocatorTests
{
    static string MakeRepo(string root, string rel, string? url)
    {
        var dir = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        var config = "[core]\n\tbare = false\n" + (url is null ? "" : $"[remote \"origin\"]\n\turl = {url}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n") +
                     "[remote \"upstream\"]\n\turl = https://github.com/someone/else\n";
        File.WriteAllText(Path.Combine(dir, ".git", "config"), config);
        return dir;
    }

    [Fact]
    public void Finds_clones_by_normalized_remote()
    {
        var root = Directory.CreateTempSubdirectory("ghn-repos").FullName;
        var api = MakeRepo(root, "api", "git@github.com:acme/api.git");
        var ado = MakeRepo(root, Path.Combine("work", "ado"), "https://dev.azure.com/contoso/Core%20Platform/_git/api");
        MakeRepo(root, Path.Combine("a", "b", "c", "too-deep"), "https://github.com/acme/deep");
        MakeRepo(root, Path.Combine("node_modules", "pkg"), "https://github.com/acme/skipped");
        var wt = Path.Combine(root, "wt");
        Directory.CreateDirectory(wt);
        File.WriteAllText(Path.Combine(wt, ".git"), "gitdir: C:/elsewhere/.git/worktrees/wt");

        var locator = new RepoLocator(() => [root]);

        Assert.Equal(api, locator.FindClone("https://github.com/acme/api"));
        Assert.Equal(ado, locator.FindClone("https://dev.azure.com/contoso/Core%20Platform/_git/api"));
        Assert.Null(locator.FindClone("https://github.com/acme/deep"));
        Assert.Null(locator.FindClone("https://github.com/acme/skipped"));
        Assert.Null(locator.FindClone("https://github.com/someone/else"));
    }

    [Fact]
    public void Missing_root_is_ignored()
    {
        var locator = new RepoLocator(() => [@"Z:\does\not\exist"]);
        Assert.Null(locator.FindClone("https://github.com/acme/api"));
    }

    [Fact]
    public void Reads_origin_url_only()
    {
        var root = Directory.CreateTempSubdirectory("ghn-repos").FullName;
        var dir = MakeRepo(root, "x", "https://github.com/acme/x");
        Assert.Equal("https://github.com/acme/x", RepoLocator.ReadOriginUrl(Path.Combine(dir, ".git", "config")));
        var none = MakeRepo(root, "y", null);
        Assert.Null(RepoLocator.ReadOriginUrl(Path.Combine(none, ".git", "config")));
    }
}
