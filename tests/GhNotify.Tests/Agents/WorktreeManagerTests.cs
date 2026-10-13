using GhNotify.Agents;
using GhNotify.Core;
using GhNotify.Tests.Support;

namespace GhNotify.Tests.Agents;

public class WorktreeManagerTests
{
    [Theory]
    [InlineData("pull/12/head", "+refs/pull/12/head:refs/ghnotify/t1")]
    [InlineData("refs/heads/feature/x", "+refs/heads/feature/x:refs/ghnotify/t1")]
    [InlineData("feature/x", "+refs/heads/feature/x:refs/ghnotify/t1")]
    public async Task Fetches_into_private_ref_then_adds_detached_worktree(string fetchRef, string refspec)
    {
        var runner = new FakeProcessRunner();
        var baseDir = Path.Combine(Path.GetTempPath(), "ghn-wt-" + Guid.NewGuid());
        var wt = await new WorktreeManager(runner, baseDir).CreateAsync(@"C:\clone", fetchRef, "t1", default);

        Assert.Equal(Path.Combine(baseDir, "t1"), wt);
        Assert.Equal(["-C", @"C:\clone", "fetch", "--no-tags", "origin", refspec], runner.Calls[0].Args);
        Assert.Equal(["-C", @"C:\clone", "worktree", "add", "--detach", wt, "refs/ghnotify/t1"], runner.Calls[1].Args);
        Assert.All(runner.Calls, c => Assert.Equal("git", c.FileName));
    }

    [Fact]
    public async Task Git_failure_throws_with_stderr()
    {
        var runner = new FakeProcessRunner { Handler = _ => FakeProcessRunner.Fail("fatal: couldn't find remote ref") };
        var ex = await Assert.ThrowsAsync<GitException>(() =>
            new WorktreeManager(runner, Path.GetTempPath()).CreateAsync(@"C:\clone", "pull/1/head", "t2", default));
        Assert.Contains("couldn't find remote ref", ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Real_git_create_and_remove()
    {
        var root = Directory.CreateTempSubdirectory("ghn-git").FullName;
        var runner = new ProcessRunner();
        async Task Git(string dir, params string[] args)
        {
            var r = await runner.RunAsync(new ProcessSpec("git", ["-C", dir, "-c", "user.name=t", "-c", "user.email=t@t", .. args]));
            Assert.True(r.ExitCode == 0, r.StdErr);
        }
        var origin = Path.Combine(root, "origin");
        Directory.CreateDirectory(origin);
        await Git(origin, "init", "-b", "main");
        File.WriteAllText(Path.Combine(origin, "a.txt"), "main");
        await Git(origin, "add", ".");
        await Git(origin, "commit", "-m", "init");
        await Git(root, "clone", origin, "clone");
        await Git(origin, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(origin, "b.txt"), "feature");
        await Git(origin, "add", ".");
        await Git(origin, "commit", "-m", "feature");

        var clone = Path.Combine(root, "clone");
        var mgr = new WorktreeManager(runner, Path.Combine(root, "worktrees"));
        var wt = await mgr.CreateAsync(clone, "feature", "abc", default);
        Assert.True(File.Exists(Path.Combine(wt, "b.txt")));

        var again = await mgr.CreateAsync(clone, "feature", "abc", default); // re-create over an existing one
        Assert.Equal(wt, again);

        await mgr.RemoveAsync(clone, wt, "abc", default);
        Assert.False(Directory.Exists(wt));
        var refs = await runner.RunAsync(new ProcessSpec("git", ["-C", clone, "for-each-ref", "refs/ghnotify"]));
        Assert.Equal("", refs.StdOut.Trim());
    }
}
