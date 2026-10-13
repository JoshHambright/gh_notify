using GhNotify.Core;
using GhNotify.GitHub;
using GhNotify.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace GhNotify.Tests.GitHub;

public class GhTokenProviderTests
{
    [Fact]
    public async Task Caches_token_for_30_minutes()
    {
        var runner = new FakeProcessRunner { Handler = _ => FakeProcessRunner.Ok("gho_abc\n") };
        var time = new FakeTimeProvider();
        var p = new GhTokenProvider(runner, time);
        Assert.Equal("gho_abc", await p.GetTokenAsync(false, default));
        await p.GetTokenAsync(false, default);
        Assert.Single(runner.Calls);
        Assert.Equal(["auth", "token"], runner.Calls[0].Args);
        time.Advance(TimeSpan.FromMinutes(31));
        await p.GetTokenAsync(false, default);
        Assert.Equal(2, runner.Calls.Count);
        await p.GetTokenAsync(true, default);
        Assert.Equal(3, runner.Calls.Count);
    }

    [Fact]
    public async Task Not_logged_in_throws_auth_exception()
    {
        var p = new GhTokenProvider(new FakeProcessRunner { Handler = _ => FakeProcessRunner.Fail("not logged in") });
        var ex = await Assert.ThrowsAsync<SourceAuthException>(() => p.GetTokenAsync(false, default));
        Assert.Contains("gh auth login", ex.Message);
    }

    [Fact]
    public async Task Missing_gh_throws_auth_exception()
    {
        var p = new GhTokenProvider(new FakeProcessRunner { Handler = _ => throw new FileNotFoundException() });
        var ex = await Assert.ThrowsAsync<SourceAuthException>(() => p.GetTokenAsync(false, default));
        Assert.Contains("not found", ex.Message);
    }
}
