using GhNotify.Core;
using ThemeMode = GhNotify.Core.ThemeMode;

namespace GhNotify.UI;

/// <summary>
/// Neutral sample settings (contoso / fabrikam / acme, alice / bob) for previews and tests. Nothing touches the network,
/// the registry or the shell. With a <see cref="ConfigStore"/> it reads and saves that store's file (tests use a temp folder);
/// without one, saves stay in memory.
/// </summary>
public sealed class SampleSettingsHost : ISettingsHost
{
    readonly ConfigStore? _store;
    AppConfig _config = Sample();

    public SampleSettingsHost(ConfigStore? store = null) => _store = store;

    public static AppConfig Sample() => new()
    {
        GitHub = new GitHubConfig { WatchRepos = ["acme/api"] },
        Display = new DisplayConfig { RepoAliases = new(StringComparer.OrdinalIgnoreCase) { ["fabrikam/customer-platform"] = "fab-cp" } },
        Notifications = new NotificationsConfig
        {
            Sounds =
            [
                new SoundRule { Match = new ItemMatch { Kind = ItemKind.ApprovalPending, Repo = "acme/*" }, Sound = "builtin:alarm", AlertOffHours = true },
                new SoundRule { Match = new ItemMatch { Kind = ItemKind.ReviewRequested }, Sound = @"C:\sounds\ding.wav" },
            ],
        },
        Filters = new FiltersConfig { Mute = ["acme/legacy-*"], Hide = ["gh:fabrikam/sandbox"] },
        Rules =
        [
            new Rule { Match = new ItemMatch { Kind = ItemKind.ReviewRequested, Repo = "acme/*", MaxChangedLines = 400 }, Agent = AgentMode.Auto, Profile = "peer-review" },
            new Rule { Match = new ItemMatch { Kind = ItemKind.MyPrCiFailing, Repo = "fabrikam/legacy-*" }, Agent = AgentMode.Button, Profile = "triage" },
        ],
        AgentProfiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["triage"] = new AgentProfile { NeedsWorktree = false, Command = ["pwsh", "-NoProfile", "-File", @"C:\scripts\triage.ps1", "{url}"] },
        },
    };

    public AppConfig CurrentConfig => _store?.Current ?? _config;
    public string ConfigPath => _store?.Path ?? @"C:\Users\alice\AppData\Roaming\GhNotify\config.json";
    public string LogsPath => @"C:\Users\alice\AppData\Local\GhNotify-data\logs";
    public string DataPath => @"C:\Users\alice\AppData\Local\GhNotify-data";
    public string? ConfigFileHash() => _store is not null ? _store.LoadedHash : "sample";
    public string? ConfigError => _store?.Error;
    /// <summary>Tests and previews set this to show the "in use" line.</summary>
    public string? HotkeyError { get; set; }

    public void SaveConfig(AppConfig config, string? expectedHash)
    {
        if (_store is not null) _store.Save(config, expectedHash);
        else
        {
            ConfigStore.Validate(config);
            _config = ConfigStore.Clone(config);
        }
    }

    public IReadOnlyList<string> DiscoveredAdoOrgs() => ["contoso", "fabrikam"];
    public Task<string> TestAdoSignInAsync(string? subscription, string? tenant) => Task.FromResult("✓ Signed in");
    public bool IsSkillInstalled(string skill) => skill is "peer-review" or "pr-shepherd";

    readonly List<ActiveMute> _mutes = [new("gh:acme/legacy-*", "forever", true), new("ado:contoso/platform", "45m left", false)];
    public IReadOnlyList<ActiveMute> ActiveMutes() => _mutes.ToList();
    public void Unmute(string pattern) => _mutes.RemoveAll(m => !m.Permanent && m.Pattern == pattern);

    public IReadOnlyList<InboxItem> CurrentItems()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new InboxItem("gh:review:acme/legacy-billing#12", Provider.GitHub, ItemKind.ReviewRequested, "acme/legacy-billing", "Fix rounding in invoice totals",
                "https://github.com/acme/legacy-billing/pull/12", "alice", now.AddHours(-6), "beef005", 18, null) { Number = 12 },
            new InboxItem("gh:ci:acme/api#57", Provider.GitHub, ItemKind.MyPrCiFailing, "acme/api", "Migrate settings loader to options pattern",
                "https://github.com/acme/api/pull/57", "you", now.AddHours(-3), "c0ffee1", 120, "CI failing: build-and-test") { Number = 57 },
            new InboxItem("ado:review:fabrikam/customer-platform/web-portal#479", Provider.Ado, ItemKind.ReviewRequested, "fabrikam/customer-platform/web-portal",
                "Update dependency versions for the portal build", "https://dev.azure.com/fabrikam/_git/pr/479", "bob", now.AddHours(-1), "41d07e9", 190, null) { Number = 479 },
        ];
    }

    public UpdateStatus UpdateStatus { get; set; } =
        new(UpdatePhase.Idle, "1.2.0", null, null, new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero));
    public Task CheckForUpdatesAsync() => Task.CompletedTask;
    public bool StartWithWindows { get; set; } = true;
    public ThemeMode ThemeMode { get; private set; } = ThemeMode.System;
    public void SetThemeMode(ThemeMode mode) => ThemeMode = mode;
    public bool CommentsWarningShown { get; set; }
    public void OpenPath(string path) { }
    /// <summary>Sounds the Notifications page test button played (tests).</summary>
    public List<string> PlayedSounds { get; } = [];
    public void PlaySound(string sound) => PlayedSounds.Add(sound);
}
