using GhNotify.Core;

namespace GhNotify.UI.Tests;

public class UpdateUiTests
{
    static UpdateStatus S(UpdatePhase p, string? ready = null) => new(p, "1.2.0", ready, null, null);

    [Fact]
    public void Footer_link_only_when_ready() =>
        Assert.Equal(("Update ready · v1.2.1 · Restart", null),
            (UpdateUi.FooterLink(S(UpdatePhase.Ready, "1.2.1")), UpdateUi.FooterLink(S(UpdatePhase.Idle))));

    [Fact]
    public void Confirm_prompt_pluralizes() =>
        Assert.Equal(("1 agent running — ", "3 agents running — "), (UpdateUi.ConfirmPrompt(1), UpdateUi.ConfirmPrompt(3)));

    [Fact]
    public void Check_item_is_disabled_with_tooltip_in_dev_builds()
    {
        var (header, enabled, tip) = UpdateUi.CheckItem(S(UpdatePhase.DevBuild));
        Assert.Equal("Check for updates", header);
        Assert.False(enabled);
        Assert.Equal("Updates are off in dev builds", tip);
        Assert.True(UpdateUi.CheckItem(S(UpdatePhase.Idle)).Enabled);
        Assert.False(UpdateUi.CheckItem(S(UpdatePhase.Downloading)).Enabled);
    }

    [Fact]
    public void Restart_item_names_the_ready_version() =>
        Assert.Equal(("Restart to update (v1.2.1)", null),
            (UpdateUi.RestartItem(S(UpdatePhase.Ready, "1.2.1")), UpdateUi.RestartItem(S(UpdatePhase.Failed))));

    [Fact]
    public void Settings_status_lines()
    {
        var checkedAt = new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero);
        Assert.Equal($"Up to date · checked {checkedAt.ToLocalTime():HH:mm}", UpdateUi.SettingsStatus(S(UpdatePhase.Idle) with { LastChecked = checkedAt }));
        Assert.Equal("Update ready (v1.2.1) — restart from the popup's ⋯ menu", UpdateUi.SettingsStatus(S(UpdatePhase.Ready, "1.2.1")));
        Assert.Equal("Last check failed: offline", UpdateUi.SettingsStatus(S(UpdatePhase.Failed) with { LastError = "offline" }));
        Assert.Equal("Updates are off in dev builds", UpdateUi.SettingsStatus(S(UpdatePhase.DevBuild)));
        Assert.Equal(("Version 1.2.0", "Version dev build"),
            (UpdateUi.SettingsVersion(S(UpdatePhase.Idle)), UpdateUi.SettingsVersion(S(UpdatePhase.DevBuild) with { CurrentVersion = null })));
    }
}

public class FooterSignatureTests
{
    static readonly SourceStatus[] Src = [new("GitHub", true, null, null), new("ADO:contoso", false, "401", null)];
    static UpdateStatus U(UpdatePhase p, string? v = null) => new(p, "1.2.0", v, null, null);
    static string Sig(UpdateStatus u, bool confirming = false, int agents = 0, SourceStatus[]? src = null) =>
        UpdateUi.FooterSignature("Updated 10:00", src ?? Src, u, confirming, agents);

    [Fact]
    public void Identical_inputs_are_equal() =>
        Assert.Equal(Sig(U(UpdatePhase.Ready, "1.2.1"), true, 2), Sig(U(UpdatePhase.Ready, "1.2.1"), true, 2));

    [Fact]
    public void Phase_confirm_count_or_source_change_differs()
    {
        var ready = Sig(U(UpdatePhase.Ready, "1.2.1"), true, 2);
        Assert.NotEqual(ready, Sig(U(UpdatePhase.Idle), true, 2));
        Assert.NotEqual(ready, Sig(U(UpdatePhase.Ready, "1.2.1"), false, 2));
        Assert.NotEqual(ready, Sig(U(UpdatePhase.Ready, "1.2.1"), true, 3));
        Assert.NotEqual(ready, Sig(U(UpdatePhase.Ready, "1.2.1"), true, 2, [new("GitHub", true, null, null)]));
    }
}
