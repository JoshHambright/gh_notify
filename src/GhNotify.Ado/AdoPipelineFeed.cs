using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Ado;

/// <summary>Reads builds (and, for stage subscriptions, build timelines) of a subscribed ADO pipeline.</summary>
public sealed class AdoPipelineFeed(AdoClient client, AdoDirectory directory) : IPipelineRunFeed
{
    const int PageSize = 50;

    // Timelines of completed builds never change, so each is read once. Running builds are re-read every poll.
    readonly ConcurrentDictionary<(string Org, string Project, long Build, DateTimeOffset? Finished), IReadOnlyList<PipelineStageInfo>> _completedTimelines = new();

    public async Task<IReadOnlyList<PipelineRunInfo>> RecentRunsAsync(PipelineSubscription sub, DateTimeOffset since, bool includeInProgress, CancellationToken ct)
    {
        var org = sub.Org ?? "";
        var baseUrl = ProjectUrl(org, sub.Project ?? "");
        // finishTime ordering would drop running builds (no finish time yet), so a stage subscription is ordered by queue time instead.
        var query = includeInProgress ? "queryOrder=queueTimeDescending" : "queryOrder=finishTimeDescending&statusFilter=completed";
        var minTime = Uri.EscapeDataString(since.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        var json = await client.GetAsync(
            $"{baseUrl}/_apis/build/builds?definitions={sub.PipelineId}&{query}&minTime={minTime}&%24top={PageSize}&api-version=7.1", ct);

        var mine = sub.OnlyMine ? (await directory.GetOrgContextAsync(org, ct)).MyIdentityIds : null;
        var runs = new List<PipelineRunInfo>();
        foreach (var b in json.Arr("value"))
        {
            if (b.Long("id") is not { } id) continue;
            var completed = b.Str("status") == "completed";
            var outcome = completed ? Outcome(b.Str("result")) : null;
            var branch = b.Str("sourceBranch") is { } sb ? DetailList.StripHeads(sb) : null;
            var requester = b.Obj("requestedFor");
            bool? byMe = mine is null ? null : requester.Str("id") is { } rid && mine.Contains(rid);

            IReadOnlyList<PipelineStageInfo> stages = [];
            var wanted = (sub.Branches.Count == 0 || (branch is not null && sub.Branches.Any(g => ItemMatcher.Glob(g, branch)))) && byMe != false;
            if (sub.Stages.Count > 0 && wanted && b.Str("status") is "completed" or "inProgress")
                stages = await TimelineAsync(org, sub.Project ?? "", baseUrl, id, completed, b.Date("finishTime"), ct);

            runs.Add(new PipelineRunInfo(Provider.Ado, $"{org}/{sub.Project}", sub.PipelineId, b.Obj("definition").Str("name") ?? sub.Pipeline,
                id.ToString(CultureInfo.InvariantCulture), b.Str("buildNumber") ?? id.ToString(CultureInfo.InvariantCulture), RunAttempt(completed, b.Date("finishTime")), branch,
                requester.Str("displayName"), byMe,
                b.Obj("_links").Obj("web").Str("href") ?? $"{baseUrl}/_build/results?buildId={id}",
                b.Str("sourceVersion"), outcome is null ? null : b.Date("finishTime"), outcome, stages));
        }
        return runs;
    }

    async Task<IReadOnlyList<PipelineStageInfo>> TimelineAsync(string org, string project, string baseUrl, long build, bool completed, DateTimeOffset? finished, CancellationToken ct)
    {
        // Keyed by finish time too: a retried stage completes the same build again, and its timeline must be read afresh.
        if (completed && _completedTimelines.TryGetValue((org, project, build, finished), out var cached)) return cached;
        var json = await client.GetAsync($"{baseUrl}/_apis/build/builds/{build}/timeline?api-version=7.1", ct);
        var stages = json.Arr("records")
            .Where(r => r.Str("type") == "Stage" && r.Str("name") is not null)
            .Select(r =>
            {
                var outcome = r.Str("state") == "completed" ? StageOutcome(r.Str("result")) : null;
                return new PipelineStageInfo(r.Str("name")!, outcome, outcome is null ? null : r.Date("finishTime"), r.Int("attempt") ?? 0);
            })
            .ToList();
        if (completed)
        {
            if (_completedTimelines.Count >= MaxCachedTimelines) _completedTimelines.Clear(); // bounded: a long-running tray app must not keep every run it ever saw
            _completedTimelines[(org, project, build, finished)] = stages;
        }
        return stages;
    }

    public async Task<IReadOnlyList<string>> ListProjectsAsync(string org, CancellationToken ct) =>
        (await directory.GetProjectsAsync(org, ct)).Select(p => p.Name).ToList();

    public async Task<IReadOnlyList<PipelinePick>> ListPipelinesAsync(string org, string project, CancellationToken ct)
    {
        var json = await client.GetAsync($"{ProjectUrl(org, project)}/_apis/build/definitions?%24top=500&api-version=7.1", ct);
        return json.Arr("value")
            .Select(d => d.Long("id") is { } id && d.Str("name") is { } name ? new PipelinePick(id, name) : null)
            .OfType<PipelinePick>().ToList();
    }

    const int MaxCachedTimelines = 500;

    static readonly DateTimeOffset AttemptEpoch = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>ADO builds have no run attempt, and a stage retry completes the same build again, so a completed run's attempt is the second it finished.</summary>
    static int RunAttempt(bool completed, DateTimeOffset? finished) =>
        completed && finished is { } f ? Math.Max(1, (int)Math.Min(int.MaxValue, (f - AttemptEpoch).TotalSeconds)) : 1;

    static string ProjectUrl(string org, string project) => $"https://dev.azure.com/{org}/{Uri.EscapeDataString(project)}";

    static PipelineOutcome? Outcome(string? result) => result switch
    {
        "succeeded" => PipelineOutcome.Succeeded,
        "partiallySucceeded" => PipelineOutcome.PartiallySucceeded,
        "failed" => PipelineOutcome.Failed,
        _ => null, // canceled and anything unknown never alerts
    };

    static PipelineOutcome? StageOutcome(string? result) => result switch
    {
        "succeeded" => PipelineOutcome.Succeeded,
        "succeededWithIssues" => PipelineOutcome.PartiallySucceeded,
        "failed" => PipelineOutcome.Failed,
        _ => null, // canceled, abandoned, skipped
    };
}
