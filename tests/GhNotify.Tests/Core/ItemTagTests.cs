using GhNotify.Core;

namespace GhNotify.Tests.Core;

public class ItemTagTests
{
    [Fact]
    public void Tag_is_16_lowercase_hex_and_stable()
    {
        var a = ItemTag.For("gh:review:acme/api#12");
        Assert.Matches("^[0-9a-f]{16}$", a);
        Assert.Equal(a, ItemTag.For("gh:review:acme/api#12"));
        Assert.NotEqual(a, ItemTag.For("gh:review:acme/api#13"));
        Assert.NotEqual(a, ItemTag.ForAgent("gh:review:acme/api#12"));
    }
}
