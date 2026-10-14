using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ItemTextTests
{
    static InboxItem It(int? number) =>
        new("k", Provider.GitHub, ItemKind.ReviewRequested, "acme/api", "T", "https://u", "a", DateTimeOffset.UnixEpoch, null, null, null) { Number = number };

    [Fact]
    public void RepoAndNumber_appends_number_when_present() => Assert.Equal("acme/api #57", ItemText.RepoAndNumber(It(57)));

    [Fact]
    public void RepoAndNumber_is_just_the_repo_without_a_number() => Assert.Equal("acme/api", ItemText.RepoAndNumber(It(null)));
}
