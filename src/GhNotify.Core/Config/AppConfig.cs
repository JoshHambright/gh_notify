using System.Text.Json.Serialization;

namespace GhNotify.Core;

public sealed class AppConfig
{
    public int PollSeconds { get; set; } = 120;
    public WorkHoursConfig WorkHours { get; set; } = new();
    [JsonPropertyName("github")] public GitHubConfig GitHub { get; set; } = new();
    public AdoConfig Ado { get; set; } = new();
    public List<string> RepoRoots { get; set; } = [DefaultRepoRoot()];

    /// <summary>C:\dev on Windows, ~/dev elsewhere. Only a new config (or a file that omits repoRoots) gets it; an explicit list is never touched.</summary>
    public static string DefaultRepoRoot() => DefaultRepoRoot(OperatingSystem.IsWindows(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string DefaultRepoRoot(bool windows, string home) => windows ? @"C:\dev" : System.IO.Path.Combine(home, "dev");
    public int MaxConcurrentAgents { get; set; } = 2;
    public int AgentTimeoutMinutes { get; set; } = 20;
    public List<string> AgentAllowedTools { get; set; } =
    [
        "Read", "Grep", "Glob", "Bash(gh pr view:*)", "Bash(gh pr diff:*)", "Bash(gh pr checks:*)",
        "Bash(gh run view:*)", "Bash(gh api:*)", "Bash(git:*)",
        "Bash(az repos pr show:*)", "Bash(az repos pr list:*)", "Bash(az repos pr policy list:*)",
        "Bash(az pipelines runs show:*)", "Bash(az pipelines runs list:*)", "Bash(az pipelines build show:*)", "Bash(az pipelines build list:*)",
        "Bash(bash:*)",
    ];
    public List<Rule> Rules { get; set; } = [];
    public Dictionary<string, AgentProfile> AgentProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<HookConfig> Hooks { get; set; } = [];
    public DisplayConfig Display { get; set; } = new();
    public UpdatesConfig Updates { get; set; } = new();
    public NotificationsConfig Notifications { get; set; } = new();
    public FiltersConfig Filters { get; set; } = new();
    /// <summary>Global hotkey that toggles the popup, e.g. "Win+Shift+G"; "" turns it off. The default depends on the OS.</summary>
    public string Hotkey { get; set; } = DefaultHotkey(OperatingSystem.IsMacOS());

    /// <summary>Win+Shift+G on Windows; Ctrl+Option+G on macOS, where Cmd+Shift+G is Finder's Go to Folder.</summary>
    public static string DefaultHotkey(bool mac) => mac ? "Ctrl+Option+G" : "Win+Shift+G";
    public ActionsConfig Actions { get; set; } = new();
    /// <summary>Item kind name (e.g. "ReviewRequested") → profile name; overrides the built-in default per kind.</summary>
    public Dictionary<string, string> DefaultProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Unknown keys round-trip through Settings saves.</summary>
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

public sealed class ActionsConfig
{
    /// <summary>Preferred merge method (used when the repo allows it); null = repo default.</summary>
    public MergeMethod? MergeMethod { get; set; }
}

public sealed class WorkHoursConfig
{
    public List<string> Days { get; set; } = ["Mon", "Tue", "Wed", "Thu", "Fri"];
    public string Start { get; set; } = "08:00";
    public string End { get; set; } = "18:00";
}

public sealed class GitHubConfig
{
    public bool Enabled { get; set; } = true;
    public bool IncludeDrafts { get; set; }
    public List<string> WatchRepos { get; set; } = [];
}

public sealed class AdoConfig
{
    public bool Enabled { get; set; } = true;
    public List<string> Orgs { get; set; } = [];
    public bool DiscoverOrgs { get; set; } = true;
    public List<string> ExcludeOrgs { get; set; } = [];
    public bool IncludeDrafts { get; set; }
    public string? AzSubscription { get; set; }
    public string? AzTenant { get; set; }
}

public sealed class ItemMatch
{
    public Provider? Provider { get; set; }
    public string? Repo { get; set; }
    public ItemKind? Kind { get; set; }
    public string? Author { get; set; }
    public int? MaxChangedLines { get; set; }
}

public enum AgentMode { Auto, Button, None }

public sealed class Rule
{
    public ItemMatch? Match { get; set; }
    public AgentMode Agent { get; set; } = AgentMode.Button;
    public string? Profile { get; set; }
}

public sealed class AgentProfile
{
    public string Prompt { get; set; } = "";
    public bool NeedsWorktree { get; set; } = true;
    /// <summary>Skill name the prompt invokes (e.g. "peer-review"); when set and not installed, the run button is disabled.</summary>
    public string? RequiresSkill { get; set; }
    public List<string> AllowedTools { get; set; } = [];
    public List<string> DisallowedTools { get; set; } = [];
    /// <summary>Run this command (argv; placeholders filled per argument) instead of claude. Exactly one of Prompt/Command.</summary>
    public List<string>? Command { get; set; }
}

public enum HookEvent { ItemAdded, ItemRemoved, ItemChanged, AgentFinished }

public sealed class HookConfig
{
    public HookEvent Event { get; set; }
    public ItemMatch? Match { get; set; }
    public List<string> Run { get; set; } = [];
}
