using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class RowMenusTests
{
    static InboxRow Row(string glyph, string? snooze = null) =>
        new("k", glyph, "acme/api", "T", "https://u", "1h", false, null, null, null, null, false, null, false, false, [], null, null, [],
            RowMode.Collapsed, null, null, false, null, null, "acme/api")
        { SnoozeText = snooze };

    [Fact]
    public void GitHub_row_offers_snooze_repo_and_org_mutes_and_hides()
    {
        var menu = RowMenus.For(Row("GH"));
        Assert.Equal(["Snooze", "Mute repo", "Mute org", "Hide repo", "Hide org"], menu.Select(m => m.Header));
        Assert.Equal(["1 hour", "4 hours", "Until tomorrow", "Next week", "Until it changes"], menu[0].Children.Select(c => c.Header));
        Assert.Equal(["1 hour", "Until tomorrow", "Forever"], menu[1].Children.Select(c => c.Header));
        Assert.Equal(new RowMenuCommand(RowMenuKind.Mute, Scope: MuteScope.Org, Duration: MuteDuration.Forever), menu[2].Children[2].Command);
        // Hide has no duration submenu: one click, undone from the footer or in Settings.
        Assert.Empty(menu[3].Children);
        Assert.Equal(new RowMenuCommand(RowMenuKind.Hide, Scope: MuteScope.Repo), menu[3].Command);
        Assert.Equal(new RowMenuCommand(RowMenuKind.Hide, Scope: MuteScope.Org), menu[4].Command);
    }

    [Fact]
    public void Ado_row_adds_project_mute_and_hide() =>
        Assert.Equal(["Snooze", "Mute repo", "Mute project", "Mute org", "Hide repo", "Hide project", "Hide org"],
            RowMenus.For(Row("ADO")).Select(m => m.Header));

    [Fact]
    public void Snoozed_row_only_unsnoozes() =>
        Assert.Equal(new RowMenuCommand(RowMenuKind.Unsnooze), Assert.Single(RowMenus.For(Row("GH", "30m left"))).Command);
}
