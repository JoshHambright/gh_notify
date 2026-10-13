using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.GitHub;

/// <param name="lastKnown">The reconciler's last-known items for the GitHub source (e.g. restored from state). Used when a repo
/// or the notifications call fails before it has ever succeeded in this process, so a restart does not drop approvals.</param>
public sealed class GitHubApprovals(GitHubClient client, TimeProvider? time = null, Func<IReadOnlyList<InboxItem>>? lastKnown = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;
    // Last successful results, so one failing repo or notifications call does not drop approvals every poll.
    readonly ConcurrentDictionary<string, IReadOnlyList<InboxItem>> _lastByRepo = new();
    IReadOnlyCollection<string> _lastNotificationRepos = [];
    bool _notificationsSeeded;

    public async Task<IReadOnlyList<InboxItem>> FetchAsync(IEnumerable<string> extraRepos, CancellationToken ct)
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in await NotificationReposAsync(ct)) repos.Add(r);
        foreach (var r in extraRepos) repos.Add(r);

        var items = new ConcurrentBag<InboxItem>();
        await Parallel.ForEachAsync(repos, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (repo, ct2) =>
        {
            foreach (var item in await ScanRepoAsync(repo.ToLowerInvariant(), ct2)) items.Add(item);
        });
        return items.ToList();
    }

    async Task<IReadOnlyCollection<string>> NotificationReposAsync(CancellationToken ct)
    {
        try
        {
            var since = _time.GetUtcNow().AddDays(-7).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var notes = await client.GetAsync($"notifications?all=true&per_page=50&since={since}", ct);
            var found = new List<string>();
            foreach (var n in notes.EnumerateArray())
                if (n.Str("reason") == "approval_requested" && n.Obj("repository").Str("full_name") is { } name)
                    found.Add(name);
            _lastNotificationRepos = found;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Log.Error("approval notifications skipped this poll; using last known repos", ex);
            if (!_notificationsSeeded && _lastNotificationRepos.Count == 0)
                _lastNotificationRepos = LastKnownApprovals().Select(i => i.Repo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        _notificationsSeeded = true;
        return _lastNotificationRepos;
    }

    async Task<IReadOnlyList<InboxItem>> ScanRepoAsync(string repo, CancellationToken ct)
    {
        try
        {
            var items = await RepoAsync(repo, ct);
            _lastByRepo[repo] = items;
            return items;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Log.Error($"approvals for {repo} skipped this poll", ex);
            if (_lastByRepo.TryGetValue(repo, out var last)) return last;
            return LastKnownApprovals().Where(i => string.Equals(i.Repo, repo, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    IEnumerable<InboxItem> LastKnownApprovals()
    {
        if (lastKnown is null) return [];
        try { return lastKnown().Where(i => i.Kind == ItemKind.ApprovalPending).ToList(); }
        catch (Exception ex)
        {
            Log.Error("could not read last-known approvals", ex);
            return [];
        }
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
                RunId = id,
                Details = DetailList.Of(("Workflow", run.Str("name")), ("Environments", string.Join(", ", envs)),
                    ("Branch", run.Str("head_branch")), ("Commit", DetailList.Short(run.Str("head_sha"))), ("Triggered by", run.Obj("actor").Str("login"))),
            });
        }
        return items;
    }
}
