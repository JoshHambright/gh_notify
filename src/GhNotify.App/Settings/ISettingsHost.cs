using GhNotify.Core;
using ThemeMode = GhNotify.Core.ThemeMode;

namespace GhNotify.App;

/// <summary>Everything the settings window needs; implemented by AppController (live) and SampleSettingsHost (previews, tests).</summary>
public interface ISettingsHost
{
    AppConfig CurrentConfig { get; }
    string ConfigPath { get; }
    string LogsPath { get; }
    string DataPath { get; }
    /// <summary>Hash of the bytes <see cref="CurrentConfig"/> was loaded from (not the file as it is now), so a broken or not-yet-reloaded file conflicts on save.</summary>
    string? ConfigFileHash();
    /// <summary>The config file's current load error (CurrentConfig is then the last working config), or null.</summary>
    string? ConfigError { get; }
    /// <summary>Why the global hotkey could not be registered ("Win+Shift+G is in use by another app — pick another"), or null.</summary>
    string? HotkeyError { get; }
    /// <summary>Throws InvalidDataException (invalid) or ConfigConflictException (changed on disk since expectedHash).</summary>
    void SaveConfig(AppConfig config, string? expectedHash);
    IReadOnlyList<string> DiscoveredAdoOrgs();
    Task<string> TestAdoSignInAsync(string? subscription, string? tenant);
    bool IsSkillInstalled(string skill);
    IReadOnlyList<ActiveMute> ActiveMutes();
    /// <summary>Lifts a timed mute now (app state). Permanent mutes live in the draft's filters.mute.</summary>
    void Unmute(string pattern);
    IReadOnlyList<InboxItem> CurrentItems();
    UpdateStatus UpdateStatus { get; }
    Task CheckForUpdatesAsync();
    bool StartWithWindows { get; set; }
    ThemeMode ThemeMode { get; }
    void SetThemeMode(ThemeMode mode);
    bool CommentsWarningShown { get; set; }
    void OpenPath(string path);
}

/// <summary>A settings page: lists the validation errors that belong to it at its top.</summary>
public interface ISettingsPage
{
    void ShowErrors(IReadOnlyList<SettingsError> errors);

    /// <summary>Entries the page could not turn into a config value (e.g. "abc" in a number box); they block Save like validation errors.</summary>
    IReadOnlyList<SettingsError> InputErrors() => [];
}
