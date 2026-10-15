using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>The settings window and its six pages, headless. Theme tokens come from <see cref="SettingsTokens"/> until the shared themes land.</summary>
public class SettingsWindowTests
{
    static SettingsWindow Open(ISettingsHost host)
    {
        SettingsTokens.Ensure();
        return new SettingsWindow(host);
    }

    static SettingsWindow Show(ISettingsHost host)
    {
        var w = Open(host);
        w.Show();
        return w;
    }

    static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ghnotify-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static ConfigStore StoreWith(string dir, string json)
    {
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, json);
        var store = new ConfigStore(path);
        store.Load();
        return store;
    }

    static IEnumerable<T> Find<T>(Control root) where T : Control => root.GetLogicalDescendants().OfType<T>();

    // ----- load -----

    [AvaloniaFact]
    public void Every_page_renders_and_opening_does_not_dirty_the_draft()
    {
        foreach (var page in Enum.GetValues<SettingsPage>())
        {
            var w = Show(new SampleSettingsHost());
            w.ShowPage(page);
            w.UpdateLayout();
            Assert.NotEmpty(Find<Control>(w.CurrentPage));
            Assert.False(w.Draft.IsDirty, $"opening {page} changed the draft");
            Assert.False(w.CanSave);
            w.Discard();
        }
    }

    [AvaloniaFact]
    public void Hooks_and_prompt_profiles_render()
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """
                {
                  "agentProfiles": { "summarize": { "prompt": "Summarize {url}", "requiresSkill": "summarize" } },
                  "rules": [ { "agent": "auto" } ],
                  "hooks": [ { "event": "itemAdded", "match": { "repo": "acme/*" }, "run": [ "pwsh", "-File", "C:\\hooks\\added.ps1" ] } ]
                }
                """);
            Assert.Null(store.Error);
            var w = Show(new SampleSettingsHost(store));
            w.ShowPage(SettingsPage.Workflows);
            var workflows = (WorkflowsPage)w.CurrentPage;
            workflows.SelectProfile("summarize");
            w.UpdateLayout();
            Assert.True(workflows.EditorShown);
            Assert.Equal("Summarize {url}", workflows.PromptBox.Text);
            Assert.Single(workflows.HookCards);
            Assert.False(w.Draft.IsDirty);
            w.Discard();
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void The_general_page_shows_the_loaded_values()
    {
        var w = Open(new SampleSettingsHost());
        var general = (GeneralPage)w.Page(SettingsPage.General);
        Assert.Equal("120", general.PollBox.Text);
        Assert.Equal(w.Draft.Config.Hotkey, general.HotkeyBox.Text);
        Assert.True(general.StartupCheck.IsChecked); // the sample host starts with Windows
        Assert.True(general.ThemeSystem.IsChecked);
        Assert.True(general.MergeDefault.IsChecked);
        Assert.Equal("Version 1.2.0", general.VersionText.Text);
        Assert.Equal("120", general.PollBox.Text);
        var sources = (SourcesPage)w.Page(SettingsPage.Sources);
        Assert.Equal("fab-cp", sources.Aliases.Single().Name);
        var notifications = (NotificationsPage)w.Page(SettingsPage.Notifications);
        Assert.Equal(2, notifications.Rows.Count);
        w.Discard();
    }

    // ----- save, validation -----

    [AvaloniaFact]
    public void Save_is_disabled_until_a_valid_change()
    {
        var w = Open(new SampleSettingsHost());
        Assert.False(w.CanSave);
        w.Draft.Config.PollSeconds = 1;
        w.Revalidate();
        Assert.False(w.CanSave);
        Assert.False(w.SaveEnabled);
        Assert.Equal("1 problem on General", w.StatusMessage);
        Assert.True(w.NavDotShown(SettingsPage.General));
        Assert.Single(((GeneralPage)w.Page(SettingsPage.General)).ShownErrors);
        w.Draft.Config.PollSeconds = 180;
        w.Revalidate();
        Assert.True(w.CanSave);
        Assert.True(w.SaveEnabled);
        Assert.False(w.NavDotShown(SettingsPage.General));
        Assert.Equal("Unsaved changes", w.StatusMessage);
        w.Discard();
    }

    [AvaloniaFact]
    public void Save_writes_config_and_keeps_a_backup()
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            var w = Open(new SampleSettingsHost(store));
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.Contains("\"pollSeconds\": 150", File.ReadAllText(store.Path));
            Assert.True(File.Exists(store.Path + ".bak"));
            Assert.Equal(150, store.Current.PollSeconds);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void Editing_a_field_then_saving_writes_the_value()
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            var w = Open(new SampleSettingsHost(store));
            var general = (GeneralPage)w.Page(SettingsPage.General);
            general.PollBox.Text = "200";
            w.Revalidate();
            Assert.True(w.CanSave);
            w.Save();
            Assert.Equal(200, store.Current.PollSeconds);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void A_file_changed_on_disk_shows_the_conflict_bar_and_overwrite_saves()
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            var w = Open(new SampleSettingsHost(store));
            File.WriteAllText(store.Path, """{ "pollSeconds": 300 }""");
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.ConflictBarShown);
            Assert.Contains("300", File.ReadAllText(store.Path));
            w.Overwrite();
            Assert.Contains("\"pollSeconds\": 150", File.ReadAllText(store.Path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void The_conflict_bar_buttons_reload_and_overwrite()
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            var w = Show(new SampleSettingsHost(store));
            File.WriteAllText(store.Path, """{ "pollSeconds": 300 }""");
            store.Load();
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.ConflictBarShown);
            Assert.Contains(Find<Button>(w), b => b.Content as string == "Overwrite");
            Find<Button>(w).First(b => b.Content as string == "Reload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(w.ConflictBarShown);
            Assert.Equal(300, w.Draft.Config.PollSeconds);
            Assert.False(w.Draft.IsDirty);
            w.Discard();
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void A_file_created_after_opening_is_a_conflict_not_an_overwrite()
    {
        var dir = TempDir();
        try
        {
            var store = new ConfigStore(Path.Combine(dir, "config.json")); // not loaded: no file when the window opens
            var w = Open(new SampleSettingsHost(store));
            Assert.Null(w.Draft.FileHashAtOpen);
            File.WriteAllText(store.Path, """{ "pollSeconds": 300 }""");
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.ConflictBarShown);
            Assert.Contains("300", File.ReadAllText(store.Path));
            w.Discard();
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void Comments_warn_once_before_saving()
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, "{\n  // poll less often\n  \"pollSeconds\": 120\n}");
            var host = new SampleSettingsHost(store);
            var w = Open(host);
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.CommentsBarShown);
            Assert.Contains("// poll less often", File.ReadAllText(store.Path));
            w.SaveAnyway();
            Assert.True(host.CommentsWarningShown);
            Assert.Contains("\"pollSeconds\": 150", File.ReadAllText(store.Path));
            Assert.Contains("// poll less often", File.ReadAllText(store.Path + ".bak"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void Cancel_with_changes_asks_inline_and_keeps_the_window()
    {
        var w = Show(new SampleSettingsHost());
        w.Draft.Config.PollSeconds = 150;
        w.RequestClose();
        Assert.True(w.DiscardBarShown);
        Assert.True(w.IsVisible);
        w.Discard();
        Assert.False(w.IsVisible);
    }

    [AvaloniaFact]
    public void Cancel_without_changes_closes_at_once()
    {
        var w = Show(new SampleSettingsHost());
        w.RequestClose();
        Assert.False(w.DiscardBarShown);
        Assert.False(w.IsVisible);
    }

    [AvaloniaFact]
    public void Local_choices_apply_on_save_without_touching_config()
    {
        var host = new SampleSettingsHost();
        var w = Open(host);
        var general = (GeneralPage)w.Page(SettingsPage.General);
        general.ThemeDark.IsChecked = true;
        Assert.Equal(["90%", "100%", "115%", "130%"], general.TextSizes.Select(r => (string?)r.Content));
        Assert.True(general.TextSizes[1].IsChecked);
        general.TextSizes[2].IsChecked = true;
        w.Revalidate();
        Assert.True(w.CanSave);
        Assert.False(w.Draft.IsDirty);
        w.Save();
        Assert.Equal(GhNotify.Core.ThemeMode.Dark, host.ThemeMode);
        Assert.Equal(1.15, host.TextScale);
    }

    [AvaloniaFact]
    public void Reset_rebuilds_the_pages_from_defaults()
    {
        var w = Open(new SampleSettingsHost());
        var before = w.Page(SettingsPage.Workflows);
        w.ResetToDefaults();
        Assert.Empty(w.Draft.Config.Rules);
        Assert.NotSame(before, w.Page(SettingsPage.Workflows));
        Assert.True(w.CanSave);
        w.Discard();
    }

    [AvaloniaFact]
    public void Reset_needs_two_clicks()
    {
        var w = Open(new SampleSettingsHost());
        var advanced = (AdvancedPage)w.Page(SettingsPage.Advanced);
        advanced.OnReset();
        Assert.True(advanced.Armed);
        Assert.NotEmpty(w.Draft.Config.Rules);
        advanced.OnReset();
        Assert.Empty(w.Draft.Config.Rules);
        w.Discard();
    }

    [AvaloniaFact]
    public void A_broken_config_on_disk_is_never_silently_replaced()
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            const string broken = """{ "pollSeconds": 5, "note": "hand edit" }""";
            File.WriteAllText(store.Path, broken);
            store.Load(); // fails validation: Current stays the last good config
            Assert.NotNull(store.Error);
            var w = Open(new SampleSettingsHost(store));
            Assert.True(w.ConfigErrorShown);
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.ConflictBarShown);
            Assert.Equal(broken, File.ReadAllText(store.Path));
            w.Overwrite(); // the user's explicit choice
            Assert.Contains("\"pollSeconds\": 150", File.ReadAllText(store.Path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void An_edit_on_disk_not_yet_reloaded_conflicts()
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            const string edited = """{ "pollSeconds": 300 }""";
            File.WriteAllText(store.Path, edited); // inside the watcher's debounce: not reloaded yet
            var w = Open(new SampleSettingsHost(store));
            Assert.False(w.ConfigErrorShown);
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.ConflictBarShown);
            Assert.Equal(edited, File.ReadAllText(store.Path));
            w.Discard();
        }
        finally { Directory.Delete(dir, true); }
    }

    // ----- number inputs -----

    [AvaloniaFact]
    public void Text_that_is_not_a_number_blocks_save()
    {
        var w = Show(new SampleSettingsHost());
        var general = (GeneralPage)w.Page(SettingsPage.General);
        general.PollBox.Text = "12a";
        w.Revalidate();
        Assert.False(w.CanSave);
        Assert.Equal("1 problem on General", w.StatusMessage);
        Assert.Equal(120, w.Draft.Config.PollSeconds);
        general.PollBox.Text = "150";
        w.Revalidate();
        Assert.True(w.CanSave);
        Assert.Equal(150, w.Draft.Config.PollSeconds);
        w.Discard();
    }

    [AvaloniaFact]
    public void Every_number_box_reports_bad_text_on_its_own_page()
    {
        var w = Open(new SampleSettingsHost());
        var sources = (SourcesPage)w.Page(SettingsPage.Sources);
        sources.MaxRepoCharsBox.Text = "x";
        w.Revalidate();
        Assert.Equal("1 problem on Sources", w.StatusMessage);
        Assert.Contains("Max repo name length: enter a whole number", sources.ShownErrors);
        sources.MaxRepoCharsBox.Text = "30";
        w.Revalidate();
        Assert.Equal(30, w.Draft.Config.Display.MaxRepoChars);
        Assert.Empty(sources.ShownErrors);
        w.Discard();
    }

    [AvaloniaFact]
    public void Out_of_range_numbers_are_validation_errors()
    {
        var w = Open(new SampleSettingsHost());
        var general = (GeneralPage)w.Page(SettingsPage.General);
        general.PollBox.Text = "1"; // parses, but the draft's rules reject it
        w.Revalidate();
        Assert.False(w.CanSave);
        Assert.Equal(1, w.Draft.Config.PollSeconds);
        Assert.NotEmpty(general.ShownErrors);
        w.Discard();
    }

    [AvaloniaFact]
    public void Max_lines_that_is_not_a_positive_number_is_an_error_not_a_clear()
    {
        var w = Open(new SampleSettingsHost());
        var workflows = (WorkflowsPage)w.Page(SettingsPage.Workflows);
        var row = workflows.RuleRows.First();
        row.MaxLines = "4OO";
        w.Revalidate();
        Assert.Equal(400, row.Rule.Match!.MaxChangedLines);
        Assert.False(w.CanSave);
        Assert.Equal("1 problem on Workflows", w.StatusMessage);
        row.MaxLines = "0";
        Assert.True(row.MaxLinesInvalid);
        row.MaxLines = "";
        w.Revalidate();
        Assert.Null(row.Rule.Match!.MaxChangedLines);
        Assert.True(w.CanSave);
        w.Discard();
    }

    // ----- hotkey -----

    [AvaloniaFact]
    public void Hotkey_capture_records_the_combination_and_clear_turns_it_off()
    {
        var w = Open(new SampleSettingsHost());
        var general = (GeneralPage)w.Page(SettingsPage.General);
        general.CaptureHotkey(Key.H, KeyModifiers.Control | KeyModifiers.Alt);
        Assert.Equal("Ctrl+Alt+H", w.Draft.Config.Hotkey);
        Assert.Equal("Ctrl+Alt+H", general.HotkeyBox.Text);
        Assert.False(general.HotkeyErrorText.IsVisible);
        general.CaptureHotkey(Key.D5, KeyModifiers.Meta | KeyModifiers.Shift);
        Assert.Equal("Shift+Win+5", w.Draft.Config.Hotkey);
        w.Revalidate();
        Assert.True(w.CanSave);

        general.HotkeyClear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("", w.Draft.Config.Hotkey);
        Assert.Equal("Off", general.HotkeyBox.Text);
        Assert.False(general.HotkeyClear.IsEnabled);
        w.Discard();
    }

    [AvaloniaFact]
    public void Hotkey_capture_ignores_lone_modifiers_and_explains_bad_combinations()
    {
        var w = Open(new SampleSettingsHost());
        var general = (GeneralPage)w.Page(SettingsPage.General);
        var before = w.Draft.Config.Hotkey;
        general.CaptureHotkey(Key.LeftCtrl, KeyModifiers.Control);
        Assert.Equal(before, w.Draft.Config.Hotkey);
        general.CaptureHotkey(Key.Space, KeyModifiers.Control);
        Assert.Equal(before, w.Draft.Config.Hotkey);
        Assert.True(general.HotkeyErrorText.IsVisible);
        Assert.Contains("F1–F12", general.HotkeyErrorText.Text);
        general.CaptureHotkey(Key.G, KeyModifiers.None); // a bare letter is refused by the parser
        Assert.Equal(before, w.Draft.Config.Hotkey);
        Assert.True(general.HotkeyErrorText.IsVisible);
        general.CaptureHotkey(Key.Back, KeyModifiers.None); // Backspace on an empty modifier set clears
        Assert.Equal("", w.Draft.Config.Hotkey);
        Assert.False(general.HotkeyErrorText.IsVisible);
        w.Discard();
    }

    [AvaloniaFact]
    public void A_key_press_in_the_hotkey_box_is_captured_but_tab_and_escape_are_not()
    {
        var w = Show(new SampleSettingsHost());
        var general = (GeneralPage)w.Page(SettingsPage.General);
        var down = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt };
        general.HotkeyBox.RaiseEvent(down);
        Assert.True(down.Handled);
        Assert.Equal("Ctrl+Alt+K", w.Draft.Config.Hotkey);
        foreach (var key in new[] { Key.Tab, Key.Escape })
        {
            var other = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
            general.HotkeyBox.RaiseEvent(other);
            Assert.Equal("Ctrl+Alt+K", w.Draft.Config.Hotkey); // never captured as a hotkey
        }
        Assert.Equal("Ctrl+Alt+K", w.Draft.Config.Hotkey);
        w.Discard();
    }

    [AvaloniaFact]
    public void The_in_use_line_shows_only_for_the_combination_the_app_tried()
    {
        var host = new SampleSettingsHost { HotkeyError = "Win+Shift+G is in use by another app — pick another" };
        var w = Open(host);
        var general = (GeneralPage)w.Page(SettingsPage.General);
        var tried = host.CurrentConfig.Hotkey;
        Assert.True(general.HotkeyErrorText.IsVisible);
        Assert.Equal(host.HotkeyError, general.HotkeyErrorText.Text);
        general.CaptureHotkey(Key.H, KeyModifiers.Control | KeyModifiers.Alt);
        Assert.False(general.HotkeyErrorText.IsVisible); // a different combination: the old clash is not about it
        Assert.NotEqual(tried, w.Draft.Config.Hotkey);
        w.Discard();
    }

    // ----- merge method -----

    [AvaloniaFact]
    public void The_merge_method_picker_sets_and_clears_the_default()
    {
        var w = Open(new SampleSettingsHost());
        var general = (GeneralPage)w.Page(SettingsPage.General);
        Assert.Null(w.Draft.Config.Actions.MergeMethod);
        general.MergeSquash.IsChecked = true;
        Assert.Equal(MergeMethod.Squash, w.Draft.Config.Actions.MergeMethod);
        general.MergeRebase.IsChecked = true;
        Assert.Equal(MergeMethod.Rebase, w.Draft.Config.Actions.MergeMethod);
        general.MergeMerge.IsChecked = true;
        Assert.Equal(MergeMethod.Merge, w.Draft.Config.Actions.MergeMethod);
        w.Revalidate();
        Assert.True(w.CanSave);
        general.MergeDefault.IsChecked = true;
        Assert.Null(w.Draft.Config.Actions.MergeMethod);
        Assert.False(w.Draft.IsDirty);
        w.Discard();
    }

    [AvaloniaFact]
    public void The_merge_method_shows_the_loaded_choice()
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "actions": { "mergeMethod": "squash" } }""");
            var w = Open(new SampleSettingsHost(store));
            var general = (GeneralPage)w.Page(SettingsPage.General);
            Assert.True(general.MergeSquash.IsChecked);
            Assert.False(general.MergeDefault.IsChecked);
            w.Discard();
        }
        finally { Directory.Delete(dir, true); }
    }

    // ----- other pages -----

    [AvaloniaFact]
    public void Sources_add_watch_repo_org_and_alias()
    {
        var w = Open(new SampleSettingsHost());
        var sources = (SourcesPage)w.Page(SettingsPage.Sources);
        sources.AddWatchRepo("acme/web");
        sources.AddWatchRepo("ACME/web"); // case-insensitive duplicate
        Assert.Equal(["acme/api", "acme/web"], w.Draft.Config.GitHub.WatchRepos);
        sources.AddOrg("northwind");
        Assert.Contains("northwind", w.Draft.Config.Ado.Orgs);
        var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
        sources.OrgBox.Text = "tailspin";
        sources.OrgBox.RaiseEvent(key);
        Assert.Contains("tailspin", w.Draft.Config.Ado.Orgs);
        Assert.Equal("", sources.OrgBox.Text);
        Assert.True(w.Draft.IsDirty);
        w.Discard();
    }

    [AvaloniaFact]
    public async Task Testing_ado_sign_in_shows_the_result()
    {
        var w = Open(new SampleSettingsHost());
        var sources = (SourcesPage)w.Page(SettingsPage.Sources);
        await sources.TestSignInAsync();
        Assert.Equal("✓ Signed in", sources.SignInResult.Text);
        Assert.True(sources.TestSignInButton.IsEnabled);
        w.Discard();
    }

    [AvaloniaFact]
    public void Filters_add_a_pattern_and_hint_counts_matches()
    {
        var w = Open(new SampleSettingsHost());
        var filters = (FiltersPage)w.Page(SettingsPage.Filters);
        filters.MuteBox.Text = "acme/legacy-*";
        Assert.Equal("matches 1 current item", filters.MuteHint.Text);
        filters.MuteBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Contains("acme/legacy-*", w.Draft.Config.Filters.Mute);
        Assert.Equal("", filters.MuteBox.Text);
        Assert.Equal(["ado:contoso/platform · 45m left"], filters.TimedMuteLines);
        w.Discard();
    }

    [AvaloniaFact]
    public void Notification_rules_reorder_and_the_test_button_plays_the_sound()
    {
        var host = new SampleSettingsHost();
        var w = Show(host);
        w.ShowPage(SettingsPage.Notifications);
        var page = (NotificationsPage)w.CurrentPage;
        w.UpdateLayout();
        var play = Find<Button>(page).First(b => b.Content as string == "▶");
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["builtin:alarm"], host.PlayedSounds);
        var down = Find<Button>(page).First(b => b.Content as string == "↓");
        down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(SampleSettingsHost.CustomSound, w.Draft.Config.Notifications.Sounds[0].Sound);
        w.Discard();
    }

    [AvaloniaFact]
    public void Choosing_custom_file_through_the_binding_changes_nothing()
    {
        var rule = new SoundRule { Sound = "builtin:chime" };
        var row = new SoundRow(rule, () => { });
        row.SoundIndex = row.CustomIndex; // e.g. arrowing onto "Custom file…": the page opens the picker on commit, not here
        Assert.Equal("builtin:chime", rule.Sound);
        row.SetCustomSound(SampleSettingsHost.CustomSound);
        Assert.Equal(SampleSettingsHost.CustomSound, rule.Sound);
    }

    // ----- workflows -----

    [AvaloniaFact]
    public void Renaming_a_profile_commits_on_leaving_the_box()
    {
        var w = Open(new SampleSettingsHost());
        var workflows = (WorkflowsPage)w.Page(SettingsPage.Workflows);
        workflows.SelectProfile("triage");
        workflows.NameBox.Text = "triage-ci";
        Assert.True(w.Draft.Config.AgentProfiles.ContainsKey("triage")); // typing alone renames nothing
        workflows.CommitRename();
        Assert.True(w.Draft.Config.AgentProfiles.ContainsKey("triage-ci"));
        Assert.Equal("triage-ci", w.Draft.Config.Rules[1].Profile);
        workflows.NameBox.Text = "review";
        workflows.CommitRename();
        w.Revalidate();
        Assert.True(w.Draft.Config.AgentProfiles.ContainsKey("triage-ci"));
        Assert.False(w.CanSave); // the refused name is a problem until fixed
        Assert.NotNull(workflows.NameErrorText);
        w.Discard();
    }

    [AvaloniaFact]
    public void Pressing_enter_in_the_name_box_renames()
    {
        var w = Show(new SampleSettingsHost());
        w.ShowPage(SettingsPage.Workflows);
        var workflows = (WorkflowsPage)w.CurrentPage;
        workflows.SelectProfile("triage");
        workflows.NameBox.Text = "triage-2";
        workflows.NameBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.True(w.Draft.Config.AgentProfiles.ContainsKey("triage-2"));
        w.Discard();
    }

    [AvaloniaFact]
    public void A_profile_in_use_cannot_be_deleted_and_switching_type_keeps_both_halves()
    {
        var w = Open(new SampleSettingsHost());
        var workflows = (WorkflowsPage)w.Page(SettingsPage.Workflows);
        workflows.SelectProfile("triage");
        Assert.True(workflows.EditorShown);
        Assert.False(workflows.DeleteEnabled); // rule 2 uses it
        Assert.True(workflows.CommandType.IsChecked);
        workflows.PromptType.IsChecked = true;
        Assert.Null(w.Draft.Config.AgentProfiles["triage"].Command);
        workflows.CommandType.IsChecked = true;
        Assert.Equal("pwsh", w.Draft.Config.AgentProfiles["triage"].Command![0]);
        workflows.SelectProfile("peer-review");
        Assert.True(workflows.BuiltInShown);
        Assert.False(workflows.EditorShown);
        w.Discard();
    }

    [AvaloniaFact]
    public void Nav_buttons_switch_pages_and_escape_asks_before_discarding()
    {
        var w = Show(new SampleSettingsHost());
        Assert.IsType<GeneralPage>(w.CurrentPage);
        w.ShowPage(SettingsPage.Filters);
        Assert.IsType<FiltersPage>(w.CurrentPage);
        w.Draft.Config.PollSeconds = 150;
        w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.True(w.DiscardBarShown);
        w.Discard();
    }

    [AvaloniaFact]
    public void App_opens_one_settings_window_at_a_time()
    {
        SettingsTokens.Ensure();
        var app = (App)Application.Current!;
        if (app.Controller is null) app.Start(() => { });
        app.OpenSettings();
        app.OpenSettings();
        var windows = app.OpenWindows.OfType<SettingsWindow>().ToList();
        Assert.Single(windows);
        windows[0].Discard();
    }
}

/// <summary>Brushes for the theme keys the settings window references, until the shared themes (owned by the popup port) provide them.</summary>
static class SettingsTokens
{
    public static void Ensure()
    {
        if (Application.Current is not { } app) return;
        void Set(string key, string color)
        {
            if (!app.Resources.ContainsKey(key)) app.Resources[key] = new SolidColorBrush(Color.Parse(color));
        }
        Set("Brush.Surface", "#000C1A");
        Set("Brush.Raised", "#0B1A29");
        Set("Brush.Text", "#FFFFFF");
        Set("Brush.Muted", "#8296AB");
        Set("Brush.Section", "#9FB0C2");
        Set("Brush.Hairline", "#0F2236");
        Set("Brush.Red", "#ED352F");
        Set("Brush.Green", "#37B84D");
        Set("Brush.ReadyText", "#5FD474");
        Set("Brush.Blue", "#0072DA");
        Set("Brush.RedTint", "#21ED352F");
        Set("Brush.Header", "#0B1A29");
        Set("Brush.Eyebrow", "#8296AB");
    }
}
