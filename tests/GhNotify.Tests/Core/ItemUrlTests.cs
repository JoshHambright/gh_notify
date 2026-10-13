using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ItemUrlTests
{
    [Theory]
    [InlineData("https://github.com/acme/api/pull/12", true)]
    [InlineData("HTTPS://dev.azure.com/contoso/p/_git/r/pullrequest/1", true)]
    [InlineData("http://github.com/acme/api/pull/12", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData(@"C:\Windows\System32\calc.exe", false)]
    [InlineData("ms-settings:", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData("github.com/acme/api", false)]
    public void Only_absolute_https_urls_are_openable(string url, bool expected) =>
        Assert.Equal(expected, ItemUrl.IsOpenable(url));
}
