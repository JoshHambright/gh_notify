using System.Text.Json;

namespace GhNotify.Core;

public enum SettingsPage { General, Sources, Notifications, Filters, Workflows, Advanced }

public sealed record SettingsError(SettingsPage Page, string Field, string Message);

/// <summary>An editable deep copy of the config for the settings window; nothing here touches disk.</summary>
public sealed class SettingsDraft
{
    readonly string _original;

    public SettingsDraft(AppConfig current, string? fileHash)
    {
        Config = ConfigStore.Clone(current);
        _original = Serialize(Config);
        FileHashAtOpen = fileHash;
    }

    public AppConfig Config { get; private set; }
    public string? FileHashAtOpen { get; }
    public bool IsDirty => Serialize(Config) != _original;

    public IReadOnlyList<SettingsError> Validate() =>
        ConfigValidator.Errors(Config).Select(e => new SettingsError(PageFor(e.Field), e.Field, e.Message)).ToList();

    public void ResetToDefaults()
    {
        var extra = Config.Extra;
        Config = new AppConfig { Extra = extra };
    }

    static string Serialize(AppConfig c) => JsonSerializer.Serialize(c, Json.Options);

    public static SettingsPage PageFor(string field)
    {
        var head = field.Split('.', '[')[0];
        return head switch
        {
            "pollSeconds" or "workHours" or "updates" or "maxConcurrentAgents" or "agentTimeoutMinutes" => SettingsPage.General,
            "github" or "ado" or "repoRoots" or "display" => SettingsPage.Sources,
            "notifications" => SettingsPage.Notifications,
            "filters" => SettingsPage.Filters,
            "rules" or "agentProfiles" or "defaultProfiles" or "hooks" or "agentAllowedTools" => SettingsPage.Workflows,
            _ => SettingsPage.Advanced,
        };
    }

    public static bool Move<T>(List<T> list, int index, int delta)
    {
        var to = index + delta;
        if (index < 0 || index >= list.Count || to < 0 || to >= list.Count) return false;
        (list[index], list[to]) = (list[to], list[index]);
        return true;
    }

    public static string KindLabel(ItemKind? kind) => kind switch
    {
        null => "Any item",
        ItemKind.ReviewRequested => "Review requested",
        ItemKind.ApprovalPending => "Approval pending",
        ItemKind.MyPrCiFailing => "My PR — CI failing",
        ItemKind.MyPrNeedsResponse => "My PR — needs response",
        ItemKind.MyPrReadyToMerge => "My PR — ready to merge",
        ItemKind.MyPrWaiting => "My PR — waiting on others",
        _ => kind.ToString()!,
    };

    public static string SoundLabel(string sound) => sound switch
    {
        "default" => "Windows default",
        "none" => "None",
        _ when sound.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase) =>
            char.ToUpperInvariant(sound["builtin:".Length]) + sound["builtin:".Length..][1..],
        _ => Path.GetFileName(sound),
    };

    static string Where(ItemMatch? m)
    {
        var s = $"When {KindLabel(m?.Kind)} in {(string.IsNullOrEmpty(m?.Repo) ? "any repo" : m!.Repo)}";
        if (!string.IsNullOrEmpty(m?.Author)) s += $" by {m!.Author}";
        if (m?.MaxChangedLines is { } n) s += $" ≤ {n} lines";
        return s;
    }

    public static string Summary(Rule r)
    {
        var profile = r.Profile ?? (r.Match?.Kind is { } k ? RulesEngine.DefaultProfileFor(k, new AppConfig()) : null);
        var mode = r.Agent switch { AgentMode.Auto => "Auto", AgentMode.None => "Off", _ => "Button" };
        return $"{Where(r.Match)} → {(profile is null ? "default" : InboxPresenter.LabelFor(profile))} · {mode}";
    }

    public static string Summary(SoundRule s) =>
        $"{Where(s.Match)} → {SoundLabel(s.Sound)}" + (s.AlertOffHours ? " · alert off-hours" : "");

    public static int CountMatches(string pattern, IEnumerable<InboxItem> items) =>
        FilterPattern.TryParse(pattern, out var p, out _) ? items.Count(p!.Matches) : -1;

    /// <summary>True when the JSON text has // or /* comments outside string literals.</summary>
    public static bool HasComments(string json)
    {
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
            }
            else if (c == '"') inString = true;
            else if (c == '/' && i + 1 < json.Length && json[i + 1] is '/' or '*') return true;
        }
        return false;
    }
}
