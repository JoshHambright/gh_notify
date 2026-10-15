using GhNotify.Platform.Windows;
using GhNotify.UI;

namespace GhNotify.Platform.Windows.Tests;

public class ToastNotifierTests
{
    [Fact]
    public void A_url_toast_activates_with_a_url_action_and_its_tag()
    {
        var (tag, action) = ToastNotifier.Translate(new Microsoft.Toolkit.Uwp.Notifications.ToastArguments().Add("tag", "gh:review").Add("url", "https://github.com/acme/api/pull/5").ToString());
        Assert.Equal("gh:review", tag);
        Assert.True(ToastActions.TryGetUrl(action, out var url));
        Assert.Equal("https://github.com/acme/api/pull/5", url);
    }

    [Fact]
    public void A_plain_toast_activates_as_open()
    {
        var (tag, action) = ToastNotifier.Translate(new Microsoft.Toolkit.Uwp.Notifications.ToastArguments().Add("tag", "summary").Add("action", "open").ToString());
        Assert.Equal("summary", tag);
        Assert.Equal(ToastActions.Open, action);
    }

    [Fact]
    public void Activations_before_the_first_handler_are_replayed_to_it_in_order()
    {
        var n = new ToastNotifier();
        n.Raise("a", "open");
        n.Raise("b", "open");
        var got = new List<string>();
        n.Activated += (tag, _) => got.Add(tag);
        Assert.Equal(["a", "b"], got);
        n.Raise("c", "open");
        Assert.Equal(["a", "b", "c"], got);
        n.Activated += (tag, _) => got.Add("late-" + tag); // a second handler gets nothing replayed
        Assert.Equal(3, got.Count);
    }

    [Fact]
    public void Toast_actions_round_trip()
    {
        Assert.True(ToastActions.TryGetUrl(ToastActions.ForUrl("https://x.test/a"), out var url));
        Assert.Equal("https://x.test/a", url);
        Assert.False(ToastActions.TryGetUrl(ToastActions.Open, out _));
    }
}
