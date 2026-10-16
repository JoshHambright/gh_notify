using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ConfigValidatorTests
{
    static IReadOnlyList<ConfigError> E(Action<AppConfig> edit)
    {
        var c = new AppConfig();
        edit(c);
        return ConfigValidator.Errors(c);
    }

    [Fact]
    public void Defaults_are_valid() => Assert.Empty(ConfigValidator.Errors(new AppConfig()));

    [Fact]
    public void Collects_every_error_with_its_field()
    {
        var errors = E(c => { c.PollSeconds = 5; c.Updates.CheckHours = 0; c.Filters.Mute.Add("acme//x"); });
        Assert.Contains(errors, e => e.Field == "pollSeconds");
        Assert.Contains(errors, e => e.Field == "updates.checkHours");
        Assert.Contains(errors, e => e.Field == "filters.mute[0]");
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("none", true)]
    [InlineData("builtin:chime", true)]
    [InlineData("builtin:kazoo", false)]
    [InlineData("<rooted>ding.wav", true)]
    [InlineData(@"ding.wav", false)]
    [InlineData("<rooted>ding.mp3", false)]
    public void Sound_values(string sound, bool ok)
    {
        sound = sound.Replace("<rooted>", Path.Combine(Path.GetTempPath(), "sounds") + Path.DirectorySeparatorChar); // rooted on every OS
        var errors = E(c => c.Notifications.Sounds.Add(new SoundRule { Sound = sound }));
        Assert.Equal(ok, !errors.Any(e => e.Field == "notifications.sounds[0].sound"));
    }

    [Theory]
    [InlineData("terminal", true)]
    [InlineData("iterm", true)]
    [InlineData("/usr/local/bin/open-term", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void Terminal_must_not_be_empty(string terminal, bool ok)
    {
        var errors = E(c => c.Display.Terminal = terminal);
        Assert.Equal(ok, !errors.Any(e => e.Field == "display.terminal"));
    }

    [Fact]
    public void Profile_needs_exactly_one_of_prompt_or_command()
    {
        Assert.Contains(E(c => c.AgentProfiles["x"] = new AgentProfile()), e => e.Field == "agentProfiles.x");
        Assert.Contains(E(c => c.AgentProfiles["x"] = new AgentProfile { Prompt = "p", Command = ["pwsh"] }), e => e.Field == "agentProfiles.x");
        Assert.Contains(E(c => c.AgentProfiles["x"] = new AgentProfile { Command = [] }), e => e.Field == "agentProfiles.x.command");
        Assert.Empty(E(c => c.AgentProfiles["x"] = new AgentProfile { Command = ["pwsh", "-File", "t.ps1", "{url}"] }));
    }

    [Fact]
    public void Default_profiles_need_known_kind_and_profile()
    {
        Assert.Contains(E(c => c.DefaultProfiles["NotAKind"] = "review"), e => e.Field == "defaultProfiles.NotAKind");
        Assert.Contains(E(c => c.DefaultProfiles["ReviewRequested"] = "nope"), e => e.Field == "defaultProfiles.ReviewRequested");
        Assert.Empty(E(c => c.DefaultProfiles["reviewRequested"] = "peer-review"));
    }

    [Fact]
    public void Nulls_are_errors_not_crashes()
    {
        Assert.Contains(E(c => c.Filters = null!), e => e.Field == "filters");
        Assert.Contains(E(c => c.Notifications.Sounds.Add(null!)), e => e.Field == "notifications.sounds[0]");
        Assert.Contains(E(c => c.Updates = null!), e => e.Field == "updates");
    }
}
