using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

public class PopupFooterTests
{
    static TextBlock Footer(PopupHarness h) => h.All<TextBlock>().First(t => t.Name == "FooterText");

    /// <summary>The footer's text with its inline links flattened ("Updated 12:00 · GitHub ✓ · … · Update ready · v1.2.1 · Restart").</summary>
    static string FooterString(PopupHarness h) => string.Concat(Footer(h).Inlines!.Select(i => i switch
    {
        Run r => r.Text,
        InlineUIContainer { Child: Button b } => b.Content as string,
        InlineUIContainer { Child: TextBlock t } => t.Text,
        _ => "",
    }));

    [AvaloniaFact]
    public void The_footer_shows_the_poll_time_the_sources_and_the_update_link()
    {
        using var h = new PopupHarness();
        var text = FooterString(h);
        Assert.StartsWith("Updated ", text);
        Assert.Contains(" · GitHub ✓", text);
        Assert.Contains(" · ADO ✓", text);
        Assert.EndsWith(" · Update ready · v1.2.1 · Restart", text);
    }

    [AvaloniaFact]
    public void A_failing_source_shows_a_red_cross_with_the_problem_in_a_tooltip()
    {
        using var h = new PopupHarness(s => s.GitHubFailing = true);
        Assert.Contains(" · GitHub ✗", FooterString(h));
        var mark = Footer(h).Inlines!.OfType<InlineUIContainer>().Select(i => i.Child).OfType<TextBlock>().First(t => t.Text == "✗");
        Assert.Equal("GitHub: token expired", ToolTip.GetTip(mark));
    }

    [AvaloniaFact]
    public void No_update_ready_means_no_link()
    {
        using var h = new PopupHarness(s => s.Update = new UpdateStatus(UpdatePhase.Idle, "1.2.0", null, null, null));
        Assert.DoesNotContain("Restart", FooterString(h));
    }

    static void AfterUpdate(SampleHost s)
    {
        s.Update = new UpdateStatus(UpdatePhase.Idle, "2.0.0", null, null, null);
        s.WhatsNewVersion = "2.0.0";
    }

    [AvaloniaFact]
    public void After_an_update_the_footer_links_to_whats_new_and_opening_it_clears_the_link()
    {
        using var h = new PopupHarness(AfterUpdate);
        Assert.EndsWith(" · Updated to v2.0.0 · What's new ✕", FooterString(h));
        h.Click(h.Button("Updated to v2.0.0 · What's new"));
        Assert.Contains("whats-new", h.Host.Calls);
        h.Pump();
        Assert.DoesNotContain("What's new", FooterString(h));
    }

    [AvaloniaFact]
    public void The_whats_new_link_can_be_dismissed_without_opening_it()
    {
        using var h = new PopupHarness(AfterUpdate);
        h.Click(h.Button("✕", Footer(h)));
        h.Pump();
        Assert.DoesNotContain("whats-new", h.Host.Calls);
        Assert.DoesNotContain("What's new", FooterString(h));
    }

    [AvaloniaFact]
    public void A_ready_update_takes_the_footer_over_the_whats_new_link()
    {
        using var h = new PopupHarness(s => s.WhatsNewVersion = "1.2.0");
        Assert.EndsWith(" · Update ready · v1.2.1 · Restart", FooterString(h));
        Assert.DoesNotContain("What's new", FooterString(h));
    }

    [AvaloniaFact]
    public void Restart_with_nothing_running_restarts_straight_away()
    {
        using var h = new PopupHarness();
        h.Click(h.Button("Update ready · v1.2.1 · Restart"));
        Assert.Contains("restart False", h.Host.Calls);
    }

    [AvaloniaFact]
    public void Restart_asks_inline_when_agents_are_running_and_offers_anyway_or_cancel()
    {
        using var h = new PopupHarness(s => s.RunningAgents = 2);
        h.Click(h.Button("Update ready · v1.2.1 · Restart"));
        Assert.Contains("restart False", h.Host.Calls);
        Assert.Contains("2 agents running — ", FooterString(h));
        Assert.NotNull(h.TryButton("Restart anyway"));

        h.Click(h.Button("Cancel", Footer(h)));
        Assert.DoesNotContain("agents running", FooterString(h));

        h.Click(h.Button("Update ready · v1.2.1 · Restart"));
        h.Click(h.Button("Restart anyway"));
        Assert.Contains("restart True", h.Host.Calls);
    }

    [AvaloniaFact]
    public void A_config_error_shows_a_banner_with_a_Fix_in_Settings_link()
    {
        using var h = new PopupHarness();
        var banner = h.All<TextBlock>().First(t => t.Name == "ConfigErrorText");
        Assert.False(banner.IsVisible);

        h.Host.ConfigError = "config.json line 4: unexpected token";
        h.Host.Raise();
        h.Pump();

        Assert.True(banner.IsVisible);
        Assert.Equal("config.json line 4: unexpected token ", ((Run)banner.Inlines![0]).Text);
        h.Click(h.Button("Fix in Settings", banner));
        Assert.Contains("open-settings", h.Host.Calls);

        h.Host.ConfigError = null;
        h.Host.Raise();
        h.Pump();
        Assert.False(banner.IsVisible);
    }

    [AvaloniaFact]
    public void The_refresh_and_theme_buttons_call_the_host()
    {
        using var h = new PopupHarness();
        var before = h.Host.Calls.Count(c => c == "refresh");
        h.Click(h.Button("↻"));
        Assert.Equal(before + 1, h.Host.Calls.Count(c => c == "refresh"));

        var theme = h.Button("☾");
        Assert.Equal("Theme: System", ToolTip.GetTip(theme));
        h.Click(theme);
        Assert.Contains("theme", h.Host.Calls);
        Assert.Equal("Theme: Dark", ToolTip.GetTip(h.Button("☾")));
    }

    [AvaloniaFact]
    public void The_menu_button_opens_the_footer_menu_in_order()
    {
        using var h = new PopupHarness();
        h.Click(h.Window.FindControl<Button>("MenuButton")!);
        var menu = h.Window.OpenMenu!;
        Assert.True(menu.IsOpen);
        var headers = menu.Items.Select(i => i is MenuItem m ? ((string?)m.Header)?.Replace("__", "_") : "-").ToList(); // "__" is how a literal underscore is written in a menu header
        Assert.Equal(["Settings…", "gh_notify v1.2.0", "Check for updates", "Restart to update (v1.2.1)", "-", "Open config", UiPlatform.StartupLabel,
            "Open logs folder", "Muted (2)", "Nothing hidden", "-", "Quit"], headers);
    }

    [AvaloniaFact]
    public void The_footer_menu_entries_call_the_host()
    {
        using var h = new PopupHarness();
        var menu = h.Window.FooterMenu();
        MenuItem Item(string header) => menu.Items.OfType<MenuItem>().First(i => ((string?)i.Header)?.Replace("__", "_") == header);
        void Click(MenuItem i) => i.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Click(Item("Settings…"));
        Click(Item("Check for updates"));
        Click(Item("Open config"));
        Click(Item("Open logs folder"));
        Click(Item("Restart to update (v1.2.1)"));
        Click(Item("Quit"));
        Assert.Contains("open-settings", h.Host.Calls);
        Assert.Contains("check-updates", h.Host.Calls);
        Assert.Contains("open-config", h.Host.Calls);
        Assert.Contains("open-logs", h.Host.Calls);
        Assert.Contains("restart False", h.Host.Calls);
        Assert.Contains("quit", h.Host.Calls);
        Assert.False(Item("gh_notify v1.2.0").IsEnabled);

        var startup = Item(UiPlatform.StartupLabel);
        Assert.False(h.Host.StartWithWindows);
        Click(startup);
        Assert.True(h.Host.StartWithWindows);

        var muted = Item("Muted (2)");
        Click((MenuItem)muted.Items[1]!);
        Assert.Contains("unmute ado:contoso/platform", h.Host.Calls);
        Assert.Equal("Unmute gh:acme/legacy-* · forever", ((MenuItem)muted.Items[0]!).Header);
    }

    [AvaloniaFact]
    public void The_footer_menu_says_when_nothing_is_muted_and_when_updates_are_off()
    {
        using var h = new PopupHarness(s =>
        {
            s.NoMutes = true;
            s.Update = new UpdateStatus(UpdatePhase.DevBuild, "dev", null, null, null);
        });
        var menu = h.Window.FooterMenu();
        var nothing = menu.Items.OfType<MenuItem>().First(i => (string?)i.Header == "Nothing muted");
        Assert.False(nothing.IsEnabled);
        var check = menu.Items.OfType<MenuItem>().First(i => (string?)i.Header == "Check for updates");
        Assert.False(check.IsEnabled);
        Assert.Equal("Updates are off in dev builds", ToolTip.GetTip(check));
        Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), i => ((string?)i.Header)?.StartsWith("Restart") == true);
    }
}
