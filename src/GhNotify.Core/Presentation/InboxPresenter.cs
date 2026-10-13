using System.Globalization;

namespace GhNotify.Core;

public sealed record InboxRow(
    string Key, string ProviderGlyph, string Repo, string Title, string Url, string Age, bool Stale, string? Detail,
    string? BadgeText, string? BadgeTooltip, string? RunButton, bool CanRun, string? RunDisabledReason, bool CanOpenInClaude,
    bool Urgent, IReadOnlyList<ItemDetail> Details, string? RunInfo, string? ResultText,
    IReadOnlyList<RowActionButton> Actions, RowMode Mode, string? Statement, string? ConfirmLabel, bool ConfirmEnabled,
    string? Warning, string? Message, string RepoDisplay);

public enum RowMode { Collapsed, Expanded, Confirming, Sending, Done, Failed }

/// <summary>UI-only state for a row, owned by the app.</summary>
public sealed record RowUiState(RowMode Mode, ItemAction? Action = null, ActionPreview? Preview = null, string? Message = null, DateTimeOffset? ConfirmOpenedAt = null);

public sealed record RowActionButton(ItemAction Action, string Label, bool Enabled, string? DisabledReason);

public sealed record InboxSection(string Title, IReadOnlyList<InboxRow> Rows);

public static class InboxPresenter
{
    static readonly (ItemKind Kind, string Title)[] Order =
    [
        (ItemKind.ApprovalPending, "Approvals"),
        (ItemKind.ReviewRequested, "Review requests"),
        (ItemKind.MyPrCiFailing, "My PRs — failing"),
        (ItemKind.MyPrNeedsResponse, "My PRs — needs response"),
    ];

    public static IReadOnlyList<InboxSection> Build(
        IReadOnlyList<InboxEntry> entries,
        Func<string, AgentRun?> runOf,
        Func<InboxItem, AgentDecision> decide,
        Func<InboxItem, AgentDecision, string?> whyCannotRun,
        DateTimeOffset now,
        Func<InboxItem, IReadOnlyList<ItemAction>>? actionsFor = null,
        Func<string, RowUiState?>? uiState = null,
        Func<string, string>? repoDisplay = null)
    {
        var sections = new List<InboxSection>();
        foreach (var (kind, title) in Order)
        {
            var rows = entries.Where(e => e.Item.Kind == kind)
                .OrderByDescending(e => e.Item.UpdatedAt)
                .Select(e => Row(e, runOf(e.Item.Key), decide(e.Item), whyCannotRun, now, actionsFor, uiState?.Invoke(e.Item.Key), repoDisplay))
                .ToList();
            if (rows.Count > 0) sections.Add(new InboxSection(title, rows));
        }
        return sections;
    }

    static InboxRow Row(InboxEntry e, AgentRun? run, AgentDecision decision, Func<InboxItem, AgentDecision, string?> why, DateTimeOffset now,
        Func<InboxItem, IReadOnlyList<ItemAction>>? actionsFor, RowUiState? ui, Func<string, string>? repoDisplay)
    {
        var i = e.Item;
        var busy = run is { State: AgentState.Queued or AgentState.Running };
        string? button = decision.Mode == AgentMode.None || busy ? null : run is null ? LabelFor(decision.ProfileName) : "Re-run";
        var reason = button is null ? null : why(i, decision);
        var mode = ui?.Mode ?? RowMode.Collapsed;
        var preview = mode is RowMode.Confirming or RowMode.Sending or RowMode.Done or RowMode.Failed ? ui?.Preview : null;
        var sending = mode == RowMode.Sending;
        var actions = (actionsFor?.Invoke(i) ?? []).Select(a => new RowActionButton(a, ActionText.ButtonLabel(a),
            !e.Stale && !sending,
            sending ? "Working…" : e.Stale ? "Source is unavailable — can't act right now" : null)).ToList();
        var confirmEnabled = mode == RowMode.Confirming && preview is { Allowed: true }
            && ui!.ConfirmOpenedAt is { } opened && now - opened >= TimeSpan.FromMilliseconds(600);
        var message = preview is { Allowed: false } ? preview.RefusalReason : ui?.Message;
        return new InboxRow(
            i.Key,
            i.Provider == Provider.GitHub ? "GH" : "ADO",
            i.Repo, i.Title, i.Url, Age(now - i.UpdatedAt), e.Stale, i.Detail,
            Badge(run), Tooltip(run),
            button, button is not null && reason is null, reason,
            run is { State: AgentState.Ready or AgentState.Failed or AgentState.Outdated, SessionId: not null, WorktreePath: not null },
            i.Kind is ItemKind.ApprovalPending or ItemKind.MyPrCiFailing, i.Details ?? [],
            run is null ? null : RunInfo(run), run?.ResultText,
            actions, mode, preview?.Statement, preview?.ConfirmLabel, confirmEnabled, preview?.Warning, message,
            repoDisplay?.Invoke(i.Repo) ?? i.Repo);
    }

    static string RunInfo(AgentRun run)
    {
        var parts = new List<string> { LabelFor(run.Profile), run.State.ToString().ToLowerInvariant() };
        if (run.CostUsd is { } c) parts.Add("$" + c.ToString("0.00", CultureInfo.InvariantCulture));
        if (run.DurationSeconds is { } d) parts.Add(Duration(d));
        return string.Join(" · ", parts);
    }

    static string Duration(int s) => s < 60 ? $"{s}s" : $"{s / 60}m {s % 60:00}s";

    public static string LabelFor(string? profileName) => profileName switch
    {
        "peer-review" => "Pre-review",
        "pr-shepherd" => "Shepherd",
        _ => $"Run {profileName}",
    };

    public static string Age(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 1 } => "now",
        { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes}m",
        { TotalHours: < 48 } => $"{(int)elapsed.TotalHours}h",
        _ => $"{(int)elapsed.TotalDays}d",
    };

    static string? Badge(AgentRun? run) => run?.State switch
    {
        AgentState.Queued => "Queued",
        AgentState.Running => "Running…",
        AgentState.Ready => "✓ Ready",
        AgentState.Failed => "✗ Failed",
        AgentState.Outdated => "Outdated",
        _ => null,
    };

    static string? Tooltip(AgentRun? run)
    {
        if (run is null) return null;
        var text = run.Summary ?? run.Error;
        if (run.CostUsd is { } cost) text = $"{text} (${cost.ToString("0.00", CultureInfo.InvariantCulture)})";
        return text;
    }
}
