using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GhNotify.Core;

namespace GhNotify.UI;

public sealed class WorkflowsPage : SettingsPageBase
{
    const string BuiltInDefault = "(built-in default)";
    static readonly string[] Placeholders = ["{url}", "{repo}", "{number}", "{title}", "{kind}", "{provider}", "{author}", "{detail}"];
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    readonly Dictionary<string, bool> _skills = new(StringComparer.OrdinalIgnoreCase);
    readonly StackPanel _defaults = new() { Margin = new Thickness(0, 0, 0, 18) };
    readonly StackPanel _rules = new();
    readonly WrapPanel _profileChips = new() { Margin = new Thickness(0, 0, 0, 4) };
    readonly StackPanel _hooks = new();
    readonly StackPanel _commandArgs = new();
    string? _selected;
    string? _renameError;
    List<RuleRow> _ruleRows = [];
    bool _loading;
    // The other half of a profile while its type is switched (prompt <-> command), so switching back loses nothing.
    string? _stashedPrompt;
    List<string>? _stashedCommand;
    AgentProfile? _editing;

    public WorkflowsPage(SettingsDraft draft, ISettingsHost host, Action changed) : base(draft, host, changed)
    {
        Body.Children.Add(Ui.Section("DEFAULTS"));
        Body.Children.Add(_defaults);

        Body.Children.Add(Ui.Section("RULES · FIRST MATCH WINS"));
        Body.Children.Add(_rules);
        var addRule = Ui.Btn("+ Add rule", () =>
        {
            Config.Rules.Add(new Rule());
            ShowRules();
            Changed();
        }, "link");
        addRule.Margin = new Thickness(0, 2, 0, 22);
        Body.Children.Add(addRule);

        Body.Children.Add(Ui.Section("PROFILES"));
        Body.Children.Add(_profileChips);
        BuildBuiltInCard();
        BuildEditorCard();

        Body.Children.Add(Ui.Section("HOOKS"));
        Body.Children.Add(_hooks);
        var addHook = Ui.Btn("+ Add hook", () =>
        {
            Config.Hooks.Add(new HookConfig { Run = [""] });
            ShowHooks();
            Changed();
        }, "link");
        addHook.Margin = new Thickness(0, 2, 0, 0);
        Body.Children.Add(addHook);

        ShowAll();
    }

    IReadOnlyList<string> ProfileNames => ProfileCatalog.All(Config).Keys.ToList();

    internal IReadOnlyList<RuleRow> RuleRows => _ruleRows;
    internal IReadOnlyList<Control> DefaultRows => _defaults.Children;
    internal IReadOnlyList<Control> HookCards => _hooks.Children;


    void ShowAll()
    {
        ShowDefaults();
        ShowRules();
        ShowProfiles();
        ShowHooks();
    }

    // ----- Defaults -----

    void ShowDefaults()
    {
        _defaults.Children.Clear();
        var options = new List<string> { BuiltInDefault };
        options.AddRange(ProfileNames);
        foreach (var kind in SettingsOptions.DefaultKinds)
        {
            var current = Config.DefaultProfiles.FirstOrDefault(kv => string.Equals(kv.Key, kind.ToString(), StringComparison.OrdinalIgnoreCase)).Value;
            List<string> items = current is not null && !options.Contains(current, StringComparer.OrdinalIgnoreCase) ? [.. options, current] : options;
            var selected = current is null ? 0 : items.FindIndex(o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase));
            var combo = Ui.Combo(items, selected, i => SetDefault(kind, i <= 0 ? null : items[i]), 200);
            combo.HorizontalAlignment = HorizontalAlignment.Left;
            _defaults.Children.Add(Ui.Field(SettingsDraft.KindLabel(kind), combo, 8));
        }
    }

    /// <summary>null ("(built-in default)") removes the key.</summary>
    void SetDefault(ItemKind kind, string? profile)
    {
        var existing = Config.DefaultProfiles.FirstOrDefault(kv => string.Equals(kv.Key, kind.ToString(), StringComparison.OrdinalIgnoreCase));
        if (existing.Key is null && profile is null) return;
        if (existing.Key is not null && string.Equals(existing.Value, profile, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var key in Config.DefaultProfiles.Keys.Where(k => string.Equals(k, kind.ToString(), StringComparison.OrdinalIgnoreCase)).ToList())
            Config.DefaultProfiles.Remove(key);
        if (profile is not null) Config.DefaultProfiles[kind.ToString()] = profile;
        Changed();
    }

    // ----- Rules -----

    void ShowRules()
    {
        var names = ProfileNames;
        _ruleRows = Config.Rules.Select(r => new RuleRow(r, names, Changed)).ToList();
        _rules.Children.Clear();
        foreach (var row in _ruleRows) _rules.Children.Add(RuleCard(row));
    }

    Control RuleCard(RuleRow row)
    {
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        tools.Children.Add(Tool("↑", "Move up", () => MoveRule(row, -1)));
        tools.Children.Add(Tool("↓", "Move down", () => MoveRule(row, 1)));
        tools.Children.Add(Ui.Remove(() =>
        {
            Config.Rules.Remove(row.Rule);
            ShowRules();
            ShowEditor(); // the delete button's "in use" state may change
            Changed();
        }));
        DockPanel.SetDock(tools, Dock.Right);

        var wrap = new WrapPanel();
        wrap.Children.AddRange(MatchControls(row, includeMaxLines: true));
        var run = new StackPanel { Orientation = Orientation.Horizontal };
        run.Children.Add(Ui.Text("RUN", "keyword"));
        var profile = Ui.Combo(row.ProfileOptions, row.ProfileIndex, i => row.ProfileIndex = i, 120);
        profile.Margin = new Thickness(0, 2, 10, 2);
        run.Children.Add(profile);
        wrap.Children.Add(run);
        var auto = Ui.Segment("Auto", row.IsAuto);
        var button = Ui.Segment("Button", row.IsButton);
        var off = Ui.Segment("Off", row.IsOff);
        auto.IsCheckedChanged += (_, _) => { if (auto.IsChecked == true) row.IsAuto = true; };
        button.IsCheckedChanged += (_, _) => { if (button.IsChecked == true) row.IsButton = true; };
        off.IsCheckedChanged += (_, _) => { if (off.IsChecked == true) row.IsOff = true; };
        var modes = Ui.Segments(auto, button, off);
        modes.Margin = new Thickness(0, 2);
        wrap.Children.Add(modes);

        var summary = Ui.Text(row.Summary, "muted", "small");
        summary.Margin = new Thickness(0, 6, 0, 0);
        row.PropertyChanged += (_, _) => summary.Text = row.Summary;

        var body = new StackPanel();
        body.Children.Add(wrap);
        body.Children.Add(summary);
        var panel = new DockPanel();
        panel.Children.Add(tools);
        panel.Children.Add(body);
        return Ui.Card(panel);
    }

    /// <summary>WHEN kind / IN repo / BY author (/ ≤ lines) controls shared by rule and hook cards.</summary>
    static List<Control> MatchControls(MatchRow row, bool includeMaxLines)
    {
        var list = new List<Control>();
        var when = Ui.Text(includeMaxLines ? "WHEN" : "FOR", "keyword");
        when.Margin = new Thickness(0, 4, 8, 4);
        list.Add(when);
        var kind = Ui.Combo(row.KindLabels, row.KindIndex, i => row.KindIndex = i, 150);
        kind.Margin = new Thickness(0, 2, 10, 2);
        list.Add(kind);
        list.Add(Labeled("IN", Ui.Box(row.Repo, "any repo", 120, v => row.Repo = v)));
        if (includeMaxLines)
        {
            list.Add(Labeled("BY", Ui.Box(row.Author, "anyone", 80, v => row.Author = v)));
            var lines = Labeled("≤", Ui.Box(row.MaxLines, "lines", 54, v => row.MaxLines = v));
            list.Add(lines);
        }
        return list;
    }

    static Control Labeled(string keyword, Control box)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(Ui.Text(keyword, "keyword"));
        box.Margin = new Thickness(0, 2, 10, 2);
        p.Children.Add(box);
        return p;
    }

    static Button Tool(string glyph, string tip, Action click)
    {
        var b = Ui.Btn(glyph, click, "icon");
        ToolTip.SetTip(b, tip);
        return b;
    }

    void MoveRule(RuleRow row, int delta)
    {
        if (!SettingsDraft.Move(Config.Rules, Config.Rules.IndexOf(row.Rule), delta)) return;
        ShowRules();
        Changed();
    }

    // ----- Profiles -----

    void ShowProfiles()
    {
        _profileChips.Children.Clear();
        foreach (var (name, profile) in ProfileCatalog.All(Config))
        {
            bool? skill = profile.RequiresSkill is { } s ? SkillInstalled(s) : null;
            var chip = new ProfileChip(name, !Config.AgentProfiles.ContainsKey(name), skill);
            var button = new RadioButton
            {
                Content = ChipContent(chip),
                Tag = name,
                GroupName = "profiles",
                IsChecked = string.Equals(name, _selected, StringComparison.OrdinalIgnoreCase),
            };
            button.Classes.Add("chip");
            if (chip.ToolTip is { } tip) ToolTip.SetTip(button, tip);
            button.IsCheckedChanged += (_, _) => { if (button.IsChecked == true) SelectProfile(name); };
            _profileChips.Children.Add(button);
        }
        var add = Ui.Btn("+ New", () => AddProfile(new AgentProfile(), "new-profile"), "link");
        add.Margin = new Thickness(4, 0, 0, 8);
        _profileChips.Children.Add(add);
        ShowEditor();
    }

    static Control ChipContent(ProfileChip chip)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (chip.Mark is { } mark)
        {
            var m = new TextBlock { Text = mark + " " };
            Ui.TokenForeground(m, chip.SkillInstalled == true ? "Brush.Green" : "Brush.Red");
            panel.Children.Add(m);
        }
        panel.Children.Add(new TextBlock { Text = chip.Name });
        return panel;
    }

    bool SkillInstalled(string skill)
    {
        if (!_skills.TryGetValue(skill, out var ok)) _skills[skill] = ok = Host.IsSkillInstalled(skill);
        return ok;
    }

    /// <summary>Opens a profile's card (the read-only view for built-ins, the editor for user profiles). Internal for tests and previews.</summary>
    internal void SelectProfile(string name)
    {
        if (string.Equals(_selected, name, StringComparison.Ordinal)) return;
        _selected = name;
        foreach (var chip in _profileChips.Children.OfType<RadioButton>())
            chip.IsChecked = string.Equals(chip.Tag as string, name, StringComparison.OrdinalIgnoreCase);
        ShowEditor();
    }

    // --- built-in (read-only) card

    Border _builtInCard = null!;
    TextBlock _builtInName = null!, _builtInPrompt = null!, _builtInAllowed = null!, _builtInDenied = null!;

    void BuildBuiltInCard()
    {
        _builtInName = new TextBlock { FontWeight = FontWeight.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var tag = Ui.Text("  built-in · read-only", "muted", "small");
        tag.VerticalAlignment = VerticalAlignment.Center;
        var duplicate = Ui.Btn("Duplicate to edit", OnDuplicate, "pill");
        DockPanel.SetDock(duplicate, Dock.Right);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(duplicate);
        head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _builtInName, tag } });
        _builtInPrompt = Ui.Text("", "muted", "small");
        _builtInAllowed = Ui.Text("", "muted", "small");
        _builtInDenied = Ui.Text("", "muted", "small");
        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(_builtInPrompt);
        body.Children.Add(Keyword("ALLOWED TOOLS"));
        body.Children.Add(_builtInAllowed);
        body.Children.Add(Keyword("ALWAYS DENIED"));
        body.Children.Add(_builtInDenied);
        _builtInCard = Ui.Card(body);
        _builtInCard.Padding = new Thickness(14, 12);
        _builtInCard.Margin = new Thickness(0, 0, 0, 22);
        _builtInCard.IsVisible = false;
        Body.Children.Add(_builtInCard);
    }

    static TextBlock Keyword(string text)
    {
        var t = Ui.Text(text, "keyword");
        t.Margin = new Thickness(0, 10, 0, 4);
        return t;
    }

    // --- user profile editor

    Border _editorCard = null!;
    internal TextBox NameBox { get; private set; } = null!;
    internal TextBox PromptBox { get; private set; } = null!;
    TextBlock _nameError = null!;
    RadioButton _promptType = null!, _commandType = null!;
    CheckBox _needsWorktree = null!;
    TextBox _requiresSkill = null!;
    Button _deleteButton = null!;
    StackPanel _promptPanel = null!, _commandPanel = null!;

    internal bool EditorShown => _editorCard.IsVisible;
    internal bool BuiltInShown => _builtInCard.IsVisible;
    internal string? NameErrorText => _nameError.IsVisible ? _nameError.Text : null;
    internal bool DeleteEnabled => _deleteButton.IsEnabled;
    internal RadioButton CommandType => _commandType;
    internal RadioButton PromptType => _promptType;

    void BuildEditorCard()
    {
        NameBox = Ui.Box("", "", 160, _ => { });
        NameBox.FontWeight = FontWeight.Bold;
        NameBox.Margin = new Thickness(0, 2, 14, 2);
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            CommitRename();
        };
        NameBox.LostFocus += (_, _) => CommitRename();
        _promptType = Ui.Segment("Claude prompt");
        _commandType = Ui.Segment("Command");
        _promptType.IsCheckedChanged += (_, _) => OnTypeChanged();
        _commandType.IsCheckedChanged += (_, _) => OnTypeChanged();
        var types = Ui.Segments(_promptType, _commandType);
        types.Margin = new Thickness(0, 2, 14, 2);
        _needsWorktree = Ui.Check("needs worktree", false, v =>
        {
            if (_loading || _editing is null) return;
            _editing.NeedsWorktree = v;
            Changed();
        });
        _needsWorktree.Margin = new Thickness(0, 2);
        var nameLabel = Ui.Text("NAME", "keyword");
        nameLabel.Margin = new Thickness(0, 4, 8, 4);
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(nameLabel);
        top.Children.Add(NameBox);
        top.Children.Add(types);
        top.Children.Add(_needsWorktree);
        _nameError = Ui.Text("", "err", "small");
        _nameError.Margin = new Thickness(0, 0, 0, 8);
        _nameError.IsVisible = false;

        PromptBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, Watermark = "What should Claude do with {url}?", VerticalContentAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 8) };
        PromptBox.Classes.Add("pill");
        PromptBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || _loading || _editing is null) return;
            _editing.Prompt = PromptBox.Text ?? "";
            Changed();
        };
        var placeholders = new WrapPanel();
        foreach (var p in Placeholders)
        {
            var chip = Ui.Btn(p, () => InsertPlaceholder(p), "pill", "small");
            chip.FontFamily = Mono;
            placeholders.Children.Add(chip);
        }
        _promptPanel = new StackPanel();
        _promptPanel.Children.Add(PromptBox);
        _promptPanel.Children.Add(placeholders);

        var addArg = Ui.Btn("+ Add argument", () =>
        {
            if (_editing is not { Command: { } cmd } p) return;
            cmd.Add("");
            ShowArgs(p);
            Changed();
        }, "link");
        addArg.FontSize = 12;
        addArg.Margin = new Thickness(0, 0, 0, 8);
        var cmdNote = Ui.Text("Tool limits don't apply to commands. Item data is also in GHN_* environment variables.", "muted", "small");
        cmdNote.Margin = new Thickness(0, 0, 0, 8);
        _commandPanel = new StackPanel();
        _commandPanel.Children.Add(_commandArgs);
        _commandPanel.Children.Add(addArg);
        _commandPanel.Children.Add(cmdNote);

        _deleteButton = Ui.Btn("Delete profile", OnDeleteProfile, "pill");
        DockPanel.SetDock(_deleteButton, Dock.Right);
        _requiresSkill = Ui.Box("", "none", 160, v =>
        {
            if (_loading || _editing is null) return;
            _editing.RequiresSkill = SettingsOptions.Blank(v);
            Changed();
        });
        var skillRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        skillRow.Children.Add(_deleteButton);
        skillRow.Children.Add(Ui.Text("REQUIRES SKILL", "keyword"));
        skillRow.Children.Add(_requiresSkill);

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(_nameError);
        body.Children.Add(_promptPanel);
        body.Children.Add(_commandPanel);
        body.Children.Add(skillRow);
        _editorCard = Ui.Card(body);
        _editorCard.Padding = new Thickness(14, 12);
        _editorCard.Margin = new Thickness(0, 0, 0, 22);
        _editorCard.IsVisible = false;
        Body.Children.Add(_editorCard);
    }

    void ShowEditor()
    {
        var all = ProfileCatalog.All(Config);
        var profile = _selected is not null && all.TryGetValue(_selected, out var p) ? p : null;
        var user = profile is not null && Config.AgentProfiles.ContainsKey(_selected!);
        _builtInCard.IsVisible = profile is not null && !user;
        _editorCard.IsVisible = user;
        if (profile is null) return;
        if (!user)
        {
            _builtInName.Text = _selected;
            _builtInPrompt.Text = profile.Command is { } cmd ? string.Join(" ", cmd) : profile.Prompt;
            var allowed = Config.AgentAllowedTools.Concat(profile.AllowedTools).Distinct().ToList();
            _builtInAllowed.Text = allowed.Count == 0 ? "none" : string.Join(", ", allowed);
            var denied = GhNotify.Agents.AgentSafety.DisallowedTools.Concat(profile.DisallowedTools).Distinct().ToList();
            _builtInDenied.Text = $"{denied.Count} write commands — posting, approving, merging, pushing and committing (gh pr review/merge, gh api writes, git push/commit, az repos/pipelines writes…)";
            ToolTip.SetTip(_builtInDenied, string.Join("\n", denied));
            return;
        }

        _loading = true;
        try
        {
            if (!ReferenceEquals(_editing, profile))
            {
                _stashedPrompt = null;
                _stashedCommand = null;
                _renameError = null;
                _nameError.IsVisible = false;
                _editing = profile;
                NameBox.Text = _selected;
                _needsWorktree.IsChecked = profile.NeedsWorktree;
                _requiresSkill.Text = profile.RequiresSkill ?? "";
            }
            // Never rewrite the box under the caret, nor over a refused name the user still has to fix.
            else if (!NameBox.IsKeyboardFocusWithin && _renameError is null && NameBox.Text != _selected) NameBox.Text = _selected;
            var isCommand = profile.Command is not null;
            (isCommand ? _commandType : _promptType).IsChecked = true;
            PromptBox.Text = profile.Prompt;
            _promptPanel.IsVisible = !isCommand;
            _commandPanel.IsVisible = isCommand;
            ShowArgs(profile);
            var uses = UsesOf(_selected!);
            _deleteButton.IsEnabled = uses.Count == 0;
            ToolTip.SetTip(_deleteButton, uses.Count == 0 ? null : "In use by " + string.Join(", ", uses));
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
        Changed();
    }

    void OnDuplicate()
    {
        if (_selected is null || !ProfileCatalog.All(Config).TryGetValue(_selected, out var p)) return;
        AddProfile(new AgentProfile
        {
            Prompt = p.Prompt,
            NeedsWorktree = p.NeedsWorktree,
            RequiresSkill = p.RequiresSkill,
            AllowedTools = [.. p.AllowedTools],
            DisallowedTools = [.. p.DisallowedTools],
            Command = p.Command is null ? null : [.. p.Command],
        }, _selected + "-copy");
    }

    /// <summary>
    /// Renames the open profile to what the name box says (on Enter or leaving the box, never per keystroke). Rules and
    /// defaults that named it follow (SettingsDraft.RenameProfile). A refused name stays in the box with its error and blocks Save.
    /// </summary>
    internal void CommitRename()
    {
        if (_loading || _selected is not { } opened || SelectedUserProfile is null) return;
        var typed = NameBox.Text ?? "";
        _renameError = Draft.RenameProfile(opened, typed);
        _nameError.Text = _renameError ?? "";
        _nameError.IsVisible = _renameError is not null;
        var renamed = Config.AgentProfiles.Keys.FirstOrDefault(k => string.Equals(k, typed.Trim(), StringComparison.Ordinal));
        if (_renameError is not null || renamed is null || renamed == opened) { Changed(); return; }
        _selected = renamed;
        ShowDefaults();
        ShowRules();
        ShowProfiles();
        Changed();
    }

    void OnTypeChanged()
    {
        if (_loading || SelectedUserProfile is not { } p) return;
        var toCommand = _commandType.IsChecked == true;
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
        Changed();
    }

    void InsertPlaceholder(string placeholder)
    {
        PromptBox.SelectedText = placeholder;
        PromptBox.CaretIndex = PromptBox.SelectionStart + placeholder.Length;
        PromptBox.SelectionStart = PromptBox.SelectionEnd = PromptBox.CaretIndex;
        PromptBox.Focus();
    }

    void ShowArgs(AgentProfile p)
    {
        _commandArgs.Children.Clear();
        if (p.Command is not { } cmd) return;
        var wrap = new WrapPanel();
        for (var i = 0; i < cmd.Count; i++)
        {
            var index = i;
            var row = new StringListRow(cmd, index, Changed);
            var box = Ui.Box(row.Value, "arg", 0, v => row.Value = v);
            box.MinWidth = 60;
            box.Width = double.NaN;
            box.FontFamily = Mono;
            box.FontSize = 12;
            var remove = Ui.Remove(() =>
            {
                if (index >= cmd.Count) return;
                cmd.RemoveAt(index);
                ShowArgs(p);
                Changed();
            }, "Remove argument", tiny: true);
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 6) };
            item.Children.Add(box);
            item.Children.Add(remove);
            wrap.Children.Add(item);
        }
        _commandArgs.Children.Add(wrap);
    }

    void OnDeleteProfile()
    {
        if (_selected is not { } name || UsesOf(name).Count > 0) return;
        Config.AgentProfiles.Remove(name);
        _selected = null;
        _editing = null;
        ShowAll();
        Changed();
    }

    // ----- Hooks -----

    void ShowHooks()
    {
        _hooks.Children.Clear();
        foreach (var hook in Config.Hooks.ToList()) _hooks.Children.Add(HookCard(new HookRow(hook, Changed)));
    }

    Control HookCard(HookRow row)
    {
        var remove = Ui.Remove(() =>
        {
            Config.Hooks.Remove(row.Hook);
            ShowHooks();
            Changed();
        });
        remove.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(remove, Dock.Right);

        var first = new WrapPanel();
        var on = Ui.Text("ON", "keyword");
        on.Margin = new Thickness(0, 4, 8, 4);
        first.Children.Add(on);
        var ev = Ui.Combo(row.EventLabels, row.EventIndex, i => row.EventIndex = i, 120);
        ev.Margin = new Thickness(0, 2, 10, 2);
        first.Children.Add(ev);
        first.Children.AddRange(MatchControls(row, includeMaxLines: false));

        var args = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var run = Ui.Text("RUN", "keyword");
        run.Margin = new Thickness(0, 4, 8, 4);
        args.Children.Add(run);
        foreach (var arg in row.Args)
        {
            var box = Ui.Box(arg.Value, "arg", 0, v => arg.Value = v);
            box.MinWidth = 60;
            box.Width = double.NaN;
            box.FontFamily = Mono;
            box.FontSize = 12;
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 8, 2) };
            item.Children.Add(box);
            item.Children.Add(Ui.Remove(() =>
            {
                if (arg.Index >= arg.List.Count) return;
                arg.List.RemoveAt(arg.Index);
                ShowHooks();
                Changed();
            }, "Remove argument", tiny: true));
            args.Children.Add(item);
        }
        var addArg = Ui.Btn("+ arg", () =>
        {
            row.Hook.Run.Add("");
            ShowHooks();
            Changed();
        }, "link");
        addArg.FontSize = 12;
        args.Children.Add(addArg);

        var body = new StackPanel();
        body.Children.Add(first);
        body.Children.Add(args);
        var panel = new DockPanel();
        panel.Children.Add(remove);
        panel.Children.Add(body);
        return Ui.Card(panel);
    }

    public override IReadOnlyList<SettingsError> InputErrors()
    {
        var errors = _ruleRows.Select((row, i) => (row, i)).Where(x => x.row.MaxLinesInvalid)
            .Select(x => new SettingsError(SettingsPage.Workflows, $"rules[{x.i}].match.maxChangedLines", $"Rule {x.i + 1}: ≤ lines must be a positive whole number"))
            .ToList();
        if (_renameError is not null) errors.Add(new SettingsError(SettingsPage.Workflows, "agentProfiles", $"Profile name: {_renameError}"));
        return errors;
    }
}
