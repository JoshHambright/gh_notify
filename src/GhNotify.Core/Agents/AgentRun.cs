namespace GhNotify.Core;

public enum AgentState { Queued, Running, Ready, Failed, Outdated }

public sealed record AgentRun
{
    public required string Key { get; init; }
    public required string Profile { get; init; }
    public AgentState State { get; init; }
    public string? SessionId { get; init; }
    public string? Summary { get; init; }
    public string? Error { get; init; }
    public decimal? CostUsd { get; init; }
    public string? HeadSha { get; init; }
    public string? ClonePath { get; init; }
    public string? WorktreePath { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public DateTimeOffset? LastOpenedAt { get; init; }
    public DateTimeOffset? ItemRemovedAt { get; init; }
    public string? ResultText { get; init; }
    public int? DurationSeconds { get; init; }
}
