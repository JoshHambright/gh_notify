using System.ComponentModel;
using GhNotify.Core;

namespace GhNotify.UI;

// Binding glue for the settings pages: thin row objects that read and write straight through to the draft's config POCOs.
// They exist where a POCO can't be bound directly — list entries that are plain strings, dictionary entries, a nullable
// ItemMatch that must stay null until something is set, and combo boxes that pick from a fixed option list by index.

/// <summary>Option lists shared by the pages.</summary>
public static class SettingsOptions
{
    public static readonly IReadOnlyList<ItemKind?> Kinds =
        [null, ItemKind.ReviewRequested, ItemKind.ApprovalPending, ItemKind.MyPrCiFailing, ItemKind.MyPrNeedsResponse, ItemKind.MyPrReadyToMerge, ItemKind.MyPrWaiting];

    public static readonly IReadOnlyList<string> KindLabels = Kinds.Select(SettingsDraft.KindLabel).ToList();

    /// <summary>The kinds that have a default profile row on the Workflows page, in display order.</summary>
    public static readonly IReadOnlyList<ItemKind> DefaultKinds =
        [ItemKind.ReviewRequested, ItemKind.MyPrCiFailing, ItemKind.MyPrNeedsResponse, ItemKind.ApprovalPending, ItemKind.MyPrReadyToMerge];

    public static readonly IReadOnlyList<HookEvent> HookEvents = Enum.GetValues<HookEvent>();
    public static readonly IReadOnlyList<string> HookEventLabels = ["Item added", "Item removed", "Item changed", "Agent finished"];

    public const string CustomSound = "Custom file…";

    public static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Raises PropertyChanged for <see cref="Summary"/> after every edit, so a card's summary line follows the edits.</summary>
public abstract class SettingsRow(Action edited) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public virtual string Summary => "";

    protected void Edited()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        edited();
    }
}

/// <summary>Edits an ItemMatch? that stays null while nothing is set (so an untouched config round-trips unchanged).</summary>
public abstract class MatchRow(Action edited) : SettingsRow(edited)
{
    protected abstract ItemMatch? Match { get; set; }

    public IReadOnlyList<string> KindLabels => SettingsOptions.KindLabels;

    public int KindIndex
    {
        get => SettingsOptions.Kinds.ToList().IndexOf(Match?.Kind);
        set { if (value >= 0 && value < SettingsOptions.Kinds.Count) Edit(m => m.Kind = SettingsOptions.Kinds[value]); }
    }

    public string Repo
    {
        get => Match?.Repo ?? "";
        set => Edit(m => m.Repo = SettingsOptions.Blank(value));
    }

    public string Author
    {
        get => Match?.Author ?? "";
        set => Edit(m => m.Author = SettingsOptions.Blank(value));
    }

    string? _badMaxLines;

    /// <summary>Text for "≤ N lines": empty clears the limit; text that isn't a positive whole number is kept (and reported) without touching the limit.</summary>
    public string MaxLines
    {
        get => _badMaxLines ?? Match?.MaxChangedLines?.ToString() ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) { _badMaxLines = null; Edit(m => m.MaxChangedLines = null); }
            else if (int.TryParse(value.Trim(), out var n) && n > 0) { _badMaxLines = null; Edit(m => m.MaxChangedLines = n); }
            else { _badMaxLines = value; Edited(); }
        }
    }

    public bool MaxLinesInvalid => _badMaxLines is not null;

    void Edit(Action<ItemMatch> edit)
    {
        var m = Match ?? new ItemMatch();
        edit(m);
        Match = m is { Provider: null, Kind: null, Repo: null, Author: null, MaxChangedLines: null } ? null : m;
        Edited();
    }
}

public sealed class SoundRow : MatchRow
{
    readonly List<string> _values;

    public SoundRow(SoundRule rule, Action edited) : base(edited)
    {
        Rule = rule;
        _values = ["default", "none", .. SoundNames.BuiltIn.Select(n => "builtin:" + n)];
        if (!_values.Contains(rule.Sound, StringComparer.OrdinalIgnoreCase)) _values.Add(rule.Sound);
        SoundOptions = [.. _values.Select(SettingsDraft.SoundLabel), SettingsOptions.CustomSound];
    }

    public SoundRule Rule { get; }
    protected override ItemMatch? Match { get => Rule.Match; set => Rule.Match = value; }
    public IReadOnlyList<string> SoundOptions { get; }
    public string SoundToolTip => Path.IsPathRooted(Rule.Sound) ? Rule.Sound : SettingsDraft.SoundLabel(Rule.Sound);
    public bool CanPlay => Rule.Sound != "none";
    public override string Summary => SettingsDraft.Summary(Rule);

    public int SoundIndex
    {
        get => _values.FindIndex(v => string.Equals(v, Rule.Sound, StringComparison.OrdinalIgnoreCase));
        set
        {
            // "Custom file…" (the last entry) changes nothing here: the page opens the picker once the selection is committed.
            if (value < 0 || value >= _values.Count) return;
            Rule.Sound = _values[value];
            Edited();
        }
    }

    public int CustomIndex => _values.Count;

    /// <summary>A .wav picked through "Custom file…".</summary>
    public void SetCustomSound(string path)
    {
        Rule.Sound = path;
        Edited();
    }

    public bool AlertOffHours
    {
        get => Rule.AlertOffHours;
        set { Rule.AlertOffHours = value; Edited(); }
    }
}

public sealed class RuleRow(Rule rule, IReadOnlyList<string> profileNames, Action edited) : MatchRow(edited)
{
    public const string KindDefault = "(kind default)";

    public Rule Rule { get; } = rule;
    protected override ItemMatch? Match { get => Rule.Match; set => Rule.Match = value; }
    public override string Summary => SettingsDraft.Summary(Rule);

    /// <summary>"(kind default)" (no profile) then every profile name; an unknown name stays listed so it isn't lost.</summary>
    public IReadOnlyList<string> ProfileOptions { get; } =
        [KindDefault, .. profileNames, .. rule.Profile is { } p && !profileNames.Contains(p, StringComparer.OrdinalIgnoreCase) ? [p] : Array.Empty<string>()];

    public int ProfileIndex
    {
        get => Rule.Profile is null ? 0 : ProfileOptions.ToList().FindIndex(n => string.Equals(n, Rule.Profile, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value < 0 || value >= ProfileOptions.Count) return;
            Rule.Profile = value == 0 ? null : ProfileOptions[value];
            Edited();
        }
    }

    public bool IsAuto { get => Rule.Agent == AgentMode.Auto; set => SetMode(value, AgentMode.Auto); }
    public bool IsButton { get => Rule.Agent == AgentMode.Button; set => SetMode(value, AgentMode.Button); }
    public bool IsOff { get => Rule.Agent == AgentMode.None; set => SetMode(value, AgentMode.None); }

    void SetMode(bool on, AgentMode mode)
    {
        if (!on || Rule.Agent == mode) return;
        Rule.Agent = mode;
        Edited();
    }
}

public sealed class HookRow : MatchRow
{
    public HookRow(HookConfig hook, Action edited) : base(edited)
    {
        Hook = hook;
        Args = hook.Run.Select((_, i) => new StringListRow(hook.Run, i, edited)).ToList();
    }

    public HookConfig Hook { get; }
    protected override ItemMatch? Match { get => Hook.Match; set => Hook.Match = value; }
    public IReadOnlyList<string> EventLabels => SettingsOptions.HookEventLabels;
    public IReadOnlyList<StringListRow> Args { get; }

    public int EventIndex
    {
        get => SettingsOptions.HookEvents.ToList().IndexOf(Hook.Event);
        set { if (value >= 0 && value < SettingsOptions.HookEvents.Count) { Hook.Event = SettingsOptions.HookEvents[value]; Edited(); } }
    }
}

/// <summary>One entry of a List&lt;string&gt; (repo root, command argument), edited in place.</summary>
public sealed class StringListRow(List<string> list, int index, Action edited)
{
    public List<string> List { get; } = list;
    public int Index { get; } = index;

    public string Value
    {
        get => Index < List.Count ? List[Index] : "";
        set { if (Index < List.Count) { List[Index] = value; edited(); } }
    }
}

/// <summary>A repo alias (prefix → short name); the page rebuilds the dictionary from its rows after each edit.</summary>
public sealed class AliasRow(string prefix, string name, Action<AliasRow> edited)
{
    string _prefix = prefix;
    string _name = name;

    public string Prefix { get => _prefix; set { _prefix = value; edited(this); } }
    public string Name { get => _name; set { _name = value; edited(this); } }
}

/// <summary>An ADO org: discovered and/or listed by hand; unchecking it adds it to ado.excludeOrgs.</summary>
public sealed class OrgRow(string name, AdoConfig ado, Action edited)
{
    public string Name { get; } = name;
    public bool IsManual => ado.Orgs.Contains(Name, StringComparer.OrdinalIgnoreCase);

    public bool Included
    {
        get => !ado.ExcludeOrgs.Contains(Name, StringComparer.OrdinalIgnoreCase);
        set
        {
            ado.ExcludeOrgs.RemoveAll(o => string.Equals(o, Name, StringComparison.OrdinalIgnoreCase));
            if (!value) ado.ExcludeOrgs.Add(Name);
            edited();
        }
    }
}

/// <summary>Whether agents can use a skill and, when they can't, a local clone's own copy of it (which agents don't load).</summary>
public sealed record SkillStatus(bool Installed, string? ProjectCopy = null);

/// <summary>A profile chip on the Workflows page.</summary>
public sealed record ProfileChip(string Name, bool BuiltIn, SkillStatus? Skill)
{
    public bool? SkillInstalled => Skill?.Installed;
    /// <summary>"✓" (skill installed, green) or "✗" (missing, red) before the name; null when the profile needs no skill.</summary>
    public string? Mark => SkillInstalled switch { true => "✓", false => "✗", _ => null };
    public string? ToolTip => Skill switch
    {
        { Installed: true } => "Skill installed",
        { ProjectCopy: not null } => "Skill is only in a repo's .claude/skills, which agents don't load — select the profile to install it for your user",
        not null => "Skill not installed — runs are disabled",
        _ => BuiltIn ? "Built-in (read-only)" : null,
    };
}

/// <summary>
/// A number box that writes straight through to an int setting. Text that isn't a whole number never reaches the config,
/// so it is reported through <see cref="Error"/> (and blocks Save) instead.
/// </summary>
public sealed class NumberInput
{
    readonly SettingsPage _page;
    readonly string _field;
    readonly string _label;

    public NumberInput(SettingsPage page, string field, string label, int value, Action<int> set, Action changed, double width)
    {
        _page = page;
        _field = field;
        _label = label;
        Box = new Avalonia.Controls.TextBox { Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture), Width = width, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        Box.Classes.Add("pill");
        Box.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.TextBox.TextProperty) return;
            if (TryValue(out var n)) set(n);
            changed();
        };
    }

    public Avalonia.Controls.TextBox Box { get; }

    public bool TryValue(out int value) =>
        int.TryParse(Box.Text?.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out value);

    public SettingsError? Error => TryValue(out _) ? null : new SettingsError(_page, _field, $"{_label}: enter a whole number");

    public static IEnumerable<SettingsError> Errors(params NumberInput[] inputs) =>
        inputs.Select(i => i.Error).OfType<SettingsError>();
}
