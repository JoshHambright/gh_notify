using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class WhatsNewTests
{
    [Theory]
    [InlineData("2.0.0", "https://github.com/JoshHambright/gh_notify/releases/tag/v2.0.0")]
    [InlineData("0.0.0-dev", "https://github.com/JoshHambright/gh_notify/releases")]
    [InlineData("99.0.0-ci", "https://github.com/JoshHambright/gh_notify/releases")]
    [InlineData(null, "https://github.com/JoshHambright/gh_notify/releases")]
    public void UrlFor_links_a_release_page_or_the_releases_list(string? version, string expected) =>
        Assert.Equal(expected, WhatsNew.UrlFor(version));

    [Fact]
    public void A_fresh_install_announces_nothing_and_remembers_the_version() =>
        Assert.Equal(((string?)null, (string?)"2.0.0"), WhatsNew.OnStart(null, null, "2.0.0", usedBefore: false));

    [Fact]
    public void An_upgrade_from_a_release_before_this_feature_announces_the_new_version() =>
        Assert.Equal(((string?)"2.0.0", (string?)"2.0.0"), WhatsNew.OnStart(null, null, "2.0.0", usedBefore: true));

    [Fact]
    public void A_version_change_announces_the_new_version() =>
        Assert.Equal(((string?)"2.1.0", (string?)"2.1.0"), WhatsNew.OnStart("2.0.0", null, "2.1.0", usedBefore: true));

    [Fact]
    public void A_restart_on_the_same_version_keeps_an_unopened_announcement() =>
        Assert.Equal(((string?)"2.0.0", (string?)"2.0.0"), WhatsNew.OnStart("2.0.0", "2.0.0", "2.0.0", usedBefore: true));

    [Fact]
    public void A_restart_on_the_same_version_after_dismissing_announces_nothing() =>
        Assert.Equal(((string?)null, (string?)"2.0.0"), WhatsNew.OnStart("2.0.0", null, "2.0.0", usedBefore: true));

    [Fact]
    public void A_non_release_version_changes_nothing() =>
        Assert.Equal(((string?)"2.0.0", (string?)"2.0.0"), WhatsNew.OnStart("2.0.0", "2.0.0", "99.0.0-ci", usedBefore: true));
}
