using System.Windows;
using System.Windows.Controls;
using GhNotify.Core;

namespace GhNotify.App;

public partial class WorkflowsPage : UserControl, ISettingsPage
{
    const string BuiltInDefault = "(built-in default)";
    static readonly string[] Placeholders = ["{url}", "{repo}", "{number}", "{title}", "{kind}", "{provider}", "{author}", "{detail}"];

    readonly SettingsDraft _draft;
    readonly ISettingsHost _host;
    readonly Action _changed;
    readonly Dictionary<string, bool> _skills = new(StringComparer.OrdinalIgnoreCase);
    string? _selected;
    string? _renameError;
    List<RuleRow> _ruleRows = [];
    bool _loading;
    // The other half of a profile while its type is switched (prompt ↔ command), so switching back loses nothing.
    string? _stashedPrompt;
    List<string>? _stashedCommand;

    public WorkflowsPage(SettingsDraft draft, ISettingsHost host, Action changed)
    {
        InitializeComponent();
        _draft = draft;
        _host = host;
        _changed = changed;
        foreach (var p in Placeholders)
        {
            var chip = new Button { Content = p, Style = (Style)Resources["Small"], FontFamily = (System.Windows.Media.FontFamily)Resources["Mono"] };
            chip.Click += (_, _) => InsertPlaceholder(p);
            PlaceholderChips.Children.Add(chip);
        }
        ShowAll();
    }

    AppConfig Config => _draft.Config;
    IReadOnlyList<string> ProfileNames => ProfileCatalog.All(Config).Keys.ToList();

    void ShowAll()
    {
        ShowDefaults();
        ShowRules();
        ShowProfiles();
        ShowHooks();
    }

    void OnEdited(object sender, RoutedEventArgs e)
    {
        if (!_loading) _changed();
    }

    // ----- Defaults -----

    void ShowDefaults()
    {
        DefaultsPanel.Children.Clear();
        var options = new List<string> { BuiltInDefault };
        options.AddRange(ProfileNames);
        foreach (var kind in SettingsOptions.DefaultKinds)
        {
            var current = Config.DefaultProfiles.FirstOrDefault(kv => string.Equals(kv.Key, kind.ToString(), StringComparison.OrdinalIgnoreCase)).Value;
            List<string> items = current is not null && !options.Contains(current, StringComparer.OrdinalIgnoreCase) ? [.. options, current] : options;
            var combo = new ComboBox { ItemsSource = items, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
            combo.SelectedIndex = current is null ? 0 : items.FindIndex(o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase));
            combo.SelectionChanged += (_, _) => SetDefault(kind, combo.SelectedIndex <= 0 ? null : items[combo.SelectedIndex]);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = SettingsDraft.KindLabel(kind), Style = (Style)FindResource("Style.FieldLabel") });
            row.Children.Add(combo);
            DefaultsPanel.Children.Add(row);
        }
    }

    /// <summary>null ("(built-in default)") removes the key.</summary>
    void SetDefault(ItemKind kind, string? profile)
    {
        foreach (var key in Config.DefaultProfiles.Keys.Where(k => string.Equals(k, kind.ToString(), StringComparison.OrdinalIgnoreCase)).ToList())
            Config.DefaultProfiles.Remove(key);
        if (profile is not null) Config.DefaultProfiles[kind.ToString()] = profile;
        _changed();
    }

    // ----- Rules -----

    void ShowRules()
    {
        var names = ProfileNames;
        Rules.ItemsSource = _ruleRows = Config.Rules.Select(r => new RuleRow(r, names, _changed)).ToList();
    }

    void OnAddRule(object sender, RoutedEventArgs e)
    {
        Config.Rules.Add(new Rule());
        ShowRules();
        _changed();
    }

    void OnRuleUp(object sender, RoutedEventArgs e) => MoveRule(sender, -1);
    void OnRuleDown(object sender, RoutedEventArgs e) => MoveRule(sender, 1);

    void MoveRule(object sender, int delta)
    {
        if (((FrameworkElement)sender).Tag is not RuleRow row || !SettingsDraft.Move(Config.Rules, Config.Rules.IndexOf(row.Rule), delta)) return;
        ShowRules();
        _changed();
    }

    void OnRuleRemove(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not RuleRow row) return;
        Config.Rules.Remove(row.Rule);
        ShowRules();
        ShowEditor(); // the delete button's "in use" state may change
        _changed();
    }

    // ----- Profiles -----

    void ShowProfiles()
    {
        ProfileChips.Children.Clear();
        foreach (var (name, profile) in ProfileCatalog.All(Config))
        {
            bool? skill = profile.RequiresSkill is { } s ? SkillInstalled(s) : null;
            var chip = new ProfileChip(name, !Config.AgentProfiles.ContainsKey(name), skill);
            var button = new RadioButton
            {
                Content = ChipContent(chip), ToolTip = chip.ToolTip, Style = (Style)FindResource("Style.Chip"), Tag = name,
                IsChecked = string.Equals(name, _selected, StringComparison.OrdinalIgnoreCase),
            };
            button.Checked += (_, _) => SelectProfile(name);
            ProfileChips.Children.Add(button);
        }
        var add = new Button { Content = "+ New", Style = (Style)FindResource("Style.LinkButton"), Margin = new Thickness(4, 0, 0, 8) };
        add.Click += (_, _) => AddProfile(new AgentProfile(), "new-profile");
        ProfileChips.Children.Add(add);
        ShowEditor();
    }

    static TextBlock ChipContent(ProfileChip chip)
    {
        var text = new TextBlock();
        if (chip.Mark is { } mark)
        {
            var run = new System.Windows.Documents.Run(mark + " ");
            run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, chip.SkillInstalled == true ? "Brush.Green" : "Brush.Red");
            text.Inlines.Add(run);
        }
        text.Inlines.Add(new System.Windows.Documents.Run(chip.Name));
        return text;
    }

    bool SkillInstalled(string skill)
    {
        if (!_skills.TryGetValue(skill, out var ok)) _skills[skill] = ok = _host.IsSkillInstalled(skill);
        return ok;
    }

    /// <summary>Opens a profile's card (the read-only view for built-ins, the editor for user profiles). Internal for previews.</summary>
    internal void SelectProfile(string name)
    {
        if (string.Equals(_selected, name, StringComparison.Ordinal)) return;
        _selected = name;
        foreach (var chip in ProfileChips.Children.OfType<RadioButton>())
            chip.IsChecked = string.Equals(chip.Tag as string, name, StringComparison.OrdinalIgnoreCase);
        ShowEditor();
    }

    void ShowEditor()
    {
        var all = ProfileCatalog.All(Config);
        var profile = _selected is not null && all.TryGetValue(_selected, out var p) ? p : null;
        var user = profile is not null && Config.AgentProfiles.ContainsKey(_selected!);
        BuiltInCard.Visibility = profile is not null && !user ? Visibility.Visible : Visibility.Collapsed;
        EditorCard.Visibility = user ? Visibility.Visible : Visibility.Collapsed;
        if (profile is null) return;
        if (!user)
        {
            BuiltInName.Text = _selected;
            BuiltInPrompt.Text = profile.Command is { } cmd ? string.Join(" ", cmd) : profile.Prompt;
            var allowed = Config.AgentAllowedTools.Concat(profile.AllowedTools).Distinct().ToList();
            BuiltInAllowed.Text = allowed.Count == 0 ? "none" : string.Join(", ", allowed);
            var denied = GhNotify.Agents.AgentSafety.DisallowedTools.Concat(profile.DisallowedTools).Distinct().ToList();
            BuiltInDenied.Text = $"{denied.Count} write commands — posting, approving, merging, pushing and committing (gh pr review/merge, gh api writes, git push/commit, az repos/pipelines writes…)";
            BuiltInDenied.ToolTip = string.Join("\n", denied);
            return;
        }

        _loading = true;
        try
        {
            if (!ReferenceEquals(EditorCard.DataContext, profile))
            {
                _stashedPrompt = null;
                _stashedCommand = null;
                _renameError = null;
                NameError.Visibility = Visibility.Collapsed;
                EditorCard.DataContext = profile;
                NameBox.Text = _selected;
            }
            // Never rewrite the box under the caret, nor over a refused name the user still has to fix.
            else if (!NameBox.IsKeyboardFocusWithin && _renameError is null && NameBox.Text != _selected) NameBox.Text = _selected;
            var isCommand = profile.Command is not null;
            (isCommand ? CommandType : PromptType).IsChecked = true;
            PromptPanel.Visibility = isCommand ? Visibility.Collapsed : Visibility.Visible;
            CommandPanel.Visibility = isCommand ? Visibility.Visible : Visibility.Collapsed;
            ShowArgs(profile);
            var uses = UsesOf(_selected!);
            DeleteProfileButton.IsEnabled = uses.Count == 0;
            DeleteProfileButton.ToolTip = uses.Count == 0 ? null : "In use by " + string.Join(", ", uses);
        }
        finally { _loading = false; }
    }

    AgentProfile? SelectedUserProfile => _selected is not null && Config.AgentProfiles.TryGetValue(_selected, out var p) ? p : null;

    /// <summary>What refers to a profile: "rule 2", "default for Review requested".</summary>
    List<string> UsesOf(string name)
    {
        var uses = new List<string>();
        for (var i = 0; i < Config.Rules.Count; i++)
            if (string.Equals(Config.Rules[i].Profile, name, StringComparison.OrdinalIgnoreCase)) uses.Add($"rule {i + 1}");
        foreach (var (kind, profile) in Config.DefaultProfiles)
            if (string.Equals(profile, name, StringComparison.OrdinalIgnoreCase))
                uses.Add("default for " + (Enum.TryParse<ItemKind>(kind, true, out var k) ? SettingsDraft.KindLabel(k) : kind));
        return uses;
    }

    void AddProfile(AgentProfile profile, string baseName)
    {
        var name = baseName;
        for (var n = 2; ProfileCatalog.All(Config).ContainsKey(name); n++) name = $"{baseName}-{n}";
        Config.AgentProfiles[name] = profile;
        _selected = name;
        ShowAll();
        _changed();
    }

    void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !ProfileCatalog.All(Config).TryGetValue(_selected, out var p)) return;
        AddProfile(new AgentProfile
        {
            Prompt = p.Prompt, NeedsWorktree = p.NeedsWorktree, RequiresSkill = p.RequiresSkill,
            AllowedTools = [.. p.AllowedTools], DisallowedTools = [.. p.DisallowedTools], Command = p.Command is null ? null : [.. p.Command],
        }, _selected + "-copy");
    }

    void OnNameKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        CommitRename();
    }

    void OnNameLostFocus(object sender, RoutedEventArgs e) => CommitRename();

    /// <summary>
    /// Renames the open profile to what the name box says (on Enter or leaving the box, never per keystroke). Rules and
    /// defaults that named it follow (SettingsDraft.RenameProfile). A refused name stays in the box with its error and blocks Save.
    /// </summary>
    internal void CommitRename()
    {
        if (_loading || _selected is not { } opened || SelectedUserProfile is null) return;
        _renameError = _draft.RenameProfile(opened, NameBox.Text);
        NameError.Text = _renameError ?? "";
        NameError.Visibility = _renameError is null ? Visibility.Collapsed : Visibility.Visible;
        var renamed = Config.AgentProfiles.Keys.FirstOrDefault(k => string.Equals(k, NameBox.Text.Trim(), StringComparison.Ordinal));
        if (_renameError is not null || renamed is null || renamed == opened) { _changed(); return; }
        _selected = renamed;
        ShowDefaults();
        ShowRules();
        ShowProfiles();
        _changed();
    }

    void OnTypeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || SelectedUserProfile is not { } p) return;
        var toCommand = CommandType.IsChecked == true;
        if (toCommand == p.Command is not null) return;
        if (toCommand)
        {
            _stashedPrompt = p.Prompt;
            p.Prompt = "";
            p.Command = _stashedCommand ?? [""];
        }
        else
        {
            _stashedCommand = p.Command;
            p.Command = null;
            p.Prompt = _stashedPrompt ?? "";
        }
        ShowEditor();
        _changed();
    }

    void InsertPlaceholder(string placeholder)
    {
        PromptBox.SelectedText = placeholder;
        PromptBox.CaretIndex = PromptBox.SelectionStart + placeholder.Length;
        PromptBox.SelectionLength = 0;
        PromptBox.Focus();
    }

    void ShowArgs(AgentProfile p) =>
        CommandArgs.ItemsSource = p.Command is { } cmd ? cmd.Select((_, i) => new StringListRow(cmd, i, _changed)).ToList() : null;

    void OnAddArg(object sender, RoutedEventArgs e)
    {
        if (SelectedUserProfile is not { Command: { } cmd } p) return;
        cmd.Add("");
        ShowArgs(p);
        _changed();
    }

    void OnRemoveArg(object sender, RoutedEventArgs e)
    {
        if (SelectedUserProfile is not { Command: { } cmd } p || ((FrameworkElement)sender).Tag is not int i || i >= cmd.Count) return;
        cmd.RemoveAt(i);
        ShowArgs(p);
        _changed();
    }

    void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } name || UsesOf(name).Count > 0) return;
        Config.AgentProfiles.Remove(name);
        _selected = null;
        EditorCard.DataContext = null;
        ShowAll();
        _changed();
    }

    // ----- Hooks -----

    void ShowHooks() => Hooks.ItemsSource = Config.Hooks.Select(h => new HookRow(h, _changed)).ToList();

    void OnAddHook(object sender, RoutedEventArgs e)
    {
        Config.Hooks.Add(new HookConfig { Run = [""] });
        ShowHooks();
        _changed();
    }

    void OnHookRemove(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not HookRow row) return;
        Config.Hooks.Remove(row.Hook);
        ShowHooks();
        _changed();
    }

    void OnHookArgAdd(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not HookRow row) return;
        row.Hook.Run.Add("");
        ShowHooks();
        _changed();
    }

    void OnHookArgRemove(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not StringListRow arg || arg.Index >= arg.List.Count) return;
        arg.List.RemoveAt(arg.Index);
        ShowHooks();
        _changed();
    }

    public IReadOnlyList<SettingsError> InputErrors()
    {
        var errors = _ruleRows.Select((row, i) => (row, i)).Where(x => x.row.MaxLinesInvalid)
            .Select(x => new SettingsError(SettingsPage.Workflows, $"rules[{x.i}].match.maxChangedLines", $"Rule {x.i + 1}: ≤ lines must be a positive whole number"))
            .ToList();
        if (_renameError is not null) errors.Add(new SettingsError(SettingsPage.Workflows, "agentProfiles", $"Profile name: {_renameError}"));
        return errors;
    }

    public void ShowErrors(IReadOnlyList<SettingsError> errors)
    {
        ErrorList.ItemsSource = errors.Select(e => e.Message).ToList();
    }
}
