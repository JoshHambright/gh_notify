using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class RowMenusTests
{
    static InboxRow Row(string glyph, string? snooze = null) =>
        new("k", glyph, "acme/api", "T", "https://u", "1h", false, null, null, null, null, false, null, false, false, [], null, null, [],
            RowMode.Collapsed, null, null, false, null, null, "acme/api") { SnoozeText = snooze };

    [Fact]
    public void GitHub_row_offers_snooze_and_repo_and_org_mutes()
    {
        var menu = RowMenus.For(Row("GH"));
        Assert.Equal(["Snooze", "Mute repo", "Mute org"], menu.Select(m => m.Header));
        Assert.Equal(["1 hour", "4 hours", "Until tomorrow", "Next week", "Until it changes"], menu[0].Children.Select(c => c.Header));
        Assert.Equal(["1 hour", "Until tomorrow", "Forever"], menu[1].Children.Select(c => c.Header));
        Assert.Equal(new RowMenuCommand(RowMenuKind.Mute, Scope: MuteScope.Org, Duration: MuteDuration.Forever), menu[2].Children[2].Command);
    }

    [Fact]
    public void Ado_row_adds_project_mute() =>
        Assert.Equal(["Snooze", "Mute repo", "Mute project", "Mute org"], RowMenus.For(Row("ADO")).Select(m => m.Header));

    [Fact]
    public void Snoozed_row_only_unsnoozes() =>
        Assert.Equal(new RowMenuCommand(RowMenuKind.Unsnooze), Assert.Single(RowMenus.For(Row("GH", "30m left"))).Command);
}
