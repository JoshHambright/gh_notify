namespace GhNotify.Core;

public enum PipelineOutcome { Failed, Succeeded, PartiallySucceeded }

/// <summary>One stage (ADO) or job (GitHub) of a run. <paramref name="Outcome"/> is null until it has a final alertable result (running, skipped, cancelled).</summary>
public sealed record PipelineStageInfo(string Name, PipelineOutcome? Outcome, DateTimeOffset? Finished);

/// <summary>A pipeline run, normalized across providers by an <see cref="IPipelineRunFeed"/>.</summary>
/// <param name="RequestedByMe">True/false when the provider knows who triggered the run; null when the identity is unknown.</param>
/// <param name="Outcome">The whole run's result; null while it is in progress or when cancelled.</param>
public sealed record PipelineRunInfo(
    Provider Provider, string Scope, long PipelineId, string Pipeline, string RunId, string RunNumber, int Attempt,
    string? Branch, string? RequestedBy, bool? RequestedByMe, string Url, string? CommitSha,
    DateTimeOffset? Finished, PipelineOutcome? Outcome, IReadOnlyList<PipelineStageInfo> Stages);

/// <summary>A run (<paramref name="Stage"/> null) or one of its stages that a subscription wants to hear about.</summary>
public sealed record PipelineHit(string? Stage, PipelineOutcome Outcome, DateTimeOffset Finished);
