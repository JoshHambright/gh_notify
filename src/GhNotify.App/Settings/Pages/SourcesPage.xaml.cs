using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GhNotify.Core;

namespace GhNotify.App;

public partial class SourcesPage : UserControl, ISettingsPage
{
    readonly SettingsDraft _draft;
    readonly ISettingsHost _host;
    readonly Action _changed;
    readonly IReadOnlyList<string> _discovered;
    List<AliasRow> _aliases = [];
    readonly bool _loading = true;

    public SourcesPage(SettingsDraft draft, ISettingsHost host, Action changed)
    {
        InitializeComponent();
        _draft = draft;
        _host = host;
        _changed = changed;
        _discovered = host.DiscoveredAdoOrgs();
        DataContext = draft.Config;
        ShowWatchRepos();
        ShowOrgs();
        ShowRoots();
        _aliases = draft.Config.Display.RepoAliases.Select(kv => new AliasRow(kv.Key, kv.Value, _ => AliasesEdited())).ToList();
        Aliases.ItemsSource = _aliases;
        _loading = false;
    }

    AppConfig Config => _draft.Config;

    void OnEdited(object sender, RoutedEventArgs e)
    {
        if (!_loading) _changed();
    }

    // ----- GitHub watch repos -----

    void ShowWatchRepos() => WatchRepos.ItemsSource = Config.GitHub.WatchRepos.ToList();

    void OnWatchRepoKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || SettingsOptions.Blank(WatchRepoBox.Text) is not { } repo) return;
        e.Handled = true;
        if (!Config.GitHub.WatchRepos.Contains(repo, StringComparer.OrdinalIgnoreCase)) Config.GitHub.WatchRepos.Add(repo);
        WatchRepoBox.Clear();
        ShowWatchRepos();
        _changed();
    }

    void OnRemoveWatchRepo(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string repo) return;
        Config.GitHub.WatchRepos.Remove(repo);
        ShowWatchRepos();
        _changed();
    }

    // ----- ADO orgs -----

    /// <summary>Discovered orgs ∪ ado.orgs (plus any excluded org, so it can be re-included).</summary>
    void ShowOrgs()
    {
        var names = _discovered.Concat(Config.Ado.Orgs).Concat(Config.Ado.ExcludeOrgs)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Orgs.ItemsSource = names.Select(n => new OrgRow(n, Config.Ado, _changed)).ToList();
        NoOrgs.Visibility = names.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnOrgKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || SettingsOptions.Blank(OrgBox.Text) is not { } org) return;
        e.Handled = true;
        if (!Config.Ado.Orgs.Contains(org, StringComparer.OrdinalIgnoreCase)) Config.Ado.Orgs.Add(org);
        Config.Ado.ExcludeOrgs.RemoveAll(o => string.Equals(o, org, StringComparison.OrdinalIgnoreCase));
        OrgBox.Clear();
        ShowOrgs();
        _changed();
    }

    void OnRemoveOrg(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string org) return;
        Config.Ado.Orgs.RemoveAll(o => string.Equals(o, org, StringComparison.OrdinalIgnoreCase));
        if (!_discovered.Contains(org, StringComparer.OrdinalIgnoreCase))
            Config.Ado.ExcludeOrgs.RemoveAll(o => string.Equals(o, org, StringComparison.OrdinalIgnoreCase));
        ShowOrgs();
        _changed();
    }

    async void OnTestSignIn(object sender, RoutedEventArgs e)
    {
        TestSignInButton.IsEnabled = false;
        SignInResult.Text = "Signing in…";
        SignInResult.SetResourceReference(ForegroundProperty, "Brush.Muted");
        string result;
        try { result = await _host.TestAdoSignInAsync(Config.Ado.AzSubscription, Config.Ado.AzTenant); }
        catch (Exception ex) { result = ex.Message; }
        SignInResult.Text = result;
        SignInResult.SetResourceReference(ForegroundProperty, result.StartsWith('✓') ? "Brush.ReadyText" : "Brush.Red");
        TestSignInButton.IsEnabled = true;
    }

    // ----- Repo roots -----

    void ShowRoots() => RepoRoots.ItemsSource = Config.RepoRoots.Select((_, i) => new StringListRow(Config.RepoRoots, i, _changed)).ToList();

    void OnAddRoot(object sender, RoutedEventArgs e)
    {
        Config.RepoRoots.Add("");
        ShowRoots();
        _changed();
    }

    void OnRemoveRoot(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not int i || i >= Config.RepoRoots.Count) return;
        Config.RepoRoots.RemoveAt(i);
        ShowRoots();
        _changed();
    }

    void OnBrowseRoot(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not int i || i >= Config.RepoRoots.Count) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder that holds your repo clones" };
        if (Directory.Exists(Config.RepoRoots[i])) dialog.InitialDirectory = Config.RepoRoots[i];
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        Config.RepoRoots[i] = dialog.FolderName;
        ShowRoots();
        _changed();
    }

    // ----- Repo aliases -----

    void AliasesEdited()
    {
        var map = Config.Display.RepoAliases;
        map.Clear();
        foreach (var row in _aliases)
            if (SettingsOptions.Blank(row.Prefix) is { } prefix) map[prefix] = row.Name;
        _changed();
    }

    void OnAddAlias(object sender, RoutedEventArgs e)
    {
        _aliases = [.. _aliases, new AliasRow("", "", _ => AliasesEdited())];
        Aliases.ItemsSource = _aliases;
    }

    void OnRemoveAlias(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AliasRow row) return;
        _aliases = _aliases.Where(r => r != row).ToList();
        Aliases.ItemsSource = _aliases;
        AliasesEdited();
    }

    public IReadOnlyList<SettingsError> InputErrors() =>
        NumberInput.Errors(SettingsPage.Sources, (MaxRepoCharsBox, "display.maxRepoChars", "Max repo name length")).ToList();

    public void ShowErrors(IReadOnlyList<SettingsError> errors) => ErrorList.ItemsSource = errors.Select(e => e.Message).ToList();
}
