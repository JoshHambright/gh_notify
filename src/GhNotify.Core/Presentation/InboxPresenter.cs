using System.Globalization;

namespace GhNotify.Core;

public sealed record InboxRow(
    string Key, string ProviderGlyph, string Repo, string Title, string Url, string Age, bool Stale, string? Detail,
    string? BadgeText, string? BadgeTooltip, string? RunButton, bool CanRun, string? RunDisabledReason, bool CanOpenInClaude);

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
        DateTimeOffset now)
    {
        var sections = new List<InboxSection>();
        foreach (var (kind, title) in Order)
        {
            var rows = entries.Where(e => e.Item.Kind == kind)
                .OrderByDescending(e => e.Item.UpdatedAt)
                .Select(e => Row(e, runOf(e.Item.Key), decide(e.Item), whyCannotRun, now))
                .ToList();
            if (rows.Count > 0) sections.Add(new InboxSection(title, rows));
        }
        return sections;
    }

    static InboxRow Row(InboxEntry e, AgentRun? run, AgentDecision decision, Func<InboxItem, AgentDecision, string?> why, DateTimeOffset now)
    {
        var i = e.Item;
        var busy = run is { State: AgentState.Queued or AgentState.Running };
        string? button = decision.Mode == AgentMode.None || busy ? null : run is null ? LabelFor(decision.ProfileName) : "Re-run";
        var reason = button is null ? null : why(i, decision);
        return new InboxRow(
            i.Key,
            i.Provider == Provider.GitHub ? "GH" : "ADO",
            i.Repo, i.Title, i.Url, Age(now - i.UpdatedAt), e.Stale, i.Detail,
            Badge(run), Tooltip(run),
            button, button is not null && reason is null, reason,
            run is { State: AgentState.Ready or AgentState.Failed or AgentState.Outdated, SessionId: not null, WorktreePath: not null });
    }

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
