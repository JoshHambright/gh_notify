using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>Hide (not just mute): the row menu hides a repo, project or org; the footer offers Undo; the footer menu lists hidden patterns.</summary>
public class PopupHideTests
{
    static void Click(MenuItem i) => i.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    static MenuItem RowItem(PopupHarness h, string key, string header) =>
        h.Window.RowMenuFor(h.RowMenuButton(key), key)!.Items.OfType<MenuItem>().First(i => (string?)i.Header == header);

    static string FooterString(PopupHarness h) => string.Concat(h.All<TextBlock>().First(t => t.Name == "FooterText").Inlines!.Select(i => i switch
    {
        Run r => r.Text,
        InlineUIContainer { Child: Button b } => b.Content as string,
        _ => "",
    }));

    [AvaloniaFact]
    public void Hide_repo_removes_every_row_in_it_and_offers_undo_in_the_footer()
    {
        using var h = new PopupHarness();
        Assert.True(h.HasCard(SampleHost.CiKey));
        Click(RowItem(h, SampleHost.CiKey, "Hide repo"));
        h.Pump();

        Assert.Contains($"hide {SampleHost.CiKey} Repo", h.Host.Calls);
        Assert.Equal(["gh:acme/api"], h.Host.HidePatterns);
        Assert.False(h.HasCard(SampleHost.CiKey));
        Assert.True(h.HasCard(SampleHost.ReadyKey)); // acme/web is a different repo
        Assert.Contains(" · Hidden gh:acme/api Undo", FooterString(h));

        h.Click(h.Button("Undo"));
        h.Pump();
        Assert.Contains("unhide gh:acme/api", h.Host.Calls);
        Assert.Empty(h.Host.HidePatterns);
        Assert.True(h.HasCard(SampleHost.CiKey));
        Assert.DoesNotContain("Hidden", FooterString(h));
    }

    [AvaloniaFact]
    public void Hide_org_on_an_ado_row_hides_the_whole_org_and_undo_ends_when_the_popup_is_reopened()
    {
        using var h = new PopupHarness();
        Click(RowItem(h, SampleHost.ApprovalKey, "Hide org"));
        h.Pump();
        Assert.Equal(["ado:contoso"], h.Host.HidePatterns);
        Assert.False(h.HasCard(SampleHost.ApprovalKey));
        Assert.False(h.HasCard(SampleHost.Approval2Key));
        Assert.Contains("Hidden ado:contoso", FooterString(h));

        h.Window.Hide();
        h.Window.ShowNearTray();
        h.Pump();
        Assert.Null(h.Host.JustHidden);
        Assert.DoesNotContain("Hidden", FooterString(h));
        Assert.Equal(["ado:contoso"], h.Host.HidePatterns); // still hidden: only Settings or the footer menu bring it back
    }

    [AvaloniaFact]
    public void The_footer_menu_lists_hidden_patterns_with_counts_and_unhides()
    {
        using var h = new PopupHarness(s => s.HidePatterns.Add("gh:acme"));
        Assert.False(h.HasCard(SampleHost.CiKey));
        var menu = h.Window.FooterMenu();
        var hidden = menu.Items.OfType<MenuItem>().First(i => (string?)i.Header == "Hidden (1)");
        var unhide = (MenuItem)hidden.Items.Single()!;
        Assert.Equal("Unhide gh:acme · 3 items", unhide.Header);

        Click(unhide);
        h.Pump();
        Assert.Contains("unhide gh:acme", h.Host.Calls);
        Assert.True(h.HasCard(SampleHost.CiKey));
    }

    [AvaloniaFact]
    public void The_muted_section_collapses_and_expands_and_the_header_count_ignores_it()
    {
        using var h = new PopupHarness();
        Assert.True(h.HasCard(SampleHost.MutedKey)); // the sample starts it expanded; the live app starts it collapsed
        // 2 approvals, 2 reviews, 1 failing, 1 ready: the muted, waiting and snoozed rows aren't counted.
        Assert.Equal(6, h.Host.HeaderCount);

        h.Host.ToggleSection(InboxPresenter.MutedSectionId);
        h.Pump();
        Assert.False(h.HasCard(SampleHost.MutedKey));
        Assert.Contains("MUTED", h.All<TextBlock>().Select(t => t.Text));
    }
}
