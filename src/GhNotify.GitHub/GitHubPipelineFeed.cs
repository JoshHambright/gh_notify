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

    // Jobs of a completed run attempt never change, so each is read once. Running runs are re-read every poll.
    const int MaxCachedRuns = 500;

    readonly ConcurrentDictionary<(string Repo, long Run, int Attempt), IReadOnlyList<PipelineStageInfo>> _completedJobs = new();
    string? _viewer;

    public async Task<IReadOnlyList<PipelineRunInfo>> RecentRunsAsync(PipelineSubscription sub, DateTimeOffset since, bool includeInProgress, CancellationToken ct)
    {
        var repo = sub.Repo ?? "";
        var created = Uri.EscapeDataString(">=" + since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var url = $"repos/{repo}/actions/workflows/{sub.PipelineId}/runs?per_page={PageSize}&created={created}" + (includeInProgress ? "" : "&status=completed");
        var json = await client.GetAsync(url, ct);

        var viewer = sub.OnlyMine ? await ViewerAsync(ct) : null;
        var runs = new List<PipelineRunInfo>();
        foreach (var r in json.Arr("workflow_runs"))
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
        var json = await client.GetAsync($"repos/{repo}/actions/runs/{run}/jobs?filter=latest&per_page=100", ct);
        var stages = json.Arr("jobs")
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
