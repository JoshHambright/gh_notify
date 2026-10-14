using System.Text.Json;
using GhNotify.Core;

namespace GhNotify.Ado;

public sealed class AdoItemActions(AdoClient client, AdoDirectory directory) : IItemActions
{
    const string ApprovalApi = "api-version=7.1-preview.1";
    const string Mismatch = "This action no longer matches what was shown — start again";
    const string Changed = "This changed since you looked — refreshing";

    sealed record ApprovalToken(string Org, string Project, string ApprovalId, ItemAction Action);
    sealed record PrToken(string Org, string Project, string RepoId, int Number, string Sha);
    sealed record MergeToken(string Org, string Project, string RepoId, int Number, string Sha, MergeMethod[] Allowed);
    sealed record RerunToken(string Org, string Project, string RepoId, int Number, string Sha, (string Id, string Name)[] Evaluations);

    const string MergeStrategyPolicyTypeId = "fa4e907d-c16b-4a4c-9dfa-4916e5d171ab";
    const string EvaluationApi = "api-version=7.1-preview.1";

    public IReadOnlyList<ItemAction> ActionsFor(InboxItem item)
    {
        if (string.IsNullOrEmpty(item.Org) || string.IsNullOrEmpty(item.Project)) return [];
        var actions = ActionText.DefaultActions(item);
        return actions.Count switch
        {
            0 => [],
            _ when actions[0] == ItemAction.ApprovePr => item.RepoId is { Length: > 0 } && item.Number is not null ? actions : [],
            _ when actions[0] is ItemAction.Merge or ItemAction.RerunFailed =>
                item.RepoId is { Length: > 0 } && item.Number is not null && item.HeadSha is { Length: > 0 } ? actions : [],
            _ => item.ApprovalId is { Length: > 0 } ? actions : [],
        };
    }

    static string Base(string org, string project) =>
        $"https://dev.azure.com/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}";

    static string ApprovalUrl(ApprovalToken t) =>
        $"{Base(t.Org, t.Project)}/_apis/pipelines/approvals/{Uri.EscapeDataString(t.ApprovalId)}?$expand=steps&{ApprovalApi}";

    static string PrUrl(string org, string project, string repoId, int number) =>
        $"{Base(org, project)}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/pullRequests/{number}";

    public async Task<ActionPreview> PreviewAsync(InboxItem item, ItemAction action, AgentRun? run, CancellationToken ct)
    {
        if (!ActionsFor(item).Contains(action)) return ActionText.Refuse("Not supported for this item");
        try
        {
            return action switch
            {
                ItemAction.ApprovePr => await PreviewPrAsync(item, run, ct),
                ItemAction.Merge => await PreviewMergeAsync(item, ct),
                ItemAction.RerunFailed => await PreviewRerunAsync(item, ct),
                _ => await PreviewApprovalAsync(item, action, ct),
            };
        }
        catch (HttpRequestException ex) { return ActionText.Refuse(ActionText.FromHttpError(ex).Error ?? ex.Message); }
        catch (RateLimitedException ex) { return ActionText.Refuse(ex.Message); }
        catch (SourceAuthException ex) { return ActionText.Refuse(ex.Message); }
    }

    async Task<bool> ApprovalStillMineAsync(ApprovalToken t, CancellationToken ct)
    {
        var ctx = await directory.GetOrgContextAsync(t.Org, ct);
        var a = await client.GetAsync(ApprovalUrl(t), ct);
        return IsPending(a.Str("status")) && a.Arr("steps").Any(s =>
            IsPending(s.Str("status")) && s.Obj("assignedApprover").Str("id") is { } id && ctx.MyIdentityIds.Contains(id));
    }

    async Task<ActionPreview> PreviewApprovalAsync(InboxItem item, ItemAction action, CancellationToken ct)
    {
        var token = new ApprovalToken(item.Org!, item.Project!, item.ApprovalId!, action);
        if (!await ApprovalStillMineAsync(token, ct)) return ActionText.Refuse("No longer waiting on you");
        var verb = action == ItemAction.Approve ? "Approve" : "Reject";
        return new ActionPreview(true, $"{verb} {item.Title} in {item.Org}/{item.Project}", $"{verb} {item.Title}", null, null, token);
    }

    static bool IsPending(string? s) => string.Equals(s, "pending", StringComparison.OrdinalIgnoreCase);

    static bool PrStillValid(JsonElement pr, string sha) =>
        string.Equals(pr.Str("status"), "active", StringComparison.OrdinalIgnoreCase)
        && pr.Obj("lastMergeSourceCommit").Str("commitId") == sha;

    async Task<ActionPreview> PreviewPrAsync(InboxItem item, AgentRun? run, CancellationToken ct)
    {
        var url = PrUrl(item.Org!, item.Project!, item.RepoId!, item.Number!.Value);
        var pr = await client.GetAsync($"{url}?api-version=7.1", ct);
        if (!string.Equals(pr.Str("status"), "active", StringComparison.OrdinalIgnoreCase)) return ActionText.Refuse("This PR is no longer open");
        var sha = pr.Obj("lastMergeSourceCommit").Str("commitId");
        if (sha is null || sha != item.HeadSha) return ActionText.Refuse("New commits since you looked — refresh.");
        var sha7 = sha.Length > 7 ? sha[..7] : sha;
        return new ActionPreview(true, $"Approve PR #{item.Number} in {item.Repo} at {sha7}", $"Approve PR #{item.Number}",
            ActionText.PrWarning(item, run), null, new PrToken(item.Org!, item.Project!, item.RepoId!, item.Number.Value, sha));
    }

    static string Branch(string? refName) => refName is { } r && r.StartsWith("refs/heads/", StringComparison.Ordinal) ? r["refs/heads/".Length..] : refName ?? "the target branch";

    static string MergeVerb(MergeMethod m) => m switch
    {
        MergeMethod.Squash => "Squash and merge",
        MergeMethod.Rebase => "Rebase and merge",
        _ => "Merge",
    };

    static string StrategyName(MergeMethod m) => m switch { MergeMethod.Squash => "squash", MergeMethod.Rebase => "rebase", _ => "noFastForward" };

    static bool ScopeApplies(JsonElement scope, string repoId, string? targetRef)
    {
        var scopeRepo = scope.Str("repositoryId");
        if (scopeRepo is not null && !string.Equals(scopeRepo, repoId, StringComparison.OrdinalIgnoreCase)) return false;
        var refName = scope.Str("refName");
        if (refName is null) return true;
        return string.Equals(scope.Str("matchKind"), "Prefix", StringComparison.OrdinalIgnoreCase)
            ? targetRef?.StartsWith(refName, StringComparison.Ordinal) == true
            : string.Equals(refName, targetRef, StringComparison.Ordinal);
    }

    async Task<List<MergeMethod>> AllowedMethodsAsync(string org, string project, string repoId, string? targetRef, CancellationToken ct)
    {
        var cfgs = await client.GetAsync($"{Base(org, project)}/_apis/policy/configurations?policyType={MergeStrategyPolicyTypeId}&api-version=7.1", ct);
        var applicable = cfgs.Arr("value")
            .Where(c => c.Bool("isEnabled") && !c.Bool("isDeleted")
                && string.Equals(c.Obj("type").Str("id"), MergeStrategyPolicyTypeId, StringComparison.OrdinalIgnoreCase)
                && c.Obj("settings").Arr("scope").Any(s => ScopeApplies(s, repoId, targetRef)))
            .ToList();
        if (applicable.Count == 0) return [MergeMethod.Merge, MergeMethod.Squash, MergeMethod.Rebase];
        var allowed = new List<MergeMethod>();
        if (applicable.Any(c => c.Obj("settings").Bool("allowNoFastForward"))) allowed.Add(MergeMethod.Merge);
        if (applicable.Any(c => c.Obj("settings").Bool("allowSquash"))) allowed.Add(MergeMethod.Squash);
        if (applicable.Any(c => c.Obj("settings").Bool("allowRebase"))) allowed.Add(MergeMethod.Rebase);
        return allowed;
    }

    async Task<JsonElement> EvaluationsAsync(string org, string project, string projectId, int prNumber, CancellationToken ct)
    {
        var artifact = Uri.EscapeDataString($"vstfs:///CodeReview/CodeReviewId/{projectId}/{prNumber}");
        return await client.GetAsync($"{Base(org, project)}/_apis/policy/evaluations?artifactId={artifact}&{EvaluationApi}", ct);
    }

    async Task<ActionPreview> PreviewMergeAsync(InboxItem item, CancellationToken ct)
    {
        var url = PrUrl(item.Org!, item.Project!, item.RepoId!, item.Number!.Value);
        var pr = await client.GetAsync($"{url}?api-version=7.1", ct);
        if (!string.Equals(pr.Str("status"), "active", StringComparison.OrdinalIgnoreCase)) return ActionText.Refuse("This PR is no longer open");
        var sha = pr.Obj("lastMergeSourceCommit").Str("commitId");
        if (sha is null || sha != item.HeadSha) return ActionText.Refuse("New commits since you looked — refresh.");
        var mergeStatus = pr.Str("mergeStatus");
        if (!string.Equals(mergeStatus, "succeeded", StringComparison.OrdinalIgnoreCase))
            return ActionText.Refuse(string.Equals(mergeStatus, "conflicts", StringComparison.OrdinalIgnoreCase)
                ? "Merge conflicts"
                : "Azure DevOps is still checking mergeability — try again in a moment");
        if (pr.Bool("isDraft")) return ActionText.Refuse("Draft PR");

        var projectId = pr.Obj("repository").Obj("project").Str("id");
        if (projectId is null) return ActionText.Refuse("Not mergeable right now");
        var evals = (await EvaluationsAsync(item.Org!, item.Project!, projectId, item.Number.Value, ct)).Arr("value").ToList();
        if (evals.Any(e => e.Str("status") == "rejected")) return ActionText.Refuse("Some policies are not passing");
        if (evals.Any(e => e.Obj("configuration").Bool("isBlocking") && e.Str("status") is "queued" or "running"))
            return ActionText.Refuse("Policies are still running");

        var targetRef = pr.Str("targetRefName");
        var allowed = await AllowedMethodsAsync(item.Org!, item.Project!, item.RepoId!, targetRef, ct);
        if (allowed.Count == 0) return ActionText.Refuse("No merge method is allowed on this branch");
        var selected = MergeDefaults.Pick(allowed, null);
        var sha7 = sha.Length > 7 ? sha[..7] : sha;
        return new ActionPreview(true, $"{MergeVerb(selected)} PR #{item.Number} into {Branch(targetRef)} in {item.Repo} at {sha7}", $"Merge PR #{item.Number}",
            null, null, new MergeToken(item.Org!, item.Project!, item.RepoId!, item.Number.Value, sha, [.. allowed]))
        { Options = new MergeOptions(allowed, selected, CanDeleteBranch: true, DeleteBranch: true) };
    }

    async Task<ActionPreview> PreviewRerunAsync(InboxItem item, CancellationToken ct)
    {
        var url = PrUrl(item.Org!, item.Project!, item.RepoId!, item.Number!.Value);
        var pr = await client.GetAsync($"{url}?api-version=7.1", ct);
        if (!string.Equals(pr.Str("status"), "active", StringComparison.OrdinalIgnoreCase)) return ActionText.Refuse("This PR is no longer open");
        var sha = pr.Obj("lastMergeSourceCommit").Str("commitId");
        if (sha is null || sha != item.HeadSha) return ActionText.Refuse("New commits since you looked — refresh.");
        var projectId = pr.Obj("repository").Obj("project").Str("id");
        const string None = "No failed build policies to re-queue";
        if (projectId is null) return ActionText.Refuse(None);
        var failed = (await EvaluationsAsync(item.Org!, item.Project!, projectId, item.Number.Value, ct)).Arr("value")
            .Where(e => e.Str("status") is "rejected" or "broken"
                && string.Equals(e.Obj("configuration").Obj("type").Str("id"), AdoPullRequests.BuildPolicyTypeId, StringComparison.OrdinalIgnoreCase)
                && e.Str("evaluationId") is not null)
            .Select(e => (Id: e.Str("evaluationId")!, Name: e.Obj("configuration").Obj("settings").Str("displayName") ?? "Build"))
            .ToArray();
        if (failed.Length == 0) return ActionText.Refuse(None);
        var count = failed.Length;
        return new ActionPreview(true,
            $"Re-queue {count} failed build {(count == 1 ? "policy" : "policies")} on PR #{item.Number} ({item.Repo}): {string.Join(", ", failed.Select(f => f.Name))}",
            $"Re-queue {count} {(count == 1 ? "build" : "builds")}", null, null,
            new RerunToken(item.Org!, item.Project!, item.RepoId!, item.Number.Value, sha, failed));
    }

    public async Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, ActionInput input, CancellationToken ct)
    {
        var comment = input.Comment;
        var outcome = "failed";
        try
        {
            if (!Matches(item, action, preview)) return new ActionResult(false, Mismatch);
            var result = preview.Token switch
            {
                PrToken pt => await ExecutePrAsync(pt, comment, ct),
                MergeToken mt => await ExecuteMergeAsync(mt, preview, input, ct),
                RerunToken rt => await ExecuteRerunAsync(rt, ct),
                _ => await ExecuteApprovalAsync((ApprovalToken)preview.Token!, comment, ct),
            };
            if (result.Ok) outcome = "ok";
            return result;
        }
        catch (HttpRequestException ex) { return ActionText.FromHttpError(ex); }
        catch (RateLimitedException ex) { return new ActionResult(false, ex.Message); }
        catch (SourceAuthException ex) { return new ActionResult(false, ex.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ActionResult(false, "The request timed out — it may have been sent. Refresh to check.", Stale: true);
        }
        catch (JsonException)
        {
            // A 2xx whose body isn't JSON (proxy page, truncated reply): the write may have landed.
            return new ActionResult(false, "Unexpected response — it may have been sent. Refresh to check.", Stale: true);
        }
        finally
        {
            Log.Info($"action {action} {item.Key}: {outcome} (comment {comment?.Length ?? 0} chars)");
        }
    }

    bool Matches(InboxItem item, ItemAction action, ActionPreview preview)
    {
        if (!preview.Allowed || !ActionsFor(item).Contains(action)) return false;
        return preview.Token switch
        {
            ApprovalToken t => action != ItemAction.ApprovePr && t.Action == action && t.Org == item.Org && t.Project == item.Project
                && t.ApprovalId == item.ApprovalId,
            PrToken t => action == ItemAction.ApprovePr && t.Org == item.Org && t.Project == item.Project
                && t.RepoId == item.RepoId && t.Number == item.Number && t.Sha == item.HeadSha,
            MergeToken t => action == ItemAction.Merge && t.Org == item.Org && t.Project == item.Project
                && t.RepoId == item.RepoId && t.Number == item.Number && t.Sha == item.HeadSha,
            RerunToken t => action == ItemAction.RerunFailed && t.Org == item.Org && t.Project == item.Project
                && t.RepoId == item.RepoId && t.Number == item.Number && t.Sha == item.HeadSha,
            _ => false,
        };
    }

    async Task<ActionResult> ExecuteMergeAsync(MergeToken t, ActionPreview preview, ActionInput input, CancellationToken ct)
    {
        if (input.Method is not { } method || !t.Allowed.Contains(method) || (preview.Options is { } o && !o.Allowed.Contains(method)))
            return new ActionResult(false, Mismatch);
        var prUrl = PrUrl(t.Org, t.Project, t.RepoId, t.Number);
        var pr = await client.GetAsync($"{prUrl}?api-version=7.1", ct);
        if (!PrStillValid(pr, t.Sha) || !string.Equals(pr.Str("mergeStatus"), "succeeded", StringComparison.OrdinalIgnoreCase)) return Stale();
        var body = new
        {
            status = "completed",
            lastMergeSourceCommit = new { commitId = t.Sha },
            completionOptions = new { mergeStrategy = StrategyName(method), deleteSourceBranch = input.DeleteBranch },
        };
        await client.SendAsync(HttpMethod.Patch, $"{prUrl}?api-version=7.1", body, ct);
        return new ActionResult(true, null);
    }

    async Task<ActionResult> ExecuteRerunAsync(RerunToken t, CancellationToken ct)
    {
        var pr = await client.GetAsync($"{PrUrl(t.Org, t.Project, t.RepoId, t.Number)}?api-version=7.1", ct);
        if (!PrStillValid(pr, t.Sha)) return Stale();
        var failed = new List<string>();
        HttpRequestException? first = null;
        foreach (var (id, name) in t.Evaluations)
        {
            try
            {
                await client.SendAsync(HttpMethod.Patch,
                    $"{Base(t.Org, t.Project)}/_apis/policy/evaluations/{Uri.EscapeDataString(id)}?{EvaluationApi}", null, ct);
            }
            catch (HttpRequestException ex)
            {
                first ??= ex;
                failed.Add(name);
            }
        }
        if (failed.Count == 0) return new ActionResult(true, null);
        if (failed.Count == t.Evaluations.Length) return ActionText.FromHttpError(first!);
        return new ActionResult(true, $"Re-queue started, but couldn't re-queue: {string.Join(", ", failed)}");
    }

    static ActionResult Stale() => new(false, Changed, Stale: true);

    async Task<ActionResult> ExecuteApprovalAsync(ApprovalToken t, string? comment, CancellationToken ct)
    {
        if (!await ApprovalStillMineAsync(t, ct)) return Stale();
        var body = new[] { new { approvalId = t.ApprovalId, status = t.Action == ItemAction.Approve ? "approved" : "rejected", comment = comment ?? "" } };
        await client.SendAsync(HttpMethod.Patch, $"{Base(t.Org, t.Project)}/_apis/pipelines/approvals?{ApprovalApi}", body, ct);
        return new ActionResult(true, null);
    }

    async Task<ActionResult> ExecutePrAsync(PrToken t, string? comment, CancellationToken ct)
    {
        var prUrl = PrUrl(t.Org, t.Project, t.RepoId, t.Number);
        var pr = await client.GetAsync($"{prUrl}?api-version=7.1", ct);
        if (!PrStillValid(pr, t.Sha)) return Stale();
        var ctx = await directory.GetOrgContextAsync(t.Org, ct);
        await client.SendAsync(HttpMethod.Put, $"{prUrl}/reviewers/{Uri.EscapeDataString(ctx.MyId)}?api-version=7.1", new { vote = 10 }, ct);
        if (string.IsNullOrWhiteSpace(comment)) return new ActionResult(true, null);
        try
        {
            await client.SendAsync(HttpMethod.Post, $"{prUrl}/threads?api-version=7.1",
                new { comments = new[] { new { parentCommentId = 0, content = comment, commentType = 1 } }, status = 1 }, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new ActionResult(true, $"Approved, but the comment couldn't be posted: {ex.Message}");
        }
        return new ActionResult(true, null);
    }
}
