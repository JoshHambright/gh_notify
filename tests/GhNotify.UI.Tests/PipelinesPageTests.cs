using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using GhNotify.Core;

namespace GhNotify.UI.Tests;

/// <summary>Settings › Pipelines, headless: the list, the add/edit form and its pickers. The sample host answers the pickers instantly.</summary>
public class PipelinesPageTests
{
    static (SettingsWindow Window, PipelinesPage Page) Open(string configJson = "{}")
    {
        SettingsTokens.Ensure();
        ISettingsHost host;
        {
            var dir = Path.Combine(Path.GetTempPath(), "ghnotify-pipes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, configJson);
            var store = new ConfigStore(path);
            store.Load();
            host = new SampleSettingsHost(store);
        }
        var w = new SettingsWindow(host);
        w.Show();
        w.ShowPage(SettingsPage.Pipelines);
        return (w, (PipelinesPage)w.Page(SettingsPage.Pipelines));
    }

    static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    const string Existing = """
        {"pipelineSubscriptions":[{"id":"s1","provider":"ado","org":"contoso","project":"Core Platform","pipelineId":42,"pipeline":"Deploy",
        "on":["failed"],"stages":["PROD*"],"createdAt":"2026-01-01T00:00:00Z"}]}
        """;

    [AvaloniaFact]
    public void An_empty_page_says_nothing_is_followed_and_opening_does_not_dirty_the_draft()
    {
        var (w, page) = Open();
        Assert.Empty(page.ListedSummaries);
        Assert.False(w.Draft.IsDirty);
        w.Discard();
    }

    [AvaloniaFact]
    public void Existing_subscriptions_are_listed_by_summary_and_can_be_toggled_and_removed()
    {
        var (w, page) = Open(Existing);
        Assert.Equal(["Deploy · contoso/Core Platform · Failed on PROD*"], page.ListedSummaries);
        page.EnabledBox(0).IsChecked = false;
        Assert.False(w.Draft.Config.PipelineSubscriptions[0].Enabled);
        page.RemoveButton(0).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(w.Draft.Config.PipelineSubscriptions);
        Assert.Empty(page.ListedSummaries);
        w.Discard();
    }

    [AvaloniaFact]
    public void Adding_an_ado_pipeline_walks_org_project_pipeline_and_saves_the_choices()
    {
        var (w, page) = Open();
        page.BeginAdd();
        Assert.False(page.SaveButton.IsEnabled); // nothing chosen yet
        Assert.Contains("contoso", page.OrgCombo.ItemsSource!.Cast<string>());
        page.OrgCombo.SelectedIndex = 0;
        Assert.Equal(["Core Platform", "Portal"], page.ProjectCombo.ItemsSource!.Cast<string>());
        page.ProjectCombo.SelectedIndex = 0;
        Assert.Equal(["Deploy", "CI"], page.PipelineCombo.ItemsSource!.Cast<string>());
        page.PipelineCombo.SelectedIndex = 0;
        page.SucceededBox.IsChecked = true;
        page.StagesBox.Text = "PROD*, QA";
        page.BranchesBox.Text = "main";
        page.MineBox.IsChecked = true;
        Assert.True(page.SaveButton.IsEnabled);
        Click(page.SaveButton);

        var s = Assert.Single(w.Draft.Config.PipelineSubscriptions);
        Assert.Equal(Provider.Ado, s.Provider);
        Assert.Equal("contoso", s.Org);
        Assert.Equal("Core Platform", s.Project);
        Assert.Equal(42, s.PipelineId);
        Assert.Equal("Deploy", s.Pipeline);
        Assert.Equal([PipelineOutcome.Failed, PipelineOutcome.Succeeded], s.On);
        Assert.Equal(["PROD*", "QA"], s.Stages);
        Assert.Equal(["main"], s.Branches);
        Assert.True(s.OnlyMine);
        Assert.True(Guid.TryParse(s.Id, out _));
        Assert.True(DateTimeOffset.Now - s.CreatedAt < TimeSpan.FromMinutes(1));
        Assert.False(page.FormOpen);
        Assert.Single(page.ListedSummaries);
        Assert.True(w.Draft.IsDirty);
        w.Discard();
    }

    [AvaloniaFact]
    public void A_github_subscription_uses_a_repo_and_workflow_and_has_no_partial_success_or_environment_wording()
    {
        var (w, page) = Open();
        page.BeginAdd();
        Assert.True(page.PartialBox.IsVisible);
        page.Choose(Provider.GitHub);
        Assert.False(page.PartialBox.IsVisible);
        Assert.Contains("Job", page.StagesLabel);
        page.RepoBox.Text = "acme/api";
        Click(page.FindWorkflowsButton);
        Assert.Equal(["Deploy", "CI"], page.WorkflowCombo.ItemsSource!.Cast<string>());
        page.WorkflowCombo.SelectedIndex = 1;
        Click(page.SaveButton);

        var s = Assert.Single(w.Draft.Config.PipelineSubscriptions);
        Assert.Equal(Provider.GitHub, s.Provider);
        Assert.Equal("acme/api", s.Repo);
        Assert.Equal(556, s.PipelineId);
        Assert.Equal("CI", s.Pipeline);
        Assert.Equal([PipelineOutcome.Failed], s.On);
        Assert.Empty(w.Draft.Validate());
        w.Discard();
    }

    [AvaloniaFact]
    public void Save_needs_an_outcome_and_a_pipeline()
    {
        var (w, page) = Open();
        page.BeginAdd();
        page.OrgCombo.SelectedIndex = 0;
        page.ProjectCombo.SelectedIndex = 0;
        page.PipelineCombo.SelectedIndex = 0;
        Assert.True(page.SaveButton.IsEnabled);
        page.FailedBox.IsChecked = false;
        Assert.False(page.SaveButton.IsEnabled);
        page.CancelForm();
        Assert.Empty(w.Draft.Config.PipelineSubscriptions);
        Assert.False(page.FormOpen);
        w.Discard();
    }

    [AvaloniaFact]
    public void Editing_changes_the_subscription_in_place_and_restarts_its_baseline()
    {
        var (w, page) = Open(Existing);
        page.BeginEdit(0);
        Assert.Equal("PROD*", page.StagesBox.Text);
        Assert.True(page.FailedBox.IsChecked);
        page.SucceededBox.IsChecked = true;
        page.StagesBox.Text = "";
        Click(page.SaveButton);

        var s = Assert.Single(w.Draft.Config.PipelineSubscriptions);
        Assert.Equal("s1", s.Id);
        Assert.Equal([PipelineOutcome.Failed, PipelineOutcome.Succeeded], s.On);
        Assert.Empty(s.Stages);
        Assert.True(DateTimeOffset.Now - s.CreatedAt < TimeSpan.FromMinutes(1)); // an edit must not replay older runs
        w.Discard();
    }

    [AvaloniaFact]
    public void A_picker_failure_is_shown_inline_and_nothing_is_saved()
    {
        SettingsTokens.Ensure();
        var host = new SampleSettingsHost { PickerError = "403 Forbidden" };
        var w = new SettingsWindow(host);
        w.Show();
        w.ShowPage(SettingsPage.Pipelines);
        var page = (PipelinesPage)w.Page(SettingsPage.Pipelines);
        page.BeginAdd();
        page.OrgCombo.SelectedIndex = 0;
        Assert.Contains("403 Forbidden", page.FormMessage);
        Assert.False(page.SaveButton.IsEnabled);
        w.Discard();
    }

    [AvaloniaFact]
    public void Validation_errors_from_the_config_show_on_the_page_and_block_saving()
    {
        var (w, page) = Open(Existing);
        w.Draft.Config.PipelineSubscriptions[0].On = [];
        w.Revalidate();
        Assert.Contains(page.ShownErrors, m => m.Contains("outcome"));
        Assert.False(w.CanSave);
        w.Discard();
    }
}
