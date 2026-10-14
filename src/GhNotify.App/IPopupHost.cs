using GhNotify.Core;

namespace GhNotify.App;

/// <summary>Everything the popup needs; implemented by AppController (live) and SampleHost (previews).</summary>
public interface IPopupHost
{
    IReadOnlyList<InboxSection> BuildSections();
    IReadOnlyList<SourceStatus> Sources();
    string? ConfigError { get; }
    DateTimeOffset? LastPolledAt { get; }
    bool IsRefreshing { get; }
    ThemeMode ThemeMode { get; }
    event Action? Changed;
    void OpenUrl(string url);
    void RunAgent(string key);
    void OpenInClaude(string key);
    /// <summary>Collapsed ↔ Expanded; collapses any other expanded row.</summary>
    void ToggleExpand(string key);
    /// <summary>Opens confirm mode and runs the live preview check.</summary>
    Task BeginActionAsync(string key, ItemAction action);
    /// <summary>Back to Expanded.</summary>
    void CancelAction(string key);
    /// <summary>Sends the action; re-checks the confirm guard itself (600 ms, allowed preview).</summary>
    Task ConfirmActionAsync(string key, string? comment);
    /// <summary>Called when the popup is shown: re-arms the 600 ms guard on any open confirm panel.</summary>
    void OnPopupShown();
    void CycleTheme();
    Task RefreshAsync();
    void OpenConfig();
    void OpenLogs();
    void Quit();
    void CopyText(string text);
    // Named SnoozeItem, not Snooze: that would shadow the Snooze record inside AppController.
    void SnoozeItem(string key, SnoozeChoice choice);
    void Unsnooze(string key);
    void Mute(string key, MuteScope scope, MuteDuration duration);
    IReadOnlyList<ActiveMute> ActiveMutes();
    void Unmute(string pattern);
    /// <summary>Expands or collapses a collapsible section ("waiting", "snoozed").</summary>
    void ToggleSection(string id);
}

/// <summary>A mute shown in the popup's active-mutes list. <paramref name="Remaining"/> is display text ("forever", "45m left").</summary>
public sealed record ActiveMute(string Pattern, string Remaining, bool Permanent);
