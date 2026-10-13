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

    public IReadOnlyList<ItemAction> ActionsFor(InboxItem item)
    {
        if (string.IsNullOrEmpty(item.Org) || string.IsNullOrEmpty(item.Project)) return [];
        var actions = ActionText.DefaultActions(item);
        return actions.Count switch
        {
            0 => [],
            _ when actions[0] == ItemAction.ApprovePr => item.RepoId is { Length: > 0 } && item.Number is not null ? actions : [],
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
            return action == ItemAction.ApprovePr ? await PreviewPrAsync(item, run, ct) : await PreviewApprovalAsync(item, action, ct);
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

    public async Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, string? comment, CancellationToken ct)
    {
        var outcome = "failed";
        try
        {
            if (!Matches(item, action, preview)) return new ActionResult(false, Mismatch);
            var result = preview.Token is PrToken pt
                ? await ExecutePrAsync(pt, comment, ct)
                : await ExecuteApprovalAsync((ApprovalToken)preview.Token!, comment, ct);
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
            _ => false,
        };
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
