using GhNotify.Core;

namespace GhNotify.Tests;

public class SourceRetirementTests
{
    static AppConfig Cfg(Action<AppConfig>? edit = null) { var c = new AppConfig(); edit?.Invoke(c); return c; }

    [Fact]
    public void GitHub_disabled_follows_config() =>
        Assert.True(SourceRetirement.IsDisabled("GitHub", Cfg(c => c.GitHub.Enabled = false)));

    [Fact]
    public void Ado_disabled_retires_all_ado_sources()
    {
        var c = Cfg(c => c.Ado.Enabled = false);
        Assert.True(SourceRetirement.IsDisabled("ADO", c));
        Assert.True(SourceRetirement.IsDisabled("ADO:contoso", c));
    }

    [Fact]
    public void Excluded_org_is_disabled() =>
        Assert.True(SourceRetirement.IsDisabled("ADO:Contoso", Cfg(c => c.Ado.ExcludeOrgs.Add("contoso"))));

    [Fact]
    public void Org_not_listed_is_disabled_when_discovery_off()
    {
        var c = Cfg(c => { c.Ado.DiscoverOrgs = false; c.Ado.Orgs.Add("a"); });
        Assert.True(SourceRetirement.IsDisabled("ADO:b", c));
        Assert.False(SourceRetirement.IsDisabled("ADO:A", c));
    }

    [Fact]
    public void Org_with_discovery_on_is_enabled() =>
        Assert.False(SourceRetirement.IsDisabled("ADO:b", Cfg()));

    [Fact]
    public void Pseudo_source_retired_only_when_a_real_org_is_healthy()
    {
        Assert.True(SourceRetirement.ShouldRetireAdoPseudoSource([new("ADO", false, "x", null), new("ADO:a", true, null, null)]));
        Assert.False(SourceRetirement.ShouldRetireAdoPseudoSource([new("ADO", false, "x", null), new("ADO:a", false, "y", null)]));
        Assert.False(SourceRetirement.ShouldRetireAdoPseudoSource([]));
    }

    static PipelineSubscription PSub(Provider p, string? org = null, bool enabled = true) =>
        new() { Id = Guid.NewGuid().ToString(), Provider = p, Org = org, Project = "P", Repo = "a/b", PipelineId = 1, Enabled = enabled };

    [Fact]
    public void Pipeline_sources_retire_with_their_last_subscription()
    {
        var none = Cfg();
        Assert.True(SourceRetirement.IsDisabled("GitHub-Pipelines", none));
        Assert.True(SourceRetirement.IsDisabled("ADO-Pipelines:acme", none));
        var c = Cfg(c => { c.PipelineSubscriptions.Add(PSub(Provider.GitHub)); c.PipelineSubscriptions.Add(PSub(Provider.Ado, "Acme")); });
        Assert.False(SourceRetirement.IsDisabled("GitHub-Pipelines", c));
        Assert.False(SourceRetirement.IsDisabled("ADO-Pipelines:acme", c));
        Assert.True(SourceRetirement.IsDisabled("ADO-Pipelines:other", c));
    }

    [Fact]
    public void Pipeline_sources_retire_when_disabled_or_their_provider_is_off()
    {
        var off = Cfg(c => c.PipelineSubscriptions.Add(PSub(Provider.GitHub, enabled: false)));
        Assert.True(SourceRetirement.IsDisabled("GitHub-Pipelines", off));
        var gh = Cfg(c => { c.GitHub.Enabled = false; c.PipelineSubscriptions.Add(PSub(Provider.GitHub)); });
        Assert.True(SourceRetirement.IsDisabled("GitHub-Pipelines", gh));
        var ado = Cfg(c => { c.Ado.Enabled = false; c.PipelineSubscriptions.Add(PSub(Provider.Ado, "acme")); });
        Assert.True(SourceRetirement.IsDisabled("ADO-Pipelines:acme", ado));
    }
}
