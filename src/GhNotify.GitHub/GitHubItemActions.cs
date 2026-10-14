using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.GitHub;

public sealed class GitHubItemActions(GitHubClient client) : IItemActions
{
    sealed record DeployToken(string Repo, long RunId, ItemAction Action, long[] EnvIds);
    sealed record PrToken(string Repo, int Number, string Sha);
    sealed record MergeToken(string Repo, int Number, string Sha, MergeMethod[] Allowed, string HeadRef, bool CanDeleteBranch);
    sealed record RerunToken(string Repo, int Number, string Sha, (long Id, string Name)[] Runs);

    const string Mismatch = "This action no longer matches what was shown — start again";
    const string StaleChanged = "This changed since you looked — refreshing";
    const string StaleHead = "New commits since you looked — refresh.";

    public IReadOnlyList<ItemAction> ActionsFor(InboxItem item) =>
        item.Kind switch
        {
            ItemKind.ApprovalPending when item.RunId is null => [],
            ItemKind.ReviewRequested when item.Number is null => [],
            ItemKind.MyPrReadyToMerge or ItemKind.MyPrCiFailing when item.Number is null || string.IsNullOrEmpty(item.HeadSha) => [],
            _ => ActionText.DefaultActions(item),
        };

    public async Task<ActionPreview> PreviewAsync(InboxItem item, ItemAction action, AgentRun? run, CancellationToken ct)
    {
        try
        {
            if (action is ItemAction.Merge or ItemAction.RerunFailed && !ActionsFor(item).Contains(action))
                return ActionText.Refuse("Not supported for this item");
            return action switch
            {
                ItemAction.ApprovePr => await PreviewPrAsync(item, run, ct),
                ItemAction.Merge => await PreviewMergeAsync(item, ct),
                ItemAction.RerunFailed => await PreviewRerunAsync(item, ct),
                _ => await PreviewDeploymentAsync(item, action, ct),
            };
        }
        catch (HttpRequestException ex) { var m = ActionText.FromHttpError(ex); return ActionText.Refuse(m.Error ?? ex.Message); }
        catch (RateLimitedException ex) { return ActionText.Refuse(ex.Message); }
        catch (SourceAuthException ex) { return ActionText.Refuse(ex.Message); }
    }

    async Task<List<(long Id, string Name)>> ApprovableAsync(string repo, long runId, CancellationToken ct)
    {
        var pending = await client.GetAsync($"repos/{repo}/actions/runs/{runId}/pending_deployments", ct);
        return pending.ValueKind == JsonValueKind.Array
            ? pending.EnumerateArray().Where(e => e.Bool("current_user_can_approve"))
                .Select(e => (Id: e.Obj("environment").Long("id"), Name: e.Obj("environment").Str("name")))
                .Where(e => e.Id is not null && e.Name is not null).Select(e => (e.Id!.Value, e.Name!)).ToList()
            : [];
    }

    async Task<ActionPreview> PreviewDeploymentAsync(InboxItem item, ItemAction action, CancellationToken ct)
    {
        if (item.RunId is not { } runId) return ActionText.Refuse("No longer waiting on you");
        var envs = await ApprovableAsync(item.Repo, runId, ct);
        if (envs.Count == 0) return ActionText.Refuse("No longer waiting on you");
        var verb = action == ItemAction.Approve ? "Approve" : "Reject";
        var names = string.Join(", ", envs.Select(e => e.Name));
        return new ActionPreview(true, $"{verb} deployment of {item.Repo} run {runId} to {names}", $"{verb} {names}",
            null, null, new DeployToken(item.Repo, runId, action, envs.Select(e => e.Id).ToArray()));
    }

    async Task<ActionPreview> PreviewPrAsync(InboxItem item, AgentRun? run, CancellationToken ct)
    {
        if (item.Number is not { } n) return ActionText.Refuse("No longer waiting on you");
        var pr = await client.GetAsync($"repos/{item.Repo}/pulls/{n}", ct);
        if (pr.Str("state") != "open") return ActionText.Refuse("This PR is no longer open");
        var sha = pr.Obj("head").Str("sha");
        if (sha is null || sha != item.HeadSha) return ActionText.Refuse(StaleHead);
        return new ActionPreview(true, $"Approve PR #{n} in {item.Repo} at {sha[..Math.Min(7, sha.Length)]}", $"Approve PR #{n}",
            ActionText.PrWarning(item, run), null, new PrToken(item.Repo, n, sha));
    }

    static string Sha7(string sha) => sha[..Math.Min(7, sha.Length)];

    static string MergeVerb(MergeMethod m) => m switch
    {
        MergeMethod.Squash => "Squash and merge",
        MergeMethod.Rebase => "Rebase and merge",
        _ => "Merge",
    };

    async Task<ActionPreview> PreviewMergeAsync(InboxItem item, CancellationToken ct)
    {
        var n = item.Number!.Value;
        var pr = await client.GetAsync($"repos/{item.Repo}/pulls/{n}", ct);
        if (pr.Str("state") != "open") return ActionText.Refuse("This PR is no longer open");
        var sha = pr.Obj("head").Str("sha");
        if (sha is null || sha != item.HeadSha) return ActionText.Refuse(StaleHead);
        if (!pr.TryGetProperty("mergeable", out var mergeable) || mergeable.ValueKind is JsonValueKind.Null)
            return ActionText.Refuse("GitHub is still checking mergeability — try again in a moment");
        var state = pr.Str("mergeable_state");
        if (state is not ("clean" or "has_hooks"))
            return ActionText.Refuse(state switch
            {
                "blocked" => "Blocked by branch protection",
                "behind" => "Behind the base branch",
                "dirty" => "Merge conflicts",
                "unstable" => "Some checks are not passing",
                "draft" => "Draft PR",
                _ => "Not mergeable right now",
            });

        var repo = await client.GetAsync($"repos/{item.Repo}", ct);
        var allowed = new List<MergeMethod>();
        if (repo.Bool("allow_merge_commit")) allowed.Add(MergeMethod.Merge);
        if (repo.Bool("allow_squash_merge")) allowed.Add(MergeMethod.Squash);
        if (repo.Bool("allow_rebase_merge")) allowed.Add(MergeMethod.Rebase);
        if (allowed.Count == 0) return ActionText.Refuse("No merge method is allowed on this repo");

        var headFull = pr.Obj("head").Obj("repo").Str("full_name");
        var baseFull = pr.Obj("base").Obj("repo").Str("full_name");
        var canDelete = headFull is not null && string.Equals(headFull, baseFull, StringComparison.OrdinalIgnoreCase);
        var deleteDefault = canDelete && !repo.Bool("delete_branch_on_merge");
        var headRef = pr.Obj("head").Str("ref") ?? "";
        var selected = MergeDefaults.Pick(allowed, null);
        var target = pr.Obj("base").Str("ref") ?? "the base branch";
        return new ActionPreview(true, $"{MergeVerb(selected)} PR #{n} into {target} in {item.Repo} at {Sha7(sha)}", $"Merge PR #{n}",
            null, null, new MergeToken(item.Repo, n, sha, [.. allowed], headRef, canDelete))
        { Options = new MergeOptions(allowed, selected, canDelete, deleteDefault) };
    }

    async Task<ActionPreview> PreviewRerunAsync(InboxItem item, CancellationToken ct)
    {
        var n = item.Number!.Value;
        var pr = await client.GetAsync($"repos/{item.Repo}/pulls/{n}", ct);
        if (pr.Str("state") != "open") return ActionText.Refuse("This PR is no longer open");
        var sha = pr.Obj("head").Str("sha");
        if (sha is null || sha != item.HeadSha) return ActionText.Refuse(StaleHead);

        var runs = await client.GetAsync($"repos/{item.Repo}/actions/runs?head_sha={Uri.EscapeDataString(sha)}&per_page=50", ct);
        var failed = runs.Arr("workflow_runs")
            .Where(r => r.Long("id") is not null && r.Long("workflow_id") is not null)
            .GroupBy(r => r.Long("workflow_id"))
            .Select(g => g.OrderByDescending(r => r.Long("run_number") ?? 0).First())
            .Where(r => r.Str("conclusion") is "failure" or "timed_out" or "cancelled")
            .Select(r => (Id: r.Long("id")!.Value, Name: r.Str("name") ?? $"run {r.Long("id")}"))
            .ToArray();
        if (failed.Length == 0)
            return ActionText.Refuse("No failed GitHub Actions runs at this commit — re-run them in the CI system that reported them");
        var names = string.Join(", ", failed.Select(f => f.Name));
        var noun = failed.Length == 1 ? "workflow" : "workflows";
        return new ActionPreview(true, $"Re-run failed jobs in {failed.Length} {noun} on #{n} ({item.Repo}): {names}",
            $"Re-run {failed.Length} {noun}", null, null, new RerunToken(item.Repo, n, sha, failed));
    }

    public async Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, ActionInput input, CancellationToken ct)
    {
        var comment = input.Comment;
        var result = new ActionResult(false, "failed");
        try
        {
            result = await ExecuteCoreAsync(item, action, preview, input, ct);
        }
        catch (HttpRequestException ex) { result = ActionText.FromHttpError(ex); }
        catch (RateLimitedException ex) { result = new ActionResult(false, ex.Message); }
        catch (SourceAuthException ex) { result = new ActionResult(false, ex.Message); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result = new ActionResult(false, "The request timed out — it may have been sent. Refresh to check.", Stale: true);
        }
        catch (JsonException)
        {
            result = new ActionResult(false, "Unexpected response from GitHub — it may have been sent. Refresh to check.", Stale: true);
        }
        finally
        {
            Log.Info($"action {action} {item.Key}: {(result.Ok ? "ok" : "failed")} (comment {comment?.Length ?? 0} chars)");
        }
        return result;
    }

    async Task<ActionResult> ExecuteCoreAsync(InboxItem item, ItemAction action, ActionPreview preview, ActionInput input, CancellationToken ct)
    {
        var comment = input.Comment;
        var mismatch = new ActionResult(false, Mismatch);
        if (!preview.Allowed) return mismatch;

        if (action == ItemAction.Merge) return await ExecuteMergeAsync(item, preview, input, ct);
        if (action == ItemAction.RerunFailed) return await ExecuteRerunAsync(item, preview, ct);

        if (action == ItemAction.ApprovePr)
        {
            if (preview.Token is not PrToken t || t.Repo != item.Repo || item.Number != t.Number) return mismatch;
            var pr = await client.GetAsync($"repos/{t.Repo}/pulls/{t.Number}", ct);
            if (pr.Str("state") != "open" || pr.Obj("head").Str("sha") != t.Sha)
                return new ActionResult(false, StaleHead, Stale: true);
            var path = $"repos/{t.Repo}/pulls/{t.Number}/reviews";
            if (string.IsNullOrWhiteSpace(comment))
                await client.PostAsync(path, new { @event = "APPROVE", commit_id = t.Sha }, ct);
            else
                await client.PostAsync(path, new { @event = "APPROVE", commit_id = t.Sha, body = comment }, ct);
            return new ActionResult(true, null);
        }

        if (preview.Token is not DeployToken d || d.Action != action || d.Repo != item.Repo || item.RunId != d.RunId) return mismatch;
        var live = (await ApprovableAsync(d.Repo, d.RunId, ct)).Select(e => e.Id).ToHashSet();
        if (!d.EnvIds.All(live.Contains)) return new ActionResult(false, StaleChanged, Stale: true);
        await client.PostAsync($"repos/{d.Repo}/actions/runs/{d.RunId}/pending_deployments",
            new { environment_ids = d.EnvIds, state = action == ItemAction.Approve ? "approved" : "rejected", comment = comment ?? "" }, ct);
        return new ActionResult(true, null);
    }

    static string MethodName(MergeMethod m) => m switch { MergeMethod.Squash => "squash", MergeMethod.Rebase => "rebase", _ => "merge" };

    async Task<ActionResult> ExecuteMergeAsync(InboxItem item, ActionPreview preview, ActionInput input, CancellationToken ct)
    {
        if (preview.Token is not MergeToken t || t.Repo != item.Repo || item.Number != t.Number || item.HeadSha != t.Sha
            || input.Method is not { } method || !t.Allowed.Contains(method) || (preview.Options is { } o && !o.Allowed.Contains(method)))
            return new ActionResult(false, Mismatch);
        var pr = await client.GetAsync($"repos/{t.Repo}/pulls/{t.Number}", ct);
        if (pr.Str("state") != "open" || pr.Obj("head").Str("sha") != t.Sha)
            return new ActionResult(false, StaleHead, Stale: true);
        await client.PutAsync($"repos/{t.Repo}/pulls/{t.Number}/merge", new { sha = t.Sha, merge_method = MethodName(method) }, ct);
        if (!input.DeleteBranch || !t.CanDeleteBranch || t.HeadRef.Length == 0) return new ActionResult(true, null);
        try
        {
            var refPath = string.Join('/', t.HeadRef.Split('/').Select(Uri.EscapeDataString));
            await client.DeleteAsync($"repos/{t.Repo}/git/refs/heads/{refPath}", ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new ActionResult(true, $"Merged, but the branch couldn't be deleted: {ex.Message}");
        }
        return new ActionResult(true, null);
    }

    async Task<ActionResult> ExecuteRerunAsync(InboxItem item, ActionPreview preview, CancellationToken ct)
    {
        if (preview.Token is not RerunToken t || t.Repo != item.Repo || item.Number != t.Number || item.HeadSha != t.Sha)
            return new ActionResult(false, Mismatch);
        var pr = await client.GetAsync($"repos/{t.Repo}/pulls/{t.Number}", ct);
        if (pr.Str("state") != "open" || pr.Obj("head").Str("sha") != t.Sha)
            return new ActionResult(false, StaleHead, Stale: true);
        var failed = new List<string>();
        HttpRequestException? first = null;
        foreach (var (id, name) in t.Runs)
        {
            try
            {
                await client.PostAsync($"repos/{t.Repo}/actions/runs/{id}/rerun-failed-jobs", new { }, ct);
            }
            catch (HttpRequestException ex)
            {
                first ??= ex;
                failed.Add(name);
            }
        }
        if (failed.Count == 0) return new ActionResult(true, null);
        if (failed.Count == t.Runs.Length) return ActionText.FromHttpError(first!);
        return new ActionResult(true, $"Re-run started, but couldn't re-run: {string.Join(", ", failed)}");
    }
}
