using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using GhNotify.Core;

namespace GhNotify.UI;

public sealed class SourcesPage : SettingsPageBase
{
    readonly IReadOnlyList<string> _discovered;
    readonly WrapPanel _watchRepos = new();
    readonly StackPanel _orgs = new();
    readonly StackPanel _roots = new();
    readonly StackPanel _aliasPanel = new();
    readonly TextBlock _noOrgs;
    readonly NumberInput _maxRepoChars;
    List<AliasRow> _aliases = [];

    public SourcesPage(SettingsDraft draft, ISettingsHost host, Action changed) : base(draft, host, changed)
    {
        _discovered = host.DiscoveredAdoOrgs();

        // GitHub
        Body.Children.Add(Ui.Section("GITHUB"));
        var gh = Ui.Row(Ui.Check("Enabled", Config.GitHub.Enabled, v => { Config.GitHub.Enabled = v; Changed(); }),
            Ui.Check("Include draft PRs", Config.GitHub.IncludeDrafts, v => { Config.GitHub.IncludeDrafts = v; Changed(); }));
        ((StackPanel)gh).Spacing = 24;
        gh.Margin = new Thickness(0, 0, 0, 12);
        Body.Children.Add(gh);
        WatchRepoBox = Ui.Box("", "owner/repo — Enter to add", 260, _ => { });
        WatchRepoBox.KeyDown += (_, e) => OnAddKey(e, WatchRepoBox, AddWatchRepo);
        var watch = new StackPanel();
        watch.Children.Add(_watchRepos);
        watch.Children.Add(WatchRepoBox);
        Body.Children.Add(Ui.Field("Watch repos", watch, 22, VerticalAlignment.Top));
        ShowWatchRepos();

        // Azure DevOps
        Body.Children.Add(Ui.Section("AZURE DEVOPS"));
        var ado = Ui.Row(Ui.Check("Enabled", Config.Ado.Enabled, v => { Config.Ado.Enabled = v; Changed(); }),
            Ui.Check("Include draft PRs", Config.Ado.IncludeDrafts, v => { Config.Ado.IncludeDrafts = v; Changed(); }),
            Ui.Check("Discover orgs", Config.Ado.DiscoverOrgs, v => { Config.Ado.DiscoverOrgs = v; Changed(); }));
        ((StackPanel)ado).Spacing = 24;
        ado.Margin = new Thickness(0, 0, 0, 12);
        Body.Children.Add(ado);
        _noOrgs = Ui.Text("No orgs discovered yet.", "muted", "small");
        _noOrgs.Margin = new Thickness(0, 0, 0, 6);
        OrgBox = Ui.Box("", "Add org — Enter to add", 260, _ => { });
        OrgBox.KeyDown += (_, e) => OnAddKey(e, OrgBox, AddOrg);
        var orgs = new StackPanel();
        orgs.Children.Add(_orgs);
        orgs.Children.Add(_noOrgs);
        orgs.Children.Add(OrgBox);
        Body.Children.Add(Ui.Field("Organizations", orgs, 10, VerticalAlignment.Top));
        ShowOrgs();
        Body.Children.Add(Ui.Field("az subscription", Ui.Box(Config.Ado.AzSubscription, "default", 300, v => { Config.Ado.AzSubscription = SettingsOptions.Blank(v); Changed(); })));
        Body.Children.Add(Ui.Field("az tenant", Ui.Box(Config.Ado.AzTenant, "default", 300, v => { Config.Ado.AzTenant = SettingsOptions.Blank(v); Changed(); })));
        TestSignInButton = Ui.Btn("Test ADO sign-in", () => _ = TestSignInAsync(), "pill");
        SignInResult = Ui.Text("", "small");
        SignInResult.VerticalAlignment = VerticalAlignment.Center;
        SignInResult.MaxWidth = 320;
        var signIn = Ui.Row(TestSignInButton, SignInResult);
        signIn.Margin = new Thickness(180, 0, 0, 22);
        Body.Children.Add(signIn);

        // Repo roots
        Body.Children.Add(Ui.Section("REPO ROOTS"));
        Body.Children.Add(_roots);
        var addRoot = Ui.Btn("+ Add repo root", () =>
        {
            Config.RepoRoots.Add("");
            ShowRoots();
            Changed();
        }, "link");
        addRoot.Margin = new Thickness(0, 2, 0, 22);
        Body.Children.Add(addRoot);
        ShowRoots();

        // Display
        Body.Children.Add(Ui.Section("DISPLAY"));
        var aliasNote = Ui.Text("Repo aliases — a prefix (org or org/project) and the short name shown instead.", "muted", "small");
        aliasNote.Margin = new Thickness(0, 0, 0, 8);
        Body.Children.Add(aliasNote);
        Body.Children.Add(_aliasPanel);
        var addAlias = Ui.Btn("+ Add alias", () =>
        {
            _aliases = [.. _aliases, new AliasRow("", "", _ => AliasesEdited())];
            ShowAliases();
        }, "link");
        addAlias.Margin = new Thickness(0, 2, 0, 14);
        Body.Children.Add(addAlias);
        _aliases = Config.Display.RepoAliases.Select(kv => new AliasRow(kv.Key, kv.Value, _ => AliasesEdited())).ToList();
        ShowAliases();
        _maxRepoChars = new NumberInput(SettingsPage.Sources, "display.maxRepoChars", "Max repo name length", Config.Display.MaxRepoChars, n => Config.Display.MaxRepoChars = n, Changed, 56);
        var chars = new TextBlock { Text = "characters", VerticalAlignment = VerticalAlignment.Center };
        chars.Classes.Add("muted");
        Body.Children.Add(Ui.Field("Max repo name length", Ui.Row(_maxRepoChars.Box, chars), 0));
    }

    internal TextBox WatchRepoBox { get; }
    internal TextBox OrgBox { get; }
    internal Button TestSignInButton { get; }
    internal TextBlock SignInResult { get; }
    internal TextBox MaxRepoCharsBox => _maxRepoChars.Box;
    internal IReadOnlyList<AliasRow> Aliases => _aliases;

    static void OnAddKey(KeyEventArgs e, TextBox box, Action<string> add)
    {
        if (e.Key != Key.Enter || SettingsOptions.Blank(box.Text) is not { } value) return;
        e.Handled = true;
        add(value);
        box.Text = "";
    }

    // ----- GitHub watch repos -----

    internal void AddWatchRepo(string repo)
    {
        if (!Config.GitHub.WatchRepos.Contains(repo, StringComparer.OrdinalIgnoreCase)) Config.GitHub.WatchRepos.Add(repo);
        ShowWatchRepos();
        Changed();
    }

    void ShowWatchRepos()
    {
        _watchRepos.Children.Clear();
        foreach (var repo in Config.GitHub.WatchRepos.ToList())
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            chip.Children.Add(new TextBlock { Text = repo, VerticalAlignment = VerticalAlignment.Center });
            chip.Children.Add(Ui.Remove(() =>
            {
                Config.GitHub.WatchRepos.Remove(repo);
                ShowWatchRepos();
                Changed();
            }));
            var border = new Border { Child = chip };
            border.Classes.Add("chipborder");
            _watchRepos.Children.Add(border);
        }
    }

    // ----- ADO orgs -----

    /// <summary>Discovered orgs ∪ ado.orgs (plus any excluded org, so it can be re-included).</summary>
    void ShowOrgs()
    {
        var names = _discovered.Concat(Config.Ado.Orgs).Concat(Config.Ado.ExcludeOrgs)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _orgs.Children.Clear();
        foreach (var row in names.Select(n => new OrgRow(n, Config.Ado, Changed)))
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
            line.Children.Add(Ui.Check(row.Name, row.Included, v => row.Included = v));
            if (row.IsManual) line.Children.Add(Ui.Remove(() => RemoveOrg(row.Name)));
            _orgs.Children.Add(line);
        }
        _noOrgs.IsVisible = names.Count == 0;
    }

    internal void AddOrg(string org)
    {
        if (!Config.Ado.Orgs.Contains(org, StringComparer.OrdinalIgnoreCase)) Config.Ado.Orgs.Add(org);
        Config.Ado.ExcludeOrgs.RemoveAll(o => string.Equals(o, org, StringComparison.OrdinalIgnoreCase));
        ShowOrgs();
        Changed();
    }

    void RemoveOrg(string org)
    {
        Config.Ado.Orgs.RemoveAll(o => string.Equals(o, org, StringComparison.OrdinalIgnoreCase));
        if (!_discovered.Contains(org, StringComparer.OrdinalIgnoreCase))
            Config.Ado.ExcludeOrgs.RemoveAll(o => string.Equals(o, org, StringComparison.OrdinalIgnoreCase));
        ShowOrgs();
        Changed();
    }

    internal async Task TestSignInAsync()
    {
        TestSignInButton.IsEnabled = false;
        SignInResult.Text = "Signing in…";
        Ui.TokenForeground(SignInResult, "Brush.Muted");
        string result;
        try { result = await Host.TestAdoSignInAsync(Config.Ado.AzSubscription, Config.Ado.AzTenant); }
        catch (Exception ex) { result = ex.Message; }
        SignInResult.Text = result;
        Ui.TokenForeground(SignInResult, result.StartsWith('✓') ? "Brush.ReadyText" : "Brush.Red");
        TestSignInButton.IsEnabled = true;
    }

    // ----- Repo roots -----

    void ShowRoots()
    {
        _roots.Children.Clear();
        for (var i = 0; i < Config.RepoRoots.Count; i++)
        {
            var index = i;
            var row = new StringListRow(Config.RepoRoots, index, Changed);
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var remove = Ui.Remove(() =>
            {
                if (index >= Config.RepoRoots.Count) return;
                Config.RepoRoots.RemoveAt(index);
                ShowRoots();
                Changed();
            });
            remove.Margin = new Thickness(6, 0, 0, 0);
            var box = Ui.Box(row.Value, @"C:\path\to\repos", 0, v => row.Value = v, stretch: true);
            var browse = Ui.Btn("Browse…", () => _ = BrowseRootAsync(index), "pill");
            browse.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(remove, Dock.Right);
            DockPanel.SetDock(browse, Dock.Right);
            line.Children.Add(remove);
            line.Children.Add(browse);
            line.Children.Add(box);
            _roots.Children.Add(line);
        }
    }

    async Task BrowseRootAsync(int i)
    {
        if (i >= Config.RepoRoots.Count) return;
        var picked = await Ui.PickFolder(this, "Choose a folder that holds your repo clones", Config.RepoRoots[i]);
        if (picked is null || i >= Config.RepoRoots.Count) return;
        Config.RepoRoots[i] = picked;
        ShowRoots();
        Changed();
    }

    // ----- Repo aliases -----

    void AliasesEdited()
    {
        var map = Config.Display.RepoAliases;
        map.Clear();
        foreach (var row in _aliases)
            if (SettingsOptions.Blank(row.Prefix) is { } prefix) map[prefix] = row.Name;
        Changed();
    }

    void ShowAliases()
    {
        _aliasPanel.Children.Clear();
        foreach (var row in _aliases)
        {
            var arrow = Ui.Text("→", "muted");
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Margin = new Thickness(10, 0);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            line.Children.Add(Ui.Box(row.Prefix, "org/project", 260, v => row.Prefix = v));
            line.Children.Add(arrow);
            line.Children.Add(Ui.Box(row.Name, "short name", 160, v => row.Name = v));
            var remove = Ui.Remove(() =>
            {
                _aliases = _aliases.Where(r => r != row).ToList();
                ShowAliases();
                AliasesEdited();
            });
            remove.Margin = new Thickness(6, 0, 0, 0);
            line.Children.Add(remove);
            _aliasPanel.Children.Add(line);
        }
    }

    public override IReadOnlyList<SettingsError> InputErrors() => NumberInput.Errors(_maxRepoChars).ToList();
}
