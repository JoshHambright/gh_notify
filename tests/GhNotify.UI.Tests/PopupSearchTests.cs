using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace GhNotify.UI.Tests;

public class PopupSearchTests
{
    static void Search(PopupHarness h, string query)
    {
        h.Press(Key.F, KeyModifiers.Control);
        h.SearchBox.Text = query;
        h.Pump();
    }

    [AvaloniaFact]
    public void The_search_box_is_hidden_until_asked_for_and_the_magnifier_toggles_it()
    {
        using var h = new PopupHarness();
        var bar = h.All<Border>().First(b => b.Name == "SearchBar");
        Assert.False(bar.IsVisible);
        h.Click(h.Button("🔍"));
        Assert.True(bar.IsVisible);
        Assert.True(h.SearchBox.IsFocused);
        h.Click(h.Button("🔍")); // empty box: the magnifier closes it again
        Assert.False(bar.IsVisible);
    }

    [AvaloniaFact]
    public void A_query_filters_by_author_title_repo_and_number()
    {
        using var h = new PopupHarness();
        Search(h, "alice");
        Assert.Equal(new[] { SampleHost.ReviewKey, SampleHost.MutedKey }.Order(), h.CardKeys().Order());

        Search(h, "jitter");
        Assert.Equal([SampleHost.ReviewKey], h.CardKeys());

        Search(h, "#57");
        Assert.Equal([SampleHost.CiKey], h.CardKeys());
        Search(h, "57");
        Assert.Equal([SampleHost.CiKey], h.CardKeys());

        Search(h, "acme/web");
        Assert.Contains(SampleHost.ReadyKey, h.CardKeys());
        Search(h, "BOB web");
        Assert.Equal(new[] { SampleHost.SnoozedKey, SampleHost.Review2Key }.Order(), h.CardKeys().Order()); // all terms must match
    }

    [AvaloniaFact]
    public void Sections_that_lose_all_their_rows_are_dropped_and_counts_are_not_filtered_in_the_header()
    {
        using var h = new PopupHarness();
        var count = h.All<TextBlock>().First(t => t.Name == "CountText").Text;
        Search(h, "#57");
        var texts = h.Texts(h.Window);
        Assert.DoesNotContain(texts, t => t.StartsWith("APPROVALS", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.StartsWith("REVIEW REQUESTS", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("MY PRS — FAILING (", StringComparison.Ordinal));
        Assert.Equal(count, h.All<TextBlock>().First(t => t.Name == "CountText").Text);
    }

    [AvaloniaFact]
    public void Collapsed_sections_are_searched_while_a_query_is_active()
    {
        using var h = new PopupHarness();
        Assert.False(h.HasCard("gh:waiting:fabrikam/api#88"));
        Search(h, "cache token");
        Assert.Equal(["gh:waiting:fabrikam/api#88"], h.CardKeys());
        Search(h, "");
        Assert.False(h.HasCard("gh:waiting:fabrikam/api#88"));
    }

    [AvaloniaFact]
    public void Nothing_matching_shows_No_matches()
    {
        using var h = new PopupHarness();
        Search(h, "zzzz");
        Assert.Empty(h.CardKeys());
        Assert.Contains("No matches", h.Texts(h.Window));
        Assert.DoesNotContain("Nothing waiting on you.", h.Texts(h.Window));

        Search(h, "");
        Assert.DoesNotContain("No matches", h.Texts(h.Window));
        Assert.NotEmpty(h.CardKeys());
    }

    [AvaloniaFact]
    public void The_batch_buttons_are_off_while_searching()
    {
        using var h = new PopupHarness();
        Assert.NotNull(h.TryButton("Approve all…"));
        Search(h, "contoso");
        Assert.Equal(2, h.CardKeys().Count(k => k.StartsWith("ado:approval:")));
        Assert.Null(h.TryButton("Approve all…"));
    }

    [AvaloniaFact]
    public void The_query_is_cleared_when_the_popup_hides()
    {
        using var h = new PopupHarness();
        var all = h.CardKeys();
        Search(h, "alice");
        Assert.True(h.CardKeys().Count < all.Count);

        h.Window.Hide();
        h.Pump();
        h.Show();

        Assert.True(string.IsNullOrEmpty(h.SearchBox.Text));
        Assert.False(h.All<Border>().First(b => b.Name == "SearchBar").IsVisible);
        Assert.Equal(all, h.CardKeys());
    }

    [AvaloniaFact]
    public void The_selection_follows_the_filtered_list()
    {
        using var h = new PopupHarness();
        Search(h, "#57");
        Assert.Equal(SampleHost.CiKey, h.Window.SelectedKey);
        h.Press(Key.Down); // from the search box: the first (only) row
        Assert.Equal(SampleHost.CiKey, h.Window.SelectedKey);
        Assert.True(h.Ring(SampleHost.CiKey).IsVisible);
    }
}
