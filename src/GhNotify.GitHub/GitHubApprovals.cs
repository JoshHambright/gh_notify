using System.Collections.Concurrent;
using System.Net;
using GhNotify.Core;

namespace GhNotify.GitHub;

public sealed class GitHubApprovals(GitHubClient client, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<IReadOnlyList<InboxItem>> FetchAsync(IEnumerable<string> extraRepos, CancellationToken ct)
    {
        var since = _time.GetUtcNow().AddDays(-7).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var notes = await client.GetAsync($"notifications?all=true&per_page=50&since={since}", ct);
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in notes.EnumerateArray())
            if (n.Str("reason") == "approval_requested" && n.Obj("repository").Str("full_name") is { } name)
                repos.Add(name);
        foreach (var r in extraRepos) repos.Add(r);

        var items = new ConcurrentBag<InboxItem>();
        await Parallel.ForEachAsync(repos, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (repo, ct2) =>
        {
            foreach (var item in await RepoAsync(repo.ToLowerInvariant(), ct2)) items.Add(item);
        });
        return items.ToList();
    }

    async Task<IReadOnlyList<InboxItem>> RepoAsync(string repo, CancellationToken ct)
    {
        System.Text.Json.JsonElement runs;
        try
        {
            runs = await client.GetAsync($"repos/{repo}/actions/runs?status=waiting&per_page=20", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            Log.Info($"skipping approvals for {repo}: {(int?)ex.StatusCode}");
            return [];
        }
        var items = new List<InboxItem>();
        foreach (var run in runs.Arr("workflow_runs"))
        {
            var id = run.Long("id");
            if (id is null) continue;
            var pending = await client.GetAsync($"repos/{repo}/actions/runs/{id}/pending_deployments", ct);
            var envs = pending.EnumerateArray()
                .Where(p => p.Bool("current_user_can_approve"))
                .Select(p => p.Obj("environment").Str("name") ?? "?")
                .ToList();
            if (envs.Count == 0) continue;
            items.Add(new InboxItem($"gh:approval:{repo}/runs/{id}", Provider.GitHub, ItemKind.ApprovalPending, repo,
                $"{run.Str("name")}: {run.Str("display_title")}", run.Str("html_url") ?? "", run.Obj("actor").Str("login") ?? "",
                run.Date("updated_at"), run.Str("head_sha"), null, $"Environment: {string.Join(", ", envs)}")
            {
                CloneUrl = $"https://github.com/{repo}",
            });
        }
        return items;
    }
}
