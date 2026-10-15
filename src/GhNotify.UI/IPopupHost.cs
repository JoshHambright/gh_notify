using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>Everything the popup needs; implemented by AppController (live) and SampleHost (previews).</summary>
public interface IPopupHost
{
    /// <summary>
    /// The sections, filtered by the search <paramref name="query"/> (null/blank = unfiltered). While a query is active the Waiting and
    /// Snoozed sections are built expanded so their rows can match, and the batch buttons are off. <see cref="HeaderCount"/> is never filtered.
    /// </summary>
    IReadOnlyList<InboxSection> BuildSections(string? query);
    IReadOnlyList<SourceStatus> Sources();
    /// <summary>The header number: active rows that are neither muted, snoozed nor waiting on others (the tray badge count).</summary>
    int HeaderCount { get; }
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
    Task ConfirmActionAsync(string key, ActionInput input);
    /// <summary>The batch approve/reject panel state (Idle when none is open).</summary>
    BatchCoordinator Batch { get; }
    /// <summary>Opens the batch panel for the Approvals section; returns a message when it can't open (null = opened).</summary>
    Task<string?> BeginBatchAsync(ItemAction action);
    /// <summary>Closes the batch panel (a no-op while items are being sent).</summary>
    void CancelBatch();
    /// <summary>Sends the ticked batch items sequentially; re-checks the 600 ms guard itself.</summary>
    Task ConfirmBatchAsync(string? comment);
    /// <summary>Called when the popup is shown: re-arms the 600 ms guard on any open confirm panel.</summary>
    void OnPopupShown();
    void CycleTheme();
    Task RefreshAsync();
    void OpenConfig();
    void OpenLogs();
    /// <summary>Opens (or re-activates) the settings window.</summary>
    void OpenSettings();
    void Quit();
    /// <summary>The "Start with Windows" toggle in the footer menu (backed by IStartupService).</summary>
    bool StartWithWindows { get; set; }
    /// <summary>The app version for the footer menu header.</summary>
    string Version { get; }
    void CopyText(string text);
    // Named SnoozeItem, not Snooze: that would shadow the Snooze record inside AppController.
    void SnoozeItem(string key, SnoozeChoice choice);
    void Unsnooze(string key);
    void Mute(string key, MuteScope scope, MuteDuration duration);
    IReadOnlyList<ActiveMute> ActiveMutes();
    void Unmute(string pattern);
    /// <summary>Expands or collapses a collapsible section ("waiting", "snoozed").</summary>
    void ToggleSection(string id);
    UpdateStatus Update { get; }
    /// <summary>Runs, or queued, agents: what a restart would interrupt.</summary>
    int RunningAgents { get; }
    /// <summary>Applies the downloaded update and shuts down, unless agents are running and <paramref name="confirmed"/> is false.</summary>
    RestartDecision RestartToUpdate(bool confirmed);
    Task CheckForUpdatesAsync();
}

/// <summary>A mute shown in the popup's active-mutes list. <paramref name="Remaining"/> is display text ("forever", "45m left").</summary>
public sealed record ActiveMute(string Pattern, string Remaining, bool Permanent);
