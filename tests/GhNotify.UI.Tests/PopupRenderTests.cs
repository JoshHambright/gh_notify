using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

public class PopupRenderTests
{
    [AvaloniaFact]
    public void Sample_rows_render_with_section_headers_and_the_header_count()
    {
        using var h = new PopupHarness();
        var keys = h.CardKeys();
        Assert.Contains(SampleHost.ApprovalKey, keys);
        Assert.Contains(SampleHost.ReviewKey, keys);
        Assert.Contains(SampleHost.CiKey, keys);
        Assert.Contains(SampleHost.ReadyKey, keys);
        Assert.Contains(SampleHost.MutedKey, keys);
        Assert.Contains(SampleHost.SnoozedKey, keys);
        Assert.DoesNotContain("gh:waiting:fabrikam/api#88", keys); // "Waiting on others" starts collapsed

        var texts = h.Texts(h.Window);
        Assert.Contains(texts, t => t.StartsWith("APPROVALS (2)", StringComparison.Ordinal)); // every header is a toggle with its count
        Assert.Contains(texts, t => t.StartsWith("REVIEW REQUESTS (", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("WAITING ON OTHERS (2)"));
        Assert.Contains("Add retry with jitter to payment gateway client", texts);
        Assert.Contains("Waiting on you", texts);
        Assert.Equal(h.Host.HeaderCount.ToString(), h.All<TextBlock>().First(t => t.Name == "CountText").Text);
    }

    [AvaloniaFact]
    public void A_row_shows_its_meta_line_badge_and_action_buttons()
    {
        using var h = new PopupHarness();
        var ci = h.Card(SampleHost.CiKey);
        Assert.Contains(h.Texts(ci), t => t.Contains("GH · acme/api #57 · "));
        Assert.Contains(h.Texts(ci), t => t == "✓ Ready"); // the finished agent run
        Assert.NotNull(h.TryButton("Re-run", ci));
        Assert.NotNull(h.TryButton("Re-run failed…", ci));

        var approval = h.Card(SampleHost.ApprovalKey);
        Assert.NotNull(h.TryButton("Approve…", approval));
        Assert.NotNull(h.TryButton("Reject…", approval));
        Assert.Contains("urgent", approval.Classes); // approvals and failing CI carry the red stripe
        Assert.Contains("muted", h.Card(SampleHost.MutedKey).Classes);
    }

    [AvaloniaFact]
    public void The_chevron_expands_a_row_on_the_first_click()
    {
        using var h = new PopupHarness();
        var card = h.Card(SampleHost.ReviewKey);
        Assert.False(h.Shows(card, "Branch"));

        h.Click(h.Chevron(SampleHost.ReviewKey));

        Assert.Contains($"toggle {SampleHost.ReviewKey}", h.Host.Calls);
        card = h.Card(SampleHost.ReviewKey);
        Assert.True(h.Shows(card, "Branch"));
        Assert.True(h.Shows(card, "feature/payment-retries → main"));
        h.Click(h.Chevron(SampleHost.ReviewKey));
        Assert.False(h.Shows(h.Card(SampleHost.ReviewKey), "Branch"));
    }

    [AvaloniaFact]
    public void Expanding_a_row_collapses_the_one_that_was_open()
    {
        using var h = new PopupHarness();
        h.Click(h.Chevron(SampleHost.ReviewKey));
        h.Click(h.Chevron(SampleHost.Review2Key));
        Assert.False(h.Shows(h.Card(SampleHost.ReviewKey), "Branch"));
        Assert.True(h.Shows(h.Card(SampleHost.Review2Key), "Branch"));
    }

    [AvaloniaFact]
    public void A_collapsible_section_header_toggles_on_the_first_click()
    {
        using var h = new PopupHarness();
        Assert.False(h.HasCard("gh:waiting:fabrikam/api#88"));
        var header = h.All<Button>().First(b => b.Tag as string == "waiting");
        h.Click(header);
        Assert.Contains("section waiting", h.Host.Calls);
        Assert.True(h.HasCard("gh:waiting:fabrikam/api#88"));
        Assert.True(h.HasCard("ado:waiting:contoso/platform/portal#214"));
        h.Click(h.All<Button>().First(b => b.Tag as string == "waiting"));
        Assert.False(h.HasCard("gh:waiting:fabrikam/api#88"));
    }

    [AvaloniaFact]
    public void A_snoozed_row_shows_its_remaining_time_and_an_unsnooze_button()
    {
        using var h = new PopupHarness();
        var card = h.Card(SampleHost.SnoozedKey);
        Assert.Contains(h.Texts(card), t => t.Contains("💤"));
        h.Click(h.Button("Unsnooze", card));
        Assert.Contains($"unsnooze {SampleHost.SnoozedKey}", h.Host.Calls);
    }

    [AvaloniaFact]
    public void A_no_op_refresh_keeps_the_row_visuals_and_footer()
    {
        using var h = new PopupHarness();
        var card = h.Card(SampleHost.CiKey);
        var chevron = h.Chevron(SampleHost.CiKey);
        var footer = h.All<TextBlock>().First(t => t.Name == "FooterText");
        var first = footer.Inlines![0];

        h.Host.Raise();
        h.Pump();
        h.Window.Render();

        Assert.Same(card, h.Card(SampleHost.CiKey));
        Assert.Same(chevron, h.Chevron(SampleHost.CiKey));
        Assert.Same(first, footer.Inlines![0]);
    }

    [AvaloniaFact]
    public void A_refresh_between_press_and_release_does_not_swallow_the_click()
    {
        using var h = new PopupHarness();
        var p = h.Center(h.Chevron(SampleHost.CiKey));
        h.Window.MouseDown(p, MouseButton.Left);
        h.Host.Raise(); // e.g. the refresh fired by opening the popup
        h.Pump();
        h.Window.MouseUp(p, MouseButton.Left);
        h.Pump();
        Assert.Contains($"toggle {SampleHost.CiKey}", h.Host.Calls);
    }

    [AvaloniaFact]
    public void A_visible_change_rebuilds_the_rows()
    {
        using var h = new PopupHarness();
        var card = h.Card(SampleHost.CiKey);
        h.Host.HiddenKeys.Add(SampleHost.MutedKey);
        h.Host.Raise();
        h.Pump();
        Assert.NotSame(card, h.Card(SampleHost.CiKey));
        Assert.False(h.HasCard(SampleHost.MutedKey));
    }

    [AvaloniaFact]
    public void An_empty_inbox_says_nothing_is_waiting()
    {
        using var h = new PopupHarness(s => s.Empty = true);
        Assert.Empty(h.CardKeys());
        Assert.Contains("Nothing waiting on you.", h.Texts(h.Window));
        Assert.DoesNotContain("No matches", h.Texts(h.Window));
    }

    [AvaloniaFact]
    public void The_agent_result_is_shown_expanded_with_copy_and_open_in_claude()
    {
        using var h = new PopupHarness();
        h.Click(h.Chevron(SampleHost.CiKey));
        var card = h.Card(SampleHost.CiKey);
        var result = h.All<TextBox>(card).First(t => t.IsReadOnly);
        Assert.True(result.IsEffectivelyVisible);
        Assert.Contains("SUMMARY: one test fixture uses the old section name", result.Text);
        Assert.Contains(h.Texts(card), t => t.StartsWith("Diagnose · ready · $0.31 · 3m 04s"));
        h.Click(h.Button("Copy", card));
        Assert.Contains(h.Host.Calls, c => c.StartsWith("copy "));
        Assert.Contains(h.All<Button>(card), b => b.IsEffectivelyVisible && b.Content as string == "Open in Claude");
    }

    [AvaloniaFact]
    public void Clicking_a_title_opens_the_item_and_the_row_buttons_call_the_host()
    {
        using var h = new PopupHarness();
        var card = h.Card(SampleHost.ReviewKey);
        var title = h.All<TextBlock>(card).First(t => t.Text == "Add retry with jitter to payment gateway client");
        h.Click(title);
        Assert.Contains(h.Host.Calls, c => c.StartsWith("open https://dev.azure.com/fabrikam/_git/pr/482"));
        h.Click(h.Button("Pre-review", card));
        Assert.Contains($"run {SampleHost.ReviewKey}", h.Host.Calls);
    }

    [AvaloniaFact]
    public void The_row_menu_offers_snooze_and_mute_and_forwards_the_choice()
    {
        using var h = new PopupHarness();
        var menu = h.Window.RowMenuFor(h.RowMenuButton(SampleHost.CiKey), SampleHost.CiKey)!;
        Assert.Equal(["Snooze", "Mute repo", "Mute org", "Hide repo", "Hide org"], menu.Items.OfType<MenuItem>().Select(i => i.Header).ToList());
        var ado = h.Window.RowMenuFor(h.RowMenuButton(SampleHost.ReviewKey), SampleHost.ReviewKey)!;
        Assert.Contains("Mute project", ado.Items.OfType<MenuItem>().Select(i => i.Header));

        var snooze = (MenuItem)menu.Items[0]!;
        Assert.Equal(["1 hour", "4 hours", "Until tomorrow", "Next week", "Until it changes"], snooze.Items.OfType<MenuItem>().Select(i => i.Header).ToList());
        ((MenuItem)snooze.Items[1]!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Contains($"snooze {SampleHost.CiKey} FourHours", h.Host.Calls);
        var repo = (MenuItem)menu.Items[1]!;
        ((MenuItem)repo.Items[2]!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Contains($"mute {SampleHost.CiKey} Repo Forever", h.Host.Calls);
    }

    [AvaloniaFact]
    public void The_row_menu_button_opens_a_menu()
    {
        using var h = new PopupHarness();
        h.Click(h.RowMenuButton(SampleHost.CiKey));
        Assert.True(h.Window.OpenMenu is { IsOpen: true });
    }
}
