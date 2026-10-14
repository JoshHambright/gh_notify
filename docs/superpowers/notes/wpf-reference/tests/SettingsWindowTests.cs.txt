using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using GhNotify.App;
using GhNotify.Core;

namespace GhNotify.App.Tests;

/// <summary>
/// Runs WPF work on one long-lived STA thread that owns the app's theme resources (xunit 2 has no [StaFact]; the
/// TrayIconRendererTests Sta helper starts a thread per call, but resources loaded on one thread can't be used from another).
/// </summary>
static class SettingsUi
{
    static readonly BlockingCollection<Action> Work = new();

    static SettingsUi()
    {
        var t = new Thread(() =>
        {
            var merged = Application.Current.Resources.MergedDictionaries;
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/GhNotify;component/Themes/Common.xaml") });
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/GhNotify;component/Themes/Dark.xaml") });
            foreach (var work in Work.GetConsumingEnumerable()) work();
        })
        { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void Run(Action action)
    {
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        Work.Add(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
            finally { done.Set(); }
        });
        done.Wait();
        if (error is not null) ExceptionDispatchInfo.Throw(error);
    }
}

public class SettingsWindowTests
{
    sealed class Collect : TraceListener
    {
        public readonly List<string> Lines = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (message is not null) Lines.Add(message); }
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    [Fact]
    public void Every_page_renders_without_binding_errors() => SettingsUi.Run(() => AssertNoBindingErrors(() => new SampleSettingsHost()));

    [Fact]
    public void Hooks_and_prompt_profiles_render_without_binding_errors() => SettingsUi.Run(() =>
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
            AssertNoBindingErrors(() => new SampleSettingsHost(store), "summarize");
        }
        finally { Directory.Delete(dir, true); }
    });

    static void AssertNoBindingErrors(Func<SampleSettingsHost> host, string profile = "triage")
    {
        var listener = new Collect();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            foreach (var page in Enum.GetValues<SettingsPage>())
            {
                // Shown offscreen: an unshown window never builds its visual tree, so its bindings would not be exercised.
                var w = new SettingsWindow(host()) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                w.ShowPage(page);
                w.Show();
                if (w.CurrentPage is WorkflowsPage workflows) workflows.SelectProfile(profile); // exercise the profile editor too
                w.Measure(new Size(880, 620));
                w.Arrange(new Rect(0, 0, 880, 620));
                w.UpdateLayout();
                Assert.NotEmpty(Descendants(w.CurrentPage).OfType<System.Windows.Controls.Control>());
                Assert.False(w.Draft.IsDirty, $"opening {page} changed the draft");
                w.Close();
            }
            Assert.Empty(listener.Lines);
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
    }

    [Fact]
    public void Save_is_disabled_until_a_valid_change() => SettingsUi.Run(() =>
    {
        var w = new SettingsWindow(new SampleSettingsHost());
        Assert.False(w.CanSave);
        w.Draft.Config.PollSeconds = 1;
        w.Revalidate();
        Assert.False(w.CanSave);
        Assert.Equal("1 problem on General", w.StatusMessage);
        w.Draft.Config.PollSeconds = 180;
        w.Revalidate();
        Assert.True(w.CanSave);
        Assert.Equal("Unsaved changes", w.StatusMessage);
        w.Close();
    });

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

    [Fact]
    public void Save_writes_config_and_keeps_a_backup() => SettingsUi.Run(() =>
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            var w = new SettingsWindow(new SampleSettingsHost(store));
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.Contains("\"pollSeconds\": 150", File.ReadAllText(store.Path));
            Assert.True(File.Exists(store.Path + ".bak"));
            Assert.Equal(150, store.Current.PollSeconds);
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void A_file_changed_on_disk_shows_the_conflict_bar_and_overwrite_saves() => SettingsUi.Run(() =>
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            var w = new SettingsWindow(new SampleSettingsHost(store));
            File.WriteAllText(store.Path, """{ "pollSeconds": 300 }""");
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.ConflictBarShown);
            Assert.Contains("300", File.ReadAllText(store.Path));
            w.Overwrite();
            Assert.Contains("\"pollSeconds\": 150", File.ReadAllText(store.Path));
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void A_file_created_after_opening_is_a_conflict_not_an_overwrite() => SettingsUi.Run(() =>
    {
        var dir = TempDir();
        try
        {
            var store = new ConfigStore(Path.Combine(dir, "config.json")); // not loaded: no file when the window opens
            var w = new SettingsWindow(new SampleSettingsHost(store));
            Assert.Null(w.Draft.FileHashAtOpen);
            File.WriteAllText(store.Path, """{ "pollSeconds": 300 }""");
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.ConflictBarShown);
            Assert.Contains("300", File.ReadAllText(store.Path));
            w.Close();
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void Comments_warn_once_before_saving() => SettingsUi.Run(() =>
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, "{\n  // poll less often\n  \"pollSeconds\": 120\n}");
            var host = new SampleSettingsHost(store);
            var w = new SettingsWindow(host);
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
    });

    [Fact]
    public void Cancel_with_changes_asks_inline_and_keeps_the_window() => SettingsUi.Run(() =>
    {
        var w = new SettingsWindow(new SampleSettingsHost());
        w.Draft.Config.PollSeconds = 150;
        w.RequestClose();
        Assert.True(w.DiscardBarShown);
        w.Close();
    });

    [Fact]
    public void Local_choices_apply_on_save_without_touching_config() => SettingsUi.Run(() =>
    {
        var host = new SampleSettingsHost();
        var w = new SettingsWindow(host);
        var general = (GeneralPage)w.Page(SettingsPage.General);
        general.ThemeDark.IsChecked = true;
        w.Revalidate();
        Assert.True(w.CanSave);
        Assert.False(w.Draft.IsDirty);
        w.Save();
        Assert.Equal(GhNotify.Core.ThemeMode.Dark, host.ThemeMode);
    });

    [Fact]
    public void Reset_rebuilds_the_pages_from_defaults() => SettingsUi.Run(() =>
    {
        var w = new SettingsWindow(new SampleSettingsHost());
        var before = w.Page(SettingsPage.Workflows);
        w.ResetToDefaults();
        Assert.Empty(w.Draft.Config.Rules);
        Assert.NotSame(before, w.Page(SettingsPage.Workflows));
        Assert.True(w.CanSave);
        w.Close();
    });

    [Fact]
    public void A_broken_config_on_disk_is_never_silently_replaced() => SettingsUi.Run(() =>
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            const string broken = """{ "pollSeconds": 5, "note": "hand edit" }""";
            File.WriteAllText(store.Path, broken);
            store.Load(); // fails validation: Current stays the last good config
            Assert.NotNull(store.Error);
            var w = new SettingsWindow(new SampleSettingsHost(store));
            Assert.True(w.ConfigErrorShown);
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.ConflictBarShown);
            Assert.Equal(broken, File.ReadAllText(store.Path));
            w.Overwrite(); // the user's explicit choice
            Assert.Contains("\"pollSeconds\": 150", File.ReadAllText(store.Path));
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void An_edit_on_disk_not_yet_reloaded_conflicts() => SettingsUi.Run(() =>
    {
        var dir = TempDir();
        try
        {
            var store = StoreWith(dir, """{ "pollSeconds": 120 }""");
            const string edited = """{ "pollSeconds": 300 }""";
            File.WriteAllText(store.Path, edited); // inside the watcher's debounce: not reloaded yet
            var w = new SettingsWindow(new SampleSettingsHost(store));
            Assert.False(w.ConfigErrorShown);
            w.Draft.Config.PollSeconds = 150;
            w.Save();
            Assert.True(w.ConflictBarShown);
            Assert.Equal(edited, File.ReadAllText(store.Path));
            w.Close();
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void Text_that_is_not_a_number_blocks_save() => SettingsUi.Run(() =>
    {
        var w = new SettingsWindow(new SampleSettingsHost());
        w.ShowActivated = false;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -10000;
        w.Show(); // live bindings, as in the app
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
        w.Close();
    });

    [Fact]
    public void Max_lines_that_is_not_a_positive_number_is_an_error_not_a_clear() => SettingsUi.Run(() =>
    {
        var w = new SettingsWindow(new SampleSettingsHost());
        var workflows = (WorkflowsPage)w.Page(SettingsPage.Workflows);
        var row = workflows.Rules.Items.OfType<RuleRow>().First();
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
        w.Close();
    });

    [Fact]
    public void Renaming_a_profile_commits_on_leaving_the_box() => SettingsUi.Run(() =>
    {
        var w = new SettingsWindow(new SampleSettingsHost());
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
        w.Close();
    });

    [Fact]
    public void Choosing_custom_file_through_the_binding_changes_nothing()
    {
        var rule = new SoundRule { Sound = "builtin:chime" };
        var row = new SoundRow(rule, () => { });
        row.SoundIndex = row.CustomIndex; // e.g. arrowing onto "Custom file…": the page opens the picker on commit, not here
        Assert.Equal("builtin:chime", rule.Sound);
        row.SetCustomSound(@"C:\sounds\ding.wav");
        Assert.Equal(@"C:\sounds\ding.wav", rule.Sound);
    }
}
