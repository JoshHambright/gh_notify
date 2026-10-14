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
    string? ConfigFileHash();
    /// <summary>Throws InvalidDataException (invalid) or ConfigConflictException (changed on disk since expectedHash).</summary>
    void SaveConfig(AppConfig config, string? expectedHash);
    IReadOnlyList<string> DiscoveredAdoOrgs();
    Task<string> TestAdoSignInAsync(string? subscription, string? tenant);
    bool IsSkillInstalled(string skill);
    IReadOnlyList<ActiveMute> ActiveMutes();
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
}
