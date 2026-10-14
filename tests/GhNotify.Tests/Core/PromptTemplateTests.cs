using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class PromptTemplateTests
{
    [Fact]
    public void Fills_all_placeholders()
    {
        var item = new InboxItem("k", Provider.GitHub, ItemKind.ReviewRequested, "acme/api", "Fix it", "https://u", "alice",
            DateTimeOffset.UnixEpoch, null, null, "d")
        { Number = 12 };
        var s = PromptTemplate.Fill("{url}|{repo}|{number}|{title}|{kind}|{provider}|{author}|{detail}|{unknown}", item);
        Assert.Equal("https://u|acme/api|12|Fix it|ReviewRequested|GitHub|alice|d|{unknown}", s);
    }

    [Fact]
    public void Values_containing_placeholders_are_not_resubstituted()
    {
        var item = new InboxItem("k", Provider.GitHub, ItemKind.ReviewRequested, "acme/api", "Rename {url} to {repo}", "https://u", "alice",
            DateTimeOffset.UnixEpoch, null, null, null);
        Assert.Equal("Rename {url} to {repo} @ https://u", PromptTemplate.Fill("{title} @ {url}", item));
    }

    [Fact]
    public void Null_number_and_detail_become_empty()
    {
        var item = new InboxItem("k", Provider.Ado, ItemKind.ApprovalPending, "o/p", "t", "u", "", DateTimeOffset.UnixEpoch, null, null, null);
        Assert.Equal("[][]", PromptTemplate.Fill("[{number}][{detail}]", item));
    }
}
