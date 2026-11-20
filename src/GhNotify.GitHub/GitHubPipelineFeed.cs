using System.Collections.Concurrent;
using System.Globalization;
using GhNotify.Core;

namespace GhNotify.GitHub;

/// <summary>
/// Reads runs (and, for stage subscriptions, jobs) of a subscribed GitHub Actions workflow. A GitHub "stage" is a job name:
/// the jobs API carries no environment (see docs/superpowers/notes/2026-11-20-pipeline-api-verification.md).
/// </summary>
public sealed class GitHubPipelineFeed(GitHubClient client) : IPipelineRunFeed
{
    const int PageSize = 50;
    const int MaxRunPages = 5; // 250 runs per poll
    const int JobPageSize = 100;
    const int MaxJobPages = 10;

    // Jobs of a completed run attempt never change, so each is read once. Running runs are re-read every poll.
    const int MaxCachedRuns = 500;

    readonly ConcurrentDictionary<(string Repo, long Run, int Attempt), IReadOnlyList<PipelineStageInfo>> _completedJobs = new();
    string? _viewer;

    public async Task<IReadOnlyList<PipelineRunInfo>> RecentRunsAsync(PipelineSubscription sub, DateTimeOffset since, bool includeInProgress, CancellationToken ct)
    {
        var repo = sub.Repo ?? "";
        // No `created` filter: it would hide an old run that was re-run today, or a long run that finishes today. The finish-time
        // baseline is applied by the matcher instead, so `since` is only the matcher's concern.
        var url = $"repos/{repo}/actions/workflows/{sub.PipelineId}/runs?per_page={PageSize}" + (includeInProgress ? "" : "&status=completed");
        var items = new List<System.Text.Json.JsonElement>();
        for (var page = 1; page <= MaxRunPages; page++)
        {
            var batch = (await client.GetAsync($"{url}&page={page}", ct)).Arr("workflow_runs").ToList();
            items.AddRange(batch);
            if (batch.Count < PageSize) break;
        }

        var viewer = sub.OnlyMine ? await ViewerAsync(ct) : null;
        var runs = new List<PipelineRunInfo>();
        foreach (var r in items)
        {
            if (r.Long("id") is not { } id) continue;
            var status = r.Str("status");
            var completed = status == "completed";
            var outcome = completed ? Outcome(r.Str("conclusion")) : null;
            var attempt = r.Int("run_attempt") ?? 1;
            var branch = r.Str("head_branch");
            var actor = r.Obj("triggering_actor").Str("login") ?? r.Obj("actor").Str("login");
            bool? byMe = !sub.OnlyMine || viewer is null || actor is null ? null : string.Equals(actor, viewer, StringComparison.OrdinalIgnoreCase);

            IReadOnlyList<PipelineStageInfo> stages = [];
            var wanted = (sub.Branches.Count == 0 || (branch is not null && sub.Branches.Any(g => ItemMatcher.Glob(g, branch)))) && byMe != false;
            if (sub.Stages.Count > 0 && wanted && status is "completed" or "in_progress" or "waiting")
                stages = await JobsAsync(repo, id, attempt, completed, ct);

            runs.Add(new PipelineRunInfo(Provider.GitHub, repo, sub.PipelineId, r.Str("name") ?? sub.Pipeline,
                id.ToString(CultureInfo.InvariantCulture), r.Int("run_number")?.ToString(CultureInfo.InvariantCulture) ?? id.ToString(CultureInfo.InvariantCulture),
                attempt, branch, actor, byMe, r.Str("html_url") ?? $"https://github.com/{repo}/actions/runs/{id}", r.Str("head_sha"),
                outcome is null ? null : r.Date("updated_at"), outcome, stages));
        }
        return runs;
    }

    async Task<string?> ViewerAsync(CancellationToken ct)
    {
        if (_viewer is not null) return _viewer;
        try { return _viewer = (await client.GetAsync("user", ct)).Str("login"); }
        catch (HttpRequestException) { return null; } // identity unknown: "only mine" then matches nothing (fails closed)
    }

    async Task<IReadOnlyList<PipelineStageInfo>> JobsAsync(string repo, long run, int attempt, bool completed, CancellationToken ct)
    {
        if (completed && _completedJobs.TryGetValue((repo, run, attempt), out var cached)) return cached;
        var jobs = new List<System.Text.Json.JsonElement>();
        for (var page = 1; page <= MaxJobPages; page++)
        {
            var batch = (await client.GetAsync($"repos/{repo}/actions/runs/{run}/jobs?filter=latest&per_page={JobPageSize}&page={page}", ct)).Arr("jobs").ToList();
            jobs.AddRange(batch);
            if (batch.Count < JobPageSize) break;
        }
        var stages = jobs
            .Where(j => j.Str("name") is not null)
            .Select(j =>
            {
                var outcome = j.Str("status") == "completed" ? Outcome(j.Str("conclusion")) : null;
                return new PipelineStageInfo(j.Str("name")!, outcome, outcome is null ? null : j.Date("completed_at"), j.Int("run_attempt") ?? 0);
            })
            .ToList();
        if (completed)
        {
            if (_completedJobs.Count >= MaxCachedRuns) _completedJobs.Clear(); // bounded: a long-running tray app must not keep every run it ever saw
            _completedJobs[(repo, run, attempt)] = stages;
        }
        return stages;
    }

    public async Task<IReadOnlyList<PipelinePick>> ListWorkflowsAsync(string repo, CancellationToken ct)
    {
        var json = await client.GetAsync($"repos/{repo}/actions/workflows?per_page=100", ct);
        return json.Arr("workflows")
            .Where(w => w.Str("state") == "active")
            .Select(w => w.Long("id") is { } id && w.Str("name") is { } name ? new PipelinePick(id, name) : null)
            .OfType<PipelinePick>().ToList();
    }

    static PipelineOutcome? Outcome(string? conclusion) => conclusion switch
    {
        "success" => PipelineOutcome.Succeeded,
        "failure" or "timed_out" => PipelineOutcome.Failed,
        _ => null, // cancelled, skipped, neutral, action_required...
    };
}
