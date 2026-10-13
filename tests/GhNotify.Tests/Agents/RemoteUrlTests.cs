using GhNotify.Agents;

namespace GhNotify.Tests.Agents;

public class RemoteUrlTests
{
    [Theory]
    [InlineData("https://github.com/Acme/Api.git", "github.com/acme/api")]
    [InlineData("https://github.com/acme/api", "github.com/acme/api")]
    [InlineData("git@github.com:acme/api.git", "github.com/acme/api")]
    [InlineData("ssh://git@github.com/acme/api.git", "github.com/acme/api")]
    [InlineData("https://dev.azure.com/contoso/Core%20Platform/_git/api", "dev.azure.com/contoso/core platform/api")]
    [InlineData("https://contoso@dev.azure.com/contoso/Core%20Platform/_git/api", "dev.azure.com/contoso/core platform/api")]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/Core%20Platform/api", "dev.azure.com/contoso/core platform/api")]
    [InlineData("ssh://git@ssh.dev.azure.com:22/v3/contoso/Proj/api", "dev.azure.com/contoso/proj/api")]
    [InlineData("https://contoso.visualstudio.com/Proj/_git/api", "dev.azure.com/contoso/proj/api")]
    [InlineData("https://contoso.visualstudio.com/DefaultCollection/Proj/_git/api", "dev.azure.com/contoso/proj/api")]
    [InlineData("contoso@vs-ssh.visualstudio.com:v3/contoso/Proj/api", "dev.azure.com/contoso/proj/api")]
    [InlineData("https://dev.azure.com/contoso/_git/Proj", "dev.azure.com/contoso/proj/proj")]
    public void Normalizes(string url, string expected) => Assert.Equal(expected, RemoteUrl.Normalize(url));

    [Theory]
    [InlineData("https://gitlab.com/acme/api")]
    [InlineData("C:\\dev\\repo")]
    [InlineData("")]
    [InlineData("https://github.com/acme")]
    public void Unknown_returns_null(string url) => Assert.Null(RemoteUrl.Normalize(url));
}
