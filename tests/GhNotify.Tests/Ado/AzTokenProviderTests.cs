using GhNotify.Ado;
using GhNotify.Core;
using GhNotify.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace GhNotify.Tests.Ado;

public class AzTokenProviderTests
{
    [Fact]
    public async Task Uses_epoch_expiry_and_refreshes_five_minutes_early()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-13T12:00:00Z"));
        var exp = time.GetUtcNow().AddMinutes(60).ToUnixTimeSeconds();
        var runner = new FakeProcessRunner { Handler = _ => FakeProcessRunner.Ok($$"""{"accessToken":"eyJ","expires_on":{{exp}}}""") };
        var p = new AzTokenProvider(runner, time);

        Assert.Equal("eyJ", await p.GetTokenAsync(false, default));
        Assert.Equal(["account", "get-access-token", "--resource", AzTokenProvider.AdoResource, "--output", "json"], runner.Calls[0].Args);
        time.Advance(TimeSpan.FromMinutes(54));
        await p.GetTokenAsync(false, default);
        Assert.Single(runner.Calls);
        time.Advance(TimeSpan.FromMinutes(2));
        await p.GetTokenAsync(false, default);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task Falls_back_to_local_expiresOn_string()
    {
        var local = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss.ffffff");
        var runner = new FakeProcessRunner { Handler = _ => FakeProcessRunner.Ok($$"""{"accessToken":"t","expiresOn":"{{local}}"}""") };
        var p = new AzTokenProvider(runner);
        await p.GetTokenAsync(false, default);
        await p.GetTokenAsync(false, default);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Not_logged_in_throws_with_az_login_hint()
    {
        var p = new AzTokenProvider(new FakeProcessRunner { Handler = _ => FakeProcessRunner.Fail("Please run 'az login'") });
        var ex = await Assert.ThrowsAsync<SourceAuthException>(() => p.GetTokenAsync(false, default));
        Assert.Contains("az login", ex.Message);
    }

    [Fact]
    public async Task Missing_az_throws_auth_exception()
    {
        var p = new AzTokenProvider(new FakeProcessRunner { Handler = _ => throw new FileNotFoundException() });
        var ex = await Assert.ThrowsAsync<SourceAuthException>(() => p.GetTokenAsync(false, default));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Aadsts_tenant_error_names_the_tenant_login_command()
    {
        var p = new AzTokenProvider(new FakeProcessRunner
        {
            Handler = _ => FakeProcessRunner.Fail("AADSTS500011: The resource principal named 499b84ac-1321-427f-aa17-267ca6975798 was not found in the tenant")
        });
        var ex = await Assert.ThrowsAsync<SourceAuthException>(() => p.GetTokenAsync(false, default));
        Assert.Contains("ado.azSubscription", ex.Message);
        Assert.Contains("az login", ex.Message);
    }

    static FakeProcessRunner OkRunner(FakeTimeProvider time) => new()
    {
        Handler = _ => FakeProcessRunner.Ok($$"""{"accessToken":"t","expires_on":{{time.GetUtcNow().AddMinutes(60).ToUnixTimeSeconds()}}}""")
    };

    [Fact]
    public async Task Subscription_is_passed_as_separate_args()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-13T12:00:00Z"));
        var runner = OkRunner(time);
        var p = new AzTokenProvider(runner, time, () => new AdoConfig { AzSubscription = "My Subscription", AzTenant = "ignored" });
        await p.GetTokenAsync(false, default);
        var a = runner.Calls[0].Args;
        Assert.Equal("--subscription", a[^2]);
        Assert.Equal("My Subscription", a[^1]);
        Assert.DoesNotContain("--tenant", a);
    }

    [Fact]
    public async Task Tenant_is_used_when_only_tenant_set()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-13T12:00:00Z"));
        var runner = OkRunner(time);
        var p = new AzTokenProvider(runner, time, () => new AdoConfig { AzTenant = "t-1", AzSubscription = "  " });
        await p.GetTokenAsync(false, default);
        var a = runner.Calls[0].Args;
        Assert.Equal("--tenant", a[^2]);
        Assert.Equal("t-1", a[^1]);
        Assert.DoesNotContain("--subscription", a);
    }

    [Fact]
    public async Task Neither_flag_when_unset()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-13T12:00:00Z"));
        var runner = OkRunner(time);
        var p = new AzTokenProvider(runner, time, () => new AdoConfig());
        await p.GetTokenAsync(false, default);
        Assert.Equal(["account", "get-access-token", "--resource", AzTokenProvider.AdoResource, "--output", "json"], runner.Calls[0].Args);
    }

    [Fact]
    public async Task Changing_subscription_bypasses_cache()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-13T12:00:00Z"));
        var runner = OkRunner(time);
        var cfg = new AdoConfig { AzSubscription = "A" };
        var p = new AzTokenProvider(runner, time, () => cfg);
        await p.GetTokenAsync(false, default);
        await p.GetTokenAsync(false, default);
        Assert.Single(runner.Calls);
        cfg.AzSubscription = "B";
        await p.GetTokenAsync(false, default);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("B", runner.Calls[1].Args[^1]);
    }
}
