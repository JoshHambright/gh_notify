using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class AboutLinksTests
{
    [Fact]
    public void IssueUrl_prefills_the_version_and_os_url_encoded() =>
        Assert.Equal(
            "https://github.com/JoshHambright/gh_notify/issues/new?body=%0A%0A---%0Agh_notify%202.1.0%2C%20Microsoft%20Windows%2010.0",
            AboutLinks.IssueUrl("2.1.0", "Microsoft Windows 10.0"));

    [Fact]
    public void IssueUrl_encodes_reserved_characters_in_the_os_text()
    {
        var url = AboutLinks.IssueUrl("dev", "Linux 6.1 #1 SMP & more");
        Assert.DoesNotContain("#", url);
        Assert.DoesNotContain("&", url);
        Assert.Contains("%23", url);
        Assert.Contains("%26", url);
    }
}
