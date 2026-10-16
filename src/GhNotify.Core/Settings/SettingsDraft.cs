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

    /// <summary>
    /// Renames a user profile: re-keys agentProfiles and points rules and defaultProfiles that named <paramref name="oldName"/> at the
    /// new name (other references are left alone). Returns an error message, or null when renamed (or unchanged).
    /// </summary>
    public string? RenameProfile(string oldName, string newName)
    {
        var profiles = Config.AgentProfiles;
        var key = profiles.Keys.FirstOrDefault(k => string.Equals(k, oldName, StringComparison.OrdinalIgnoreCase));
        if (key is null) return $"No profile named '{oldName}'.";
        var name = newName.Trim();
        if (name.Length == 0) return "The profile needs a name.";
        if (name == key) return null;
        var sameProfile = string.Equals(name, key, StringComparison.OrdinalIgnoreCase);
        if (!sameProfile && profiles.ContainsKey(name)) return $"A profile named '{name}' already exists.";
        if (!sameProfile && ProfileCatalog.BuiltIn.ContainsKey(name)) return $"'{name}' is a built-in profile name.";

        var profile = profiles[key];
        profiles.Remove(key);
        profiles[name] = profile;
        foreach (var rule in Config.Rules.Where(r => string.Equals(r.Profile, key, StringComparison.OrdinalIgnoreCase))) rule.Profile = name;
        foreach (var kind in Config.DefaultProfiles.Where(kv => string.Equals(kv.Value, key, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
            Config.DefaultProfiles[kind] = name;
        return null;
    }

    static string Serialize(AppConfig c) => JsonSerializer.Serialize(c, Json.Options);

    public static SettingsPage PageFor(string field)
    {
        var head = field.Split('.', '[')[0];
        return head switch
        {
            "pollSeconds" or "hotkey" or "actions" or "workHours" or "updates" or "maxConcurrentAgents" or "agentTimeoutMinutes" => SettingsPage.General,
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
        "default" => OperatingSystem.IsMacOS() ? "macOS default" : "Windows default",
        "none" => "None",
        _ when sound.Length > "builtin:".Length && sound.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase) =>
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
