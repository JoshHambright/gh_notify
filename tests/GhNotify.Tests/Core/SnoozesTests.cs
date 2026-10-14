using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class SnoozesTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
    static InboxItem It(string key, string repo = "acme/api", Provider p = Provider.GitHub) =>
        new(key, p, ItemKind.ReviewRequested, repo, "T", "https://u", "alice", Now, "s1", 5, null);
    static InboxEntry En(InboxItem i) => new(i, "GitHub", false);
    static readonly WorkHoursConfig Wh = new() { Days = ["Mon", "Tue", "Wed", "Thu", "Fri"], Start = "08:00", End = "18:00" };

    [Fact]
    public void Expired_snooze_wakes_with_current_item()
    {
        var r = Snoozes.Evaluate([new Snooze("k1", Now.AddMinutes(-1), false)], InboxDiff.Empty, [En(It("k1"))], Now);
        Assert.Empty(r.Kept);
        Assert.Equal("k1", Assert.Single(r.Woken).Key);
    }

    [Fact]
    public void Change_wakes_any_snooze()
    {
        var old = It("k1");
        var diff = new InboxDiff([], [], [new ItemChange(old, old with { HeadSha = "s2" })], []);
        var r = Snoozes.Evaluate([new Snooze("k1", Now.AddHours(3), false), new Snooze("k2", null, true)], diff, [En(old with { HeadSha = "s2" }), En(It("k2"))], Now);
        Assert.Equal("k1", Assert.Single(r.Woken).Key);
        Assert.Equal("k2", Assert.Single(r.Kept).Key);
    }

    [Fact]
    public void Item_that_left_drops_its_snooze_silently()
    {
        var r = Snoozes.Evaluate([new Snooze("gone", null, true)], InboxDiff.Empty, [], Now);
        Assert.Empty(r.Kept);
        Assert.Empty(r.Woken);
    }

    [Fact]
    public void Snooze_times()
    {
        var local = new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.Zero); // Friday
        Assert.Equal(local.AddHours(1), SnoozeTimes.For(SnoozeChoice.OneHour, local, Wh).Until);
        Assert.Equal(local.AddHours(4), SnoozeTimes.For(SnoozeChoice.FourHours, local, Wh).Until);
        var untilChange = SnoozeTimes.For(SnoozeChoice.UntilChange, local, Wh);
        Assert.Null(untilChange.Until);
        Assert.True(untilChange.UntilChange);
        Assert.Equal(new DateTime(2026, 10, 19, 8, 0, 0), SnoozeTimes.For(SnoozeChoice.Tomorrow, local, Wh).Until!.Value.DateTime);
        Assert.Equal(new DateTime(2026, 10, 19, 8, 0, 0), SnoozeTimes.For(SnoozeChoice.NextWeek, local, Wh).Until!.Value.DateTime);
    }

    [Theory]
    [InlineData(MuteScope.Repo, Provider.GitHub, "acme/api", "gh:acme/api")]
    [InlineData(MuteScope.Org, Provider.GitHub, "acme/api", "gh:acme")]
    [InlineData(MuteScope.Project, Provider.GitHub, "acme/api", null)]
    [InlineData(MuteScope.Repo, Provider.Ado, "contoso/platform/api", "ado:contoso/platform/api")]
    [InlineData(MuteScope.Project, Provider.Ado, "contoso/platform/api", "ado:contoso/platform")]
    [InlineData(MuteScope.Org, Provider.Ado, "contoso/platform/api", "ado:contoso")]
    public void Mute_patterns(MuteScope scope, Provider p, string repo, string? expected) =>
        Assert.Equal(expected, MutePatterns.For(It("k", repo, p), scope));

    [Fact]
    public void Expired_timed_mutes_are_pruned() =>
        Assert.Equal(["b"], Snoozes.PruneMutes([new TimedMute("a", Now.AddSeconds(-1)), new TimedMute("b", Now.AddHours(1))], Now).Select(m => m.Pattern));

    [Theory]
    [InlineData(30, "30m left")]
    [InlineData(150, "2h 30m left")]
    public void Remaining_text(int minutes, string text) =>
        Assert.Equal(text, Snoozes.Remaining(new Snooze("k", Now.AddMinutes(minutes), false), Now));

    [Fact]
    public void Remaining_text_for_until_change() =>
        Assert.Equal("until it changes", Snoozes.Remaining(new Snooze("k", null, true), Now));
}
