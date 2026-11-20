using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class PipelineConfigTests
{
    const string Json1 = """
        {"pipelineSubscriptions":[{"id":"a","provider":"ado","org":"acme","project":"P","pipelineId":42,"pipeline":"Deploy",
        "on":["failed"],"stages":["PROD*"],"branches":["main"],"onlyMine":true,"enabled":true,"createdAt":"2026-11-20T09:00:00Z"}]}
        """;

    [Fact]
    public void Subscriptions_load_with_camel_case_names()
    {
        var c = JsonSerializer.Deserialize<AppConfig>(Json1, Json.Options)!;
        var s = Assert.Single(c.PipelineSubscriptions);
        Assert.Equal("a", s.Id);
        Assert.Equal(Provider.Ado, s.Provider);
        Assert.Equal(42, s.PipelineId);
        Assert.Equal([PipelineOutcome.Failed], s.On);
        Assert.Equal(["PROD*"], s.Stages);
        Assert.Equal(["main"], s.Branches);
        Assert.True(s.OnlyMine);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T09:00:00Z"), s.CreatedAt);
        Assert.Equal("acme/P", s.Scope);
    }

    [Fact]
    public void Subscriptions_round_trip()
    {
        var c = JsonSerializer.Deserialize<AppConfig>(Json1, Json.Options)!;
        var again = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c, Json.Options), Json.Options)!;
        Assert.Equivalent(c.PipelineSubscriptions, again.PipelineSubscriptions);
    }

    [Fact]
    public void A_config_without_the_key_has_no_subscriptions()
    {
        var c = JsonSerializer.Deserialize<AppConfig>("{}", Json.Options)!;
        Assert.Empty(c.PipelineSubscriptions);
    }

    [Fact]
    public void A_github_subscription_scopes_to_its_repo() =>
        Assert.Equal("acme/shop", new PipelineSubscription { Provider = Provider.GitHub, Repo = "acme/shop" }.Scope);
}
