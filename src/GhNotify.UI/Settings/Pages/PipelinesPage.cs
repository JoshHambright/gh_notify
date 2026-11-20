using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>
/// Settings › Pipelines: the pipelines you follow and what makes each one alert. One shared add/edit form sits under the list;
/// its pickers (org → project → pipeline, or repo → workflow) are loaded from the host and fail inline, never in a dialog.
/// </summary>
public sealed class PipelinesPage : SettingsPageBase
{
    readonly StackPanel _list = new() { Spacing = 6, Margin = new Thickness(0, 0, 0, 10) };
    readonly List<(CheckBox Enabled, Button Remove, string Summary)> _rows = [];
    readonly Button _addButton;

    readonly StackPanel _form = new() { IsVisible = false };
    readonly StackPanel _adoPanel = new();
    readonly StackPanel _gitHubPanel = new();
    readonly RadioButton _adoRadio = Ui.Segment("Azure DevOps", isChecked: true);
    readonly RadioButton _gitHubRadio = Ui.Segment("GitHub");
    readonly TextBlock _stagesLabel = Ui.Text("", "field");
    readonly TextBlock _message = Ui.Text("", "err", "small");

    PipelineSubscription? _editing;
    Provider _provider = Provider.Ado;
    List<PipelinePick> _picks = [];
    PipelinePick? _chosen;
    bool _loading;
    int _generation; // bumped whenever a selection changes, so a slow picker result for an older selection is dropped

    public PipelinesPage(SettingsDraft draft, ISettingsHost host, Action changed) : base(draft, host, changed)
    {
        Body.Children.Add(Ui.Section("PIPELINES", 8));
        var note = Ui.Text("Follow a pipeline to get a popup when a run ends the way you care about, for example when the PROD stage fails. " +
                           "Rows stay in the popup's Pipelines section until you dismiss them. Only runs that finish after you add a pipeline alert.", "muted", "small");
        note.Margin = new Thickness(0, 0, 0, 10);
        Body.Children.Add(note);
        Body.Children.Add(_list);
        _addButton = Ui.Btn("Add pipeline…", () => BeginAdd(), "pill");
        _addButton.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.Add(_addButton);

        OrgCombo = Ui.Combo([], -1, _ => { }, 220);
        ProjectCombo = Ui.Combo([], -1, _ => { }, 220);
        PipelineCombo = Ui.Combo([], -1, _ => { }, 220);
        WorkflowCombo = Ui.Combo([], -1, _ => { }, 220);
        OrgCombo.SelectionChanged += (_, _) => OnOrgChanged();
        ProjectCombo.SelectionChanged += (_, _) => OnProjectChanged();
        PipelineCombo.SelectionChanged += (_, _) => OnPickChanged(PipelineCombo);
        WorkflowCombo.SelectionChanged += (_, _) => OnPickChanged(WorkflowCombo);
        RepoBox = Ui.Box("", "owner/repo", 220, _ => OnRepoChanged());
        FindWorkflowsButton = Ui.Btn("Find workflows", () => _ = LoadWorkflowsAsync(), "pill");
        FailedBox = Ui.Check("Failed", true, _ => UpdateSave());
        SucceededBox = Ui.Check("Succeeded", false, _ => UpdateSave());
        PartialBox = Ui.Check("Succeeded with issues", false, _ => UpdateSave());
        StagesBox = Ui.Box("", "PROD*, QA — leave empty to judge the whole run", 300, _ => { });
        BranchesBox = Ui.Box("", "main, release/* — leave empty for any branch", 300, _ => { });
        MineBox = Ui.Check("Only runs I triggered", false, _ => { });
        SaveButton = Ui.Btn("Save pipeline", SaveForm, "pill", "primary");
        var cancel = Ui.Btn("Cancel", CancelForm, "pill");

        _adoRadio.IsCheckedChanged += (_, _) => { if (_adoRadio.IsChecked == true) Choose(Provider.Ado); };
        _gitHubRadio.IsCheckedChanged += (_, _) => { if (_gitHubRadio.IsChecked == true) Choose(Provider.GitHub); };

        _form.Margin = new Thickness(0, 14, 0, 0);
        _form.Children.Add(Ui.Section("ADD OR EDIT", 8));
        _form.Children.Add(Ui.Field("Provider", Ui.Segments(_adoRadio, _gitHubRadio)));
        _adoPanel.Children.Add(Ui.Field("Organization", OrgCombo));
        _adoPanel.Children.Add(Ui.Field("Project", ProjectCombo));
        _adoPanel.Children.Add(Ui.Field("Pipeline", PipelineCombo));
        _gitHubPanel.Children.Add(Ui.Field("Repository", Ui.Row(RepoBox, FindWorkflowsButton)));
        _gitHubPanel.Children.Add(Ui.Field("Workflow", WorkflowCombo));
        _form.Children.Add(_adoPanel);
        _form.Children.Add(_gitHubPanel);
        _form.Children.Add(Ui.Field("Alert when", Ui.Row(FailedBox, SucceededBox, PartialBox)));
        var stagesRow = Ui.Field("", StagesBox);
        stagesRow.Children.RemoveAt(0);
        DockPanel.SetDock(_stagesLabel, Dock.Left);
        _stagesLabel.VerticalAlignment = VerticalAlignment.Center;
        stagesRow.Children.Insert(0, _stagesLabel);
        _form.Children.Add(stagesRow);
        _form.Children.Add(Ui.Field("Branches", BranchesBox));
        _form.Children.Add(Ui.Field("", MineBox));
        _form.Children.Add(Ui.Row(SaveButton, cancel));
        _message.Margin = new Thickness(0, 8, 0, 0);
        _form.Children.Add(_message);
        Body.Children.Add(_form);

        Choose(Provider.Ado);
        ShowList();
    }

    internal ComboBox OrgCombo { get; }
    internal ComboBox ProjectCombo { get; }
    internal ComboBox PipelineCombo { get; }
    internal ComboBox WorkflowCombo { get; }
    internal TextBox RepoBox { get; }
    internal Button FindWorkflowsButton { get; }
    internal CheckBox FailedBox { get; }
    internal CheckBox SucceededBox { get; }
    internal CheckBox PartialBox { get; }
    internal CheckBox MineBox { get; }
    internal TextBox StagesBox { get; }
    internal TextBox BranchesBox { get; }
    internal Button SaveButton { get; }
    internal bool FormOpen => _form.IsVisible;
    internal string StagesLabel => _stagesLabel.Text ?? "";
    internal string FormMessage => _message.Text ?? "";
    internal IReadOnlyList<string> ListedSummaries => _rows.Select(r => r.Summary).ToList();
    internal CheckBox EnabledBox(int index) => _rows[index].Enabled;
    internal Button RemoveButton(int index) => _rows[index].Remove;

    // ----- list -----

    void ShowList()
    {
        _list.Children.Clear();
        _rows.Clear();
        if (Config.PipelineSubscriptions.Count == 0)
            _list.Children.Add(Ui.Text("No pipelines followed yet.", "muted", "small"));
        foreach (var sub in Config.PipelineSubscriptions.ToList())
        {
            var summary = SettingsDraft.PipelineSubscriptionSummary(sub);
            var enabled = Ui.Check(summary, sub.Enabled, on =>
            {
                sub.Enabled = on;
                if (on) sub.CreatedAt = DateTimeOffset.Now; // re-enabling restarts the baseline, so runs seen before don't toast again
                Changed();
            });
            var remove = Ui.Remove(() =>
            {
                Draft.RemovePipelineSubscription(sub.Id);
                if (_editing == sub) CancelForm();
                ShowList();
                Changed();
            }, "Stop following this pipeline");
            var edit = Ui.Btn("Edit", () => BeginEdit(Config.PipelineSubscriptions.IndexOf(sub)), "pill");
            DockPanel.SetDock(remove, Dock.Right);
            DockPanel.SetDock(edit, Dock.Right);
            var line = new DockPanel();
            line.Children.Add(remove);
            line.Children.Add(edit);
            line.Children.Add(enabled);
            _list.Children.Add(line);
            _rows.Add((enabled, remove, summary));
        }
    }

    // ----- form -----

    internal void BeginAdd()
    {
        _editing = null;
        ResetForm();
        _adoRadio.IsChecked = true;
        Choose(Provider.Ado);
        FillOrgs(null);
        Open();
    }

    internal void BeginEdit(int index)
    {
        if (index < 0 || index >= Config.PipelineSubscriptions.Count) return;
        var sub = Config.PipelineSubscriptions[index];
        _editing = sub;
        ResetForm();
        _loading = true;
        try
        {
            if (sub.Provider == Provider.GitHub) _gitHubRadio.IsChecked = true; else _adoRadio.IsChecked = true;
            Choose(sub.Provider);
            _chosen = new PipelinePick(sub.PipelineId, sub.Pipeline);
            _picks = [_chosen];
            if (sub.Provider == Provider.Ado)
            {
                FillOrgs(sub.Org);
                ProjectCombo.ItemsSource = new List<string> { sub.Project ?? "" };
                ProjectCombo.SelectedIndex = 0;
                PipelineCombo.ItemsSource = new List<string> { sub.Pipeline };
                PipelineCombo.SelectedIndex = 0;
            }
            else
            {
                RepoBox.Text = sub.Repo ?? "";
                WorkflowCombo.ItemsSource = new List<string> { sub.Pipeline };
                WorkflowCombo.SelectedIndex = 0;
            }
            FailedBox.IsChecked = sub.On.Contains(PipelineOutcome.Failed);
            SucceededBox.IsChecked = sub.On.Contains(PipelineOutcome.Succeeded);
            PartialBox.IsChecked = sub.On.Contains(PipelineOutcome.PartiallySucceeded);
            StagesBox.Text = string.Join(", ", sub.Stages);
            BranchesBox.Text = string.Join(", ", sub.Branches);
            MineBox.IsChecked = sub.OnlyMine;
        }
        finally { _loading = false; }
        Open();
    }

    void Open()
    {
        _form.IsVisible = true;
        UpdateSave();
    }

    internal void CancelForm()
    {
        _form.IsVisible = false;
        _editing = null;
        _message.Text = "";
    }

    void ResetForm()
    {
        _loading = true;
        try
        {
            _chosen = null;
            _picks = [];
            _message.Text = "";
            OrgCombo.ItemsSource = new List<string>();
            ProjectCombo.ItemsSource = new List<string>();
            PipelineCombo.ItemsSource = new List<string>();
            WorkflowCombo.ItemsSource = new List<string>();
            RepoBox.Text = "";
            FailedBox.IsChecked = true;
            SucceededBox.IsChecked = false;
            PartialBox.IsChecked = false;
            StagesBox.Text = "";
            BranchesBox.Text = "";
            MineBox.IsChecked = false;
        }
        finally { _loading = false; }
    }

    void FillOrgs(string? select)
    {
        var orgs = Host.DiscoveredAdoOrgs().Concat(Config.Ado.Orgs).Concat(select is null ? [] : [select])
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var was = _loading;
        _loading = true;
        try
        {
            OrgCombo.ItemsSource = orgs;
            OrgCombo.SelectedIndex = select is null ? -1 : orgs.FindIndex(o => string.Equals(o, select, StringComparison.OrdinalIgnoreCase));
        }
        finally { _loading = was; }
        if (orgs.Count == 0) _message.Text = "No Azure DevOps organization found. Add one on the Sources page, or sign in with az.";
    }

    internal void Choose(Provider provider)
    {
        _provider = provider;
        _adoPanel.IsVisible = provider == Provider.Ado;
        _gitHubPanel.IsVisible = provider == Provider.GitHub;
        PartialBox.IsVisible = provider == Provider.Ado; // GitHub has no "succeeded with issues"
        if (provider == Provider.GitHub) PartialBox.IsChecked = false;
        _stagesLabel.Text = provider == Provider.Ado ? "Stages" : "Jobs";
        StagesBox.Watermark = provider == Provider.Ado
            ? "PROD*, QA — leave empty to judge the whole run"
            : "deploy-prod*, deploy-qa — job names; leave empty to judge the whole run";
        if (_gitHubRadio.IsChecked != (provider == Provider.GitHub)) _gitHubRadio.IsChecked = provider == Provider.GitHub;
        if (_adoRadio.IsChecked != (provider == Provider.Ado)) _adoRadio.IsChecked = provider == Provider.Ado;
        if (!_loading)
        {
            _chosen = null;
            _message.Text = "";
        }
        UpdateSave();
    }

    // ----- pickers -----

    void OnRepoChanged()
    {
        if (_loading) return;
        _generation++;
        _chosen = null; // a workflow id belongs to the repo it was listed for
        _picks = [];
        WorkflowCombo.ItemsSource = new List<string>();
        UpdateSave();
    }

    void OnOrgChanged()
    {
        if (_loading) return;
        _generation++;
        ProjectCombo.ItemsSource = new List<string>();
        PipelineCombo.ItemsSource = new List<string>();
        _chosen = null;
        _message.Text = "";
        UpdateSave();
        if (OrgCombo.SelectedItem is string org) _ = LoadProjectsAsync(org);
    }

    void OnProjectChanged()
    {
        if (_loading) return;
        _generation++;
        PipelineCombo.ItemsSource = new List<string>();
        _chosen = null;
        _message.Text = "";
        UpdateSave();
        if (OrgCombo.SelectedItem is string org && ProjectCombo.SelectedItem is string project) _ = LoadPipelinesAsync(org, project);
    }

    void OnPickChanged(ComboBox combo)
    {
        if (_loading) return;
        _chosen = combo.SelectedIndex >= 0 && combo.SelectedIndex < _picks.Count ? _picks[combo.SelectedIndex] : null;
        UpdateSave();
    }

    async Task LoadProjectsAsync(string org)
    {
        var gen = _generation;
        try
        {
            var projects = (await Host.ListAdoProjectsAsync(org)).ToList();
            if (gen != _generation) return;
            ProjectCombo.ItemsSource = projects;
            ProjectCombo.SelectedIndex = -1;
            if (projects.Count == 0) _message.Text = $"{org} has no projects you can see.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { _message.Text = $"Couldn't load projects: {ex.Message}"; }
    }

    async Task LoadPipelinesAsync(string org, string project)
    {
        var gen = _generation;
        try
        {
            var picks = (await Host.ListAdoPipelinesAsync(org, project)).ToList();
            if (gen != _generation) return;
            _picks = picks;
            PipelineCombo.ItemsSource = _picks.Select(p => p.Name).ToList();
            PipelineCombo.SelectedIndex = -1;
            if (_picks.Count == 0) _message.Text = $"{project} has no build pipelines you can see.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { _message.Text = $"Couldn't load pipelines: {ex.Message}"; }
    }

    async Task LoadWorkflowsAsync()
    {
        var repo = (RepoBox.Text ?? "").Trim();
        if (!ValidRepo(repo)) { _message.Text = "Enter the repository as owner/repo."; return; }
        _message.Text = "";
        var gen = _generation;
        try
        {
            var picks = (await Host.ListGitHubWorkflowsAsync(repo)).ToList();
            if (gen != _generation) return;
            _picks = picks;
            WorkflowCombo.ItemsSource = _picks.Select(p => p.Name).ToList();
            WorkflowCombo.SelectedIndex = -1;
            _chosen = null;
            UpdateSave();
            if (_picks.Count == 0) _message.Text = $"{repo} has no active workflows you can see.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { _message.Text = $"Couldn't load workflows: {ex.Message}"; }
    }

    // ----- save -----

    static bool ValidRepo(string repo) => repo.Split('/') is [{ Length: > 0 }, { Length: > 0 }];

    static List<string> Split(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    List<PipelineOutcome> Outcomes()
    {
        var on = new List<PipelineOutcome>();
        if (FailedBox.IsChecked == true) on.Add(PipelineOutcome.Failed);
        if (SucceededBox.IsChecked == true) on.Add(PipelineOutcome.Succeeded);
        if (_provider == Provider.Ado && PartialBox.IsChecked == true) on.Add(PipelineOutcome.PartiallySucceeded);
        return on;
    }

    void UpdateSave()
    {
        var scopeOk = _provider == Provider.Ado
            ? OrgCombo.SelectedItem is string && ProjectCombo.SelectedItem is string
            : ValidRepo((RepoBox.Text ?? "").Trim());
        SaveButton.IsEnabled = _chosen is not null && scopeOk && Outcomes().Count > 0;
    }

    void SaveForm()
    {
        if (!SaveButton.IsEnabled || _chosen is null) return;
        var sub = _editing ?? new PipelineSubscription();
        sub.Provider = _provider;
        if (_provider == Provider.Ado)
        {
            sub.Org = (string)OrgCombo.SelectedItem!;
            sub.Project = (string)ProjectCombo.SelectedItem!;
            sub.Repo = null;
        }
        else
        {
            sub.Repo = (RepoBox.Text ?? "").Trim();
            sub.Org = null;
            sub.Project = null;
        }
        sub.PipelineId = _chosen.Id;
        sub.Pipeline = _chosen.Name;
        sub.On = Outcomes();
        sub.Stages = Split(StagesBox.Text);
        sub.Branches = Split(BranchesBox.Text);
        sub.OnlyMine = MineBox.IsChecked == true;
        // An edit restarts the baseline too, so changing what alerts never replays older runs.
        if (_editing is null) Draft.AddPipelineSubscription(sub, DateTimeOffset.Now);
        else sub.CreatedAt = DateTimeOffset.Now;
        CancelForm();
        ShowList();
        Changed();
    }
}
