using System.Globalization;

namespace GhNotify.Core;

public sealed record InboxRow(
    string Key, string ProviderGlyph, string Repo, string Title, string Url, string Age, bool Stale, string? Detail,
    string? BadgeText, string? BadgeTooltip, string? RunButton, bool CanRun, string? RunDisabledReason, bool CanOpenInClaude,
    bool Urgent, IReadOnlyList<ItemDetail> Details, string? RunInfo, string? ResultText,
    IReadOnlyList<RowActionButton> Actions, RowMode Mode, string? Statement, string? ConfirmLabel, bool ConfirmEnabled,
    string? Warning, string? Message, string RepoDisplay)
{
    public string? MutedBy { get; init; }
    public string? SnoozeText { get; init; }
    public string? Number { get; init; }
    public string? Author { get; init; }
}

public enum RowMode { Collapsed, Expanded, Confirming, Sending, Done, Failed }

/// <summary>UI-only state for a row, owned by the app.</summary>
public sealed record RowUiState(RowMode Mode, ItemAction? Action = null, ActionPreview? Preview = null, string? Message = null, DateTimeOffset? ConfirmOpenedAt = null);

public sealed record RowActionButton(ItemAction Action, string Label, bool Enabled, string? DisabledReason);

public sealed record InboxSection(string Title, IReadOnlyList<InboxRow> Rows)
{
    public string Id { get; init; } = Title;
    public bool Collapsible { get; init; }
    public bool Expanded { get; init; } = true;
    public int Count { get; init; } = Rows.Count;
}

public static class InboxPresenter
{
    static readonly (ItemKind Kind, string Title)[] Order =
    [
        (ItemKind.ApprovalPending, "Approvals"),
        (ItemKind.ReviewRequested, "Review requests"),
        (ItemKind.MyPrCiFailing, "My PRs — failing"),
        (ItemKind.MyPrNeedsResponse, "My PRs — needs response"),
        (ItemKind.MyPrReadyToMerge, "My PRs — ready to merge"),
        (ItemKind.MyPrWaiting, "Waiting on others"),
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
            if (rows.Count > 0) sections.Add(new InboxSection(title, rows)
            {
                Id = kind == ItemKind.MyPrWaiting ? "waiting" : kind.ToString(),
                Collapsible = kind == ItemKind.MyPrWaiting,
            });
        }
        return sections;
    }

    public static IReadOnlyList<InboxSection> Build(
        InboxView view,
        Func<string, AgentRun?> runOf,
        Func<InboxItem, AgentDecision> decide,
        Func<InboxItem, AgentDecision, string?> whyCannotRun,
        DateTimeOffset now,
        Func<InboxItem, IReadOnlyList<ItemAction>>? actionsFor,
        Func<string, RowUiState?>? uiState,
        Func<string, string>? repoDisplay,
        bool waitingExpanded,
        bool snoozedExpanded)
    {
        InboxRow RowOf(InboxEntry e) =>
            Row(e, runOf(e.Item.Key), decide(e.Item), whyCannotRun, now, actionsFor, uiState?.Invoke(e.Item.Key), repoDisplay);

        var sections = new List<InboxSection>();
        foreach (var (kind, title) in Order)
        {
            if (kind == ItemKind.MyPrWaiting) continue;
            var rows = view.Active.Where(v => v.Entry.Item.Kind == kind)
                .OrderByDescending(v => v.Entry.Item.UpdatedAt)
                .Select(v => RowOf(v.Entry) with { MutedBy = v.MutedBy })
                .ToList();
            if (rows.Count > 0) sections.Add(new InboxSection(title, rows) { Id = kind.ToString() });
        }
        if (view.Waiting.Count > 0)
        {
            var rows = waitingExpanded
                ? view.Waiting.OrderByDescending(v => v.Entry.Item.UpdatedAt).Select(v => RowOf(v.Entry) with { MutedBy = v.MutedBy }).ToList()
                : [];
            sections.Add(new InboxSection("Waiting on others", rows)
            { Id = "waiting", Collapsible = true, Expanded = waitingExpanded, Count = view.Waiting.Count });
        }
        if (view.Snoozed.Count > 0)
        {
            var rows = snoozedExpanded
                ? view.Snoozed.OrderByDescending(s => s.Entry.Item.UpdatedAt)
                    .Select(s => RowOf(s.Entry) with { SnoozeText = Snoozes.Remaining(s.Snooze, now), Actions = [], RunButton = null, CanRun = false })
                    .ToList()
                : [];
            sections.Add(new InboxSection("Snoozed", rows)
            { Id = "snoozed", Collapsible = true, Expanded = snoozedExpanded, Count = view.Snoozed.Count });
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
        // Sending, or Done while the provider catches up: the item may still be listed but must not be acted on again.
        var disabledReason = mode switch
        {
            RowMode.Sending => "Working…",
            RowMode.Done => "Done — refreshing",
            _ => e.Stale ? "Source is unavailable — can't act right now" : null,
        };
        var actions = (actionsFor?.Invoke(i) ?? []).Select(a => new RowActionButton(a, ActionText.ButtonLabel(a),
            disabledReason is null, disabledReason)).ToList();
        var confirmEnabled = mode == RowMode.Confirming && !e.Stale && preview is { Allowed: true }
            && ui!.ConfirmOpenedAt is { } opened && now - opened >= ActionText.ConfirmDelay;
        var message = preview is { Allowed: false } ? preview.RefusalReason : ui?.Message;
        return new InboxRow(
            i.Key,
            i.Provider == Provider.GitHub ? "GH" : "ADO",
            i.Repo, i.Title, i.Url, Age(now - i.UpdatedAt), e.Stale, i.Detail,
            Badge(run), Tooltip(run),
            button, button is not null && reason is null, reason,
            run is { State: AgentState.Ready or AgentState.Failed or AgentState.Outdated, SessionId: not null, WorktreePath: not null },
            i.Kind is ItemKind.ApprovalPending or ItemKind.MyPrCiFailing, WithPr(i),
            run is null ? null : RunInfo(run), run?.ResultText,
            actions, mode, preview?.Statement, preview?.ConfirmLabel, confirmEnabled, preview?.Warning, message,
            repoDisplay?.Invoke(i.Repo) ?? i.Repo)
        { Number = i.Number is { } n ? $"#{n}" : null, Author = i.Author };
    }

    static IReadOnlyList<ItemDetail> WithPr(InboxItem i)
    {
        var details = i.Details ?? [];
        if (i.Number is not { } n || details.Any(d => d.Label == "PR")) return details;
        var at = details.ToList().FindIndex(d => d.Label == "Repo") + 1;
        return [.. details.Take(at), new ItemDetail("PR", $"#{n}"), .. details.Skip(at)];
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
        "review" or "peer-review" => "Pre-review",
        "diagnose-ci" or "pr-shepherd" => "Diagnose",
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
